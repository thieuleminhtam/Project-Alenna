using UnityEngine;

namespace MoonlitParry
{
    /// <summary>
    /// Gaunt undead. Two attacks: a claw swipe when close, and a curled-up rolling charge from mid range.
    /// Both can be parried (parrying the roll bounces it back). Breaking its stamina leaves it kneeling for 4 s.
    /// </summary>
    public class Zombie : EnemyBase
    {
        enum S { Idle, Emerge, Chase, SwingWindup, Swing, RollWindup, Roll, RollEnd, Recover, Hurt, Stagger, Collapsed, Getup, Dead }

        public const int Damage = 1;              // hearts
        const float SwingRange = 1.7f;
        const float RollMin = 4.0f, RollMax = 9.5f;
        const float Aggro = 9f;

        S st;
        float t, swingReadyAt, rollReadyAt, recoverFor, staggerFor, knockVx, rollStartX;
        bool hitDone, forcedAggro, wasRolling;

        public override float Height { get { return 2.6f; } }

        public void Init(Vector3 pos, bool emerge)
        {
            DisplayName = "Xác Sống";
            MaxHp = Mathf.Max(5, Tuning.I.zombieHp); MaxStamina = Mathf.Max(10f, Tuning.I.zombieStamina); StaminaRegen = 14f; RegenDelay = 2.5f; CollapseDuration = 4f;
            SetupCommon("zombie", "Zombie", pos, Order.Enemy, 0.4f, 4f);
            var c = gameObject.AddComponent<CapsuleCollider2D>();
            c.size = new Vector2(0.8f, 1.9f);
            c.offset = new Vector2(0f, 0.95f);
            RegisterCollider(c);
            rollReadyAt = Time.time + Random.Range(0.2f, 1.2f);
            swingReadyAt = Time.time;

            var p = Player;
            if (p != null) FaceTo(p.transform.position.x - pos.x);
            if (emerge)
            {
                st = S.Emerge;
                Invulnerable = true;
                forcedAggro = true;
                rollReadyAt = Time.time;   // roll right after rising (spawn distance is set up for it)
                anim.Play("emerge", 9f, false, true);
                anim.SortingOrder = Order.Tiles - 2;      // rises out of the ground: drawn behind the terrain until it is out
                Sfx.Play("emerge", 0.8f);
                Fx.DustBig(pos, 0.8f);
            }
            else
            {
                st = S.Idle;
                anim.Play("idle", 6f, true, true);
            }
        }

