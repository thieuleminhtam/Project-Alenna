using UnityEngine;

namespace MoonlitParry
{
    /// <summary>Bonfire: rest (R) to refill hearts/flasks and set the checkpoint. Enemies respawn.</summary>
    public class Bonfire : MonoBehaviour
    {
        /// <summary>Bonfires sit on the very top layer (above grass and the dark foreground strip) so they are never hidden.</summary>
        public const int Front = Order.Foreground + 2;

        public int Id;
        public bool Lit { get; private set; }
        SpriteRenderer fire, unlit, glow;
        SpriteAnim anim;
        AudioSource loop;
        float seed;

        public static Bonfire Create(Transform parent, Vector3 basePos, int id, bool lit)
        {
            var go = new GameObject("Bonfire_" + id);
            go.transform.SetParent(parent, false);
            go.transform.position = basePos;
            var b = go.AddComponent<Bonfire>();
            b.Id = id;
            b.seed = Random.value * 10f;
            b.unlit = Gfx.Sprite("unlit", go.transform, basePos, SpriteBank.One("Decor/bonfire_unlit"), Bonfire.Front);
            // the flame frames are centre-pivoted and include the shrine: lift them by half their height
            var fx = SpriteBank.Set("FX");
            Sprite[] bf;
            float half = fx.TryGetValue("bonfire", out bf) && bf.Length > 0 && bf[0] != null ? bf[0].bounds.extents.y : 1.06f;
            b.fire = Gfx.Sprite("fire", go.transform, basePos + new Vector3(0f, half, 0f), null, Bonfire.Front);
            b.anim = new SpriteAnim(b.fire, fx);
            b.anim.Play("bonfire", 10f, true, true);
            b.glow = Gfx.Sprite("glow", go.transform, basePos + new Vector3(0f, 0.9f, 0f), Fx.Glow, Bonfire.Front - 1);
            b.glow.transform.localScale = new Vector3(5f, 4f, 1f);
            b.loop = Sfx.Loop(go, "fire_loop");
            b.SetLit(lit, false);
            return b;
        }

        public void SetLit(bool lit, bool fx)
        {
            Lit = lit;
            fire.enabled = lit;
            unlit.enabled = !lit;
            glow.color = lit ? new Color(1f, 0.6f, 0.3f, 0.35f) : new Color(1f, 0.5f, 0.3f, 0.08f);
            if (fx && lit)
            {
                Fx.Rise(transform.position + Vector3.up * 0.6f, new Color(1f, 0.7f, 0.3f), 20, 0.6f, 3f);
                Sfx.Play("rest", 0.8f, 0f);
            }
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (Lit) anim.Tick(dt);
            if (Lit && Random.value < 0.08f)
                FxAnim.Particle(Fx.EmberSprite, transform.position + new Vector3(Random.Range(-0.3f, 0.3f), 1.2f, 0f),
                    new Vector2(Random.Range(-0.3f, 0.3f), Random.Range(1f, 2f)), Random.Range(0.6f, 1.2f), new Color(1f, 0.6f, 0.2f), -0.5f, Fx.EmberScale, Bonfire.Front + 1);
            var c = glow.color;
            float baseA = Lit ? 0.35f : 0.08f;
            c.a = baseA + (Lit ? 0.06f : 0.02f) * (Mathf.PerlinNoise(Time.time * 6f, seed) - 0.5f) * 2f;
            glow.color = c;
            var p = GameManager.I != null ? GameManager.I.Player : null;
            if (loop != null)
            {
                float d = p != null ? Mathf.Abs(p.transform.position.x - transform.position.x) : 99f;
                loop.volume = Lit ? Mathf.Clamp01(1f - (d - 2f) / 10f) * 0.45f * Sfx.UserSfx : 0f;
            }
        }
    }

    /// <summary>A barrier that rises out of the ground to lock an area (portcullis or boss door).</summary>
    public class Gate : MonoBehaviour
    {
        Transform spr;
        BoxCollider2D wall;
        float hiddenY, shownY, k, target;
        public bool Closed { get; private set; }

        public static Gate Create(Transform parent, string sprite, float x, float groundY, float width, bool closed)
        {
            var go = new GameObject("Gate_" + sprite);
            go.transform.SetParent(parent, false);
            go.transform.position = new Vector3(x, groundY, 0f);
            var g = go.AddComponent<Gate>();
            var s = SpriteBank.One("Decor/" + sprite);
            float h = s != null ? s.bounds.size.y : 4.5f;
            float gw = s != null ? s.bounds.size.x : width;
            // Hollow Knight style: a narrow side-on grate stored inside the arch above the opening; it slams DOWN across
            // the path. A mask the size of the opening hides it while it is raised.
            g.shownY = groundY - 0.06f;
            g.hiddenY = groundY + h + 0.15f;
            var sr = Gfx.Sprite("door", go.transform, new Vector3(x, closed ? g.shownY : g.hiddenY, 0f), s, Order.Gate);
            sr.maskInteraction = SpriteMaskInteraction.VisibleInsideMask;
            g.spr = sr.transform;
            var mg = new GameObject("opening_mask");
            mg.transform.SetParent(go.transform, false);
            mg.transform.position = new Vector3(x, groundY - 0.6f, 0f);
            mg.transform.localScale = new Vector3(gw + 1.2f, h + 0.6f - 0.06f, 1f);
            var mask = mg.AddComponent<SpriteMask>();
            mask.sprite = UnitSprite();
            var wg = new GameObject("wall");
            wg.layer = Layers.Ground;
            wg.transform.SetParent(go.transform, false);
            wg.transform.position = new Vector3(x, groundY + 4f, 0f);
            g.wall = wg.AddComponent<BoxCollider2D>();
            g.wall.size = new Vector2(width, 8f);
            g.Closed = closed;
            g.wall.enabled = closed;
            g.k = g.target = closed ? 1f : 0f;
            return g;
        }

        static Sprite unit;

        /// <summary>1 x 1 unit white sprite with a bottom-centre pivot (for the opening mask).</summary>
        static Sprite UnitSprite()
        {
            if (unit == null)
            {
                var t = Texture2D.whiteTexture;
                unit = Sprite.Create(t, new Rect(0, 0, t.width, t.height), new Vector2(0.5f, 0f), t.width);
            }
            return unit;
        }

        public void Close()
        {
            if (Closed) return;
            Closed = true;
            wall.enabled = true;
            target = 1f;
            Sfx.Play("door", 0.9f, 0f);
            if (CameraFollow.I != null) CameraFollow.I.Shake(0.12f, 0.6f);
        }

        public void Open()
        {
            if (!Closed) return;
            Closed = false;
            wall.enabled = false;
            target = 0f;
            Sfx.Play("door", 0.7f, 0f);
        }

        void Update()
        {
            if (Mathf.Approximately(k, target)) return;
            k = Mathf.MoveTowards(k, target, Time.deltaTime * (target > k ? 3.5f : 1.2f));
            float e = k * k;                                     // slams down accelerating, lifts off and slows near the top
            var p = spr.position;
            p.y = Mathf.Lerp(hiddenY, shownY, e);
            spr.position = p;
            if (target > 0.5f && k >= 1f) Fx.DustBig(new Vector3(p.x, shownY + 0.06f, 0f), 0.8f);
        }
    }
}
