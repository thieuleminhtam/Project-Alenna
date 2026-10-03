using UnityEngine;

namespace MoonlitParry
{
    /// <summary>
    /// Slope-aware ground handling for any character. Probes the ground under the feet, sticks the body to
    /// slopes (no sliding, no launching off crests) and turns horizontal speed into speed along the surface.
    /// Origin of the owner = feet.
    /// </summary>
    public class GroundMotor
    {
        readonly Rigidbody2D rb;
        readonly float halfW;
        float ignoreUntil;
        Collider2D ignoreCol;
        float ignoreColUntil;

        public bool Grounded { get; private set; }
        public Vector2 Normal { get; private set; }
        public float GroundY { get; private set; }
        public Collider2D GroundCollider { get; private set; }
        public float AirTime { get; private set; }
        /// <summary>
        /// While standing on a slope the body rests on the highest of the three probes (so it never digs in), which
        /// leaves the middle of the feet hovering above the surface. Sink = that gap; the sprite is drawn this much
        /// lower so the feet stay planted on the slope.
        /// </summary>
        public float Sink { get; private set; }

        public GroundMotor(Rigidbody2D rb, float halfWidth)
        {
            this.rb = rb;
            halfW = halfWidth;
            Normal = Vector2.up;
        }

        /// <summary>Leave the ground (jump / knockback) — skip probing briefly so we don't snap back.</summary>
        public void Launch(float t = 0.12f)
        {
            ignoreUntil = Time.time + t;
            Grounded = false;
        }

        public void IgnoreCollider(Collider2D c, float t)
        {
            ignoreCol = c;
            ignoreColUntil = Time.time + t;
        }

        public void Probe(float dt)
        {
            bool was = Grounded;
            Grounded = false;
            if (Time.time < ignoreUntil || (!was && rb.Vel().y > 0.6f))
            {
                AirTime += dt;
                return;
            }
            float reach = was ? 0.5f : 0.14f;
            const float up = 0.35f;
            float best = float.MaxValue;
            RaycastHit2D bestHit = new RaycastHit2D();
            bool found = false, centerFound = false;
            float centerY = 0f;
            Vector2 centerNormal = Vector2.up;
            Vector2 p = rb.position;
            for (int i = -1; i <= 1; i++)
            {
                var origin = new Vector2(p.x + i * halfW * 0.8f, p.y + up);
                var hits = Physics2D.RaycastAll(origin, Vector2.down, up + reach + (i == 0 ? halfW : 0f), Layers.GroundMask);
                foreach (var h in hits)
                {
                    if (h.distance < 0.001f) continue;                  // started inside (one-way platform from below)
                    if (h.normal.y < 0.55f) continue;                    // walls
                    if (h.collider == ignoreCol && Time.time < ignoreColUntil) continue;
                    if (i == 0) { centerFound = true; centerY = h.point.y; centerNormal = h.normal; }
                    if (h.distance <= up + reach && h.distance < best) { best = h.distance; bestHit = h; found = true; }
                    break;
                }
            }
            Sink = 0f;
            if (found)
            {
                Grounded = true;
                Normal = bestHit.normal;
                GroundY = bestHit.point.y;
                GroundCollider = bestHit.collider;
                AirTime = 0f;
                if (centerFound)
                {
                    // only when the gap is explained by a slope under the probes (not a ledge edge over a drop):
                    // plant the feet on the surface under the middle of the body
                    float gap = GroundY - centerY;
                    float tb = Mathf.Abs(bestHit.normal.x) / Mathf.Max(0.3f, bestHit.normal.y);
                    float tc = Mathf.Abs(centerNormal.x) / Mathf.Max(0.3f, centerNormal.y);
                    float expect = halfW * 0.8f * Mathf.Max(tb, tc);
                    if (gap > 0.002f && gap <= expect * 1.25f + 0.02f) Sink = gap;
                }
            }
            else AirTime += dt;
        }

        /// <summary>
        /// Apply a horizontal speed. On ground: velocity follows the surface, gravity off, feet snapped.
        /// In the air: x set, y untouched, gravity on.
        /// </summary>
        public void Move(ref Vector2 v, float vx, float gravityScale)
        {
            if (Grounded)
            {
                var tan = new Vector2(Normal.y, -Normal.x);
                float k = Mathf.Max(0.5f, tan.x);
                v = tan * (vx / k);
                rb.gravityScale = 0f;
                float dy = rb.position.y - GroundY;
                if (Mathf.Abs(dy) > 0.01f) rb.position = new Vector2(rb.position.x, GroundY);
            }
            else
            {
                v.x = vx;
                rb.gravityScale = gravityScale;
            }
        }
    }
}
