using System.Collections.Generic;
using UnityEngine;

namespace MoonlitParry
{
    /// <summary>Physics layers (by index — no need to name them in Tags & Layers).</summary>
    public static class Layers
    {
        public const int Ground = 8;
        public const int Player = 9;
        public const int Enemy = 10;
        public static int GroundMask { get { return 1 << Ground; } }
        public static int PlayerMask { get { return 1 << Player; } }
        public static int EnemyMask { get { return 1 << Enemy; } }
    }

    /// <summary>
    /// Characters pass through each other. Done per collider pair (Physics2D.IgnoreCollision) so the
    /// project's global layer-collision matrix is never modified.
    /// </summary>
    public static class Actors
    {
        static readonly List<Collider2D> all = new List<Collider2D>();

        public static void Register(Collider2D c)
        {
            all.RemoveAll(x => x == null);
            foreach (var o in all)
                if (o != null && o != c) Physics2D.IgnoreCollision(c, o, true);
            all.Add(c);
        }

        public static void Clear() { all.Clear(); }
    }

    /// <summary>Sorting orders shared by everything.</summary>
    public static class Order
    {
        public const int Sky = -100, Mountains = -90, Forest = -80, Fog = -75, Ruins = -70;
        public const int Trees = -40, DecorBack = -20, Gate = -15, Tiles = -10, Under = -9, Decor = -5;
        public const int Enemy = 10, Boss = 11, Player = 20, GrassFront = 23, Fx = 30, Foreground = 50;
    }

    public static class Gfx
    {
        static Material mat;
        static bool matLoaded;

        /// <summary>Unlit sprite material created by the editor setup (needed when the project uses URP).</summary>
        public static Material Material
        {
            get
            {
                if (!matLoaded)
                {
                    mat = Resources.Load<Material>("MoonlitSpriteMat");
                    matLoaded = true;
                }
                return mat;
            }
        }

        public static GameObject Make(string name, Transform parent, Vector3 pos)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = pos;
            return go;
        }

        public static SpriteRenderer Renderer(GameObject go, Sprite s, int order)
        {
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = s;
            sr.sortingOrder = order;
            var m = Material;
            if (m != null) sr.sharedMaterial = m;
            return sr;
        }

        public static SpriteRenderer Sprite(string name, Transform parent, Vector3 pos, Sprite s, int order)
        {
            return Renderer(Make(name, parent, pos), s, order);
        }

        static Shader hdShader;
        static bool hdLoaded;
        static Material hdMat, hdFlash;

        /// <summary>Unlit premultiplied shader used by the HD rigs / ribbons / terrain (supports a white flash).</summary>
        public static Shader HDShader
        {
            get
            {
                if (!hdLoaded)
                {
                    hdLoaded = true;
                    hdShader = Resources.Load<Shader>("MoonlitHD");
                    if (hdShader == null) hdShader = Shader.Find("MoonlitParry/HD");
                    if (hdShader != null && !hdShader.isSupported) hdShader = null;
                    if (hdShader == null) Debug.LogWarning("[MoonlitParry] MoonlitHD shader missing/unsupported - falling back to the sprite material.");
                }
                return hdShader;
            }
        }

        public static Material HDMat
        {
            get
            {
                if (hdMat == null)
                {
                    if (HDShader != null) hdMat = new Material(HDShader) { name = "MoonlitHD" };
                    else hdMat = Material != null ? new Material(Material) : new Material(Shader.Find("Sprites/Default"));
                }
                return hdMat;
            }
        }

        public static Material HDFlashMat
        {
            get
            {
                if (hdFlash == null)
                {
                    hdFlash = new Material(HDMat) { name = "MoonlitHD_Flash" };
                    if (hdFlash.HasProperty("_Flash")) hdFlash.SetFloat("_Flash", 1f);
                }
                return hdFlash;
            }
        }

