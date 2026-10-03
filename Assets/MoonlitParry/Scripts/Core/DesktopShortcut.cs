using System;
using System.IO;
using System.Runtime.InteropServices;
using UnityEngine;

namespace MoonlitParry
{
    /// <summary>
    /// Windows builds: the first time the game runs it puts a shortcut named after the game (with the game icon) on the
    /// desktop, so friends who unzipped the download can start it from there. If the game folder is moved later, the
    /// existing shortcut is re-pointed; a shortcut the player deleted is not created again.
    /// The .lnk is written by the Windows shell's own ShellLink object, called through its vtable (no COM interop layer).
    /// </summary>
    public static class DesktopShortcut
    {
        const string Fallback = "Lord of Alenna (Demo)";
        const string KeyDone = "mp_shortcut_done", KeyExe = "mp_shortcut_exe";
        const string TestArg = "-mp-shortcut-test";

        /// <summary>Set when a new desktop shortcut was made; the HUD shows it once.</summary>
        public static string Notice;

        public static string Title
        {
            get
            {
                string t = string.IsNullOrEmpty(Application.productName) ? Fallback : Application.productName;
                foreach (char c in Path.GetInvalidFileNameChars()) t = t.Replace(c.ToString(), "");
                return t.Trim().Length > 0 ? t.Trim() : Fallback;
            }
        }

#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void OnGameStart()
        {
            try
            {
                string testDir = TestDir();
                if (testDir != null) { RunTest(testDir); return; }
                Ensure();
            }
            catch (Exception e) { Debug.LogWarning("[Shortcut] " + e.Message); }
        }
#endif

        static void Ensure()
        {
            string exe = ExePath();
            if (exe == null || !File.Exists(exe)) return;
            string tmp = Full(Path.GetTempPath());
            if (tmp != null && exe.StartsWith(tmp, StringComparison.OrdinalIgnoreCase)) return;      // started from inside a zip viewer
            string desk = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            if (string.IsNullOrEmpty(desk) || !Directory.Exists(desk)) return;
            string lnk = Path.Combine(desk, Title + ".lnk");
            bool done = PlayerPrefs.GetInt(KeyDone, 0) == 1;
            if (done && (string.Equals(PlayerPrefs.GetString(KeyExe, ""), exe, StringComparison.OrdinalIgnoreCase) || !File.Exists(lnk)))
                return;                         // made before: only follow a moved game, never bring back a deleted shortcut
            string err = Create(lnk, exe, Path.GetDirectoryName(exe), exe, Title);
            if (err != null) { Debug.LogWarning("[Shortcut] " + err); return; }
            PlayerPrefs.SetInt(KeyDone, 1);
            PlayerPrefs.SetString(KeyExe, exe);
            PlayerPrefs.Save();
            if (!done) Notice = "Đã tạo lối tắt \"" + Title + "\" ngoài Desktop";
        }

        /// <summary>Full path of the running player .exe (…\Name.exe next to …\Name_Data).</summary>
        public static string ExePath()
        {
            string data = Application.dataPath;
            if (string.IsNullOrEmpty(data)) return null;
            data = data.TrimEnd('/', '\\');
            string leaf = Path.GetFileName(data);
            if (!leaf.EndsWith("_Data", StringComparison.OrdinalIgnoreCase)) return null;
            return Full(Path.Combine(Path.GetDirectoryName(data), leaf.Substring(0, leaf.Length - 5) + ".exe"));
        }

        static string Full(string p)
        {
            try { return string.IsNullOrEmpty(p) ? null : Path.GetFullPath(p); }
            catch { return null; }
        }

