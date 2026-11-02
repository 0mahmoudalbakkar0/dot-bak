// .BAK - application shell: tray, global mouse hook, hotkeys, auto-start, wheel logic
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace BakApp
{
    internal static class Startup
    {
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string ValueName = "BAK";
        const string LegacyValueName = "ExtensionWheel";

        static string Command { get { return "\"" + Application.ExecutablePath + "\" --startup"; } }

        public static void Set(bool on)
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunKey, true))
                {
                    if (k == null) return;
                    k.DeleteValue(LegacyValueName, false);
                    if (on) k.SetValue(ValueName, Command); else k.DeleteValue(ValueName, false);
                }
            }
            catch (Exception ex) { Log.Write("startup: " + ex.Message); }
        }

        // Keeps the Run entry pointing at the current exe (e.g. after the folder was moved).
        public static void Heal()
        {
            if (!Store.Config.AutoStart) return;
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunKey, false))
                {
                    object v = k == null ? null : k.GetValue(ValueName);
                    if (v == null || Convert.ToString(v) != Command) Set(true);
                }
            }
            catch (Exception ex) { Log.Write("startup heal: " + ex.Message); }
        }
    }

    internal sealed class HotkeyWindow : NativeWindow, IDisposable
    {
        public event Action<int> Pressed;

        public HotkeyWindow()
        {
            CreateParams cp = new CreateParams();
            cp.Parent = new IntPtr(-3); // HWND_MESSAGE
            CreateHandle(cp);
        }

        public bool Register(int id, uint mods, uint vk) { return Native.RegisterHotKey(Handle, id, mods | 0x4000, vk); }
        public void Unregister(int id) { Native.UnregisterHotKey(Handle, id); }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Native.WM_HOTKEY)
            {
                Action<int> h = Pressed;
                if (h != null) h(m.WParam.ToInt32());
                return;
            }
            base.WndProc(ref m);
        }

        public void Dispose() { if (Handle != IntPtr.Zero) DestroyHandle(); }
    }

    internal sealed class MenuColors : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground { get { return Ui.Card; } }
        public override Color ImageMarginGradientBegin { get { return Ui.Card; } }
        public override Color ImageMarginGradientMiddle { get { return Ui.Card; } }
        public override Color ImageMarginGradientEnd { get { return Ui.Card; } }
        public override Color MenuBorder { get { return Ui.Border; } }
        public override Color MenuItemBorder { get { return Ui.Mix(Ui.Card, Ui.Accent, 0.3f); } }
        public override Color MenuItemSelected { get { return Ui.Mix(Ui.Card, Ui.Accent, 0.3f); } }
        public override Color MenuItemSelectedGradientBegin { get { return Ui.Mix(Ui.Card, Ui.Accent, 0.3f); } }
        public override Color MenuItemSelectedGradientEnd { get { return Ui.Mix(Ui.Card, Ui.Accent, 0.3f); } }
        public override Color SeparatorDark { get { return Ui.Border; } }
        public override Color SeparatorLight { get { return Ui.Card; } }
    }

    internal sealed class TrayApp : ApplicationContext
    {
        const int HkOpen = 1, HkUndo = 2, HkEsc = 10, HkDigit = 11;
        const int PgExt = 0, PgHistory = 4;

        readonly NotifyIcon tray = new NotifyIcon();
        readonly ContextMenuStrip menu = new ContextMenuStrip();
        ToolStripMenuItem pauseItem;
        readonly HotkeyWindow hk;
        readonly WheelWindow wheel = new WheelWindow();
        readonly ToastWindow toast = new ToastWindow();
        readonly Control sync = new Control();
        readonly System.Windows.Forms.Timer holdTimer = new System.Windows.Forms.Timer();
        readonly Native.LowLevelMouseProc hookProc;
        readonly EventWaitHandle openEvent;
        readonly object gate = new object();
        readonly ManualResetEvent hookReady = new ManualResetEvent(false);
        IntPtr hook = IntPtr.Zero;
        uint hookThreadId;
        SettingsForm settings;
        volatile bool armed, trigDown, downSwallowed, wheelOpen, paused;
        volatile int downButton;     // which button went down while swallowed: 0 middle, 1 X1, 2 X2
        bool wheelKeysOn, quitting;
        int state;            // guarded by gate: 0 idle, 1 waiting for the selection, 2 wheel held open
        bool upHeld;          // guarded by gate: the button release was held back while deciding
        int armX, armY, armTick, armId, seenId, stateTick;
        bool prefetching, prefetchDone, holdReached;
        List<string> prefetchFiles;
        IntPtr armRoot = IntPtr.Zero;
        List<string> pendingFiles = new List<string>();
        string pendingKey;

        public Icon AppIcon { get; private set; }

        public TrayApp(bool startup)
        {
            IntPtr dummy = sync.Handle; // force handle creation so BeginInvoke works
            GC.KeepAlive(dummy);
            AppIcon = MakeIcon();

            hk = new HotkeyWindow();
            hk.Pressed += OnHotkey;
            AppConfig cfg = Store.Config;
            if (!hk.Register(HkOpen, cfg.HkOpenMods, cfg.HkOpenVk))
                Log.Write("hotkey (open) unavailable");
            if (!hk.Register(HkUndo, cfg.HkUndoMods, cfg.HkUndoVk))
                Log.Write("hotkey (undo) unavailable");

            wheel.Chosen += OnChosen;
            wheel.Dismissed += OnWheelClosed;
            wheel.HoldHeld = delegate { return trigDown; };

            BuildMenu();

            tray.Icon = AppIcon;
            tray.Text = AppInfo.Name;
            tray.ContextMenuStrip = menu;
            tray.DoubleClick += delegate { OpenSettings(PgExt, null); };
            tray.Visible = true;

            hookProc = HookCallback;
            Thread ht = new Thread(HookThreadMain);
            ht.IsBackground = true;
            ht.Name = "BAK.Hook";
            ht.Start();
            if (!hookReady.WaitOne(3000) || hook == IntPtr.Zero)
                ShowToast(Loc.T("toast.nohook"), Loc.F("toast.nohook.sub", Hotkeys.Format(cfg.HkOpenMods, cfg.HkOpenVk)), null, null, true);

            holdTimer.Interval = 20;
            holdTimer.Tick += OnHoldTick;
            holdTimer.Start();

            openEvent = new EventWaitHandle(false, EventResetMode.AutoReset, "Local\\BAK.Open");
            Thread t = new Thread(delegate()
            {
                while (!quitting)
                {
                    try
                    {
                        openEvent.WaitOne();
                        if (quitting) return;
                        sync.BeginInvoke(new Action(delegate { OpenSettings(PgExt, null); }));
                    }
                    catch { return; }
                }
            });
            t.IsBackground = true;
            t.Start();

            Startup.Heal();
            if (Store.FirstRun)
            {
                Startup.Set(Store.Config.AutoStart);
                if (!startup) OpenSettings(PgExt, null);
            }
            else if (!startup)
                ShowToast(Loc.F("toast.running", Ui.Ltr(AppInfo.Name)), Loc.F("toast.running.sub", TriggerName()), null, null, false);
        }

        static string TriggerName()
        {
            int b = Store.Config.TriggerButton;
            return Loc.T(b == 1 ? "trig.x1" : b == 2 ? "trig.x2" : "trig.mid");
        }

        void BuildMenu()
        {
            menu.Items.Clear();
            menu.RightToLeft = Loc.Rtl ? RightToLeft.Yes : RightToLeft.No;
            menu.Renderer = new ToolStripProfessionalRenderer(new MenuColors());
            menu.ShowImageMargin = false;
            menu.Font = Ui.F(10, FontStyle.Regular);
            menu.ForeColor = Ui.TextC;
            menu.BackColor = Ui.Card;
            AddItem(Loc.T("tray.settings"), delegate { OpenSettings(PgExt, null); });
            AddItem(Loc.T("tray.undo"), delegate { UndoLast(); });
            AddItem(Loc.T("tray.history"), delegate { OpenSettings(PgHistory, null); });
            menu.Items.Add(new ToolStripSeparator());
            pauseItem = AddItem(paused ? Loc.T("tray.resume") : Loc.T("tray.pause"), delegate { TogglePause(); });
            AddItem(Loc.T("tray.report"), delegate { Ui.OpenUrl(AppInfo.TelegramUrl); });
            menu.Items.Add(new ToolStripSeparator());
            AddItem(Loc.T("tray.quit"), delegate { Quit(); });
        }

        ToolStripMenuItem AddItem(string text, EventHandler click)
        {
            ToolStripMenuItem it = new ToolStripMenuItem(text);
            it.ForeColor = Ui.TextC;
            it.Padding = new Padding(4, 6, 4, 6);
            it.Click += click;
            menu.Items.Add(it);
            return it;
        }

        Icon MakeIcon()
        {
            using (Bitmap bmp = new Bitmap(32, 32, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
            {
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                    Ui.DrawLogo(g, new RectangleF(1, 1, 30, 30), Ui.Accent);
                }
                IntPtr h = bmp.GetHicon();
                try { return (Icon)Icon.FromHandle(h).Clone(); }
                finally { Native.DestroyIcon(h); }
            }
        }

        public void OnConfigChanged()
        {
            Icon old = AppIcon;
            AppIcon = MakeIcon();
            tray.Icon = AppIcon;
            if (old != null) old.Dispose();
            BuildMenu();
        }

        // ------------------------------------------------------------ mouse hook (runs on its own thread)
        static class Kern
        {
            [DllImport("kernel32.dll")] public static extern uint GetCurrentThreadId();
            [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] public static extern bool PostThreadMessage(uint idThread, uint msg, IntPtr w, IntPtr l);
            [DllImport("user32.dll")] public static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extra);
        }

        // A low-level hook must never wait on anything slow, or mouse input freezes system-wide.
        // So it lives on a dedicated thread and only flips flags; all slow work happens elsewhere.
        void HookThreadMain()
        {
            try
            {
                hookThreadId = Kern.GetCurrentThreadId();
                hook = Native.SetWindowsHookEx(Native.WH_MOUSE_LL, hookProc, Native.GetModuleHandle(null), 0);
                if (hook == IntPtr.Zero) Log.Write("hook failed: " + Marshal.GetLastWin32Error());
            }
            catch (Exception ex) { Log.Write("hook thread: " + ex.Message); }
            finally { hookReady.Set(); }
            if (hook == IntPtr.Zero) return;
            Application.Run();
            Native.UnhookWindowsHookEx(hook);
            hook = IntPtr.Zero;
        }

        static bool IsFileView(int x, int y, out IntPtr root)
        {
            root = IntPtr.Zero;
            Native.POINT p = new Native.POINT(); p.x = x; p.y = y;
            IntPtr h = Native.WindowFromPoint(p);
            if (h == IntPtr.Zero) return false;
            string c = Native.ClassName(h);
            if (c != "DirectUIHWND" && c != "SysListView32") return false;
            IntPtr r = Native.GetAncestor(h, 2);
            string rc = Native.ClassName(r);
            bool explorer = rc == "CabinetWClass" || rc == "ExploreWClass";
            bool desktop = (rc == "Progman" || rc == "WorkerW") && c == "SysListView32";
            if (!explorer && !desktop) return false;
            root = r;
            return true;
        }

        // Which of our three buttons does this message belong to? Returns -1 when none; isDown tells press from release.
        static int Classify(int msg, uint mouseData, out bool isDown)
        {
            isDown = false;
            if (msg == Native.WM_MBUTTONDOWN) { isDown = true; return 0; }
            if (msg == Native.WM_MBUTTONUP) return 0;
            if (msg == 0x020B || msg == 0x020C)   // WM_XBUTTONDOWN / WM_XBUTTONUP
            {
                isDown = msg == 0x020B;
                int xb = (int)((mouseData >> 16) & 0xFFFF);
                return xb == 1 ? 1 : (xb == 2 ? 2 : -1);
            }
            return -1;
        }

        // The button-down is swallowed only over a file view, so Explorer never sees a half-finished
        // click. A short click is replayed on release; a long press opens the wheel.
        IntPtr HookCallback(int code, IntPtr wParam, IntPtr lParam)
        {
            try
            {
                if (code >= 0)
                {
                    int msg = wParam.ToInt32();
                    if (msg == 0x0201) { armed = false; }   // left button went down: not a hold gesture
                    else if (msg == Native.WM_MBUTTONDOWN || msg == Native.WM_MBUTTONUP || msg == 0x020B || msg == 0x020C)
                    {
                        Native.MSLLHOOKSTRUCT ms = (Native.MSLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(Native.MSLLHOOKSTRUCT));
                        bool injected = (ms.flags & 1) != 0;
                        bool isDown;
                        int btn = Classify(msg, ms.mouseData, out isDown);
                        if (!injected && btn >= 0)
                        {
                            if (isDown)
                            {
                                if (btn == Store.Config.TriggerButton)
                                {
                                    trigDown = true;
                                    armed = false;
                                    IntPtr root;
                                    if (!paused && !wheelOpen && IsFileView(ms.pt.x, ms.pt.y, out root))
                                    {
                                        armX = ms.pt.x; armY = ms.pt.y; armTick = Environment.TickCount; armRoot = root;
                                        Interlocked.Increment(ref armId);
                                        downButton = btn;
                                        armed = true;
                                        downSwallowed = true;
                                        return new IntPtr(1);
                                    }
                                }
                            }
                            else if (btn == downButton && (downSwallowed || btn == Store.Config.TriggerButton))
                            {
                                trigDown = false;
                                armed = false;
                                if (downSwallowed)
                                {
                                    downSwallowed = false;
                                    bool release = false, replay = false;
                                    lock (gate)
                                    {
                                        if (state == 1) upHeld = true;
                                        else if (state == 2) { state = 0; release = true; }
                                        else replay = true;   // short click or cancelled gesture: give Explorer its click back
                                    }
                                    if (release) wheel.NotifyRelease();
                                    if (replay) ReplayClick(downButton);
                                    return new IntPtr(1);
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex) { Log.Write("hook: " + ex.Message); }
            return Native.CallNextHookEx(hook, code, wParam, lParam);
        }

        static void ReplayClick(int btn)
        {
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    if (btn == 0)
                    {
                        Kern.mouse_event(0x0020, 0, 0, 0, UIntPtr.Zero); // MIDDLEDOWN
                        Kern.mouse_event(0x0040, 0, 0, 0, UIntPtr.Zero); // MIDDLEUP
                    }
                    else
                    {
                        Kern.mouse_event(0x0080, 0, 0, (uint)btn, UIntPtr.Zero); // XDOWN
                        Kern.mouse_event(0x0100, 0, 0, (uint)btn, UIntPtr.Zero); // XUP
                    }
                }
                catch (Exception ex) { Log.Write("replay: " + ex.Message); }
            });
        }

        static void LeftClickHere()
        {
            Kern.mouse_event(0x0002, 0, 0, 0, UIntPtr.Zero); // LEFTDOWN
            Kern.mouse_event(0x0004, 0, 0, 0, UIntPtr.Zero); // LEFTUP
        }

        void OnHoldTick(object sender, EventArgs e)
        {
            try
            {
                if (armId != seenId)
                {
                    seenId = armId; prefetching = false; prefetchDone = false; holdReached = false; prefetchFiles = null;
                }
                bool waiting;
                lock (gate) waiting = state == 1;
                if (waiting && unchecked(Environment.TickCount - stateTick) > 3000) { AbortDecision(); return; }
                if (!armed) return;
                if (!trigDown) { armed = false; return; }
                Point c = Cursor.Position;
                int tol = Ui.S(10);
                if (Math.Abs(c.X - armX) > tol || Math.Abs(c.Y - armY) > tol) { armed = false; return; }
                int el = unchecked(Environment.TickCount - armTick);
                int hold = Store.Config.HoldMs;
                if (!prefetching && el >= hold / 2) { prefetching = true; StartFetch(armRoot, seenId, true); }
                if (el < hold) return;
                armed = false;
                lock (gate)
                {
                    if (state != 0 || !trigDown) return;
                    state = 1; upHeld = false; stateTick = Environment.TickCount;
                }
                holdReached = true;
                if (prefetchDone) FinishHold(prefetchFiles, armRoot, false);
            }
            catch (Exception ex) { Log.Write("hold tick: " + ex); }
        }

        // Reads the selection on a worker thread (COM is slow) and reports back on the UI thread.
        void StartFetch(IntPtr root, int id, bool forHold)
        {
            Thread t = new Thread(delegate()
            {
                List<string> files = ExplorerSelection(root);
                try { sync.BeginInvoke(new Action(delegate { OnFetched(files, root, id, forHold); })); } catch { }
            });
            t.SetApartmentState(ApartmentState.STA);
            t.IsBackground = true;
            t.Start();
        }

        void OnFetched(List<string> files, IntPtr root, int id, bool forHold)
        {
            if (!forHold) { OpenWheelWith(files, root, false); return; }
            if (id != seenId) return;
            prefetchFiles = files; prefetchDone = true;
            if (holdReached) FinishHold(files, root, false);
        }

        // Nothing selected and "select automatically" is on: click the item under the cursor, then read the selection again.
        void AutoSelectThenRetry(IntPtr root)
        {
            Thread t = new Thread(delegate()
            {
                try
                {
                    LeftClickHere();
                    Thread.Sleep(160);
                }
                catch (Exception ex) { Log.Write("autoselect: " + ex.Message); }
                List<string> files = ExplorerSelection(root);
                try { sync.BeginInvoke(new Action(delegate { FinishHold(files, root, true); })); } catch { }
            });
            t.SetApartmentState(ApartmentState.STA);
            t.IsBackground = true;
            t.Start();
        }

        void FinishHold(List<string> files, IntPtr root, bool retried)
        {
            holdReached = false;
            bool go = files != null && files.Count > 0;
            if (!go && !retried && Store.Config.AutoSelect)
            {
                bool still;
                lock (gate) still = state == 1;
                if (still) { AutoSelectThenRetry(root); return; }
            }
            bool replay = false, releasedAlready = false;
            lock (gate)
            {
                if (state != 1) return;
                if (!go) { replay = upHeld; upHeld = false; state = 0; }
                else if (upHeld) { upHeld = false; releasedAlready = true; state = 0; }
                else state = 2;
            }
            if (!go)
            {
                if (replay) ReplayClick(downButton); // nothing to act on: the click was held back, give it back to Explorer
                ShowToast(Loc.T("toast.select"), Loc.T("toast.select.sub"), null, null, false);
                return;
            }
            OpenWheelWith(files, root, !releasedAlready);
        }

        void AbortDecision()
        {
            bool replay;
            lock (gate) { replay = upHeld; upHeld = false; state = 0; }
            holdReached = false;
            if (replay) ReplayClick(downButton);
        }

        void OnWheelClosed()
        {
            wheelOpen = false;
            lock (gate) { if (state == 2 && !trigDown) state = 0; }
            UnregisterWheelKeys();
        }

        // ------------------------------------------------------------ hotkeys
        // Changes one of the two global shortcuts. On failure the previous one stays active.
        public bool TrySetHotkey(bool isOpen, uint mods, uint vk, out string error)
        {
            error = null;
            AppConfig cfg = Store.Config;
            string v = Hotkeys.Validate(mods, vk);
            if (v != null) { error = Loc.T(v); return false; }
            uint otherM = isOpen ? cfg.HkUndoMods : cfg.HkOpenMods, otherV = isOpen ? cfg.HkUndoVk : cfg.HkOpenVk;
            if (otherM == mods && otherV == vk) { error = Loc.T("hk.err.same"); return false; }
            int id = isOpen ? HkOpen : HkUndo;
            uint oldM = isOpen ? cfg.HkOpenMods : cfg.HkUndoMods, oldV = isOpen ? cfg.HkOpenVk : cfg.HkUndoVk;
            hk.Unregister(id);
            if (!hk.Register(id, mods, vk))
            {
                hk.Register(id, oldM, oldV);
                error = Loc.T("hk.err.inuse");
                return false;
            }
            if (isOpen) { cfg.HkOpenMods = mods; cfg.HkOpenVk = vk; }
            else { cfg.HkUndoMods = mods; cfg.HkUndoVk = vk; }
            return true;
        }

        void OnHotkey(int id)
        {
            if (id == HkOpen)
            {
                if (wheel.IsOpen) { wheel.Dismiss(); return; }
                IntPtr fg = Native.GetForegroundWindow();
                string cls = Native.ClassName(fg);
                if (cls != "CabinetWClass" && cls != "ExploreWClass" && cls != "Progman" && cls != "WorkerW")
                {
                    ShowToast(Loc.T("toast.openexp"), Loc.F("toast.openexp.sub", Hotkeys.Format(Store.Config.HkOpenMods, Store.Config.HkOpenVk)), null, null, false);
                    return;
                }
                StartFetch(fg, 0, false);
            }
            else if (id == HkUndo) UndoLast();
            else if (id == HkEsc) wheel.HandleKey(-1);
            else if (id >= HkDigit && id < HkDigit + 8) wheel.HandleKey(id - HkDigit);
        }

        void RegisterWheelKeys()
        {
            if (wheelKeysOn) return;
            wheelKeysOn = true;
            hk.Register(HkEsc, 0, 0x1B);
            for (int i = 0; i < 8; i++) hk.Register(HkDigit + i, 0, (uint)(0x31 + i));
        }

        void UnregisterWheelKeys()
        {
            if (!wheelKeysOn) return;
            wheelKeysOn = false;
            hk.Unregister(HkEsc);
            for (int i = 0; i < 8; i++) hk.Unregister(HkDigit + i);
        }

        // ------------------------------------------------------------ explorer / desktop selection
        static List<string> ReadItems(dynamic items)
        {
            List<string> found = new List<string>();
            int n = items.Count;
            for (int j = 0; j < n; j++)
            {
                dynamic it = items.Item(j);
                if ((bool)it.IsFolder) continue;
                string p = Convert.ToString((object)it.Path);
                if (!string.IsNullOrEmpty(p) && File.Exists(p)) found.Add(p);
            }
            return found;
        }

        static List<string> ExplorerSelection(IntPtr root)
        {
            List<string> list = new List<string>();
            object shellObj = null;
            try
            {
                Type t = Type.GetTypeFromProgID("Shell.Application");
                shellObj = Activator.CreateInstance(t);
                dynamic shell = shellObj;
                object winsObj = shell.Windows();
                string rc = Native.ClassName(root);
                if (rc == "Progman" || rc == "WorkerW")
                {
                    // The desktop is not listed among Explorer windows; ask for it explicitly (CSIDL_DESKTOP, SWC_DESKTOP, SWFO_NEEDDISPATCH).
                    object[] args = new object[] { 0, null, 8, 0, 1 };
                    ParameterModifier pm = new ParameterModifier(5);
                    pm[0] = true; pm[1] = true; pm[3] = true;
                    object disp = winsObj.GetType().InvokeMember("FindWindowSW", BindingFlags.InvokeMethod, null, winsObj, args, new ParameterModifier[] { pm }, null, null);
                    if (disp != null)
                    {
                        dynamic desk = disp;
                        list = ReadItems(desk.Document.SelectedItems());
                    }
                }
                else
                {
                    dynamic wins = winsObj;
                    int count = wins.Count;
                    for (int i = 0; i < count; i++)
                    {
                        try
                        {
                            dynamic w = wins.Item(i);
                            if (w == null) continue;
                            long h = Convert.ToInt64((object)w.HWND);
                            if (h != root.ToInt64()) continue;
                            List<string> found = ReadItems(w.Document.SelectedItems());
                            if (found.Count > 0) { list = found; break; }
                        }
                        catch (Exception ex) { Log.Write("selection item: " + ex.Message); }
                    }
                }
            }
            catch (Exception ex) { Log.Write("selection: " + ex.Message); }
            finally { if (shellObj != null) { try { Marshal.ReleaseComObject(shellObj); } catch { } } }
            return list;
        }

        // ------------------------------------------------------------ wheel
        void OpenWheelWith(List<string> files, IntPtr root, bool hold)
        {
            if (files == null || files.Count == 0)
            {
                if (!hold) ShowToast(Loc.T("toast.select"), Loc.T("toast.select.sub2"), null, null, false);
                return;
            }
            if (wheel.IsOpen) return;
            WheelContext ctx = new WheelContext();
            string key;
            List<WheelOption> opts = BuildOptions(files, ctx, out key);
            pendingFiles = files;
            pendingKey = key;
            RegisterWheelKeys();
            wheelOpen = true;
            try { wheel.Open(opts, ctx, Cursor.Position, hold, Ui.Accent, Store.Config.Animations); }
            catch (Exception ex)
            {
                Log.Write("wheel open: " + ex);
                OnWheelClosed();
                lock (gate) { if (state == 2) state = 0; }
            }
        }

        List<WheelOption> BuildOptions(List<string> files, WheelContext ctx, out string key)
        {
            AppConfig cfg = Store.Config;
            string k = Ext.Of(files[0]);
            if (!files.All(f => Ext.Of(f) == k)) k = null;
            key = k;

            List<string> targets = new List<string>();
            if (k != null && cfg.Profiles.ContainsKey(k)) targets.AddRange(cfg.Profiles[k]);
            if (cfg.UsageOrder && k != null) targets = targets.OrderByDescending(t => cfg.UsageOf(k, t)).ToList();

            string suggested = null;
            if (files.Count == 1)
            {
                string b, e;
                Ext.Split(Path.GetFileName(files[0]), out b, out e);
                ctx.BaseName = b;
                ctx.Caption = Path.GetFileName(files[0]);
                ctx.SourceLabel = Ext.Label(k);
                ctx.BackupName = Path.GetFileName(Ext.BackupPath(files[0]));
                if (cfg.Smart && k != ".bak")
                {
                    Detected d = FileKind.Detect(files[0]);
                    if (d != null)
                    {
                        string curGroup = FileKind.GroupOf(k == Ext.None ? null : k);
                        bool mismatch = curGroup == null ? d.Ext != null : !FileKind.Compat(d, curGroup);
                        if (mismatch)
                        {
                            ctx.Status = Loc.F("wheel.actual", d.Name);
                            ctx.StatusLevel = 2;
                            if (d.Ext != null && d.Ext != k) suggested = d.Ext;
                        }
                        else if (curGroup != null)
                        {
                            ctx.Status = Loc.T("wheel.match");
                            ctx.StatusLevel = 1;
                        }
                    }
                }
            }
            else
            {
                ctx.SourceLabel = Loc.F("wheel.nfiles", files.Count);
                ctx.Caption = k != null ? Ext.Label(k) : Loc.T("wheel.mixed");
            }

            List<WheelOption> opts = new List<WheelOption>();
            if (files.All(f => Ext.RestorePath(f) != null))
            {
                string inner = "";
                if (files.Count == 1)
                {
                    string ib, ie;
                    Ext.Split(Path.GetFileNameWithoutExtension(files[0]), out ib, out ie);
                    inner = ie;
                    ctx.RestoreName = Path.GetFileName(Ext.RestorePath(files[0]));
                }
                WheelOption rs = new WheelOption();
                rs.Kind = OptKind.Restore; rs.Label = inner.Length > 0 ? inner : Loc.T("wheel.original"); rs.Hint = Loc.T("wheel.restore");
                opts.Add(rs);
            }
            if (suggested != null)
            {
                WheelOption s = new WheelOption();
                s.Kind = OptKind.Rename; s.Label = suggested; s.Ext = suggested; s.Suggested = true; s.Hint = Loc.T("wheel.suggested");
                opts.Add(s);
                targets.Remove(suggested);
            }
            foreach (string t in targets)
            {
                if (opts.Count >= 7) break;
                WheelOption o = new WheelOption();
                o.Kind = OptKind.Rename; o.Label = t; o.Ext = t;
                opts.Add(o);
            }
            WheelOption bk = new WheelOption(); bk.Kind = OptKind.Backup; bk.Label = ".bak"; bk.Hint = Loc.T("wheel.backup");
            opts.Add(bk);
            WheelOption c = new WheelOption(); c.Kind = OptKind.Custom; c.Hint = Loc.T("wheel.custom");
            opts.Add(c);
            WheelOption m = new WheelOption(); m.Kind = OptKind.Manage; m.Hint = Loc.T("wheel.manage");
            opts.Add(m);
            return opts;
        }

        void OnChosen(WheelOption o)
        {
            List<string> files = pendingFiles;
            string key = pendingKey;
            sync.BeginInvoke(new Action(delegate
            {
                try
                {
                    if (o.Kind == OptKind.Manage) OpenSettings(PgExt, key);
                    else if (o.Kind == OptKind.Custom) DoCustom(files, key);
                    else if (o.Kind == OptKind.Backup) DoBackup(files);
                    else if (o.Kind == OptKind.Restore) DoRestore(files);
                    else DoRename(files, o.Ext);
                }
                catch (Exception ex)
                {
                    Log.Write("chosen: " + ex);
                    ShowToast(Loc.T("toast.error"), ex.Message, null, null, true);
                }
            }));
        }

        void DoCustom(List<string> files, string key)
        {
            bool save;
            string v = Dialogs.Prompt(Loc.T("custom.title"),
                Loc.T("custom.msg"),
                "", key != null ? Loc.T("custom.save") : null,
                delegate(string x) { string r; return Ext.TryNormTarget(x, out r) ? null : Loc.T("err.badext"); }, out save);
            if (v == null) return;
            string t;
            if (!Ext.TryNormTarget(v, out t)) return;
            if (save && key != null && t != key)
            {
                List<string> list;
                if (!Store.Config.Profiles.TryGetValue(key, out list)) { list = new List<string>(); Store.Config.Profiles[key] = list; }
                if (!list.Contains(t)) { list.Add(t); Store.Save(); }
            }
            DoRename(files, t);
        }

        void DoBackup(List<string> files)
        {
            RenameResult r = Ops.Backup(files);
            if (r.Errors.Count > 0)
                ShowToast(r.Done.Count > 0 ? Loc.F("toast.bak.partial", r.Done.Count, r.Errors.Count) : Loc.T("toast.bak.fail"), r.Errors[0], null, null, true);
            else if (r.Done.Count > 0)
                ShowToast(Loc.T("toast.bak.ok"), r.Done.Count == 1 ? Path.GetFileName(r.Done[0][1]) : Loc.F("wheel.nfiles", r.Done.Count), null, null, false);
        }

        void DoRestore(List<string> files)
        {
            RenameResult r = Ops.Restore(files);
            if (r.Errors.Count > 0)
            {
                HistoryEntry en = r.Entry;
                string title = r.Done.Count > 0 ? Loc.F("toast.rest.partial", r.Done.Count, r.Errors.Count) : Loc.T("toast.rest.fail");
                if (en != null) ShowToast(title, r.Errors[0], Loc.T("toast.undo"), delegate { UndoEntry(en); }, true);
                else ShowToast(title, r.Errors[0], null, null, true);
            }
            else if (r.Done.Count > 0 && r.Entry != null)
            {
                HistoryEntry en = r.Entry;
                ShowToast(Loc.T("toast.rest.ok"), r.Done.Count == 1 ? Path.GetFileName(r.Done[0][1]) : Loc.F("wheel.nfiles", r.Done.Count), Loc.T("toast.undo"), delegate { UndoEntry(en); }, false);
            }
        }

        void DoRename(List<string> files, string ext)
        {
            if (string.IsNullOrEmpty(ext) || files.Count == 0) return;
            AppConfig cfg = Store.Config;

            if (cfg.ConfirmDanger && Ext.IsDangerous(ext) && !files.Any(f => Ext.IsDangerous(Ext.Of(f))))
            {
                if (!Dialogs.Confirm(Loc.T("danger.title"),
                    Loc.F("danger.msg", Ui.Ltr(ext)),
                    Loc.T("btn.continue"), Loc.T("btn.cancel"), true)) return;
            }

            if (cfg.ConfirmMismatch && cfg.Smart)
            {
                foreach (string f in files.Take(20))
                {
                    Detected d = FileKind.Detect(f);
                    if (d != null && !FileKind.CompatTarget(d, ext))
                    {
                        if (!Dialogs.Confirm(Loc.T("mismatch.title"),
                            Loc.F("mismatch.msg", Ui.Ltr(Path.GetFileName(f)), Ui.Ltr(d.Name), Ui.Ltr(ext)),
                            Loc.T("btn.continue"), Loc.T("btn.cancel"), false)) return;
                        break;
                    }
                }
            }

            List<string> exts = files.Select(f => ext).ToList();
            RenameResult r = Ops.Apply(files, exts);

            if (r.Errors.Count > 0)
            {
                string title = r.Done.Count > 0 ? Loc.F("toast.ren.partial", r.Done.Count, r.Errors.Count) : Loc.T("toast.ren.fail");
                if (r.Done.Count > 0 && r.Entry != null)
                {
                    HistoryEntry en = r.Entry;
                    ShowToast(title, r.Errors[0], Loc.T("toast.undo"), delegate { UndoEntry(en); }, true);
                }
                else ShowToast(title, r.Errors[0], null, null, true);
            }
            else if (r.Done.Count > 0 && r.Entry != null)
            {
                HistoryEntry en = r.Entry;
                string title = r.Done.Count == 1 ? Loc.T("toast.ren.ok") : Loc.F("toast.ren.many", r.Done.Count);
                string sub = r.Done.Count == 1 ? Path.GetFileName(r.Done[0][1]) : ext;
                ShowToast(title, sub, Loc.T("toast.undo"), delegate { UndoEntry(en); }, false);
            }
            else
                ShowToast(Loc.T("toast.nochange"), Loc.T("toast.nochange.sub"), null, null, false);

            if (settings != null && !settings.IsDisposed) settings.Invalidate(true);
        }

        // ------------------------------------------------------------ undo / toast / settings
        public void UndoEntry(HistoryEntry e)
        {
            string err;
            int n = Ops.Undo(e, out err);
            if (n > 0 && err == null) ShowToast(Loc.T("toast.undone"), n == 1 ? Loc.T("toast.undone.one") : Loc.F("toast.undone.many", n), null, null, false);
            else if (err != null) ShowToast(Loc.T("toast.undo.fail"), err, null, null, true);
        }

        void UndoLast()
        {
            for (int i = Store.History.Count - 1; i >= 0; i--)
            {
                HistoryEntry e = Store.History[i];
                if (!e.Undone && e.Items.Count > 0) { UndoEntry(e); return; }
            }
            ShowToast(Loc.T("toast.nothing"), Loc.T("toast.nothing.sub"), null, null, false);
        }

        void ShowToast(string title, string sub, string action, Action cb, bool error)
        {
            if (!error && !Store.Config.Toasts) return;
            try { toast.ShowToast(title, sub, action, cb, Ui.Accent, error, Store.Config.Animations); }
            catch (Exception ex) { Log.Write("toast: " + ex.Message); }
        }

        // Always shown (used to confirm "Save"), even when ordinary notifications are switched off.
        public void Notify(string title, string sub)
        {
            try { toast.ShowToast(title, sub, null, null, Ui.Accent, false, Store.Config.Animations); }
            catch (Exception ex) { Log.Write("notify: " + ex.Message); }
        }

        void TogglePause()
        {
            paused = !paused;
            armed = false;
            pauseItem.Text = paused ? Loc.T("tray.resume") : Loc.T("tray.pause");
            tray.Text = paused ? AppInfo.Name + " (" + Loc.T("tray.paused") + ")" : AppInfo.Name;
        }

        public void OpenSettings(int page, string sourceKey)
        {
            if (settings == null || settings.IsDisposed)
            {
                settings = new SettingsForm(this);
                settings.FormClosed += delegate { settings = null; };
            }
            settings.ShowPage(page, sourceKey);
            if (settings.WindowState == FormWindowState.Minimized) settings.WindowState = FormWindowState.Normal;
            settings.Show();
            Ui.ForceFront(settings);
        }

        // Language changed: rebuild the window (and tray menu) in the new language and come back to the same page.
        public void ReopenSettings(int page)
        {
            sync.BeginInvoke(new Action(delegate
            {
                SettingsForm old = settings;
                settings = null;
                BuildMenu();
                if (old != null && !old.IsDisposed) { old.Hide(); old.Dispose(); }
                OpenSettings(page, null);
            }));
        }

        public void Quit()
        {
            if (quitting) return;
            quitting = true;
            try { holdTimer.Stop(); } catch { }
            try { if (hookThreadId != 0) Kern.PostThreadMessage(hookThreadId, 0x0012, IntPtr.Zero, IntPtr.Zero); } catch { }
            try { UnregisterWheelKeys(); hk.Unregister(HkOpen); hk.Unregister(HkUndo); hk.Dispose(); } catch { }
            try { openEvent.Set(); } catch { }
            try { tray.Visible = false; tray.Dispose(); } catch { }
            if (settings != null && !settings.IsDisposed) settings.Close();
            ExitThread();
        }
    }

    internal static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            bool startup = args != null && args.Any(a => a == "--startup");
            bool created;
            using (Mutex instance = new Mutex(true, "Local\\BAK.Instance", out created))
            {
                if (!created)
                {
                    try { EventWaitHandle.OpenExisting("Local\\BAK.Open").Set(); } catch { }
                    return;
                }
                try { Native.SetProcessDPIAware(); } catch { }
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
                Application.ThreadException += delegate(object s, ThreadExceptionEventArgs e) { Log.Write("UI exception: " + e.Exception); };
                AppDomain.CurrentDomain.UnhandledException += delegate(object s, UnhandledExceptionEventArgs e) { Log.Write("fatal: " + e.ExceptionObject); };
                Store.Load();
                Ui.Init();
                Application.Run(new TrayApp(startup));
                GC.KeepAlive(instance);
            }
        }
    }
}
