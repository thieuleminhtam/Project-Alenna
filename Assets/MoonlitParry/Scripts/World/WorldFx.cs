using System.Collections.Generic;
using UnityEngine;

namespace MoonlitParry
{
    [DefaultExecutionOrder(50)]
    public class CameraFollow : MonoBehaviour
    {
        public static CameraFollow I;
        public Transform target;
        public float minX = -1000f, maxX = 1000f, minY = 4.44f, maxY = 100f;
        public const float PPU = 16f;
        public const float BaseSize = 8.4375f;           // 1080p: 4 screen px per 16 px/unit art pixel (was 6.75)
        /// <summary>HD framing: the camera sits high so only the grass edge of the ground shows (no underground view).</summary>
        public const float RestY = 5.75f, LookUp = 4.5f;   // scaled with BaseSize: same ground line on screen
        Camera cam;
        Vector3 basePos, vel;
        float look, shakeAmt, shakeTime, punch;
        bool hasFocus;
        Vector3 focus;
        float targetSize = BaseSize;

        void Awake()
        {
            I = this;
            cam = GetComponent<Camera>();
        }

        /// <summary>Look at a point (cutscenes) and zoom in. Pass null to return to the player.</summary>
        public void Focus(Vector3? point, float size = 4.6f)
        {
            hasFocus = point.HasValue;
            if (hasFocus) focus = point.Value;
            targetSize = hasFocus ? size : BaseSize;
        }

        public void Punch(float amount) { punch = Mathf.Max(punch, amount); }

        public void Snap()
        {
            if (target == null) return;
            cam.orthographicSize = targetSize;
            basePos = Goal();
            transform.position = new Vector3(basePos.x, basePos.y, -10f);
        }

        public void Shake(float amount, float time)
        {
            shakeAmt = Mathf.Max(shakeAmt, amount);
            shakeTime = Mathf.Max(shakeTime, time);
        }

        Vector3 Goal()
        {
            Vector3 p = hasFocus ? focus : target.position + new Vector3(look, LookUp, 0f);
            float halfW = cam.orthographicSize * cam.aspect;
            float halfH = cam.orthographicSize;
            float x = p.x;
            if (maxX - minX < halfW * 2f) x = (minX + maxX) * 0.5f;
            else x = Mathf.Clamp(x, minX + halfW, maxX - halfW);
            // keep the ground line in a comfortable place; when zoomed in the min height drops accordingly
            float lowY = minY - (BaseSize - halfH);
            float y = Mathf.Clamp(p.y, lowY, maxY);
            return new Vector3(x, y, -10f);
        }

        void LateUpdate()
        {
            if (target == null) return;
            float dt = Time.deltaTime;
            float udt = Time.unscaledDeltaTime;
            var pc = target.GetComponent<PlayerController>();
            int facing = pc != null ? pc.Facing : 1;
            look = Mathf.MoveTowards(look, facing * 1.6f, dt * 3f);

            punch = Mathf.MoveTowards(punch, 0f, udt * 1.5f);
            float size = Mathf.Lerp(cam.orthographicSize, targetSize - punch, 1f - Mathf.Exp(-udt * 4f));
            cam.orthographicSize = size;

            basePos = Vector3.SmoothDamp(basePos, Goal(), ref vel, hasFocus ? 0.35f : 0.13f, Mathf.Infinity, Mathf.Max(hasFocus ? udt : dt, 1e-5f));

            float x = basePos.x;           // HD art: no pixel snapping
            float y = basePos.y;
            if (shakeTime > 0f)
            {
                shakeTime -= udt;
                x += Random.Range(-shakeAmt, shakeAmt);
                y += Random.Range(-shakeAmt, shakeAmt);
                if (shakeTime <= 0f) shakeAmt = 0f;
            }
            transform.position = new Vector3(x, y, -10f);
        }
    }

    /// <summary>Infinite horizontally-wrapping parallax layers (sky, mountains, forest, fog, ruins, foreground).</summary>
    [DefaultExecutionOrder(100)]
    public class ParallaxBackground : MonoBehaviour
    {
        class Layer
        {
            public Transform root;
            public Transform[] copies;
            public float factor, yOffset, drift, width;
            public bool tiled, screenBottom;
            public int group;               // 0 = always, 1 = wilds (fade out in the arena), 2 = colosseum (fade in)
            public SpriteRenderer[] srs;
        }

        readonly List<Layer> layers = new List<Layer>();
        Camera cam;
        float driftT;
        float arenaX = float.MaxValue;

        /// <summary>World x where the colosseum backdrop takes over (boss gate).</summary>
        public void SetArenaX(float x) { arenaX = x; }

