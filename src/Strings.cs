// .BAK - localisation (Arabic / English)
using System;
using System.Collections.Generic;
using System.Globalization;

namespace BakApp
{
    internal static class Loc
    {
        static readonly Dictionary<string, string> ar = new Dictionary<string, string>();
        static readonly Dictionary<string, string> en = new Dictionary<string, string>();
        static string lang = "en";

        public static string Lang
        {
            get { return lang; }
            set { lang = value == "ar" ? "ar" : "en"; }
        }

        public static bool Rtl { get { return lang == "ar"; } }

        // First run: Arabic Windows gets Arabic, everything else gets English.
        public static string Detect()
        {
            try
            {
                if (CultureInfo.InstalledUICulture.TwoLetterISOLanguageName == "ar") return "ar";
                if (CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ar") return "ar";
            }
            catch { }
            return "en";
        }

        public static string T(string key)
        {
            string v;
            Dictionary<string, string> d = lang == "ar" ? ar : en;
            if (d.TryGetValue(key, out v)) return v;
            if (en.TryGetValue(key, out v)) return v;
            return key;
        }

        public static string F(string key, params object[] args)
        {
            try { return string.Format(T(key), args); }
            catch (FormatException) { return T(key); }
        }

        static void Add(string key, string a, string e) { ar[key] = a; en[key] = e; }

        static Loc()
        {
            Add("app.subtitle", "حوّل الامتداد بلمسة", "Change extensions in a flick");
            Add("settings.title", "الإعدادات", "Settings");
            Add("nav.ext", "أنواع الملفات", "File types");
            Add("nav.general", "عام", "General");
            Add("nav.controls", "التحكم", "Controls");
            Add("nav.look", "المظهر واللغة", "Look & language");
            Add("nav.history", "السجل", "History");
            Add("nav.about", "حول", "About");
            Add("btn.ok", "موافق", "OK");
            Add("btn.cancel", "إلغاء", "Cancel");
            Add("btn.add", "إضافة", "Add");
            Add("btn.delete", "حذف", "Delete");
            Add("btn.change", "تغيير", "Change");
            Add("btn.default", "الافتراضي", "Default");
            Add("btn.continue", "متابعة", "Continue");
            Add("noext", "بدون امتداد", "No extension");
            Add("ext.title", "أنواع الملفات والخيارات", "File types & options");
            Add("ext.desc", "اختر نوع الملف، ثم رتّب الامتدادات التي تظهر لك في العجلة.", "Pick a file type, then arrange the extensions shown on the wheel.");
            Add("ext.src", "الامتداد الحالي", "Current extension");
            Add("ext.src.add", "+ نوع جديد", "+ New type");
            Add("ext.pick", "اختر نوعًا من القائمة", "Pick a type from the list");
            Add("ext.tgt.header", "يتحوّل {0} إلى", "{0} can become");
            Add("ext.tgt.remove", "إزالة المحدد", "Remove");
            Add("ext.reset", "استعادة القائمة الافتراضية", "Restore defaults");
            Add("ext.reset.title", "استعادة الافتراضي", "Restore defaults");
            Add("ext.reset.msg", "سيتم استبدال كل أنواع الملفات والخيارات الحالية بالقائمة الافتراضية الشائعة. هل تريد المتابعة؟", "All current file types and options will be replaced by the common defaults. Continue?");
            Add("ext.reset.yes", "استعادة", "Restore");
            Add("ext.warn", "تغيير الامتداد يغيّر الاسم فقط ولا يحوّل محتوى الملف.", "Changing an extension only renames the file; it does not convert its content.");
            Add("ext.note", "الأرقام بجانب الامتدادات تدل على عدد مرات استخدامها، وتُرتَّب بها العجلة تلقائيًا.", "Numbers next to extensions show how often you used them; the wheel is ordered by them.");
            Add("src.new.title", "نوع ملف جديد", "New file type");
            Add("src.new.msg", "اكتب الامتداد الذي تريد إضافته (مثل .md) أو اكتب «بدون امتداد».", "Type the extension to add (like .md) or \"no extension\".");
            Add("src.del.title", "حذف النوع", "Delete type");
            Add("src.del.msg", "سيتم حذف النوع {0} وكل خياراته. هل أنت متأكد؟", "Type {0} and all of its options will be deleted. Are you sure?");
            Add("err.badext", "اكتب امتدادًا صالحًا مثل .png", "Enter a valid extension like .png");
            Add("err.pickfirst", "اختر نوعًا من القائمة أولًا", "Pick a type from the list first");
            Add("err.same", "هذا هو الامتداد الحالي نفسه", "That is the current extension");
            Add("err.dup", "الامتداد موجود بالفعل في القائمة", "Already in the list");
            Add("general.title", "عام", "General");
            Add("g.startup", "التشغيل مع ويندوز", "Start with Windows");
            Add("g.startup.sub", "تعمل الأداة بصمت في الخلفية بعد تسجيل الدخول.", "Runs quietly in the background after sign-in.");
            Add("g.smart", "الفحص الذكي للمحتوى", "Smart content check");
            Add("g.smart.sub", "يقرأ بداية الملف ليعرف نوعه الحقيقي ويقترح الامتداد المناسب.", "Reads the start of the file to detect its real type and suggest the right extension.");
            Add("g.usage", "ترتيب حسب الاستخدام", "Order by usage");
            Add("g.usage.sub", "الامتدادات الأكثر استخدامًا تظهر أولًا في العجلة.", "The most-used extensions come first on the wheel.");
            Add("g.danger", "تحذير الامتدادات الخطرة", "Warn about risky extensions");
            Add("g.danger.sub", "يسألك قبل تحويل ملف إلى exe أو bat أو غيرها.", "Asks before turning a file into exe, bat and similar.");
            Add("g.mismatch", "تحذير عدم تطابق المحتوى", "Warn on content mismatch");
            Add("g.mismatch.sub", "ينبّهك إذا كان الامتداد الجديد لا يناسب محتوى الملف.", "Alerts you if the new extension does not fit the file content.");
            Add("g.toasts", "إشعارات النتيجة", "Result notifications");
            Add("g.toasts.sub", "تظهر رسالة صغيرة بعد كل تغيير مع زر تراجع.", "A small message with an Undo button after each change.");
            Add("g.anim", "الحركات", "Animations");
            Add("g.anim.sub", "تأثيرات ظهور العجلة والإشعارات.", "Opening effects for the wheel and notifications.");
            Add("controls.title", "التحكم", "Controls");
            Add("c.trigger", "زر فتح العجلة", "Wheel button");
            Add("c.trigger.sub", "اضغط مطولًا بهذا الزر على ملف لفتح العجلة. الافتراضي: زر عجلة الفأرة.", "Press and hold this button on a file to open the wheel. Default: middle mouse button.");
            Add("trig.mid", "عجلة الفأرة", "Middle button");
            Add("trig.x1", "زر جانبي 1 (للخلف)", "Side button 1 (back)");
            Add("trig.x2", "زر جانبي 2 (للأمام)", "Side button 2 (forward)");
            Add("c.hold", "مدة الضغط", "Hold time");
            Add("c.hold.sub", "كم تضغط قبل فتح العجلة.", "How long to hold before the wheel opens.");
            Add("c.autosel", "تحديد الملف تلقائيًا", "Select the file automatically");
            Add("c.autosel.sub", "مفعّل: يُحدَّد الملف تحت المؤشر عند الضغط. معطّل: حدّد الملف بالزر الأيسر أولًا.", "On: the file under the cursor is selected when you press. Off: select the file with the left button first.");
            Add("hk.open.title", "اختصار فتح العجلة", "Open-wheel shortcut");
            Add("hk.open.sub", "يفتح العجلة للملف المحدد.", "Opens the wheel for the selected file.");
            Add("hk.undo.title", "اختصار التراجع", "Undo shortcut");
            Add("hk.undo.sub", "يتراجع عن آخر تغيير.", "Reverts the last change.");
            Add("hk.dlg.msg", "اضغط الاختصار الجديد على لوحة المفاتيح، ثم اضغط موافق.", "Press the new shortcut on your keyboard, then press OK.");
            Add("hk.err.nomod", "أضف مفتاح Ctrl أو Alt أو Shift مع حرف أو رقم.", "Add Ctrl, Alt or Shift together with a letter or number.");
            Add("hk.err.risky", "استخدم Alt أو Ctrl+Shift حتى لا يتعارض مع اختصارات الكتابة.", "Use Alt or Ctrl+Shift so it does not clash with typing shortcuts.");
            Add("hk.err.reserved", "هذا الاختصار محجوز لويندوز.", "This shortcut is reserved by Windows.");
            Add("hk.err.same", "الاختصار مستخدم لوظيفة أخرى في الأداة.", "That shortcut is already used by another function here.");
            Add("hk.err.inuse", "الاختصار مستخدم في برنامج آخر، جرّب غيره.", "That shortcut is taken by another program. Try another.");
            Add("c.note", "إن تعذّر على الأداة تسجيل اختصار فاختر غيره. التغييرات هنا تُطبَّق فورًا.", "If a shortcut cannot be registered, pick another. Changes here apply immediately.");
            Add("look.title", "المظهر واللغة", "Look & language");
            Add("look.lang", "لغة التطبيق", "App language");
            Add("look.lang.sub", "تتغيّر الواجهة والقوائم فورًا.", "The interface and menus change immediately.");
            Add("look.accent", "لون التمييز", "Accent colour");
            Add("look.accent.sub", "يظهر في العجلة والأزرار.", "Used on the wheel and buttons.");
            Add("look.theme", "الوضع الفاتح/الداكن يتبع إعدادات ويندوز تلقائيًا.", "Light/dark mode follows your Windows settings automatically.");
            Add("hist.title", "السجل", "History");
            Add("hist.desc", "كل تغيير يُسجَّل ويمكنك التراجع عنه.", "Every change is recorded and can be undone.");
            Add("hist.empty", "لا توجد تغييرات بعد", "No changes yet");
            Add("hist.undo", "تراجع عن المحدد", "Undo selected");
            Add("hist.undone", "تم التراجع", "Undone");
            Add("hist.clear", "مسح السجل", "Clear history");
            Add("hist.clear.title", "مسح السجل", "Clear history");
            Add("hist.clear.msg", "سيُمسح السجل ولن تتمكن من التراجع عن التغييرات السابقة.", "The history will be cleared and earlier changes can no longer be undone.");
            Add("hist.clear.yes", "مسح", "Clear");
            Add("about.title", "حول الأداة", "About");
            Add("about.tagline", "غيّر امتداد أي ملف بعجلة سريعة", "Change any file extension with a quick wheel");
            Add("about.author", "المطوّر", "Developer");
            Add("about.report", "الإبلاغ عن مشكلة", "Report a problem");
            Add("about.shortcuts", "طريقة الاستخدام", "How to use");
            Add("about.l1", "١. اضغط مطولًا بـ {0} على أي ملف في المستكشف أو سطح المكتب.", "1. Press and hold {0} on any file in Explorer or on the desktop.");
            Add("about.l2", "٢. أو حدّد الملف واضغط {0}.", "2. Or select the file and press {0}.");
            Add("about.l3", "٣. للتراجع عن آخر تغيير اضغط {0}.", "3. To undo the last change press {0}.");
            Add("about.l4", "٤. في العجلة: اسحب إلى الخيار أو اضغط رقمه، و Esc للإغلاق.", "4. On the wheel: move to an option or press its number; Esc closes.");
            Add("about.warn", "تغيير الامتداد لا يحوّل المحتوى: ملف نصي لن يصبح صورة.", "Changing the extension does not convert content: a text file will not become an image.");
            Add("about.folder", "فتح مجلد البيانات", "Open data folder");
            Add("about.quit", "إنهاء الأداة", "Quit");
            Add("about.copy", "© MAHMOUD_ALBAKKAR — رخصة MIT", "© MAHMOUD_ALBAKKAR — MIT License");
            Add("save.btn", "حفظ الإعدادات", "Save settings");
            Add("save.auto", "التغييرات تُحفظ تلقائيًا أثناء التعديل.", "Changes are saved automatically as you edit.");
            Add("save.done", "تم الحفظ ✓ {0}", "Saved ✓ {0}");
            Add("save.toast.title", "تم حفظ الإعدادات", "Settings saved");
            Add("save.toast.sub", "تم تأكيد كل التعديلات.", "All changes are confirmed.");
            Add("tray.settings", "فتح الإعدادات", "Open settings");
            Add("tray.undo", "التراجع عن آخر تغيير", "Undo last change");
            Add("tray.history", "سجل التغييرات", "Change history");
            Add("tray.pause", "إيقاف الضغط المطوّل مؤقتًا", "Pause press-and-hold");
            Add("tray.resume", "استئناف الضغط المطوّل", "Resume press-and-hold");
            Add("tray.paused", "متوقف", "paused");
            Add("tray.report", "الإبلاغ عن مشكلة", "Report a problem");
            Add("tray.quit", "إنهاء الأداة", "Quit");
            Add("wheel.actual", "المحتوى الفعلي: {0}", "Actual content: {0}");
            Add("wheel.match", "المحتوى مطابق ✓", "Content matches ✓");
            Add("wheel.nfiles", "{0} ملفات", "{0} files");
            Add("wheel.mixed", "أنواع مختلفة", "Mixed types");
            Add("wheel.original", "الأصل", "Original");
            Add("wheel.restore", "استعادة الاسم الأصلي", "Restore original name");
            Add("wheel.suggested", "يطابق المحتوى", "Matches content");
            Add("wheel.backup", "نسخة احتياطية", "Backup copy");
            Add("wheel.custom", "امتداد آخر", "Other extension");
            Add("wheel.manage", "الإعدادات", "Settings");
            Add("custom.title", "امتداد مخصص", "Custom extension");
            Add("custom.msg", "اكتب الامتداد الجديد (مثل .png)", "Type the new extension (like .png)");
            Add("custom.save", "حفظه كخيار دائم لهذا النوع", "Keep it as a permanent option for this type");
            Add("danger.title", "امتداد قابل للتنفيذ", "Executable extension");
            Add("danger.msg", "الامتداد {0} يعمل كبرنامج أو سكربت عند فتحه. تأكد أن الملف موثوق قبل المتابعة.", "The extension {0} runs as a program or script when opened. Make sure the file is trusted before continuing.");
            Add("mismatch.title", "المحتوى لا يطابق الامتداد", "Content does not match");
            Add("mismatch.msg", "يبدو أن محتوى {0} من نوع {1}، لكنك تريد تسميته {2}. قد لا يفتح بعد ذلك، فتغيير الامتداد لا يحوّل المحتوى.", "The content of {0} looks like {1}, but you want to name it {2}. It may not open afterwards; changing the extension does not convert content.");
            Add("toast.running", "{0} يعمل الآن", "{0} is running");
            Add("toast.running.sub", "اضغط مطوّلًا بـ {0} على أي ملف.", "Press and hold {0} on any file.");
            Add("toast.nohook", "تعذّر تفعيل الضغط المطوّل", "Press-and-hold could not start");
            Add("toast.nohook.sub", "استخدم {0} لفتح العجلة.", "Use {0} to open the wheel.");
            Add("toast.select", "حدّد ملفًا أولًا", "Select a file first");
            Add("toast.select.sub", "حدّد الملف بالزر الأيسر ثم اضغط مطوّلًا بزر العجلة.", "Select the file with the left button, then press and hold the wheel button.");
            Add("toast.select.sub2", "حدّد ملفًا أو أكثر ثم أعد المحاولة.", "Select one or more files and try again.");
            Add("toast.openexp", "افتح مستكشف الملفات أولًا", "Open File Explorer first");
            Add("toast.openexp.sub", "حدّد ملفًا داخل نافذة المستكشف ثم اضغط {0}.", "Select a file in an Explorer window, then press {0}.");
            Add("toast.error", "حدث خطأ", "Something went wrong");
            Add("toast.undo", "تراجع", "Undo");
            Add("toast.bak.ok", "تم إنشاء نسخة احتياطية", "Backup created");
            Add("toast.bak.fail", "تعذّر إنشاء النسخة", "Backup failed");
            Add("toast.bak.partial", "تم نسخ {0} وتعذّر {1}", "{0} copied, {1} failed");
            Add("toast.rest.ok", "تمت الاستعادة", "Restored");
            Add("toast.rest.fail", "تعذّرت الاستعادة", "Restore failed");
            Add("toast.rest.partial", "تمت استعادة {0} وتعذّر {1}", "{0} restored, {1} failed");
            Add("toast.ren.ok", "تم تغيير الامتداد", "Extension changed");
            Add("toast.ren.many", "تم تغيير {0} ملفات", "{0} files changed");
            Add("toast.ren.fail", "تعذّر تغيير الامتداد", "Could not change the extension");
            Add("toast.ren.partial", "تم تغيير {0} وتعذّر {1}", "{0} changed, {1} failed");
            Add("toast.nochange", "لا تغيير", "No change");
            Add("toast.nochange.sub", "الملف يحمل هذا الامتداد بالفعل.", "The file already has this extension.");
            Add("toast.undone", "تم التراجع", "Undone");
            Add("toast.undone.one", "عاد الملف إلى اسمه الأصلي", "The file is back to its original name");
            Add("toast.undone.many", "عادت {0} ملفات إلى أسمائها", "{0} files are back to their names");
            Add("toast.undo.fail", "تعذّر التراجع", "Undo failed");
            Add("toast.nothing", "لا يوجد شيء للتراجع عنه", "Nothing to undo");
            Add("toast.nothing.sub", "لم تتم أي تغييرات بعد.", "No changes yet.");
            Add("err.notfound", "الملف لم يعد موجودًا.", "The file no longer exists.");
            Add("err.exists", "يوجد ملف بالاسم {0} في نفس المجلد.", "A file named {0} already exists in this folder.");
            Add("err.noorig", "لا يوجد اسم أصلي لاستعادته.", "There is no original name to restore.");
            Add("err.exists_orig", "يوجد ملف بالاسم الأصلي {0} بالفعل.", "A file with the original name {0} already exists.");
            Add("err.cantfind", "تعذّر العثور على الملف لإرجاعه.", "Could not find the file to revert.");
            Add("err.orig_used", "الاسم الأصلي مستخدم الآن لملف آخر.", "The original name is now used by another file.");
        }
    }
}