        /// <summary>A material instance of the HD shader showing a given texture (for MeshRenderers).</summary>
        public static Material HDTexMat(Texture tex)
        {
            var m = new Material(HDMat);
            m.mainTexture = tex;
            return m;
        }
    }

    public static class RbExt
    {
        public static Vector2 Vel(this Rigidbody2D rb)
        {
#if UNITY_6000_0_OR_NEWER
            return rb.linearVelocity;
#else
            return rb.velocity;
#endif
        }

        public static void SetVel(this Rigidbody2D rb, Vector2 v)
        {
#if UNITY_6000_0_OR_NEWER
            rb.linearVelocity = v;
#else
            rb.velocity = v;
#endif
        }

        public static void SetVel(this Rigidbody2D rb, float x, float y)
        {
            rb.SetVel(new Vector2(x, y));
        }
    }

    /// <summary>Loads frame sprites from Resources/Sprites/&lt;folder&gt; grouped by "clip_00" names.</summary>
    public static class SpriteBank
    {
        static readonly Dictionary<string, Dictionary<string, Sprite[]>> sets = new Dictionary<string, Dictionary<string, Sprite[]>>();
        static readonly Dictionary<Sprite, Sprite> whites = new Dictionary<Sprite, Sprite>();
        static readonly Dictionary<string, Sprite> singles = new Dictionary<string, Sprite>();

        public static Dictionary<string, Sprite[]> Set(string folder)
        {
            Dictionary<string, Sprite[]> d;
            if (sets.TryGetValue(folder, out d)) return d;
            d = new Dictionary<string, Sprite[]>();
            var all = Resources.LoadAll<Sprite>("Sprites/" + folder);
            var hdAll = Resources.LoadAll<Sprite>("HD/" + folder);    // HD art overrides clips of the same name (environment only)
            var hdList = new List<Sprite>();
            foreach (var s in hdAll)
            {
                int k = s.name.LastIndexOf('_');
                if (HDAllowed(folder + "/" + (k > 0 ? s.name.Substring(0, k) : s.name))) hdList.Add(s);
            }
            var hd = hdList.ToArray();
            if (all.Length == 0 && hd.Length == 0)
                Debug.LogError("[MoonlitParry] Không tìm thấy sprite trong Resources/Sprites/" + folder +
                               ". Hãy chạy menu Moonlit Parry > Setup Project.");
            for (int pass = 0; pass < 2; pass++)
            {
                var groups = new Dictionary<string, List<Sprite>>();
                foreach (var s in pass == 0 ? all : hd)
                {
                    int i = s.name.LastIndexOf('_');
                    string key = i > 0 ? s.name.Substring(0, i) : s.name;
                    List<Sprite> l;
                    if (!groups.TryGetValue(key, out l)) { l = new List<Sprite>(); groups[key] = l; }
                    l.Add(s);
                }
                foreach (var kv in groups)
                {
                    kv.Value.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
                    d[kv.Key] = kv.Value.ToArray();
                }
            }
            sets[folder] = d;
            return d;
        }

        /// <summary>
        /// Art direction: only the environment (sky/background layers, terrain, platforms, props, ruins, trees, fire of
        /// bonfires/braziers, ambient motes) uses the drawn HD art. Characters, combat effects and UI stay pixel art.
        /// </summary>
        public static bool HDAllowed(string path)
        {
            if (path.StartsWith("BG/") || path.StartsWith("Decor/") || path.StartsWith("Tiles/") || path.StartsWith("Terrain/")) return true;
            if (path.StartsWith("FX/"))
            {
                string n = path.Substring(3);
                return n.StartsWith("brazier") || n == "glow" || n == "pollen" || n == "petal";
            }
            return false;
        }

        public static Sprite One(string path)
        {
            Sprite s;
            if (singles.TryGetValue(path, out s)) return s;
            if (HDAllowed(path)) s = Resources.Load<Sprite>("HD/" + path);   // HD version first (environment only)
            if (s == null) s = Resources.Load<Sprite>("Sprites/" + path);
            if (s == null) Debug.LogWarning("[MoonlitParry] Missing sprite: " + path);
            singles[path] = s;
            return s;
        }

        /// <summary>White silhouette of a sprite (hit flash). Needs Read/Write enabled — set by the importer.</summary>
        public static Sprite White(Sprite s)
        {
            if (s == null) return null;
            Sprite w;
            if (whites.TryGetValue(s, out w) && w != null) return w;
            var tex = s.texture;
            if (!tex.isReadable) { whites[s] = s; return s; }
            var r = s.textureRect;
            int x = Mathf.RoundToInt(r.x), y = Mathf.RoundToInt(r.y), wd = Mathf.RoundToInt(r.width), ht = Mathf.RoundToInt(r.height);
            var px = tex.GetPixels(x, y, wd, ht);
            for (int i = 0; i < px.Length; i++) px[i] = new Color(1f, 1f, 1f, px[i].a > 0.01f ? 1f : 0f);
            var t = new Texture2D(wd, ht, TextureFormat.RGBA32, false);
            t.filterMode = tex.filterMode;
            t.wrapMode = TextureWrapMode.Clamp;
            t.SetPixels(px);
            t.Apply();
            w = UnityEngine.Sprite.Create(t, new Rect(0, 0, wd, ht), new Vector2(s.pivot.x / wd, s.pivot.y / ht), s.pixelsPerUnit, 0, SpriteMeshType.FullRect);
            whites[s] = w;
            return w;
        }

        static readonly Dictionary<string, Dictionary<string, float[]>> timings = new Dictionary<string, Dictionary<string, float[]>>();

        /// <summary>
        /// Per-frame durations from Resources/Sprites/&lt;folder&gt;/timing.txt ("clip:w0,w1,..." per line, in units of
        /// the clip's nominal frame time). In-between frames share the time slot of the key pose they follow, so the
        /// gameplay timing of the key poses does not change when frames are added. Clips not listed play evenly.
        /// </summary>
        public static Dictionary<string, float[]> Timing(string folder)
        {
            Dictionary<string, float[]> d;
            if (timings.TryGetValue(folder, out d)) return d;
            d = new Dictionary<string, float[]>();
            var ta = Resources.Load<TextAsset>("Sprites/" + folder + "/timing");
            if (ta != null)
            {
                foreach (var raw in ta.text.Split('\n'))
                {
                    var line = raw.Trim();
                    int c = line.IndexOf(':');
                    if (c <= 0) continue;
                    var parts = line.Substring(c + 1).Split(',');
                    var w = new float[parts.Length];
                    bool ok = true;
                    for (int i = 0; i < parts.Length; i++)
                        ok &= float.TryParse(parts[i], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out w[i]);
                    if (ok) d[line.Substring(0, c)] = w;
                }
            }
            timings[folder] = d;
            return d;
        }

        public static void Prewarm(Dictionary<string, Sprite[]> set)
        {
            foreach (var kv in set)
                foreach (var s in kv.Value) White(s);
        }
    }

    /// <summary>Common API of the frame animator (pixel sprites) and the HD cut-out rig.</summary>
    public interface IAnim
    {
        string Name { get; }
        int Frame { get; }
        bool Finished { get; }
        int Length { get; }
        bool Has(string clip);
        int CountOf(string clip);
        void Play(string clip, float fps, bool loop, bool restart = false);
        void SetFrame(int f);
        void Tick(float dt);
        Color Tint { get; set; }
        bool Flash { get; set; }
        int SortingOrder { get; set; }
        Transform Bone(string name);
    }

    public static class Anims
    {
        /// <summary>HD rig if Resources/HD/Rigs/&lt;rig&gt;.json exists, else the pixel frames in Resources/Sprites/&lt;folder&gt;.</summary>
        /// <summary>Characters use the pixel frames; set true to use the HD cut-out rigs instead.</summary>
        public static bool UseRigs = false;

        public static IAnim Create(Transform visual, string rig, string folder, int order)
        {
            if (UseRigs && RigData.Exists(rig))
            {
                var r = new RigAnim(visual, rig, order);
                if (r.Valid) return r;
            }
            var sr = Gfx.Renderer(visual.gameObject, null, order);
            var set = SpriteBank.Set(folder);
            SpriteBank.Prewarm(set);
            return new SpriteAnim(sr, set, SpriteBank.Timing(folder));
        }
    }

    /// <summary>Frame animator driven manually via Tick() so game logic and visuals stay in lockstep.</summary>
    public class SpriteAnim : IAnim
    {
        readonly SpriteRenderer sr;
        readonly Dictionary<string, Sprite[]> set;
        readonly Dictionary<string, float[]> timing;
        Sprite[] frames;
        float[] weights;
        float fps, t;
        bool loop;

        public string Name { get; private set; }
        public int Frame { get; private set; }
        public bool Finished { get; private set; }
        public int Length { get { return frames == null ? 0 : frames.Length; } }

        public SpriteAnim(SpriteRenderer sr, Dictionary<string, Sprite[]> set, Dictionary<string, float[]> timing = null)
        {
            this.sr = sr;
            this.set = set;
            this.timing = timing;
        }

        float Dur(int f) { return weights != null && f < weights.Length && weights[f] > 0.001f ? weights[f] : 1f; }

        public bool Has(string clip) { return set.ContainsKey(clip); }

        public int CountOf(string clip)
        {
            Sprite[] f;
            return set.TryGetValue(clip, out f) ? f.Length : 0;
        }

        public void Play(string clip, float fps, bool loop, bool restart = false)
        {
            if (!restart && clip == Name)
            {
                this.fps = fps;
                this.loop = loop;
                return;
            }
            Sprite[] f;
            if (!set.TryGetValue(clip, out f) || f.Length == 0)
            {
                Debug.LogWarning("[MoonlitParry] Missing clip " + clip);
                return;
            }
            frames = f;
            float[] w;
            weights = timing != null && timing.TryGetValue(clip, out w) && w.Length == f.Length ? w : null;
            Name = clip;
            this.fps = fps;
            this.loop = loop;
            Frame = 0;
            t = 0f;
            Finished = false;
            Apply();
        }

        public void SetFrame(int f)
        {
            if (frames == null) return;
            Frame = Mathf.Clamp(f, 0, frames.Length - 1);
            Apply();
        }

        public void Tick(float dt)
        {
            if (frames == null || Finished) return;
            t += dt * fps;
            while (t >= Dur(Frame))
            {
                t -= Dur(Frame);
                if (Frame + 1 >= frames.Length)
                {
                    if (loop) Frame = 0;
                    else { Finished = true; t = 0f; break; }
                }
                else Frame++;
            }
            Apply();
        }

        void Apply()
        {
            if (frames != null && frames.Length > 0) sr.sprite = frames[Frame];
        }

        public Color Tint { get { return sr.color; } set { sr.color = value; } }

        SpriteRenderer flashSr;
        bool flashOn;
        /// <summary>White silhouette overlay (needs readable textures).</summary>
        public bool Flash
        {
            get { return flashOn; }
            set
            {
                flashOn = value;
                if (value && flashSr == null)
                {
                    flashSr = Gfx.Sprite("Flash", sr.transform, sr.transform.position, null, sr.sortingOrder + 1);
                }
                if (flashSr == null) return;
                flashSr.enabled = value;
                if (value) flashSr.sprite = SpriteBank.White(sr.sprite);
            }
        }

        public Transform Bone(string name) { return sr.transform; }
        public int SortingOrder { get { return sr.sortingOrder; } set { sr.sortingOrder = value; } }
    }
}