        bool hd;

        public void Build(Camera c)
        {
            cam = c;
            if (Resources.Load<Sprite>("HD/BG/sky") != null) { BuildHD(); return; }
            Add("BG/sky", 0f, 0f, Order.Sky, false);
            Add("BG/mountains", 0.06f, 0f, Order.Mountains, true);
            Add("BG/forest", 0.18f, 0f, Order.Forest, true).group = 1;
            Add("BG/fog", 0.28f, -2.6f, Order.Fog, true, 0.25f).group = 1;
            Add("BG/ruins", 0.42f, 0f, Order.Ruins, true).group = 1;
            Add("BG/col_far", 0.22f, 0f, Order.Forest + 1, true).group = 2;
            Add("BG/col_near", 0.45f, 0f, Order.Ruins + 1, true).group = 2;
            var fg = Add("BG/foreground", 1.3f, 0f, Order.Foreground, true);
            fg.screenBottom = true;
        }

        /// <summary>
        /// Layered moonlit dark-fantasy backdrop (Tools/ArtGenerator/hd/env_dark.py; keep LAYERS there in sync): starry
        /// sky + moon, cloud banks, pale peaks, castle crags, blossom hills with a ruined aqueduct, three depths of
        /// cherry forest (with ruined towers / abbey wall) or the colosseum wall + colonnades in the arena, violet mist,
        /// moon shafts, giant near cherry trees / broken columns, dark meadow with fallen petals. Clouds do not move.
        /// </summary>
        void BuildHD()
        {
            hd = true;
            Add("BG/sky", 0f, 0f, Order.Sky, false);
            AddHD("clouds", 0.012f, 2.6f, -98, 0, 0f);
            AddHD("peaks", 0.025f, -0.5f, -96, 0, 0f);
            AddHD("crags", 0.05f, -0.6f, -94, 0, 0f);
            AddHD("hills", 0.09f, -0.7f, -92, 0, 0f);
            AddHD("farforest", 0.15f, -0.8f, -90, 1, 0f);
            AddHD("arena_far", 0.16f, -0.8f, -90, 2, 0f);
            AddHD("midfar", 0.22f, -0.9f, -88, 1, 0f);
            AddHD("mist", 0.26f, -1.4f, -87, 0, 0f);
            AddHD("shafts", 0.27f, -1.5f, -86, 0, 0f);
            AddHD("midforest", 0.32f, -0.9f, -84, 1, 0f);
            AddHD("arena_mid", 0.3f, -0.9f, -84, 2, 0f);
            AddHD("near", 0.48f, -1.0f, -80, 1, 0f);
            AddHD("arena_near", 0.48f, -1.0f, -80, 2, 0f);
            AddHD("meadow", 0.7f, -6.6f, -75, 0, 0f);      // 6 units of fill below the grass line (meadow.png is 11 u tall)
            var fg = Add("BG/foreground", 1.3f, 0f, Order.Foreground, true);
            fg.screenBottom = true;
        }

        Layer AddHD(string name, float factor, float bottom, int order, int group, float drift)
        {
            var L = Add("BG/" + name, factor, 0f, order, true, drift);
            float h = L.srs[0].sprite != null ? L.srs[0].sprite.bounds.size.y : 8f;
            L.yOffset = bottom + h * 0.5f - CameraFollow.RestY;
            L.group = group;
            return L;
        }

        Layer Add(string sprite, float factor, float yOff, int order, bool tiled, float drift = 0f)
        {
            var s = SpriteBank.One(sprite);
            var L = new Layer { factor = factor, yOffset = yOff, tiled = tiled, drift = drift };
            L.root = new GameObject(sprite.Replace("BG/", "bg_")).transform;
            L.root.SetParent(transform, false);
            int n = tiled ? 3 : 1;
            L.copies = new Transform[n];
            L.srs = new SpriteRenderer[n];
            L.width = s != null ? s.bounds.size.x : 24f;
            for (int i = 0; i < n; i++)
            {
                var sr = Gfx.Sprite("copy" + i, L.root, Vector3.zero, s, order);
                L.copies[i] = sr.transform;
                L.srs[i] = sr;
            }
            layers.Add(L);
            return L;
        }

