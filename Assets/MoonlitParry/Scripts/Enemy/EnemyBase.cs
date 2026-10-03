using System;
using UnityEngine;

namespace MoonlitParry
{
    public enum HitResult { Ignored, Hit, Blocked, Parried }

    public struct HitInfo
    {
        public int hearts;          // damage in hearts
        public float sourceX;
        public Vector2 point;
        public bool unparryable;    // e.g. shockwaves: jump or roll instead
    }

    /// <summary>
    /// Shared enemy logic: HP, stamina (poise) that breaks into a 4 s collapse, finisher hooks,
    /// hit flash, death. Subclasses implement the AI.
    /// </summary>
    public abstract class EnemyBase : MonoBehaviour
    {
        public string DisplayName = "";
        public int MaxHp = 70, Hp;
        public float MaxStamina = 100f, Stamina;
        public float StaminaRegen = 14f, RegenDelay = 2.5f, CollapseDuration = 4f;

        public bool IsDead { get; protected set; }
        public bool Collapsed { get; protected set; }
        public bool BeingFinished { get; protected set; }
        public bool Invulnerable { get; protected set; }
        public bool CanBeFinished { get { return Collapsed && !BeingFinished && !IsDead; } }
        public float ShowBarsUntil;
        public Action<EnemyBase> Died;

        public abstract float Height { get; }
        public virtual float FinisherDistance { get { return 1.35f; } }
        public virtual float FinisherScale { get { return 1f; } }
        public virtual bool IsBoss { get { return false; } }
        public Vector3 Center { get { return transform.position + Vector3.up * Height * 0.5f; } }
        public int Facing { get { return facing; } }

        protected Rigidbody2D rb;
        protected Collider2D col;
        protected Transform visual;
        protected IAnim anim;
        protected GroundMotor motor;
        protected int facing = -1;
        protected float flashTimer, lastHitTime = -99f, collapseTimer;
        static PhysicsMaterial2D noFriction;

        protected PlayerController Player { get { return GameManager.I != null ? GameManager.I.Player : null; } }

        protected void SetupCommon(string rig, string folder, Vector3 pos, int order, float halfWidth, float gravity)
        {
            gameObject.layer = Layers.Enemy;
            transform.position = pos;
            Hp = MaxHp;
            Stamina = MaxStamina;

            rb = gameObject.AddComponent<Rigidbody2D>();
            rb.gravityScale = gravity;
            rb.freezeRotation = true;
            rb.interpolation = RigidbodyInterpolation2D.Interpolate;
            motor = new GroundMotor(rb, halfWidth);

            visual = new GameObject("Visual").transform;
            visual.SetParent(transform, false);
            anim = Anims.Create(visual, rig, folder, order);
        }

        protected void RegisterCollider(Collider2D c)
        {
            if (noFriction == null) noFriction = new PhysicsMaterial2D("MP_NoFriction") { friction = 0f, bounciness = 0f };
            col = c;
            c.sharedMaterial = noFriction;
            Actors.Register(c);
        }

        protected void FaceTo(float dx)
        {
            if (Mathf.Abs(dx) > 0.05f) facing = dx > 0 ? 1 : -1;
        }

        /// <summary>Ledge or wall in front?</summary>
        protected bool Blocked(float halfW, float wallHeight = 0.8f)
        {
            Vector2 pos = rb.position;
            var ground = Physics2D.Raycast(pos + new Vector2(facing * (halfW + 0.3f), 0.6f), Vector2.down, 1.8f, Layers.GroundMask);
            if (ground.collider == null) return true;
            var wall = Physics2D.Raycast(pos + new Vector2(0f, wallHeight), new Vector2(facing, 0f), halfW + 0.25f, Layers.GroundMask);
            return wall.collider != null && Mathf.Abs(wall.normal.x) > 0.7f && wall.collider.GetComponent<PlatformEffector2D>() == null;
        }

        protected HitResult HitPlayer(Vector2 off, Vector2 size, int hearts, bool unparryable)
        {
            var p = Player;
            if (p == null) return HitResult.Ignored;
            Vector2 center = rb.position + new Vector2(off.x * facing, off.y);
            var hit = Physics2D.OverlapBox(center, size, 0f, Layers.PlayerMask);
            if (hit == null) return HitResult.Ignored;
            var pc = hit.GetComponentInParent<PlayerController>();
            if (pc == null) return HitResult.Ignored;
            return pc.ReceiveHit(new HitInfo { hearts = hearts, sourceX = transform.position.x, point = center, unparryable = unparryable }, this);
        }

