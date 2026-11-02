// .BAK - core layer (C# 5 compatible, builds with the csc.exe that ships with Windows)
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace BakApp
{
    // ------------------------------------------------------------------ native
    internal static class Native
    {
        public const int WH_MOUSE_LL = 14;
        public const int WM_MBUTTONDOWN = 0x0207;
        public const int WM_MBUTTONUP = 0x0208;
        public const int WM_HOTKEY = 0x0312;
        public const int VK_LBUTTON = 0x01;
        public const int VK_MBUTTON = 0x04;

        [StructLayout(LayoutKind.Sequential)] public struct POINT { public int x; public int y; }
        [StructLayout(LayoutKind.Sequential)] public struct SIZE { public int cx; public int cy; }
        [StructLayout(LayoutKind.Sequential)] public struct RECT { public int left; public int top; public int right; public int bottom; }
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct BLENDFUNCTION { public byte BlendOp; public byte BlendFlags; public byte SourceConstantAlpha; public byte AlphaFormat; }
        [StructLayout(LayoutKind.Sequential)]
        public struct MSLLHOOKSTRUCT { public POINT pt; public uint mouseData; public uint flags; public uint time; public IntPtr dwExtraInfo; }
        [StructLayout(LayoutKind.Sequential)]
        public struct GUITHREADINFO
        {
            public int cbSize; public int flags; public IntPtr hwndActive; public IntPtr hwndFocus; public IntPtr hwndCapture;
            public IntPtr hwndMenuOwner; public IntPtr hwndMoveSize; public IntPtr hwndCaret; public RECT rcCaret;
        }

        public delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)] public static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);
        [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] public static extern bool UnhookWindowsHookEx(IntPtr hhk);
        [DllImport("user32.dll")] public static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
        [DllImport("kernel32.dll", CharSet = CharSet.Auto)] public static extern IntPtr GetModuleHandle(string lpModuleName);
        [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
        [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] public static extern bool UnregisterHotKey(IntPtr hWnd, int id);
        [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] public static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")] public static extern short GetAsyncKeyState(int vKey);
        [DllImport("user32.dll")] public static extern IntPtr WindowFromPoint(POINT p);
        [DllImport("user32.dll")] public static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);
        [DllImport("user32.dll", CharSet = CharSet.Auto)] public static extern int GetClassName(IntPtr hWnd, StringBuilder text, int count);
        [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] public static extern bool SetForegroundWindow(IntPtr hWnd);
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] public static extern bool IsWindowVisible(IntPtr hWnd);
        [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] public static extern bool GetGUIThreadInfo(uint idThread, ref GUITHREADINFO info);
        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst, ref POINT pptDst, ref SIZE psize, IntPtr hdcSrc, ref POINT pptSrc, int crKey, ref BLENDFUNCTION pblend, int dwFlags);
        [DllImport("user32.dll")] public static extern IntPtr GetDC(IntPtr hWnd);
        [DllImport("user32.dll")] public static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);
        [DllImport("gdi32.dll")] public static extern IntPtr CreateCompatibleDC(IntPtr hdc);
        [DllImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] public static extern bool DeleteDC(IntPtr hdc);
        [DllImport("gdi32.dll")] public static extern IntPtr SelectObject(IntPtr hdc, IntPtr hgdiobj);
        [DllImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] public static extern bool DeleteObject(IntPtr hObject);
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] public static extern bool SetProcessDPIAware();
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern uint RegisterWindowMessage(string lpString);
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] public static extern bool DestroyIcon(IntPtr hIcon);
        [DllImport("user32.dll")] public static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);
        [DllImport("dwmapi.dll")] public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        public static string ClassName(IntPtr h)
        {
            if (h == IntPtr.Zero) return "";
            StringBuilder sb = new StringBuilder(256);
            GetClassName(h, sb, sb.Capacity);
            return sb.ToString();
        }

        public static IntPtr RootAt(int x, int y)
        {
            POINT p = new POINT(); p.x = x; p.y = y;
            IntPtr h = WindowFromPoint(p);
            if (h == IntPtr.Zero) return IntPtr.Zero;
            return GetAncestor(h, 2); // GA_ROOT
        }

        public static bool IsShellClass(string cls)
        {
            return cls == "CabinetWClass" || cls == "ExploreWClass" || cls == "Progman" || cls == "WorkerW";
        }

        // Explorer captured the mouse on right-button-down; since we swallow the matching "up",
        // ask the capturing window to leave its modal mouse state.
        public static void CancelCapture(IntPtr anyWindowOfThread)
        {
            try
            {
                uint pid;
                uint tid = GetWindowThreadProcessId(anyWindowOfThread, out pid);
                GUITHREADINFO gi = new GUITHREADINFO();
                gi.cbSize = Marshal.SizeOf(typeof(GUITHREADINFO));
                if (GetGUIThreadInfo(tid, ref gi) && gi.hwndCapture != IntPtr.Zero)
                    PostMessage(gi.hwndCapture, 0x001F, IntPtr.Zero, IntPtr.Zero); // WM_CANCELMODE
            }
            catch (Exception ex) { Log.Write("CancelCapture: " + ex.Message); }
        }
    }

    // ------------------------------------------------------------------ log
    internal static class Log
    {
        static readonly object gate = new object();
        public static void Write(string text)
        {
            try
            {
                lock (gate)
                {
                    Directory.CreateDirectory(Store.Dir);
                    string p = Path.Combine(Store.Dir, "log.txt");
                    FileInfo fi = new FileInfo(p);
                    if (fi.Exists && fi.Length > 400000) File.Delete(p);
                    File.AppendAllText(p, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + text + Environment.NewLine, Encoding.UTF8);
                }
            }
            catch { }
        }
    }

    // ------------------------------------------------------------------ model
    internal sealed class AppConfig
    {
        public int HoldMs = 450;
        public bool AutoStart = true;
        public bool Smart = true;
        public bool UsageOrder = true;
        public bool ConfirmDanger = true;
        public bool ConfirmMismatch = true;
        public bool Toasts = true;
        public bool Animations = true;
        public string Accent = "system";
        public int TriggerButton = 0;          // 0 middle, 1 side button 1 (back), 2 side button 2 (forward)
        public uint HkOpenMods = 3, HkOpenVk = 0x45;   // Ctrl+Alt+E
        public uint HkUndoMods = 3, HkUndoVk = 0x5A;   // Ctrl+Alt+Z
        public bool AutoSelect = true;         // select the file under the cursor automatically
        public string Lang = "";               // "ar" | "en" ("" = not chosen yet)
        public int ProfilesVersion = 0;
        public Dictionary<string, List<string>> Profiles = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, int> Usage = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        public int UsageOf(string source, string target)
        {
            int v;
            return Usage.TryGetValue(source + ">" + target, out v) ? v : 0;
        }
    }

    internal sealed class HistoryEntry
    {
        public string Id = Guid.NewGuid().ToString("N");
        public DateTime Time = DateTime.Now;
        public List<string[]> Items = new List<string[]>(); // [old, new]
        public bool Undone;
    }

    // ------------------------------------------------------------------ storage
    internal static class Store
    {
        public static readonly string Dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BAK");
        public static AppConfig Config = new AppConfig();
        public static List<HistoryEntry> History = new List<HistoryEntry>();
        public static bool FirstRun;

        static string ConfigPath { get { return Path.Combine(Dir, "config.json"); } }
        static string HistoryPath { get { return Path.Combine(Dir, "history.json"); } }

        static JavaScriptSerializer Ser()
        {
            JavaScriptSerializer s = new JavaScriptSerializer();
            s.MaxJsonLength = int.MaxValue;
            return s;
        }

        static string Str(Dictionary<string, object> d, string k, string def)
        {
            object o; if (d.TryGetValue(k, out o) && o != null) return Convert.ToString(o); return def;
        }
        static bool Bool(Dictionary<string, object> d, string k, bool def)
        {
            object o; if (d.TryGetValue(k, out o) && o is bool) return (bool)o; return def;
        }
        static int Int(Dictionary<string, object> d, string k, int def)
        {
            object o;
            if (d.TryGetValue(k, out o) && o != null) { try { return Convert.ToInt32(o); } catch { } }
            return def;
        }
        static List<string> Strs(object o)
        {
            List<string> r = new List<string>();
            System.Collections.IEnumerable e = o as System.Collections.IEnumerable;
            if (e != null && !(o is string)) foreach (object x in e) if (x != null) r.Add(Convert.ToString(x));
            return r;
        }

        public static void Load()
        {
            try { Directory.CreateDirectory(Dir); } catch { }
            try
            {
                // settings of the previous name of this tool ("Extension Wheel") are carried over once
                string legacy = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ExtensionWheel");
                if (!File.Exists(ConfigPath) && Directory.Exists(legacy))
                    foreach (string n in new string[] { "config.json", "history.json", "extensions.json" })
                    {
                        string src = Path.Combine(legacy, n);
                        if (File.Exists(src)) File.Copy(src, Path.Combine(Dir, n), false);
                    }
            }
            catch (Exception ex) { Log.Write("legacy migrate: " + ex.Message); }
            Config = new AppConfig();
            Config.Profiles = Defaults.Profiles();
            Config.ProfilesVersion = Defaults.Version;
            if (File.Exists(ConfigPath))
            {
                try { ReadConfig(File.ReadAllText(ConfigPath, Encoding.UTF8)); }
                catch (Exception ex)
                {
                    Log.Write("config read failed: " + ex.Message);
                    try { File.Copy(ConfigPath, ConfigPath + ".bad", true); } catch { }
                }
            }
            else
            {
                FirstRun = true;
                string old = Path.Combine(Dir, "extensions.json"); // settings of the old PowerShell prototype
                if (File.Exists(old)) { try { MigrateOld(old); } catch (Exception ex) { Log.Write("migrate failed: " + ex.Message); } }
            }
            bool dirty = FirstRun;
            if (Config.ProfilesVersion < Defaults.Version)
            {
                Defaults.Merge(Config.Profiles);   // new built-in extensions are added once; the user's own choices stay first
                Config.ProfilesVersion = Defaults.Version;
                dirty = true;
            }
            if (Config.Lang != "ar" && Config.Lang != "en") { Config.Lang = Loc.Detect(); dirty = true; }
            Loc.Lang = Config.Lang;
            if (dirty) Save();
            LoadHistory();
        }

        static void ReadConfig(string text)
        {
            Dictionary<string, object> root = Ser().DeserializeObject(text) as Dictionary<string, object>;
            if (root == null) return;
            AppConfig c = Config;
            c.HoldMs = Math.Max(200, Math.Min(1500, Int(root, "holdMs", c.HoldMs)));
            c.AutoStart = Bool(root, "autoStart", c.AutoStart);
            c.Smart = Bool(root, "smart", c.Smart);
            c.UsageOrder = Bool(root, "usageOrder", c.UsageOrder);
            c.ConfirmDanger = Bool(root, "confirmDanger", c.ConfirmDanger);
            c.ConfirmMismatch = Bool(root, "confirmMismatch", c.ConfirmMismatch);
            c.Toasts = Bool(root, "toasts", c.Toasts);
            c.Animations = Bool(root, "animations", c.Animations);
            c.Accent = Str(root, "accent", c.Accent);
            c.TriggerButton = Math.Max(0, Math.Min(2, Int(root, "triggerButton", c.TriggerButton)));
            c.AutoSelect = Bool(root, "autoSelect", c.AutoSelect);
            c.Lang = Str(root, "lang", c.Lang);
            c.ProfilesVersion = Int(root, "profilesVersion", 0);
            uint om = (uint)Int(root, "hkOpenMods", (int)c.HkOpenMods), ov = (uint)Int(root, "hkOpenVk", (int)c.HkOpenVk);
            if (Hotkeys.Validate(om, ov) == null) { c.HkOpenMods = om; c.HkOpenVk = ov; }
            uint um = (uint)Int(root, "hkUndoMods", (int)c.HkUndoMods), uv = (uint)Int(root, "hkUndoVk", (int)c.HkUndoVk);
            if (Hotkeys.Validate(um, uv) == null && !(um == c.HkOpenMods && uv == c.HkOpenVk)) { c.HkUndoMods = um; c.HkUndoVk = uv; }

            object po;
            if (root.TryGetValue("profiles", out po))
            {
                Dictionary<string, object> dict = po as Dictionary<string, object>;
                if (dict != null)
                {
                    Dictionary<string, List<string>> np = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
                    foreach (KeyValuePair<string, object> kv in dict)
                    {
                        string key;
                        if (!Ext.TryNormSource(kv.Key, out key)) continue;
                        List<string> list = new List<string>();
                        foreach (string t in Strs(kv.Value))
                        {
                            string nt;
                            if (Ext.TryNormTarget(t, out nt) && nt != key && !list.Contains(nt)) list.Add(nt);
                        }
                        np[key] = list;
                    }
                    c.Profiles = np;
                }
            }
            object uo;
            if (root.TryGetValue("usage", out uo))
            {
                Dictionary<string, object> ud = uo as Dictionary<string, object>;
                if (ud != null)
                    foreach (KeyValuePair<string, object> kv in ud)
                    {
                        try { c.Usage[kv.Key] = Convert.ToInt32(kv.Value); } catch { }
                    }
            }
        }

        static void MigrateOld(string path)
        {
            Dictionary<string, object> root = Ser().DeserializeObject(File.ReadAllText(path, Encoding.UTF8)) as Dictionary<string, object>;
            if (root == null) return;
            object po;
            if (!root.TryGetValue("profiles", out po)) return;
            System.Collections.IEnumerable arr = po as System.Collections.IEnumerable;
            if (arr == null) return;
            Dictionary<string, List<string>> np = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (object item in arr)
            {
                Dictionary<string, object> d = item as Dictionary<string, object>;
                if (d == null) continue;
                string key;
                if (!Ext.TryNormSource(Str(d, "source", ""), out key)) continue;
                List<string> list = new List<string>();
                object to;
                if (d.TryGetValue("targets", out to))
                    foreach (string t in Strs(to))
                    {
                        string nt;
                        if (Ext.TryNormTarget(t, out nt) && nt != key && !list.Contains(nt)) list.Add(nt);
                    }
                np[key] = list;
            }
            if (np.Count > 0) Config.Profiles = np;
        }

        public static void Save()
        {
            try
            {
                Directory.CreateDirectory(Dir);
                AppConfig c = Config;
                Dictionary<string, object> root = new Dictionary<string, object>();
                root["version"] = 3;
                root["holdMs"] = c.HoldMs;
                root["autoStart"] = c.AutoStart;
                root["smart"] = c.Smart;
                root["usageOrder"] = c.UsageOrder;
                root["confirmDanger"] = c.ConfirmDanger;
                root["confirmMismatch"] = c.ConfirmMismatch;
                root["toasts"] = c.Toasts;
                root["animations"] = c.Animations;
                root["accent"] = c.Accent;
                root["triggerButton"] = c.TriggerButton;
                root["autoSelect"] = c.AutoSelect;
                root["lang"] = c.Lang;
                root["profilesVersion"] = c.ProfilesVersion;
                root["hkOpenMods"] = (int)c.HkOpenMods;
                root["hkOpenVk"] = (int)c.HkOpenVk;
                root["hkUndoMods"] = (int)c.HkUndoMods;
                root["hkUndoVk"] = (int)c.HkUndoVk;
                Dictionary<string, object> prof = new Dictionary<string, object>();
                foreach (KeyValuePair<string, List<string>> kv in c.Profiles) prof[kv.Key] = kv.Value.ToArray();
                root["profiles"] = prof;
                Dictionary<string, object> use = new Dictionary<string, object>();
                foreach (KeyValuePair<string, int> kv in c.Usage) use[kv.Key] = kv.Value;
                root["usage"] = use;
                AtomicWrite(ConfigPath, Ser().Serialize(root));
            }
            catch (Exception ex) { Log.Write("config save failed: " + ex.Message); }
        }

        static void AtomicWrite(string path, string text)
        {
            string tmp = path + ".tmp";
            File.WriteAllText(tmp, text, new UTF8Encoding(false));
            if (File.Exists(path)) File.Replace(tmp, path, null); else File.Move(tmp, path);
        }

        static void LoadHistory()
        {
            History = new List<HistoryEntry>();
            try
            {
                if (!File.Exists(HistoryPath)) return;
                Dictionary<string, object> root = Ser().DeserializeObject(File.ReadAllText(HistoryPath, Encoding.UTF8)) as Dictionary<string, object>;
                if (root == null) return;
                object eo;
                if (!root.TryGetValue("entries", out eo)) return;
                System.Collections.IEnumerable arr = eo as System.Collections.IEnumerable;
                if (arr == null) return;
                foreach (object item in arr)
                {
                    Dictionary<string, object> d = item as Dictionary<string, object>;
                    if (d == null) continue;
                    HistoryEntry e = new HistoryEntry();
                    e.Id = Str(d, "id", e.Id);
                    try { e.Time = new DateTime(Convert.ToInt64(d["time"]), DateTimeKind.Utc).ToLocalTime(); } catch { }
                    e.Undone = Bool(d, "undone", false);
                    object io;
                    if (d.TryGetValue("items", out io))
                    {
                        System.Collections.IEnumerable pairs = io as System.Collections.IEnumerable;
                        if (pairs != null)
                            foreach (object pr in pairs)
                            {
                                List<string> two = Strs(pr);
                                if (two.Count == 2) e.Items.Add(new string[] { two[0], two[1] });
                            }
                    }
                    if (e.Items.Count > 0) History.Add(e);
                }
            }
            catch (Exception ex) { Log.Write("history read failed: " + ex.Message); }
        }

        public static void SaveHistory()
        {
            try
            {
                Directory.CreateDirectory(Dir);
                List<object> list = new List<object>();
                foreach (HistoryEntry e in History)
                {
                    Dictionary<string, object> d = new Dictionary<string, object>();
                    d["id"] = e.Id;
                    d["time"] = e.Time.ToUniversalTime().Ticks;
                    d["undone"] = e.Undone;
                    List<object> items = new List<object>();
                    foreach (string[] it in e.Items) items.Add(new string[] { it[0], it[1] });
                    d["items"] = items;
                    list.Add(d);
                }
                Dictionary<string, object> root = new Dictionary<string, object>();
                root["version"] = 1;
                root["entries"] = list;
                AtomicWrite(HistoryPath, Ser().Serialize(root));
            }
            catch (Exception ex) { Log.Write("history save failed: " + ex.Message); }
        }

        public static void AddHistory(HistoryEntry e)
        {
            History.Add(e);
            while (History.Count > 300) History.RemoveAt(0);
            SaveHistory();
        }
    }

    // ------------------------------------------------------------------ extensions
    internal static class Ext
    {
        public const string None = "__NONE__";
        static readonly Regex Valid = new Regex(@"^\.[A-Za-z0-9][A-Za-z0-9_+\-]{0,15}(\.[A-Za-z0-9][A-Za-z0-9_+\-]{0,15})?$");
        static readonly string[] Danger = { ".exe", ".bat", ".cmd", ".com", ".scr", ".ps1", ".vbs", ".vbe", ".jse", ".wsf", ".wsh", ".msi", ".msp", ".hta", ".jar", ".lnk", ".reg", ".cpl", ".pif" };

        public static bool TryNormTarget(string s, out string result)
        {
            result = null;
            s = (s ?? "").Trim();
            if (s.Length == 0) return false;
            if (!s.StartsWith(".")) s = "." + s;
            if (!Valid.IsMatch(s)) return false;
            result = s.ToLowerInvariant();
            return true;
        }

        public static bool TryNormSource(string s, out string result)
        {
            result = null;
            s = (s ?? "").Trim();
            string flat = s.Replace("(", "").Replace(")", "").Trim();
            if (flat.Length == 0 || flat == "\u0628\u062F\u0648\u0646 \u0627\u0645\u062A\u062F\u0627\u062F" || flat.ToLowerInvariant() == "none" || flat.ToLowerInvariant() == "no extension" || flat.ToLowerInvariant() == "noext" || s == None) { result = None; return true; }
            return TryNormTarget(s, out result);
        }

        // "photo.jpg" -> base "photo", ext ".jpg". Names such as ".gitignore" or "README" have no extension.
        public static void Split(string fileName, out string baseName, out string ext)
        {
            int i = fileName.LastIndexOf('.');
            if (i <= 0 || i == fileName.Length - 1) { baseName = fileName; ext = ""; }
            else { baseName = fileName.Substring(0, i); ext = fileName.Substring(i); }
        }

        public static string Of(string path)
        {
            string b, e; Split(Path.GetFileName(path), out b, out e);
            return e.Length == 0 ? None : e.ToLowerInvariant();
        }

        public static string Label(string key) { return key == None ? Loc.T("noext") : key; }

        public static bool IsDangerous(string ext) { return ext != null && Danger.Contains(ext.ToLowerInvariant()); }

        // "report.docx.bak" -> "report.docx"; returns null when there is nothing to restore.
        public static string RestorePath(string path)
        {
            string name = Path.GetFileName(path);
            if (!name.EndsWith(".bak", StringComparison.OrdinalIgnoreCase)) return null;
            string stripped = name.Substring(0, name.Length - 4);
            if (stripped.Length == 0 || stripped.EndsWith(".")) return null;
            return Path.Combine(Path.GetDirectoryName(path), stripped);
        }

        // "report.docx" -> "report.docx.bak", or "report (2).docx.bak" when that already exists.
        public static string BackupPath(string path)
        {
            string dir = Path.GetDirectoryName(path);
            string b, e;
            Split(Path.GetFileName(path), out b, out e);
            string cand = path + ".bak";
            for (int i = 2; File.Exists(cand) || Directory.Exists(cand); i++)
                cand = Path.Combine(dir, b + " (" + i + ")" + e + ".bak");
            return cand;
        }

        public static string NewPath(string path, string newExt)
        {
            string b, e; Split(Path.GetFileName(path), out b, out e);
            return Path.Combine(Path.GetDirectoryName(path), b + newExt);
        }
    }

    internal static class Defaults
    {
        // Bump when the built-in list grows: existing users get the new entries merged in once.
        public const int Version = 3;

        // Interchangeable names of the same format (a rename here is always safe).
        static readonly string[][] Aliases = new string[][]
        {
            new string[] { ".jpg", ".jpeg", ".jpe", ".jfif" },
            new string[] { ".htm", ".html" },
            new string[] { ".tif", ".tiff" },
            new string[] { ".mpg", ".mpeg" },
            new string[] { ".yml", ".yaml" },
            new string[] { ".mid", ".midi" },
            new string[] { ".aif", ".aiff" },
            new string[] { ".md", ".markdown" },
            new string[] { ".txt", ".text" },
            new string[] { ".mp4", ".m4v" }
        };

        // What people most often turn a plain text file into (and back): scripts, data and config files.
        static readonly string[] Pool = { ".txt", ".py", ".json", ".md", ".csv", ".html", ".js", ".log", ".bat", ".xml", ".ini", ".yml", ".css", ".sql", ".ps1", ".sh", ".cfg" };
        static readonly string[] MoreTextSources = { ".htm", ".yaml", ".markdown", ".text", ".tsv", ".conf", ".toml", ".ts", ".java", ".cs", ".php" };

        // Office / package formats are ZIP files: renaming to .zip is the classic way to look inside.
        static readonly string[] ZipFormats = { ".docx", ".xlsx", ".pptx", ".apk", ".jar", ".epub", ".odt" };

        static void Add(Dictionary<string, List<string>> r, string source, string target)
        {
            if (source == target) return;
            List<string> list;
            if (!r.TryGetValue(source, out list)) { list = new List<string>(); r[source] = list; }
            if (!list.Contains(target)) list.Add(target);
        }

        static void MoveToEnd(Dictionary<string, List<string>> r, string source, string target)
        {
            List<string> list;
            if (r.TryGetValue(source, out list) && list.Remove(target)) list.Add(target);
        }

        public static Dictionary<string, List<string>> Profiles()
        {
            Dictionary<string, List<string>> r = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

            foreach (string[] g in Aliases)
                foreach (string src in g)
                    foreach (string t in g) Add(r, src, t);

            List<string> textSources = new List<string>(Pool);
            textSources.AddRange(MoreTextSources);
            foreach (string src in textSources)
                foreach (string t in Pool) Add(r, src, t);
            MoveToEnd(r, ".txt", ".text");
            MoveToEnd(r, ".md", ".markdown");
            MoveToEnd(r, ".yml", ".yaml");
            MoveToEnd(r, ".html", ".htm");

            foreach (string fmt in ZipFormats)
            {
                Add(r, ".zip", fmt);
                Add(r, fmt, ".zip");
            }

            Add(r, ".svg", ".xml");
            Add(r, ".svg", ".html");
            Add(r, ".xml", ".svg");

            foreach (string t in new string[] { ".txt", ".py", ".json", ".md", ".html", ".bat", ".sh", ".csv", ".js", ".xml" })
                Add(r, Ext.None, t);

            return r;
        }

        // Adds every built-in option the list does not have yet (the user's own order and choices are kept).
        public static void Merge(Dictionary<string, List<string>> target)
        {
            foreach (KeyValuePair<string, List<string>> kv in Profiles())
            {
                List<string> list;
                if (!target.TryGetValue(kv.Key, out list)) { target[kv.Key] = kv.Value; continue; }
                foreach (string t in kv.Value) if (!list.Contains(t)) list.Add(t);
            }
        }
    }

    // ------------------------------------------------------------------ file type sniffing
    internal sealed class Detected
    {
        public string Group;
        public string Name;
        public string Ext; // canonical extension, or null when the type is ambiguous (never suggested)
    }

    internal static class FileKind
    {
        static readonly Dictionary<string, string> extGroup = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        static readonly string[] TextLike = { "text", "html", "xml", "json", "svg", "rtf" };
        static readonly string[] ZipLike = { "zip", "docx", "xlsx", "pptx", "odf", "epub", "jar", "apk" };

        static FileKind()
        {
            string[] table =
            {
                "jpeg:.jpg,.jpeg,.jpe,.jfif", "png:.png,.apng", "gif:.gif", "bmp:.bmp,.dib", "webp:.webp", "tiff:.tif,.tiff", "ico:.ico",
                "svg:.svg", "pdf:.pdf", "zip:.zip", "docx:.docx,.docm,.dotx,.dotm", "xlsx:.xlsx,.xlsm,.xltx,.xltm",
                "pptx:.pptx,.pptm,.potx,.ppsx", "odf:.odt,.ods,.odp,.odg", "epub:.epub", "jar:.jar,.war", "apk:.apk",
                "rar:.rar", "7z:.7z", "gz:.gz,.tgz", "bz2:.bz2,.tbz2", "xz:.xz,.txz", "mp3:.mp3", "ogg:.ogg,.oga,.ogv,.opus,.spx",
                "flac:.flac", "wav:.wav", "avi:.avi", "isom:.mp4,.m4v,.mov,.m4a,.3gp,.3g2,.f4v,.m4b", "mkv:.mkv,.webm,.mka",
                "exe:.exe,.dll,.sys,.scr,.ocx,.cpl,.efi", "sqlite:.db,.sqlite,.sqlite3,.db3", "rtf:.rtf",
                "ole:.doc,.xls,.ppt,.msi,.msg,.pub,.vsd,.dot,.xlt,.pot", "html:.html,.htm,.xhtml",
                "xml:.xml,.xsl,.xslt,.xaml,.config,.csproj,.resx", "json:.json,.jsonc,.geojson",
                "text:.txt,.text,.log,.md,.markdown,.csv,.tsv,.ini,.cfg,.conf,.yml,.yaml,.toml,.js,.mjs,.ts,.tsx,.jsx,.css,.scss,.py,.ps1,.bat,.cmd,.vbs,.sh,.c,.cpp,.h,.hpp,.cs,.java,.php,.sql,.rb,.go,.rs,.tex,.srt,.vtt,.lrc,.reg,.inf"
            };
            foreach (string row in table)
            {
                int i = row.IndexOf(':');
                string g = row.Substring(0, i);
                foreach (string e in row.Substring(i + 1).Split(',')) extGroup[e] = g;
            }
        }

        public static string GroupOf(string ext)
        {
            string g;
            if (ext != null && extGroup.TryGetValue(ext, out g)) return g;
            return null;
        }

        static bool IsTextLike(string g) { return g != null && TextLike.Contains(g); }

        // Is the content type 'd' plausible for a file whose extension belongs to group 'cur'?
        public static bool Compat(Detected d, string cur)
        {
            if (d == null || cur == null) return true;
            if (d.Group == cur) return true;
            if (d.Group == "zip" && ZipLike.Contains(cur)) return true;
            if (cur == "zip" && ZipLike.Contains(d.Group)) return true;
            if (IsTextLike(d.Group) && IsTextLike(cur)) return true;
            return false;
        }

        public static bool CompatTarget(Detected d, string targetExt) { return Compat(d, GroupOf(targetExt)); }

        static Detected D(string group, string name, string ext)
        {
            Detected d = new Detected(); d.Group = group; d.Name = name; d.Ext = ext; return d;
        }

        static bool Ascii(byte[] b, int n, int off, string s)
        {
            if (off + s.Length > n) return false;
            for (int i = 0; i < s.Length; i++) if (b[off + i] != (byte)s[i]) return false;
            return true;
        }

        static bool Has(byte[] b, int n, byte b0, byte b1, byte b2, byte b3)
        {
            return n >= 4 && b[0] == b0 && b[1] == b1 && b[2] == b2 && b[3] == b3;
        }

        public static Detected Detect(string path)
        {
            try
            {
                FileInfo fi = new FileInfo(path);
                if (!fi.Exists || fi.Length < 4) return null;
                byte[] b = new byte[4096];
                int n;
                using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                    n = fs.Read(b, 0, b.Length);
                if (n < 4) return null;

                if (n >= 8 && b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47) return D("png", "PNG", ".png");
                if (b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF) return D("jpeg", "JPEG", ".jpg");
                if (Ascii(b, n, 0, "GIF8")) return D("gif", "GIF", ".gif");
                if (Ascii(b, n, 0, "RIFF") && n >= 12)
                {
                    if (Ascii(b, n, 8, "WEBP")) return D("webp", "WebP", ".webp");
                    if (Ascii(b, n, 8, "WAVE")) return D("wav", "WAV", ".wav");
                    if (Ascii(b, n, 8, "AVI ")) return D("avi", "AVI", ".avi");
                    return null;
                }
                if (b[0] == (byte)'B' && b[1] == (byte)'M' && n >= 14 && b[6] == 0 && b[7] == 0 && b[8] == 0 && b[9] == 0) return D("bmp", "BMP", ".bmp");
                if (Has(b, n, 0x49, 0x49, 0x2A, 0x00) || Has(b, n, 0x4D, 0x4D, 0x00, 0x2A)) return D("tiff", "TIFF", ".tif");
                if (Has(b, n, 0, 0, 1, 0) && n >= 6 && b[4] >= 1 && b[4] <= 64 && b[5] == 0) return D("ico", "ICO", ".ico");

                int pdfAt = IndexOf(b, n, "%PDF-");
                if (pdfAt >= 0 && pdfAt < 1024) return D("pdf", "PDF", ".pdf");

                if (Has(b, n, 0x50, 0x4B, 0x03, 0x04)) return ZipKind(path);
                if (Ascii(b, n, 0, "Rar!")) return D("rar", "RAR", ".rar");
                if (n >= 6 && b[0] == 0x37 && b[1] == 0x7A && b[2] == 0xBC && b[3] == 0xAF && b[4] == 0x27 && b[5] == 0x1C) return D("7z", "7-Zip", ".7z");
                if (b[0] == 0x1F && b[1] == 0x8B) return D("gz", "GZIP", ".gz");
                if (Ascii(b, n, 0, "BZh")) return D("bz2", "BZIP2", ".bz2");
                if (n >= 6 && b[0] == 0xFD && b[1] == 0x37 && b[2] == 0x7A && b[3] == 0x58 && b[4] == 0x5A && b[5] == 0x00) return D("xz", "XZ", ".xz");
                if (Has(b, n, 0xD0, 0xCF, 0x11, 0xE0)) return D("ole", "Office (OLE)", null);
                if (Ascii(b, n, 0, "SQLite format 3")) return D("sqlite", "SQLite", ".db");
                if (Ascii(b, n, 0, "{\\rtf")) return D("rtf", "RTF", ".rtf");
                if (Ascii(b, n, 0, "OggS")) return D("ogg", "OGG", ".ogg");
                if (Ascii(b, n, 0, "fLaC")) return D("flac", "FLAC", ".flac");
                if (Has(b, n, 0x1A, 0x45, 0xDF, 0xA3)) return D("mkv", "MKV/WebM", null);
                if (n >= 12 && Ascii(b, n, 4, "ftyp")) return D("isom", "MP4/MOV", null);

                bool utf16 = (b[0] == 0xFF && b[1] == 0xFE) || (b[0] == 0xFE && b[1] == 0xFF);
                if (!utf16)
                {
                    if (Ascii(b, n, 0, "ID3")) return D("mp3", "MP3", ".mp3");
                    if (b[0] == 0xFF && (b[1] & 0xE0) == 0xE0 && ((b[1] >> 3) & 3) != 1 && (b[1] & 6) == 2) return D("mp3", "MP3", ".mp3");
                    if (b[0] == (byte)'M' && b[1] == (byte)'Z') return D("exe", "Executable / DLL", null);
                }
                return TextKind(path, b, n, utf16);
            }
            catch (Exception ex)
            {
                Log.Write("Detect " + path + ": " + ex.Message);
                return null;
            }
        }

        static int IndexOf(byte[] b, int n, string s)
        {
            for (int i = 0; i + s.Length <= n; i++)
            {
                bool ok = true;
                for (int j = 0; j < s.Length; j++) if (b[i + j] != (byte)s[j]) { ok = false; break; }
                if (ok) return i;
            }
            return -1;
        }

        static Detected ZipKind(string path)
        {
            try
            {
                using (System.IO.Compression.ZipArchive z = System.IO.Compression.ZipFile.OpenRead(path))
                {
                    HashSet<string> names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (System.IO.Compression.ZipArchiveEntry e in z.Entries) names.Add(e.FullName);
                    if (names.Contains("word/document.xml")) return D("docx", "Word (DOCX)", ".docx");
                    if (names.Contains("xl/workbook.xml")) return D("xlsx", "Excel (XLSX)", ".xlsx");
                    if (names.Contains("ppt/presentation.xml")) return D("pptx", "PowerPoint (PPTX)", ".pptx");
                    if (names.Contains("AndroidManifest.xml") && names.Contains("classes.dex")) return D("apk", "Android APK", ".apk");
                    if (names.Contains("mimetype"))
                    {
                        System.IO.Compression.ZipArchiveEntry me = z.GetEntry("mimetype");
                        if (me != null)
                            using (StreamReader sr = new StreamReader(me.Open(), Encoding.ASCII))
                            {
                                string mt = sr.ReadToEnd().Trim();
                                if (mt == "application/epub+zip") return D("epub", "EPUB", ".epub");
                                if (mt.Contains("opendocument.text")) return D("odf", "OpenDocument Text", ".odt");
                                if (mt.Contains("opendocument.spreadsheet")) return D("odf", "OpenDocument Sheet", ".ods");
                                if (mt.Contains("opendocument.presentation")) return D("odf", "OpenDocument Slides", ".odp");
                            }
                    }
                    if (names.Contains("META-INF/MANIFEST.MF")) return D("jar", "Java JAR", ".jar");
                }
            }
            catch { }
            return D("zip", "ZIP", ".zip");
        }

        static Detected TextKind(string path, byte[] b, int n, bool utf16)
        {
            int start = 0;
            if (n >= 3 && b[0] == 0xEF && b[1] == 0xBB && b[2] == 0xBF) start = 3;
            if (!utf16)
            {
                int bad = 0;
                for (int i = start; i < n; i++)
                {
                    byte c = b[i];
                    if (c == 0) return null; // binary of unknown kind
                    if (c < 9 || (c > 13 && c < 32 && c != 27)) bad++;
                }
                if (bad * 50 > n) return null;
            }
            if (utf16) return D("text", "Text", null);
            string head = Encoding.UTF8.GetString(b, start, Math.Min(n - start, 1024)).TrimStart().ToLowerInvariant();
            if (head.StartsWith("<?xml")) return head.Contains("<svg") ? D("svg", "SVG", ".svg") : D("xml", "XML", ".xml");
            if (head.StartsWith("<!doctype html") || head.StartsWith("<html") || head.Contains("<html")) return D("html", "HTML", ".html");
            if (head.StartsWith("<svg")) return D("svg", "SVG", ".svg");
            if (head.StartsWith("{") || head.StartsWith("["))
            {
                try
                {
                    if (new FileInfo(path).Length <= 2 * 1024 * 1024)
                    {
                        JavaScriptSerializer s = new JavaScriptSerializer();
                        s.MaxJsonLength = int.MaxValue;
                        s.DeserializeObject(File.ReadAllText(path));
                        return D("json", "JSON", ".json");
                    }
                }
                catch { }
            }
            return D("text", "Text", null);
        }
    }

    // ------------------------------------------------------------------ rename operations
    internal sealed class RenameResult
    {
        public List<string[]> Done = new List<string[]>();
        public List<string> Errors = new List<string>();
        public HistoryEntry Entry;
    }

    internal static class AppInfo
    {
        public const string Name = ".BAK";
        public const string Version = "1.1.8";
        public const string Author = "MAHMOUD_ALBAKKAR";
        public const string GitHubProfile = "https://github.com/0mahmoudalbakkar0";
        public const string RepoUrl = "https://github.com/0mahmoudalbakkar0/dot-bak";
        public const string TelegramUrl = "https://t.me/+NonxC28MrMBjYjlk";
    }

    internal static class Ops
    {
        public static RenameResult Apply(IList<string> files, IList<string> newExts)
        {
            RenameResult r = new RenameResult();
            AppConfig cfg = Store.Config;
            for (int i = 0; i < files.Count; i++)
            {
                string old = files[i];
                string ext = newExts[i];
                if (string.IsNullOrEmpty(ext)) continue;
                try
                {
                    if (!File.Exists(old)) throw new FileNotFoundException(Loc.T("err.notfound"));
                    string np = Ext.NewPath(old, ext);
                    if (string.Equals(old, np, StringComparison.Ordinal)) continue;
                    bool caseOnly = string.Equals(old, np, StringComparison.OrdinalIgnoreCase);
                    if (!caseOnly && (File.Exists(np) || Directory.Exists(np)))
                        throw new IOException(Loc.F("err.exists", Path.GetFileName(np)));
                    File.Move(old, np);
                    r.Done.Add(new string[] { old, np });
                    string key = Ext.Of(old) + ">" + ext;
                    int u; cfg.Usage.TryGetValue(key, out u); cfg.Usage[key] = u + 1;
                }
                catch (Exception ex) { r.Errors.Add(Path.GetFileName(old) + " — " + ex.Message); }
            }
            if (r.Done.Count > 0)
            {
                HistoryEntry e = new HistoryEntry();
                e.Items.AddRange(r.Done);
                r.Entry = e;
                Store.AddHistory(e);
                Store.Save();
            }
            return r;
        }

        // report.docx.bak -> report.docx (recorded in the history, so it can be undone).
        public static RenameResult Restore(IList<string> files)
        {
            RenameResult r = new RenameResult();
            foreach (string old in files)
            {
                try
                {
                    if (!File.Exists(old)) throw new FileNotFoundException(Loc.T("err.notfound"));
                    string np = Ext.RestorePath(old);
                    if (np == null) throw new IOException(Loc.T("err.noorig"));
                    if (File.Exists(np) || Directory.Exists(np))
                        throw new IOException(Loc.F("err.exists_orig", Path.GetFileName(np)));
                    File.Move(old, np);
                    r.Done.Add(new string[] { old, np });
                }
                catch (Exception ex) { r.Errors.Add(Path.GetFileName(old) + " \u2014 " + ex.Message); }
            }
            if (r.Done.Count > 0)
            {
                HistoryEntry e = new HistoryEntry();
                e.Items.AddRange(r.Done);
                r.Entry = e;
                Store.AddHistory(e);
            }
            return r;
        }

        // Copies each file to "<name>.<ext>.bak". The original is never touched.
        public static RenameResult Backup(IList<string> files)
        {
            RenameResult r = new RenameResult();
            foreach (string old in files)
            {
                try
                {
                    if (!File.Exists(old)) throw new FileNotFoundException(Loc.T("err.notfound"));
                    string np = Ext.BackupPath(old);
                    File.Copy(old, np, false);
                    r.Done.Add(new string[] { old, np });
                }
                catch (Exception ex) { r.Errors.Add(Path.GetFileName(old) + " \u2014 " + ex.Message); }
            }
            return r;
        }

        // Reverts an entry (all of it, or whatever can still be reverted). Returns the number of restored files.
        public static int Undo(HistoryEntry e, out string error)
        {
            error = null;
            int restored = 0;
            List<string[]> left = new List<string[]>();
            for (int i = e.Items.Count - 1; i >= 0; i--)
            {
                string old = e.Items[i][0], now = e.Items[i][1];
                try
                {
                    bool caseOnly = string.Equals(old, now, StringComparison.OrdinalIgnoreCase);
                    if (!File.Exists(now)) throw new FileNotFoundException(Loc.T("err.cantfind"));
                    if (!caseOnly && (File.Exists(old) || Directory.Exists(old))) throw new IOException(Loc.T("err.orig_used"));
                    File.Move(now, old);
                    restored++;
                }
                catch (Exception ex) { left.Add(e.Items[i]); if (error == null) error = ex.Message; }
            }
            left.Reverse();
            e.Items = left;
            if (left.Count == 0) e.Undone = true;
            Store.SaveHistory();
            return restored;
        }
    }
}