        void LateUpdate()
        {
            if (cam == null) return;
            driftT += Time.deltaTime;
            Vector3 c = cam.transform.position;
            float halfH = cam.orthographicSize;
            float blend = Mathf.Clamp01((c.x - (arenaX - 6f)) / 10f);
            foreach (var L in layers)
            {
                if (L.group != 0)
                {
                    float a = L.group == 1 ? 1f - blend : blend;
                    bool on = a > 0.01f;
                    foreach (var r in L.srs)
                    {
                        r.enabled = on;
                        if (on) r.color = new Color(1f, 1f, 1f, a);
                    }
                    if (!on) continue;
                }
                float restY = hd ? CameraFollow.RestY : 2.75f;
                float lh = L.srs[0].sprite != null ? L.srs[0].sprite.bounds.size.y : 3f;
                // HD backdrop: every layer keeps its height in the world (only the horizontal parallax remains), so
                // jumping or standing on high ground never lifts the painted ground line off the real one.
                float y = L.screenBottom ? (hd ? c.y - halfH - 0.5f + lh * 0.5f : c.y - halfH + 1.5f)
                                         : hd ? restY + L.yOffset
                                              : c.y + L.yOffset - (c.y - restY) * L.factor * 0.35f;
                if (!L.tiled)
                {
                    L.copies[0].position = new Vector3(c.x, c.y, 0f);
                    float need = halfH * 2f * cam.aspect / Mathf.Max(0.01f, L.width);
                    float s = Mathf.Max(1f, need, halfH * 2f / 13.5f);
                    L.copies[0].localScale = new Vector3(s, s, 1f);
                    continue;
                }
                float px = c.x * (1f - L.factor) + driftT * L.drift;
                float rel = c.x - px;
                float baseX = px + Mathf.Floor(rel / L.width) * L.width + L.width * 0.5f;
                for (int i = 0; i < L.copies.Length; i++)
                {
                    float x = baseX + (i - 1) * L.width;
                    if (!hd)
                    {
                        x = Mathf.Round(x * CameraFollow.PPU) / CameraFollow.PPU;
                        L.copies[i].position = new Vector3(x, Mathf.Round(y * CameraFollow.PPU) / CameraFollow.PPU, 0f);
                    }
                    else L.copies[i].position = new Vector3(x, y, 0f);
                }
            }
        }
    }

    /// <summary>
    /// Falling cherry petals (three depths: behind the near trees, among the props, in front of the fighters) blown to
    /// the left by the night wind with gusts, plus a few cold moon motes. Pixel fallback keeps the old fireflies.
    /// </summary>
    public class AmbientParticles : MonoBehaviour
    {
        class Mote
        {
            public Transform t; public SpriteRenderer sr; public Vector2 pos, vel; public float phase, scale, spin; public int depth; public bool firefly;
        }

        readonly List<Mote> motes = new List<Mote>();
        Camera cam;
        bool hd;

        public void Build(Camera c)
        {
            cam = c;
            hd = Resources.Load<Sprite>("HD/FX/pollen") != null;
            var glow = hd ? SpriteBank.One("FX/pollen") : SpriteBank.One("FX/firefly");
            var pix = hd ? SpriteBank.One("FX/petal") : SpriteBank.One("FX/pixel");
            int motesN = hd ? 12 : 22, petalsN = hd ? 72 : 24;
            for (int i = 0; i < motesN + petalsN; i++)
            {
                bool ff = i < motesN;
                int depth = ff ? 0 : (i % 20 < 7 ? 0 : i % 20 < 15 ? 1 : 2);        // 35% back, 40% mid, 25% front
                int order = ff ? -82 : depth == 0 ? -77 : depth == 1 ? Order.Decor + 1 : Order.Fx - 1;
                var sr = Gfx.Sprite(ff ? "mote" : "petal", transform, Vector3.zero, ff ? glow : pix, order);
                var m = new Mote { t = sr.transform, sr = sr, firefly = ff, depth = depth, phase = Random.value * 10f };
                if (!ff && !hd) sr.transform.localScale = new Vector3(2f, 1f, 1f);
                Respawn(m, true);
                motes.Add(m);
            }
        }

