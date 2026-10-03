using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace MoonlitParry.EditorTools
{
    /// <summary>
    /// One-click Windows build + zip (to share the game), and a zip of the project source.
    /// Menu: Moonlit Parry ▸ Build Windows (zip) / Pack Source (zip).
    /// The same jobs also start when a file Assets/MoonlitParry/Captures~/build.txt appears (contents "windows",
    /// "source", "shortcut" or several) so they can be triggered from outside the editor. Results go to Captures~
    /// (ignored by Unity): LordOfAlenna_Demo_Windows.zip, MoonlitParry_Source.zip and build_log.txt.
    /// "shortcut" checks the desktop-shortcut code: once from the editor and once inside the built player (headless).
    /// </summary>
    [InitializeOnLoad]
    public static class MoonlitBuild
    {
        static double next;

        static string ProjectRoot { get { return Path.GetDirectoryName(Application.dataPath); } }
        static string GameFolder { get { return Path.Combine(ProjectRoot, "Builds/LordOfAlenna_Windows/" + Branding.ProductName); } }
        static string GameExe { get { return Path.Combine(GameFolder, Branding.ProductName + ".exe"); } }
        const string ZipName = "LordOfAlenna_Demo_Windows.zip";
        static string Cap { get { return Path.Combine(Application.dataPath, "MoonlitParry/Captures~"); } }

        static MoonlitBuild()
        {
            EditorApplication.update += Poll;
        }

        static void Poll()
        {
            if (EditorApplication.timeSinceStartup < next) return;
            next = EditorApplication.timeSinceStartup + 2.0;
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            string trig = Path.Combine(Cap, "build.txt");
            if (!File.Exists(trig)) return;
            string what;
            try { what = File.ReadAllText(trig).Trim().ToLowerInvariant(); File.Delete(trig); }
            catch { return; }
            EditorApplication.delayCall += () =>
            {
                if (what.Contains("windows") || what.Length == 0) BuildWindows();
                if (what.Contains("source")) PackSource();
                if (what.Contains("shortcut")) TestShortcut();
            };
        }

        static void Log(string text)
        {
            try
            {
                Directory.CreateDirectory(Cap);
                File.AppendAllText(Path.Combine(Cap, "build_log.txt"), DateTime.Now.ToString("HH:mm:ss") + "  " + text + "\n");
            }
            catch { }
            Debug.Log("[Moonlit Parry] " + text);
        }

        [MenuItem("Moonlit Parry/Build Windows (zip)", false, 100)]
        public static void BuildWindows()
        {
            try
            {
                Branding.Apply(false);
                string outDir = GameFolder;
                if (Directory.Exists(outDir)) Directory.Delete(outDir, true);
                Directory.CreateDirectory(outDir);
                var scenes = new List<string>();
                foreach (var s in EditorBuildSettings.scenes)
                    if (s.enabled && !string.IsNullOrEmpty(s.path)) scenes.Add(s.path);
                if (scenes.Count == 0) scenes.Add(EditorSceneManager.GetActiveScene().path);
                Log("build start, scenes: " + string.Join(", ", scenes.ToArray()));
                var opts = new BuildPlayerOptions
                {
                    scenes = scenes.ToArray(),
                    locationPathName = GameExe,
                    target = BuildTarget.StandaloneWindows64,
                    targetGroup = BuildTargetGroup.Standalone,
                    options = BuildOptions.None
                };
                BuildReport report = BuildPipeline.BuildPlayer(opts);
                var sum = report.summary;
                Log("build " + sum.result + "  size " + sum.totalSize + "  errors " + sum.totalErrors + "  time " + sum.totalTime);
                if (sum.result != BuildResult.Succeeded) return;
                foreach (var d in Directory.GetDirectories(outDir, "*DoNotShip*")) Directory.Delete(d, true);
                string n = Branding.ProductName;
                File.WriteAllText(Path.Combine(outDir, "README.txt"),         // ASCII name: zip tools mangle accented names
                    n + "\r\n\r\n" +
                    "Mở \"" + n + ".exe\" để chơi. Lần đầu mở, game tự tạo lối tắt \"" + n + "\" ngoài Desktop,\r\n" +
                    "lần sau bấm vào lối tắt đó là vào game.\r\n" +
                    "Nếu Windows hiện \"Windows protected your PC\": bấm \"More info\" rồi \"Run anyway\".\r\n\r\n" +
                    "Điều khiển: A/D di chuyển, Space nhảy, chuột trái chém, chuột phải parry, Shift lộn né,\r\n" +
                    "1 uống bình máu, F kết liễu, R nghỉ ở lửa trại / thử lại, Esc hoặc P tạm dừng.\r\n",
                    new UTF8Encoding(true));
                string zip = Path.Combine(Cap, ZipName);
                Log(Zip(outDir, zip, true) ? "zip ok " + zip + " (" + new FileInfo(zip).Length + " bytes)" : "zip FAILED (folder: " + outDir + ")");
            }
            catch (Exception e) { Log("build EXCEPTION " + e); }
        }

        [MenuItem("Moonlit Parry/Pack Source (zip)", false, 101)]
        public static void PackSource()
        {
            try
            {
                string tmp = Path.Combine(ProjectRoot, "Builds/src_tmp");
                if (Directory.Exists(tmp)) Directory.Delete(tmp, true);
                CopyDir(Application.dataPath, Path.Combine(tmp, "Assets"));
                CopyDir(Path.Combine(ProjectRoot, "Packages"), Path.Combine(tmp, "Packages"));
                CopyDir(Path.Combine(ProjectRoot, "ProjectSettings"), Path.Combine(tmp, "ProjectSettings"));
                string zip = Path.Combine(Cap, "MoonlitParry_Source.zip");
                Log(Zip(tmp, zip, false) ? "source zip ok (" + new FileInfo(zip).Length + " bytes)" : "source zip FAILED");
                Directory.Delete(tmp, true);
            }
            catch (Exception e) { Log("source EXCEPTION " + e); }
        }

        static void CopyDir(string src, string dst)
        {
            if (!Directory.Exists(src)) return;
            Directory.CreateDirectory(dst);
            foreach (var f in Directory.GetFiles(src))
            {
                string n = Path.GetFileName(f);
                if (n.StartsWith(".")) continue;
                File.Copy(f, Path.Combine(dst, n), true);
            }
            foreach (var d in Directory.GetDirectories(src))
            {
                string n = Path.GetFileName(d);
                if (n.EndsWith("~") || n.StartsWith(".")) continue;          // Captures~ and other ignored folders
                CopyDir(d, Path.Combine(dst, n));
            }
        }

        /// <summary>
        /// Shortcut check: writes a .lnk from the editor and reads it back through the shell, then (if a build exists)
        /// runs the player headless with -mp-shortcut-test so it does the same from inside the game. All into
        /// Captures~/shortcut_test; nothing touches the desktop or the game's saved settings.
        /// </summary>
        [MenuItem("Moonlit Parry/Test Desktop Shortcut", false, 103)]
        public static void TestShortcut()
        {
            try
            {
                string dir = Path.Combine(Cap, "shortcut_test");
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
                Directory.CreateDirectory(dir);
                string target = File.Exists(GameExe) ? GameExe : EditorApplication.applicationPath;
                string lnk = Path.Combine(dir, "editor.lnk");
                string err = MoonlitParry.DesktopShortcut.Create(lnk, target, Path.GetDirectoryName(target), target, "editor test");
                Log("shortcut editor: " + (err ?? "ok -> " + MoonlitParry.DesktopShortcut.Describe(lnk)));
                if (!File.Exists(GameExe)) { Log("shortcut player: skipped (no build at " + GameExe + ")"); return; }
                var psi = new System.Diagnostics.ProcessStartInfo(GameExe,
                    "-batchmode -nographics -mp-shortcut-test \"" + dir + "\" -logFile \"" + Path.Combine(dir, "player.log") + "\"")
                { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = GameFolder };
                var proc = System.Diagnostics.Process.Start(psi);
                bool exited = proc.WaitForExit(90000);
                if (!exited) { try { proc.Kill(); } catch { } }
                string res = Path.Combine(dir, "player_result.txt");
                Log("shortcut player: " + (exited ? "exit " + proc.ExitCode : "timeout") + "\n" +
                    (File.Exists(res) ? File.ReadAllText(res) : "(no player_result.txt)"));
            }
            catch (Exception e) { Log("shortcut EXCEPTION " + e); }
        }

        /// <summary>ZipFile.CreateFromDirectory through reflection (keeps this script compiling on any API level).
        /// With <paramref name="withFolder"/> the zip holds the folder itself, so unzipping gives one tidy folder.</summary>
        static bool Zip(string dir, string zip, bool withFolder)
        {
            try
            {
                if (File.Exists(zip)) File.Delete(zip);
                Type t = null;
                foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
                {
                    t = a.GetType("System.IO.Compression.ZipFile", false);
                    if (t != null) break;
                }
                if (t == null)
                    foreach (var n in new[] { "System.IO.Compression.ZipFile", "System.IO.Compression.FileSystem", "System.IO.Compression" })
                    {
                        try { t = Assembly.Load(n).GetType("System.IO.Compression.ZipFile", false); } catch { }
                        if (t != null) break;
                    }
                if (t == null) { Log("ZipFile type not found"); return false; }
                MethodInfo four = null;
                foreach (var mi in t.GetMethods(BindingFlags.Public | BindingFlags.Static))
                {
                    var ps = mi.GetParameters();
                    if (mi.Name == "CreateFromDirectory" && ps.Length == 4 && ps[2].ParameterType.IsEnum && ps[3].ParameterType == typeof(bool))
                    { four = mi; break; }
                }
                if (four != null)
                {
                    object level = Enum.Parse(four.GetParameters()[2].ParameterType, "Optimal");
                    four.Invoke(null, new object[] { dir, zip, level, withFolder });
                }
                else
                {
                    if (withFolder) Log("zip: 4-argument CreateFromDirectory not found, zipping without the top folder");
                    t.GetMethod("CreateFromDirectory", new[] { typeof(string), typeof(string) }).Invoke(null, new object[] { dir, zip });
                }
                return File.Exists(zip);
            }
            catch (Exception e) { Log("zip EXCEPTION " + e.Message); return false; }
        }
    }
}