        /// <summary>Points on the player's body used for shaped (arc / line) attacks.</summary>
        static Vector2[] BodyPoints(PlayerController p)
        {
            var b = p.HurtBounds;
            Vector2 c = b.center;
            float hx = b.extents.x, hy = b.extents.y;
            return new[]
            {
                c, new Vector2(c.x, c.y + hy * 0.85f), new Vector2(c.x, c.y - hy * 0.85f),
                new Vector2(c.x - hx, c.y), new Vector2(c.x + hx, c.y),
                new Vector2(c.x - hx, c.y + hy * 0.5f), new Vector2(c.x + hx, c.y - hy * 0.5f),
            };
        }

        /// <summary>Is angle a (degrees) inside the sweep from a0 to a1 (either direction, may pass ±180)?</summary>
        public static bool AngleInSweep(float a, float a0, float a1)
        {
            float lo = Mathf.Min(a0, a1), hi = Mathf.Max(a0, a1);
            if (hi - lo >= 360f) return true;
            while (a < lo) a += 360f;
            while (a >= lo + 360f) a -= 360f;
            return a <= hi;
        }

        /// <summary>World position of a point given in "facing space" (x forward) relative to this enemy.</summary>
        protected Vector2 Local(Vector2 off) { return rb.position + new Vector2(off.x * facing, off.y); }

        /// <summary>
        /// Weapon arc: a ring sector around <paramref name="pivotOff"/> (facing space). Angles are in degrees,
        /// 0 = forward, 90 = up, 180 = behind, -90 = down. The sector swept so far is a0 → a1.
        /// </summary>
        protected HitResult HitPlayerArc(Vector2 pivotOff, float r0, float r1, float a0, float a1, int hearts, bool unparryable)
        {
            var p = Player;
            if (p == null || p.IsDead) return HitResult.Ignored;
            Vector2 pivot = Local(pivotOff);
            foreach (var pt in BodyPoints(p))
            {
                Vector2 d = pt - pivot;
                d.x *= facing;
                float r = d.magnitude;
                if (r < r0 || r > r1) continue;
                float ang = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
                if (!AngleInSweep(ang, a0, a1)) continue;
                return p.ReceiveHit(new HitInfo { hearts = hearts, sourceX = pivot.x, point = pt, unparryable = unparryable }, this);
            }
            return HitResult.Ignored;
        }

        /// <summary>Straight weapon (thrust) from a to b with the given half-thickness, in world space.</summary>
        protected HitResult HitPlayerLine(Vector2 a, Vector2 b, float radius, int hearts, bool unparryable)
        {
            var p = Player;
            if (p == null || p.IsDead) return HitResult.Ignored;
            Vector2 ab = b - a;
            float len2 = Mathf.Max(0.0001f, ab.sqrMagnitude);
            foreach (var pt in BodyPoints(p))
            {
                float k = Mathf.Clamp01(Vector2.Dot(pt - a, ab) / len2);
                if ((a + ab * k - pt).sqrMagnitude > radius * radius) continue;
                return p.ReceiveHit(new HitInfo { hearts = hearts, sourceX = a.x, point = pt, unparryable = unparryable }, this);
            }
            return HitResult.Ignored;
        }

        protected bool PlayerInBox(Vector2 off, Vector2 size)
        {
            var p = Player;
            if (p == null || p.IsDead) return false;
            return Physics2D.OverlapBox(Local(off), size, 0f, Layers.PlayerMask) != null;
        }

        protected void TickCommon(float dt)
        {
            if (!Collapsed && !IsDead && Time.time - lastHitTime > RegenDelay)
                Stamina = Mathf.Min(MaxStamina, Stamina + StaminaRegen * dt);
            if (Collapsed && !BeingFinished)
            {
                collapseTimer -= dt;
                if (collapseTimer <= 0f)
                {
                    Collapsed = false;
                    Stamina = MaxStamina;
                    OnCollapseEnd();
                }
            }
            if (flashTimer > 0f) flashTimer -= dt;
            if (visual != null)
            {
                visual.localScale = new Vector3(facing, 1f, 1f);
                float sinkTarget = motor != null && motor.Grounded && !IsDead ? motor.Sink : 0f;     // feet planted on slopes
                visualSink = Mathf.MoveTowards(visualSink, sinkTarget, 4f * dt);
                visual.localPosition = new Vector3(0f, -visualSink, 0f);
            }
        }
        float visualSink;