        void Respawn(Mote m, bool anywhere)
        {
            Vector3 c = cam.transform.position;
            float hw = cam.orthographicSize * cam.aspect + 2f, hh = cam.orthographicSize + 1f;
            if (m.firefly)
            {
                m.pos = new Vector2(c.x + Random.Range(-hw, hw), c.y + Random.Range(-hh * 0.7f, hh * 0.5f));
                m.vel = new Vector2(Random.Range(-0.3f, 0.3f), Random.Range(-0.2f, 0.2f));
                m.sr.color = hd ? new Color(0.82f, 0.88f, 1f) : Random.value < 0.5f ? new Color(0.85f, 1f, 0.6f) : new Color(0.65f, 0.9f, 1f);
                return;
            }
            // enter from the top or from the windward (right) edge
            if (anywhere) m.pos = new Vector2(c.x + Random.Range(-hw, hw), c.y + Random.Range(-hh, hh));
            else if (Random.value < 0.6f) m.pos = new Vector2(c.x + Random.Range(-hw * 0.6f, hw + 0.4f), c.y + hh);
            else m.pos = new Vector2(c.x + hw, c.y + Random.Range(-hh * 0.4f, hh));
            float sp = m.depth == 0 ? 0.65f : m.depth == 1 ? 1f : 1.35f;          // nearer petals drift faster
            m.vel = new Vector2(Random.Range(-1.5f, -0.55f), Random.Range(-1.0f, -0.5f)) * sp;
            m.spin = Random.Range(0.6f, 1.6f) * (Random.value < 0.5f ? -1f : 1f);
            if (!hd) { m.sr.color = Random.value < 0.6f ? new Color(0.95f, 0.92f, 1f, 0.9f) : new Color(1f, 0.75f, 0.85f, 0.9f); return; }
            float r = Random.value;
            Color col = r < 0.45f ? new Color(1f, 0.74f, 0.86f) : r < 0.8f ? new Color(0.94f, 0.6f, 0.78f) : new Color(1f, 0.9f, 0.95f);
            if (m.depth == 0) { col = Color.Lerp(col, new Color(0.42f, 0.3f, 0.5f), 0.45f); col.a = 0.8f; m.scale = Random.Range(0.45f, 0.62f); }
            else if (m.depth == 1) { col = Color.Lerp(col, new Color(0.5f, 0.38f, 0.56f), 0.18f); col.a = 0.92f; m.scale = Random.Range(0.62f, 0.82f); }
            else { col.a = 0.95f; m.scale = Random.Range(0.85f, 1.15f); }
            m.sr.color = col;
        }

        void LateUpdate()
        {
            if (cam == null) return;
            float dt = Time.deltaTime, t = Time.time;
            Vector3 c = cam.transform.position;
            float hw = cam.orthographicSize * cam.aspect + 2.5f, hh = cam.orthographicSize + 1.5f;
            // night wind: steady breeze to the left with a gust every few seconds
            float gust = Mathf.Max(0f, Mathf.Sin(t * 0.45f)) * Mathf.Max(0f, Mathf.Sin(t * 1.3f + 1f));
            foreach (var m in motes)
            {
                if (m.firefly)
                {
                    m.pos += (m.vel + new Vector2(Mathf.Sin(t * 0.7f + m.phase), Mathf.Cos(t * 0.9f + m.phase * 1.3f)) * 0.35f) * dt;
                    var col = m.sr.color;
                    col.a = 0.3f + 0.6f * Mathf.Max(0f, Mathf.Sin(t * 1.6f + m.phase));
                    m.sr.color = col;
                }
                else
                {
                    float sp = m.depth == 0 ? 0.65f : m.depth == 1 ? 1f : 1.35f;
                    m.pos += new Vector2(m.vel.x - gust * 2.2f * sp + Mathf.Sin(t * 2f + m.phase) * 0.55f, m.vel.y + Mathf.Cos(t * 1.7f + m.phase) * 0.25f) * dt;
                    if (hd)
                    {
                        float flip = Mathf.Sin(t * 3.4f * Mathf.Abs(m.spin) + m.phase);        // tumbling: the petal shows its edge
                        m.t.localScale = new Vector3((Mathf.Abs(flip) * 0.75f + 0.25f) * m.scale, m.scale, 1f);
                        m.t.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(t * 1.1f + m.phase) * 70f + t * 40f * m.spin);
                    }
                    else m.t.localScale = new Vector3(Mathf.Sin(t * 5f + m.phase) > 0f ? 2f : 1f, 1f, 1f);
                }
                if (Mathf.Abs(m.pos.x - c.x) > hw || Mathf.Abs(m.pos.y - c.y) > hh) Respawn(m, false);
                m.t.position = hd ? new Vector3(m.pos.x, m.pos.y, 0f) : new Vector3(Mathf.Round(m.pos.x * 16f) / 16f, Mathf.Round(m.pos.y * 16f) / 16f, 0f);
            }
        }
    }

    /// <summary>Flickering glow attached to lanterns / flowers.</summary>
    public class GlowFlicker : MonoBehaviour
    {
        public float baseAlpha = 0.35f, amount = 0.08f, speed = 7f;
        SpriteRenderer sr;
        float seed;
        void Start() { sr = GetComponent<SpriteRenderer>(); seed = Random.value * 10f; }
        void Update()
        {
            if (sr == null) return;
            var c = sr.color;
            c.a = baseAlpha + amount * (Mathf.PerlinNoise(Time.time * speed, seed) - 0.5f) * 2f;
            sr.color = c;
        }
    }

}
