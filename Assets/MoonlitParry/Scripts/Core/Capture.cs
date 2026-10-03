using System.IO;
using UnityEngine;

namespace MoonlitParry
{
    /// <summary>
    /// Debug screenshots into Assets/MoonlitParry/Captures~ (the ~ folder is ignored by the asset importer).
    /// Key 9 / F12 saves one; if Captures~/auto.txt exists (content = seconds between shots) a rolling set of 12 is kept.
    /// </summary>
    public class Capture : MonoBehaviour
    {
        static int count;
        float interval = -1f, next;
        int auto;

        static string Dir { get { return Path.Combine(Application.dataPath, "MoonlitParry/Captures~"); } }

        public static void Shot(string tag)
        {
            try
            {
                Directory.CreateDirectory(Dir);
                string p = Path.Combine(Dir, tag + "_" + (count++).ToString("000") + ".png");
                ScreenCapture.CaptureScreenshot(p);
            }
            catch (System.Exception e) { Debug.LogWarning("[MoonlitParry] capture failed: " + e.Message); }
        }

        void Start()
        {
            try
            {
                string f = Path.Combine(Dir, "auto.txt");
                if (File.Exists(f))
                {
                    float v;
                    interval = float.TryParse(File.ReadAllText(f).Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v) ? (v > 0f ? Mathf.Max(0.2f, v) : -1f) : 2f;
                    next = Time.realtimeSinceStartup + 1.5f;
                }
            }
            catch { interval = -1f; }
        }

        void Update()
        {
            if (interval <= 0f || Time.realtimeSinceStartup < next) return;
            next = Time.realtimeSinceStartup + interval;
            try
            {
                Directory.CreateDirectory(Dir);
                ScreenCapture.CaptureScreenshot(Path.Combine(Dir, "auto_" + (auto++ % 12).ToString("00") + ".png"));
            }
            catch { }
        }
    }
}
