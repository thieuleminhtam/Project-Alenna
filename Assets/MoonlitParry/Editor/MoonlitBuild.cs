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
    /// "source" or both) so they can be triggered from outside the editor. Results go to Captures~ (ignored by Unity):
    /// MoonlitParry_Windows.zip, MoonlitParry_Source.zip and build_log.txt.
    /// </summary>
    [InitializeOnLoad]
    public static class MoonlitBuild
    {
        static double next;

        static string ProjectRoot { get { return Path.GetDirectoryName(Application.dataPath); } }
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
                if (what.Contains("source")) PackSource();
                if (what.Contains("windows") || what.Length == 0) BuildWindows();
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
                string outDir = Path.Combine(ProjectRoot, "Builds/MoonlitParry_Windows");
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
                    locationPathName = Path.Combine(outDir, "MoonlitParry.exe"),
                    target = BuildTarget.StandaloneWindows64,
                    targetGroup = BuildTargetGroup.Standalone,
                    options = BuildOptions.None
                };
                BuildReport report = BuildPipeline.BuildPlayer(opts);
                var sum = report.summary;
                Log("build " + sum.result + "  size " + sum.totalSize + "  errors " + sum.totalErrors + "  time " + sum.totalTime);
                if (sum.result != BuildResult.Succeeded) return;
                foreach (var d in Directory.GetDirectories(outDir, "*DoNotShip*")) Directory.Delete(d, true);
                File.WriteAllText(Path.Combine(outDir, "README.txt"),
                    "Moonlit Parry - demo\r\n\r\nChay MoonlitParry.exe.\r\n\r\nDieu khien: A/D di chuyen, Space nhay, chuot trai chem, chuot phai parry,\r\n" +
                    "Shift lon, 1 binh mau, F ket lieu, R nghi o lua trai / thu lai, Esc tam dung.\r\n");
                string zip = Path.Combine(Cap, "MoonlitParry_Windows.zip");
                Log(Zip(outDir, zip) ? "zip ok " + zip + " (" + new FileInfo(zip).Length + " bytes)" : "zip FAILED (folder: " + outDir + ")");
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
                Log(Zip(tmp, zip) ? "source zip ok (" + new FileInfo(zip).Length + " bytes)" : "source zip FAILED");
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

        /// <summary>ZipFile.CreateFromDirectory through reflection (keeps this script compiling on any API level).</summary>
        static bool Zip(string dir, string zip)
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
                var m = t.GetMethod("CreateFromDirectory", new[] { typeof(string), typeof(string) });
                m.Invoke(null, new object[] { dir, zip });
                return File.Exists(zip);
            }
            catch (Exception e) { Log("zip EXCEPTION " + e.Message); return false; }
        }
    }
}