        void Update()
        {
            if (rb == null) return;
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            t += dt;
            if (!IsDead) motor.Probe(dt);
            var v = rb.Vel();
            float vx = 0f;
            var p = Player;
            bool alive = p != null && !p.IsDead;
            float dx = alive ? p.transform.position.x - transform.position.x : 0f;
            float dy = alive ? p.transform.position.y - transform.position.y : 99f;
            float dist = Mathf.Abs(dx);
            int f = anim.Frame;

            switch (st)
            {
                case S.Idle:
                    anim.Play("idle", 6f, true);
                    if (alive && (forcedAggro || (dist < Aggro && Mathf.Abs(dy) < 3f)))
                    {
                        Go(S.Chase);
                        if (Random.value < 0.6f) Sfx.Play("groan", 0.5f, 0.1f);
                    }
                    break;

                case S.Emerge:
                    if (f >= 6 && anim.SortingOrder != Order.Enemy) anim.SortingOrder = Order.Enemy;
                    if (anim.Finished) { Invulnerable = false; anim.SortingOrder = Order.Enemy; Go(S.Chase); }
                    break;

                case S.Chase:
                    if (!alive) { Go(S.Idle); break; }
                    FaceTo(dx);
                    if (dist <= SwingRange && Time.time >= swingReadyAt && Mathf.Abs(dy) < 1.6f) { StartSwingWindup(); break; }
                    if (dist >= RollMin && dist <= RollMax && Time.time >= rollReadyAt && Mathf.Abs(dy) < 1.6f) { StartRollWindup(); break; }
                    if (dist > 1.3f && !Blocked(0.4f))
                    {
                        vx = facing * 2.0f;
                        anim.Play("walk", 9f, true);
                    }
                    else anim.Play("idle", 6f, true);
                    if (!forcedAggro && (dist > Aggro * 2f || Mathf.Abs(dy) > 5f)) Go(S.Idle);
                    break;

                case S.SwingWindup:
                    if (t >= 0.5f)
                    {
                        Go(S.Swing);
                        hitDone = false;
                        anim.Play("swing", 14f, false, true);
                        Sfx.Play("slash1", 0.45f);
                    }
                    break;

                case S.Swing:
                    vx = f <= 1 ? facing * 2.4f : 0f;
                    if (!hitDone && f >= 1 && f <= 2)
                    {
                        var r = HitPlayer(new Vector2(1.1f, 1.2f), new Vector2(1.9f, 1.8f), Damage, false);
                        if (r != HitResult.Ignored) hitDone = true;
                        if (st != S.Swing) { v = rb.Vel(); vx = knockVx; break; }   // parried → stagger / collapse
                    }
                    if (anim.Finished) StartRecover(0.55f);
                    break;

                case S.RollWindup:
                    if (t >= 0.55f)
                    {
                        Go(S.Roll);
                        hitDone = false;
                        rollStartX = transform.position.x;
                        anim.Play("roll", 18f, true, true);
                        Sfx.Play("roll_rumble", 0.7f);
                    }
                    break;

                case S.Roll:
                    vx = facing * 8.5f;
                    if (!hitDone)
                    {
                        var r = HitPlayer(new Vector2(0.2f, 0.6f), new Vector2(1.3f, 1.2f), Damage, false);
                        if (r != HitResult.Ignored) hitDone = true;
                        if (st != S.Roll) { v = rb.Vel(); vx = knockVx; break; }
                        if (r == HitResult.Blocked) { EndRoll(); break; }
                    }
                    bool passed = alive && (transform.position.x - p.transform.position.x) * facing > 2.2f;
                    float traveled = Mathf.Abs(transform.position.x - rollStartX);
                    if (passed || traveled > 11f || t > 1.5f || (t > 0.15f && Blocked(0.5f, 0.5f))) EndRoll();
                    break;

                case S.RollEnd:
                    vx = facing * Mathf.Max(0f, 8.5f - t * 30f);
                    if (anim.Finished) { FaceTo(dx); StartRecover(0.35f); }
                    break;

                case S.Recover:
                    anim.Play("idle", 6f, true);
                    if (t >= recoverFor) Go(S.Chase);
                    break;

                case S.Hurt:
                    vx = knockVx;
                    knockVx = Mathf.MoveTowards(knockVx, 0f, 17f * dt);
                    if (anim.Finished) Go(S.Chase);
                    break;

                case S.Stagger:
                    vx = knockVx;
                    if (motor.Grounded) knockVx = Mathf.MoveTowards(knockVx, 0f, 14f * dt);
                    if (t >= staggerFor) { swingReadyAt = Time.time + 0.4f; Go(S.Chase); }
                    break;

                case S.Collapsed:
                    vx = knockVx;
                    knockVx = Mathf.MoveTowards(knockVx, 0f, 20f * dt);
                    if (anim.Finished) anim.SetFrame(Mathf.Repeat(Time.time * 3f, 2f) < 1f ? 4 : 5);
                    break;

                case S.Getup:
                    if (anim.Finished) { swingReadyAt = Time.time + 0.3f; Go(S.Chase); }
                    break;

                case S.Dead:
                    {
                        float fade = Mathf.Clamp01(1f - (f - 4) / 4f);
                        if (f >= 5) anim.Tint = new Color(1f, 1f, 1f, fade);       // the body sinks into dust
                    }
                    if (anim.Finished) { Destroy(gameObject); rb = null; return; }
                    break;
            }

            if (!IsDead)
            {
                if (st == S.Stagger && !motor.Grounded) { v.x = vx; rb.gravityScale = 4f; }
                else motor.Move(ref v, vx, 4f);
                rb.SetVel(v);
            }
            if (st != S.Collapsed || !anim.Finished) anim.Tick(dt);
            TickCommon(dt);
        }

        void Go(S s) { st = s; t = 0f; }

        void StartSwingWindup()
        {
            Go(S.SwingWindup);
            anim.Play("swing_windup", 5f / 0.5f, false, true);
            Sfx.Play("windup", 0.35f, 0.1f);
        }

        void StartRollWindup()
        {
            Go(S.RollWindup);
            anim.Play("roll_windup", 5f / 0.55f, false, true);
            Sfx.Play("groan", 0.6f, 0.1f);
        }

        void EndRoll()
        {
            Go(S.RollEnd);
            anim.Play("roll_end", 11f, false, true);
            rollReadyAt = Time.time + 3.5f;
            Fx.Dust(transform.position);
        }

        void StartRecover(float dur)
        {
            Go(S.Recover);
            recoverFor = dur;
            swingReadyAt = Time.time + 0.9f;
        }

        protected override void OnHurt(int dir, float knock)
        {
            if (st == S.Idle || st == S.Chase || st == S.Recover || st == S.Hurt)
            {
                Go(S.Hurt);
                anim.Play("hurt", 12f, false, true);
                knockVx = dir * knock * 1.05f;
                facing = -dir;
            }
            forcedAggro = true;
        }

        protected override void OnParried()
        {
            wasRolling = st == S.Roll;
            Go(S.Stagger);
            anim.Play("stagger", 10f, true, true);
            staggerFor = wasRolling ? 0.85f : 0.6f;
            if (wasRolling)
            {
                knockVx = -facing * 6f;
                motor.Launch(0.15f);
                rb.SetVel(knockVx, 5f);
                rollReadyAt = Time.time + 3.5f;
            }
            else knockVx = -facing * 2.5f;
        }

        protected override void OnCollapse()
        {
            Go(S.Collapsed);
            anim.Play("collapse", 10f, false, true);
            knockVx = 0f;
        }

        protected override void OnCollapseEnd()
        {
            Go(S.Getup);
            anim.Play("getup", 9f, false, true);
        }

        protected override void OnDeath()
        {
            Go(S.Dead);
            anim.Play("death", 11f, false, true);
        }
    }
}
