using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace MoonlitParry
{
    // ------------------------------------------------------------------ data (Resources/HD/Rigs/<name>.json, written by Tools/ArtGenerator/hd/rig.py)
    [Serializable] public class RigSpriteDef { public string n; public float x, y, w, h, px, py; }
    [Serializable] public class RigBoneDef { public string n; public int p; public float x, y, r, sx = 1f, sy = 1f; public string s; public int z; }
    [Serializable] public class RigRibbonDef { public string n; public int b; public string t; public float[] pts; public float[] w; public int z; public float[] k; public float g, d, wind; public int pins; public float fl, drag; public int floor; }
    [Serializable] public class RigKeyDef { public int e; public float[] v; public string[] s; public int[] z; }
    [Serializable] public class RigClipDef { public string n; public int cr; public float blend; public RigKeyDef[] keys; }
    [Serializable] public class RigDef { public string name; public float ppu; public string atlas; public int w, h; public RigSpriteDef[] sprites; public RigBoneDef[] bones; public RigRibbonDef[] ribbons; public RigClipDef[] clips; }

    /// <summary>Loaded rig: definition + atlas + sprites (shared between instances).</summary>
    public class RigData
    {
        public RigDef def;
        public Texture2D atlas;
        public readonly Dictionary<string, Sprite> sprites = new Dictionary<string, Sprite>();
        public readonly Dictionary<string, Rect> uv = new Dictionary<string, Rect>();
        public readonly Dictionary<string, RigClipDef> clips = new Dictionary<string, RigClipDef>();

        static readonly Dictionary<string, RigData> cache = new Dictionary<string, RigData>();

        public static bool Exists(string name)
        {
            RigData d;
            if (cache.TryGetValue(name, out d)) return d != null;
            return Resources.Load<TextAsset>("HD/Rigs/" + name) != null;
        }

        public static RigData Load(string name)
        {
            RigData d;
            if (cache.TryGetValue(name, out d)) return d;
            var ta = Resources.Load<TextAsset>("HD/Rigs/" + name);
            if (ta == null) { Debug.LogError("[MoonlitParry] Missing rig HD/Rigs/" + name + ".json"); cache[name] = null; return null; }
            d = new RigData { def = JsonUtility.FromJson<RigDef>(ta.text) };
            d.atlas = Resources.Load<Texture2D>(d.def.atlas);
            if (d.atlas == null) { Debug.LogError("[MoonlitParry] Missing rig atlas " + d.def.atlas); cache[name] = null; return null; }
            float sx = d.atlas.width / (float)Mathf.Max(1, d.def.w), sy = d.atlas.height / (float)Mathf.Max(1, d.def.h);   // atlas may be downscaled by the importer
            foreach (var s in d.def.sprites)
            {
                var r = new Rect(s.x * sx, s.y * sy, s.w * sx, s.h * sy);
                d.sprites[s.n] = Sprite.Create(d.atlas, r, new Vector2(s.px / s.w, s.py / s.h), d.def.ppu * sx, 0, SpriteMeshType.FullRect);
                d.sprites[s.n].name = name + "_" + s.n;
                d.uv[s.n] = new Rect(r.x / d.atlas.width, r.y / d.atlas.height, r.width / d.atlas.width, r.height / d.atlas.height);
            }
            foreach (var c in d.def.clips) d.clips[c.n] = c;
            cache[name] = d;
            return d;
        }
    }

    /// <summary>
    /// Cut-out skeletal animator with the same API as SpriteAnim (Play / Tick / Frame / Finished / SetFrame), so gameplay
    /// frame thresholds keep working. Visual poses are interpolated between keys (Catmull-Rom + per-key easing) and
    /// cross-faded between clips; ribbons (hair, coat tails, flags) are simulated with Verlet chains.
    /// </summary>
    public class RigAnim : IAnim
    {
        readonly RigData data;
        readonly Transform root;
        readonly Transform[] bones;
        readonly SpriteRenderer[] srs;
        readonly Sprite[] defaultSprite;
        readonly int nb;
        readonly List<RigRibbon> ribbons = new List<RigRibbon>();
        readonly float[] pose, from;
        readonly string[] curSwap;
        RigClipDef clip;
        float fps, t, blendT, blendDur;
        bool loop, flash;
        Color tint = Color.white;

        public string Name { get; private set; }
        public int Frame { get; private set; }
        public bool Finished { get; private set; }
        public int Length { get { return clip == null ? 0 : clip.keys.Length; } }
        public bool Valid { get { return data != null; } }
        public Transform Root { get { return root; } }
        public SortingGroup Group { get; private set; }

        public RigAnim(Transform parent, string rigName, int sortingOrder)
        {
            data = RigData.Load(rigName);
            var go = new GameObject("Rig_" + rigName);
            go.transform.SetParent(parent, false);
            root = go.transform;
            Group = go.AddComponent<SortingGroup>();
            Group.sortingOrder = sortingOrder;
            if (data == null) return;
            var def = data.def;
            nb = def.bones.Length;
            bones = new Transform[nb];
            srs = new SpriteRenderer[nb];
            defaultSprite = new Sprite[nb];
            pose = new float[nb * 5];
            from = new float[nb * 5];
            curSwap = new string[nb];
            var mat = Gfx.HDMat;
            for (int i = 0; i < nb; i++)
            {
                var b = def.bones[i];
                var bgo = new GameObject(b.n);
                bgo.transform.SetParent(b.p < 0 ? root : bones[b.p], false);
                bgo.transform.localPosition = new Vector3(b.x, b.y, 0f);
                bgo.transform.localRotation = Quaternion.Euler(0f, 0f, b.r);
                bgo.transform.localScale = new Vector3(b.sx, b.sy, 1f);
                bones[i] = bgo.transform;
                if (!string.IsNullOrEmpty(b.s))
                {
                    Sprite sp;
                    data.sprites.TryGetValue(b.s, out sp);
                    defaultSprite[i] = sp;
                    var sr = bgo.AddComponent<SpriteRenderer>();
                    sr.sprite = sp;
                    sr.sortingOrder = b.z;
                    sr.sharedMaterial = mat;
                    srs[i] = sr;
                }
                pose[i * 5] = b.x; pose[i * 5 + 1] = b.y; pose[i * 5 + 2] = b.r; pose[i * 5 + 3] = b.sx; pose[i * 5 + 4] = b.sy;
            }
            if (def.ribbons != null)
                foreach (var rd in def.ribbons) ribbons.Add(new RigRibbon(this, rd, data, root, bones[rd.b]));
            go.AddComponent<RigView>().anim = this;
        }

        public int SortingOrder { get { return Group.sortingOrder; } set { Group.sortingOrder = value; } }

        public Transform Bone(string name)
        {
            if (data == null) return root;
            for (int i = 0; i < nb; i++) if (data.def.bones[i].n == name) return bones[i];
            return root;
        }

        public bool Has(string c) { return data != null && data.clips.ContainsKey(c); }

        public int CountOf(string c)
        {
            RigClipDef cd;
            return data != null && data.clips.TryGetValue(c, out cd) ? cd.keys.Length : 0;
        }

        public void Play(string c, float fps, bool loop, bool restart = false)
        {
            if (data == null) return;
            if (!restart && c == Name)
            {
                this.fps = fps;
                this.loop = loop;
                return;
            }
            RigClipDef cd;
            if (!data.clips.TryGetValue(c, out cd) || cd.keys == null || cd.keys.Length == 0)
            {
                Debug.LogWarning("[MoonlitParry] Missing rig clip " + c);
                return;
            }
            StartBlend(cd.blend > 0f ? cd.blend : 0.08f);
            clip = cd;
            Name = c;
            this.fps = fps;
            this.loop = loop;
            Frame = 0;
            t = 0f;
            Finished = false;
        }

        public void SetFrame(int f)
        {
            if (clip == null) return;
            int nf = Mathf.Clamp(f, 0, clip.keys.Length - 1);
            if (nf != Frame) StartBlend(0.045f);
            Frame = nf;
            t = 0f;
        }

        void StartBlend(float dur)
        {
            if (clip == null) { blendDur = 0f; return; }
            Array.Copy(pose, from, pose.Length);
            blendT = 0f;
            blendDur = dur;
        }

        public void Tick(float dt)
        {
            if (clip == null || Finished) return;
            t += dt * fps;
            while (t >= 1f)
            {
                t -= 1f;
                if (Frame + 1 >= clip.keys.Length)
                {
                    if (loop) Frame = 0;
                    else { Finished = true; t = 0f; break; }
                }
                else Frame++;
            }
        }

        // ------------------------------------------------------------------ look
        public Color Tint
        {
            get { return tint; }
            set
            {
                tint = value;
                if (srs == null) return;
                foreach (var sr in srs) if (sr != null) sr.color = value;
                foreach (var r in ribbons) r.SetColor(value);
            }
        }

        public bool Flash
        {
            get { return flash; }
            set
            {
                if (flash == value || srs == null) return;
                flash = value;
                var m = value ? Gfx.HDFlashMat : Gfx.HDMat;
                foreach (var sr in srs) if (sr != null) sr.sharedMaterial = m;
                foreach (var r in ribbons) r.SetFlash(value);
            }
        }

        public void SetVisible(bool on)
        {
            if (srs == null) return;
            foreach (var sr in srs) if (sr != null) sr.enabled = on;
            foreach (var r in ribbons) r.SetVisible(on);
        }

        // ------------------------------------------------------------------ evaluation
        static float Ease(int e, float u)
        {
            switch (e)
            {
                case 1: return u * u * (3f - 2f * u);
                case 2: return u * u;
                case 3: return 1f - (1f - u) * (1f - u);
                case 4: return u * u * u * u;
                case 5: return 0f;
                case 6: { float a = 1f - u; return 1f - a * a * a * a; }
            }
            return u;
        }

        static float CR(float p0, float p1, float p2, float p3, float u)
        {
            float u2 = u * u, u3 = u2 * u;
            return 0.5f * ((2f * p1) + (-p0 + p2) * u + (2f * p0 - 5f * p1 + 4f * p2 - p3) * u2 + (-p0 + 3f * p1 - 3f * p2 + p3) * u3);
        }

        /// <summary>Called from RigView.LateUpdate (after gameplay moved the body).</summary>
        public void Apply(float dt)
        {
            if (data == null || clip == null) return;
            var keys = clip.keys;
            int n = keys.Length;
            int f = Mathf.Clamp(Frame, 0, n - 1);
            float u = Finished || (!loop && f >= n - 1) ? 0f : Mathf.Clamp01(t);
            int i0, i2, i3;
            if (loop) { i0 = (f - 1 + n) % n; i2 = (f + 1) % n; i3 = (f + 2) % n; }
            else { i0 = Mathf.Max(0, f - 1); i2 = Mathf.Min(n - 1, f + 1); i3 = Mathf.Min(n - 1, f + 2); }
            var k1 = keys[f];
            float e = Ease(k1.e, u);
            float[] A = keys[i0].v, B = k1.v, C = keys[i2].v, D = keys[i3].v;
            bool cr = clip.cr != 0;
            float bw = 1f;
            if (blendDur > 0f && blendT < blendDur)
            {
                blendT += dt;
                bw = Mathf.Clamp01(blendT / blendDur);
                bw = bw * bw * (3f - 2f * bw);
            }
            int len = Mathf.Min(B.Length, nb * 5);
            for (int j = 0; j < len; j++)
            {
                float a0 = A[j], b0 = B[j], c0 = C[j], d0 = D[j];
                if (j % 5 == 2)
                {
                    // rotations take the short way round, so looping spins (rolls) can pass through 360
                    a0 = b0 + Mathf.DeltaAngle(b0, a0);
                    c0 = b0 + Mathf.DeltaAngle(b0, c0);
                    d0 = c0 + Mathf.DeltaAngle(c0, d0);
                }
                float val = cr ? CR(a0, b0, c0, d0, e) : b0 + (c0 - b0) * e;
                if (bw < 1f)
                {
                    float a = from[j];
                    if (j % 5 == 2) val = a + Mathf.DeltaAngle(a, val) * bw;
                    else val = a + (val - a) * bw;
                }
                pose[j] = val;
            }
            for (int i = 0; i < nb; i++)
            {
                int o = i * 5;
                var tr = bones[i];
                tr.localPosition = new Vector3(pose[o], pose[o + 1], 0f);
                tr.localRotation = Quaternion.Euler(0f, 0f, pose[o + 2]);
                tr.localScale = new Vector3(pose[o + 3], pose[o + 4], 1f);
                var sr = srs[i];
                if (sr == null)
                {
                    // a bone without a default sprite may still get one through a swap
                    string sw0 = k1.s != null && i < k1.s.Length ? k1.s[i] : null;
                    if (!string.IsNullOrEmpty(sw0) && sw0 != "-") EnsureRenderer(i, sw0);
                    continue;
                }
                string sw = k1.s != null && i < k1.s.Length ? k1.s[i] : null;
                if (sw != curSwap[i])
                {
                    curSwap[i] = sw;
                    if (string.IsNullOrEmpty(sw)) { sr.sprite = defaultSprite[i]; sr.enabled = true; }
                    else if (sw == "-") sr.enabled = false;
                    else { Sprite sp; sr.sprite = data.sprites.TryGetValue(sw, out sp) ? sp : defaultSprite[i]; sr.enabled = true; }
                }
                int z = k1.z != null && i < k1.z.Length && k1.z[i] != -9999 ? k1.z[i] : data.def.bones[i].z;
                if (sr.sortingOrder != z) sr.sortingOrder = z;
            }
            foreach (var r in ribbons) r.Step(dt);
        }

        void EnsureRenderer(int i, string sprite)
        {
            var sr = bones[i].gameObject.AddComponent<SpriteRenderer>();
            sr.sharedMaterial = flash ? Gfx.HDFlashMat : Gfx.HDMat;
            sr.color = tint;
            sr.sortingOrder = data.def.bones[i].z;
            Sprite sp;
            data.sprites.TryGetValue(sprite, out sp);
            sr.sprite = sp;
            srs[i] = sr;
            defaultSprite[i] = null;
        }
    }

    /// <summary>Drives a RigAnim's pose + ribbons after gameplay has moved the character.</summary>
    [DefaultExecutionOrder(60)]
    public class RigView : MonoBehaviour
    {
        public RigAnim anim;
        void LateUpdate() { if (anim != null) anim.Apply(Time.deltaTime); }
    }

    /// <summary>Verlet chain rendered as a textured quad strip (hair, coat tails, horse mane, flag).</summary>
    public class RigRibbon
    {
        readonly RigRibbonDef d;
        readonly Transform anchor, holder, rigRoot;
        readonly Vector2[] rest;
        readonly Vector2[] p, prev;
        readonly float[] restLen;
        readonly Mesh mesh;
        readonly Vector3[] verts;
        readonly Color[] cols;
        readonly MeshRenderer mr;
        readonly Material mat;
        bool init;
        float time;

        public RigRibbon(RigAnim owner, RigRibbonDef def, RigData data, Transform rigRoot, Transform anchor)
        {
            d = def;
            this.anchor = anchor;
            this.rigRoot = rigRoot;
            int n = def.pts.Length / 2;
            rest = new Vector2[n];
            for (int i = 0; i < n; i++) rest[i] = new Vector2(def.pts[i * 2], def.pts[i * 2 + 1]);
            p = new Vector2[n];
            prev = new Vector2[n];
            restLen = new float[n];
            for (int i = 1; i < n; i++) restLen[i] = (rest[i] - rest[i - 1]).magnitude;
            var go = new GameObject("ribbon_" + def.n);
            go.transform.SetParent(rigRoot, false);
            holder = go.transform;
            var mf = go.AddComponent<MeshFilter>();
            mr = go.AddComponent<MeshRenderer>();
            mat = Gfx.HDTexMat(data.atlas);
            mr.sharedMaterial = mat;
            mr.sortingOrder = def.z;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mesh = new Mesh { name = "ribbon" };
            mesh.MarkDynamic();
            verts = new Vector3[n * 2];
            cols = new Color[n * 2];
            var uvs = new Vector2[n * 2];
            Rect r;
            if (!data.uv.TryGetValue(def.t, out r)) r = new Rect(0, 0, 1, 1);
            for (int i = 0; i < n; i++)
            {
                float u = r.xMin + r.width * i / (float)(n - 1);
                uvs[i * 2] = new Vector2(u, r.yMax);
                uvs[i * 2 + 1] = new Vector2(u, r.yMin);
                cols[i * 2] = cols[i * 2 + 1] = Color.white;
            }
            var tris = new int[(n - 1) * 6];
            for (int i = 0; i < n - 1; i++)
            {
                int a = i * 2;
                tris[i * 6] = a; tris[i * 6 + 1] = a + 2; tris[i * 6 + 2] = a + 1;
                tris[i * 6 + 3] = a + 1; tris[i * 6 + 4] = a + 2; tris[i * 6 + 5] = a + 3;
            }
            mesh.vertices = verts;
            mesh.uv = uvs;
            mesh.colors = cols;
            mesh.triangles = tris;
            mf.sharedMesh = mesh;
        }

        public void SetColor(Color c)
        {
            for (int i = 0; i < cols.Length; i++) cols[i] = c;
            mesh.colors = cols;
        }

        public void SetFlash(bool on) { if (mat.HasProperty("_Flash")) mat.SetFloat("_Flash", on ? 1f : 0f); }
        public void SetVisible(bool on) { mr.enabled = on; }

        Vector2 Target(int i) { return anchor.TransformPoint(rest[i]); }

        public void Step(float dt)
        {
            int n = p.Length;
            var m = anchor.localToWorldMatrix;
            float sc = Mathf.Sqrt(Mathf.Abs(m.m00 * m.m11 - m.m01 * m.m10));
            if (!init)
            {
                for (int i = 0; i < n; i++) p[i] = prev[i] = Target(i);
                init = true;
            }
            if (dt > 0f)
            {
                int steps = Mathf.Clamp(Mathf.CeilToInt(dt / (1f / 60f)), 1, 4);
                float h = dt / steps;
                for (int s = 0; s < steps; s++) Sim(h, sc);
            }
            Build(m, sc);
        }

        void Sim(float h, float sc)
        {
            int n = p.Length;
            time += h;
            for (int i = 0; i < n; i++)
            {
                if (i < d.pins)
                {
                    prev[i] = p[i];
                    p[i] = Target(i);
                    continue;
                }
                Vector2 vel = (p[i] - prev[i]) * Mathf.Pow(d.d, h * 30f);
                prev[i] = p[i];
                Vector2 acc = new Vector2(d.wind, d.g);
                if (d.fl != 0f)
                {
                    Vector2 seg = p[i] - p[i - 1];
                    float ln = seg.magnitude + 1e-5f;
                    acc += new Vector2(-seg.y, seg.x) / ln * d.fl * Mathf.Sin(time * 10f - i * 1.1f) * (i / (float)n);
                }
                p[i] += vel + acc * h * h;
                float k = 1f - Mathf.Pow(1f - d.k[Mathf.Min(i, d.k.Length - 1)], h * 60f);
                p[i] += (Target(i) - p[i]) * k;
            }
            for (int it = 0; it < 4; it++)
                for (int i = Mathf.Max(1, d.pins); i < n; i++)
                {
                    Vector2 seg = p[i] - p[i - 1];
                    float ln = seg.magnitude + 1e-6f;
                    float diff = (ln - restLen[i] * sc) / ln;
                    if (i - 1 < d.pins) p[i] -= seg * diff;
                    else { p[i - 1] += seg * diff * 0.5f; p[i] -= seg * diff * 0.5f; }
                }
            // hard stretch limit so fast swings never elongate the cloth, plus an optional ground plane at the rig's feet
            float floorY = rigRoot.position.y + 0.03f;
            for (int i = Mathf.Max(1, d.pins); i < n; i++)
            {
                Vector2 seg = p[i] - p[i - 1];
                float ln = seg.magnitude;
                float mx = restLen[i] * sc * 1.08f;
                if (ln > mx) p[i] = p[i - 1] + seg * (mx / ln);
                if (d.floor != 0 && p[i].y < floorY) p[i].y = floorY;
            }
        }

        void Build(Matrix4x4 m, float sc)
        {
            int n = p.Length;
            float det = m.m00 * m.m11 - m.m01 * m.m10;
            float flip = det >= 0f ? 1f : -1f;
            for (int i = 0; i < n; i++)
            {
                Vector2 a = p[Mathf.Max(0, i - 1)], b = p[Mathf.Min(n - 1, i + 1)];
                Vector2 tg = b - a;
                float ln = tg.magnitude + 1e-6f;
                Vector2 nrm = new Vector2(-tg.y, tg.x) / ln * flip;
                float hw = d.w[Mathf.Min(i, d.w.Length - 1)] * 0.5f * sc;
                verts[i * 2] = holder.InverseTransformPoint(p[i] + nrm * hw);
                verts[i * 2 + 1] = holder.InverseTransformPoint(p[i] - nrm * hw);
            }
            mesh.vertices = verts;
            mesh.RecalculateBounds();
        }
    }
}