        // ------------------------------------------------------------------ self test (used by the build tools)
        static string TestDir()
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < args.Length; i++)
                if (args[i] == TestArg) return args[i + 1];
            return null;
        }

        /// <summary>Writes the shortcut into <paramref name="dir"/> instead of the desktop, reports, quits. No PlayerPrefs.</summary>
        static void RunTest(string dir)
        {
            string exe = ExePath();
            string report = "exe=" + exe + "\nexe exists=" + (exe != null && File.Exists(exe)) +
                            "\ndesktop=" + Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory) + "\ntitle=" + Title;
            try
            {
                Directory.CreateDirectory(dir);
                string lnk = Path.Combine(dir, Title + ".lnk");
                string err = exe == null ? "no exe path" : Create(lnk, exe, Path.GetDirectoryName(exe), exe, Title);
                report += "\ncreate=" + (err ?? "ok");
                if (err == null) report += "\nread back=" + Describe(lnk);
            }
            catch (Exception e) { report += "\nexception=" + e; }
            try { File.WriteAllText(Path.Combine(dir, "player_result.txt"), report); } catch { }
            Application.Quit();
        }

        // ------------------------------------------------------------------ shell link through raw COM vtables
        [DllImport("ole32.dll")] static extern int CoInitializeEx(IntPtr reserved, uint coInit);
        [DllImport("ole32.dll")] static extern void CoUninitialize();
        [DllImport("ole32.dll")] static extern int CoCreateInstance(ref Guid clsid, IntPtr outer, uint context, ref Guid iid, out IntPtr obj);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int QueryFn(IntPtr self, ref Guid iid, out IntPtr obj);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate uint ReleaseFn(IntPtr self);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int StrFn(IntPtr self, [MarshalAs(UnmanagedType.LPWStr)] string s);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int StrIntFn(IntPtr self, [MarshalAs(UnmanagedType.LPWStr)] string s, int n);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int IntFn(IntPtr self, int n);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int GetPathFn(IntPtr self, IntPtr buf, int cch, IntPtr findData, uint flags);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int GetBufFn(IntPtr self, IntPtr buf, int cch);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int GetIconFn(IntPtr self, IntPtr buf, int cch, out int index);

        static readonly Guid ClsidShellLink = new Guid("00021401-0000-0000-C000-000000000046");
        static readonly Guid IidShellLinkW = new Guid("000214F9-0000-0000-C000-000000000046");
        static readonly Guid IidPersistFile = new Guid("0000010b-0000-0000-C000-000000000046");

        // IShellLinkW slots after IUnknown (QueryInterface 0, AddRef 1, Release 2)
        const int GetPath = 3, GetDescription = 6, SetDescription = 7, GetWorkingDirectory = 8, SetWorkingDirectory = 9,
                  SetShowCmd = 15, GetIconLocation = 16, SetIconLocation = 17, SetPath = 20;
        // IPersistFile slots after IUnknown + IPersist.GetClassID
        const int Load = 5, Save = 6;

        static T Fn<T>(IntPtr obj, int slot) where T : class
        {
            IntPtr vtable = Marshal.ReadIntPtr(obj);
            return Marshal.GetDelegateForFunctionPointer(Marshal.ReadIntPtr(vtable, slot * IntPtr.Size), typeof(T)) as T;
        }

        static string Hr(string what, int hr) { return what + " failed 0x" + hr.ToString("X8"); }

        /// <summary>Opens a ShellLink object (and its IPersistFile), runs <paramref name="body"/>, releases everything.</summary>
        static string WithLink(Func<IntPtr, IntPtr, string> body)
        {
            IntPtr link = IntPtr.Zero, file = IntPtr.Zero;
            int init = CoInitializeEx(IntPtr.Zero, 2);                    // COINIT_APARTMENTTHREADED; may already be set up
            try
            {
                Guid clsid = ClsidShellLink, iid = IidShellLinkW, iidFile = IidPersistFile;
                int hr = CoCreateInstance(ref clsid, IntPtr.Zero, 1, ref iid, out link);   // CLSCTX_INPROC_SERVER
                if (hr < 0 || link == IntPtr.Zero) return Hr("CoCreateInstance", hr);
                hr = Fn<QueryFn>(link, 0)(link, ref iidFile, out file);
                if (hr < 0 || file == IntPtr.Zero) return Hr("QueryInterface(IPersistFile)", hr);
                return body(link, file);
            }
            catch (Exception e) { return e.GetType().Name + ": " + e.Message; }
            finally
            {
                if (file != IntPtr.Zero) Fn<ReleaseFn>(file, 2)(file);
                if (link != IntPtr.Zero) Fn<ReleaseFn>(link, 2)(link);
                if (init >= 0) CoUninitialize();                          // S_OK / S_FALSE took a reference
            }
        }

        /// <summary>Writes a .lnk to <paramref name="target"/>. Returns null on success, otherwise what went wrong.</summary>
        public static string Create(string lnk, string target, string workDir, string icon, string description)
        {
            return WithLink((link, file) =>
            {
                int hr = Fn<StrFn>(link, SetPath)(link, target);
                if (hr < 0) return Hr("SetPath", hr);
                Fn<StrFn>(link, SetWorkingDirectory)(link, workDir ?? "");
                Fn<StrIntFn>(link, SetIconLocation)(link, icon ?? target, 0);
                Fn<StrFn>(link, SetDescription)(link, description ?? "");
                Fn<IntFn>(link, SetShowCmd)(link, 1);                                // SW_SHOWNORMAL
                hr = Fn<StrIntFn>(file, Save)(file, lnk, 1);
                if (hr < 0) return Hr("Save", hr);
                return File.Exists(lnk) ? null : "the .lnk file was not written";
            });
        }

        /// <summary>Loads a .lnk through the shell: "target | working dir | icon,index | description", or the error.</summary>
        public static string Describe(string lnk)
        {
            IntPtr buf = Marshal.AllocHGlobal(2 * 1024);
            try
            {
                return WithLink((link, file) =>
                {
                    int hr = Fn<StrIntFn>(file, Load)(file, lnk, 0);                // STGM_READ
                    if (hr < 0) return Hr("Load", hr);
                    Marshal.WriteInt16(buf, 0);
                    Fn<GetPathFn>(link, GetPath)(link, buf, 1024, IntPtr.Zero, 0);
                    string target = Marshal.PtrToStringUni(buf);
                    Marshal.WriteInt16(buf, 0);
                    Fn<GetBufFn>(link, GetWorkingDirectory)(link, buf, 1024);
                    string dir = Marshal.PtrToStringUni(buf);
                    int index;
                    Marshal.WriteInt16(buf, 0);
                    Fn<GetIconFn>(link, GetIconLocation)(link, buf, 1024, out index);
                    string icon = Marshal.PtrToStringUni(buf) + "," + index;
                    Marshal.WriteInt16(buf, 0);
                    Fn<GetBufFn>(link, GetDescription)(link, buf, 1024);
                    return target + " | " + dir + " | " + icon + " | " + Marshal.PtrToStringUni(buf);
                });
            }
            finally { Marshal.FreeHGlobal(buf); }
        }
    }
}