        void LateUpdate()
        {
            if (anim == null) return;
            anim.Flash = flashTimer > 0f;
        }

        // ---------------------------------------------------------------- damage API (called by the player)
        public void TakeHit(int dmg, float staminaDmg, int dir, float knock)
        {
            if (IsDead || Invulnerable || BeingFinished) return;
            lastHitTime = Time.time;
            ShowBarsUntil = Time.time + 5f;
            Hp -= dmg;
            flashTimer = 0.09f;
            Sfx.Play("enemy_hurt", 0.55f);
            if (Hp <= 0) { Hp = 0; Die(); return; }
            if (!Collapsed)
            {
                LoseStamina(staminaDmg);
                if (Stamina <= 0f) { Collapse(); return; }
                OnHurt(dir, knock);
            }
        }

        /// <summary>Time of the last stamina loss (HUD flashes the bar).</summary>
        public float StaminaHitAt { get; private set; }
        /// <summary>Stamina before the last loss (HUD shows the chunk that was taken).</summary>
        public float StaminaBefore { get; private set; }

        void LoseStamina(float amount)
        {
            StaminaBefore = Stamina;
            StaminaHitAt = Time.time;
            Stamina -= amount;
        }

        /// <summary>Our attack was blocked (late guard): a smaller stamina chip, no stagger.</summary>
        public void Guarded(float staminaDmg)
        {
            if (IsDead || Collapsed) return;
            lastHitTime = Time.time;
            ShowBarsUntil = Time.time + 5f;
            LoseStamina(staminaDmg);
            if (Stamina <= 0f) Collapse();
        }

        /// <summary>Our attack was parried by the player.</summary>
        public void Parried(float staminaDmg)
        {
            if (IsDead) return;
            lastHitTime = Time.time;
            ShowBarsUntil = Time.time + 5f;
            if (staminaDmg > 0f) LoseStamina(staminaDmg);
            if (Stamina <= 0f) Collapse();
            else OnParried();
        }

        protected void Collapse()
        {
            Collapsed = true;
            Stamina = 0f;
            collapseTimer = CollapseDuration;
            Fx.Break(Center, IsBoss ? 2.2f : 1.3f);
            Sfx.Play("posture_break", 0.9f, 0.02f);
            TimeFx.HitStop(0.1f);
            if (CameraFollow.I != null) CameraFollow.I.Shake(0.15f, 0.2f);
            if (GameManager.I != null) GameManager.I.OnEnemyCollapsed(this);
            OnCollapse();
        }

        public void BeginFinisher() { BeingFinished = true; }

        public void ApplyFinisher()
        {
            int dmg = Mathf.CeilToInt(MaxHp * 0.3f);
            Hp -= dmg;
            flashTimer = 0.2f;
            ShowBarsUntil = Time.time + 5f;
            Fx.Finisher(Center, FinisherScale);
            Sfx.Play("fin_hit", 1f, 0.02f);
            TimeFx.HitStop(0.2f);
            if (CameraFollow.I != null) CameraFollow.I.Shake(0.3f, 0.35f);
            if (HUD.I != null) HUD.I.Popup("-" + dmg, Center + Vector3.up * (Height * 0.6f), new Color(1f, 0.85f, 0.85f));
            BeingFinished = false;
            if (Hp <= 0) { Hp = 0; Die(); return; }
            Collapsed = false;
            Stamina = MaxStamina;
            OnCollapseEnd();
        }

        protected void Die()
        {
            IsDead = true;
            Collapsed = false;
            BeingFinished = false;
            if (col != null) col.enabled = false;
            rb.bodyType = RigidbodyType2D.Kinematic;
            rb.SetVel(Vector2.zero);
            Sfx.Play("enemy_die", IsBoss ? 1f : 0.7f);
            Fx.Rise(Center, IsBoss ? new Color(0.5f, 0.95f, 0.85f) : new Color(0.85f, 0.8f, 0.6f), IsBoss ? 40 : 16, IsBoss ? 2f : 0.6f, 2.5f);
            TimeFx.HitStop(IsBoss ? 0.25f : 0.08f);
            OnDeath();
            if (Died != null) Died(this);
            if (GameManager.I != null) GameManager.I.OnEnemyDied(this);
        }

        protected abstract void OnHurt(int dir, float knock);
        protected abstract void OnParried();
        protected abstract void OnCollapse();
        protected abstract void OnCollapseEnd();
        protected abstract void OnDeath();
    }
}
