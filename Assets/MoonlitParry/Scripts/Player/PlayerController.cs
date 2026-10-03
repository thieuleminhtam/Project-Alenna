using System.Collections.Generic;
using UnityEngine;

namespace MoonlitParry
{
    /// <summary>
    /// White-haired swordswoman. Hearts + stamina, 3-hit combo (rising slash → heavy down slash → thrust),
    /// hovering air slashes, sword-raise parry, roll, flask, finisher on collapsed enemies, bonfire rest.
    /// </summary>
    public class PlayerController : MonoBehaviour
    {
        // ---- tuning
        public const float RunSpeed = 6.4f;
        public const float JumpVel = 15.9f;      // single jump only (no double jump): ~3.6 tiles high
        public const float GravityScale = 3.6f;
        public const float MaxFall = 18f;
        // perfect-parry window and roll i-frames come from Tuning (Inspector / Esc menu)
        static float PerfectParry { get { return Tuning.I.perfectParryWindow; } }
        static float GuardEnd { get { return Tuning.I.perfectParryWindow + 0.14f; } }   // late guard: blocks, costs stamina
        static float ParryEnd { get { return GuardEnd + 0.12f; } }
        public const float ParryDelay = 0.30f;     // min time between parries (none after a successful one)
        public const float RollRecover = 0.14f;    // end-lag after a roll
        public const float RollCooldown = 0.45f;
        public const float StaminaRegen = 15f, StaminaRegenDelay = 1.2f;   // stamina is only spent by blocking
        public const float BrokenTime = 2f;

        public int MaxHearts = 3, Hearts;
        public int MaxFlasks = 2, Flasks;
        public float MaxStamina = 100f, Stamina;
        public int Facing { get; private set; }
        public int ParryCount { get; private set; }
        public bool IsDead { get { return state == State.Dead; } }
        public bool IsBroken { get { return state == State.Broken; } }
        public bool Grounded { get { return motor != null && motor.Grounded; } }
        public bool CanInteract { get { return state == State.Normal && Grounded; } }
        public EnemyBase FinisherCandidate { get; private set; }
        /// <summary>Body box enemies test their attacks against.</summary>
        public Bounds HurtBounds { get { return col != null ? col.bounds : new Bounds(transform.position + Vector3.up * 0.9f, new Vector3(0.6f, 1.75f, 0f)); } }

        public enum SpawnMode { Normal, Wake, FromRest }
        enum State { Wake, Normal, Attack, Parry, ParryCounter, Roll, RollRecover, Hurt, Heal, Broken, Finisher, Rest, Dead }

        struct AttackDef
        {
            public string clip, sfx;
            public float fps, stDmg, lunge, knock, shake;
            public int dmg, activeFrom, activeTo, chainFrom, lungeFrame;
            public Vector2 off, size;
        }

        static AttackDef A(string clip, float fps, int dmg, float stDmg, Vector2 off, Vector2 size, int a0, int a1, int chain, int lf, float lunge, float knock, float shake, string sfx)
        {
            return new AttackDef
            {
                clip = clip, fps = fps, dmg = dmg, stDmg = stDmg, off = off, size = size, activeFrom = a0, activeTo = a1,
                chainFrom = chain, lungeFrame = lf, lunge = lunge, knock = knock, shake = shake, sfx = sfx
            };
        }

        // damage is low on purpose — the real damage comes from breaking stamina and finishing (30% max HP)
        static readonly AttackDef[] Attacks =
        {
            A("attack1", 20f, 6, 16f, new Vector2(1.3f, 1.05f), new Vector2(2.8f, 1.7f), 2, 3, 4, 1, 4.6f, 2.0f, 0.05f, "slash1"),   // horizontal cut
            A("attack2", 18f, 8, 22f, new Vector2(1.35f, 1.1f), new Vector2(2.5f, 2.4f), 3, 4, 5, 2, 4.5f, 3.0f, 0.10f, "slash2"),
            A("attack3", 18f, 10, 28f, new Vector2(1.9f, 1.05f), new Vector2(3.0f, 0.9f), 2, 4, 99, 2, 5.5f, 5.0f, 0.10f, "slash3"),
        };

        Rigidbody2D rb;
        CapsuleCollider2D col;
        Transform visual;
        IAnim anim;
        SwordTrail trail;
        GroundMotor motor;

        State state;
        float stateTime, hs, pendingVy, prevVy;
        bool wasGrounded, airAttackUsed, launched, rollLatch;
        float coyote, invulnUntil, hurtBlinkUntil, flashTimer, staminaRegenAt, parryReadyAt, restWakeFor, rollReadyAt;
        int combo, lastFinisherFrame;
        float visualSink;
        bool queued, lungeDone, healApplied, deathReported, finisherApplied;
        EnemyBase finisherTarget;
        bool holdWake;
        readonly HashSet<EnemyBase> hitSet = new HashSet<EnemyBase>();
        Collider2D dropping;
        float dropUntil;
        Vector3 safePos;

        float bJump = -9f, bAttack = -9f, bParry = -9f, bRoll = -9f, bHeal = -9f, bFinish = -9f;

        public string DebugInfo
        {
            get
            {
                if (rb == null) return "player chưa khởi tạo";
                var v = rb.Vel();
                return "state " + state + " | grounded " + Grounded + " | vel (" + v.x.ToString("F1") + ", " + v.y.ToString("F1") +
                       ") | pos (" + transform.position.x.ToString("F1") + ", " + transform.position.y.ToString("F1") + ") | stamina " + Stamina.ToString("F0");
            }
        }

        public void Init(Vector3 spawn, SpawnMode mode)
        {
            gameObject.layer = Layers.Player;
            transform.position = spawn;
            safePos = spawn;
            Facing = 1;
            var tu = Tuning.I;
            MaxHearts = Mathf.Max(1, tu.hearts);
            MaxFlasks = Mathf.Max(0, tu.flasks);
            MaxStamina = Mathf.Max(10f, tu.maxStamina);
            Hearts = MaxHearts;
            Flasks = MaxFlasks;
            Stamina = MaxStamina;

            rb = gameObject.AddComponent<Rigidbody2D>();
            rb.gravityScale = GravityScale;
            rb.freezeRotation = true;
            rb.interpolation = RigidbodyInterpolation2D.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;

            col = gameObject.AddComponent<CapsuleCollider2D>();
            col.size = new Vector2(0.62f, 1.75f);
            col.offset = new Vector2(0f, 0.875f);
            col.sharedMaterial = new PhysicsMaterial2D("PlayerNoFriction") { friction = 0f, bounciness = 0f };
            Actors.Register(col);
            motor = new GroundMotor(rb, 0.28f);

            visual = new GameObject("Visual").transform;
            visual.SetParent(transform, false);
            anim = Anims.Create(visual, "heroine", "Player", Order.Player);
            if (anim is RigAnim) trail = SwordTrail.Create(anim.Bone("sword"), new Vector2(0.22f, 0f), new Vector2(0.99f, 0.04f), Order.Player + 2);

            if (mode == SpawnMode.Wake) { state = State.Wake; anim.Play("wake", 5f, false, true); }
            else if (mode == SpawnMode.FromRest) { state = State.Rest; restWakeFor = 1.1f; anim.Play("rest", 2f, true, true); }
            else { state = State.Normal; anim.Play("idle", 7f, true, true); }
        }

        // ------------------------------------------------------------------ input buffering (unscaled time)
        void ReadInput()
        {
            float now = Time.unscaledTime;
            if (GameInput.JumpDown) bJump = now;
            if (GameInput.AttackDown) bAttack = now;
            if (GameInput.ParryDown) bParry = now;
            if (!GameInput.RollHeld) rollLatch = false;          // must let go of Shift before the next roll
            if (GameInput.RollDown && !rollLatch) bRoll = now;
            if (GameInput.HealDown) bHeal = now;
            if (GameInput.FinisherDown) bFinish = now;
        }

        static bool Consume(ref float t, float window = 0.14f)
        {
            if (Time.unscaledTime - t <= window) { t = -9f; return true; }
            return false;
        }

        static bool Peek(float t, float window = 0.14f) { return Time.unscaledTime - t <= window; }

        bool Spend(float cost)
        {
            Stamina -= cost;
            staminaRegenAt = Time.time + StaminaRegenDelay;
            if (Stamina <= 0f)
            {
                Stamina = 0f;
                StartBroken();
                return false;
            }
            return true;
        }

        // ------------------------------------------------------------------ update
        void Update()
        {
            if (rb == null) return;
            ReadInput();
            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            bool inputOn = GameManager.I == null || GameManager.I.InputEnabled;
            if (!inputOn) bJump = bAttack = bParry = bRoll = bHeal = bFinish = -9f;
            float input = inputOn ? GameInput.MoveX : 0f;

            stateTime += dt;
            launched = false;
            bool hover = false;
            var v = rb.Vel();

            motor.Probe(dt);
            bool grounded = motor.Grounded;
            if (grounded)
            {
                coyote = 0.1f;
                airAttackUsed = false;
                if (!wasGrounded && prevVy < -7f) { Fx.Dust(transform.position); Sfx.Play("land", 0.5f); }
            }
            else coyote -= dt;
            wasGrounded = grounded;

            if (pendingVy > 0f)
            {
                v.y = pendingVy;
                pendingVy = 0f;
                motor.Launch(0.12f);
                launched = true;
            }

            FinisherCandidate = (state == State.Normal || state == State.Attack || state == State.ParryCounter) && GameManager.I != null
                ? GameManager.I.FindFinishable(transform.position) : null;

            switch (state)
            {
                case State.Wake:
                    hs = 0f;
                    if (anim.Finished) ToNormal();
                    break;
                case State.Normal: UpdateNormal(dt, input, ref v); break;
                case State.Attack: hover = UpdateAttack(dt, input, ref v); break;
                case State.Parry: UpdateParry(dt, ref v); break;
                case State.ParryCounter: UpdateParryCounter(dt, ref v); break;
                case State.Roll: UpdateRoll(); break;
                case State.RollRecover:
                    hs = Mathf.MoveTowards(hs, 0f, 40f * dt);
                    if (stateTime >= RollRecover) ToNormal();
                    break;
                case State.Hurt:
                    hs = Mathf.MoveTowards(hs, 0f, 14f * dt);
                    if (stateTime > 0.38f) ToNormal();
                    break;
                case State.Heal: UpdateHeal(dt); break;
                case State.Broken:
                    hs = Mathf.MoveTowards(hs, 0f, 20f * dt);
                    if (stateTime >= BrokenTime) { Stamina = MaxStamina; ToNormal(); }
                    break;
                case State.Finisher: UpdateFinisher(); break;
                case State.Rest:
                    hs = 0f;
                    if (restWakeFor > 0f && stateTime >= restWakeFor) { restWakeFor = 0f; ToNormal(); }
                    break;
                case State.Dead:
                    hs = Mathf.MoveTowards(hs, 0f, 20f * dt);
                    if (!deathReported && stateTime > 1.3f)
                    {
                        deathReported = true;
                        if (GameManager.I != null) GameManager.I.OnPlayerDied();
                    }
                    break;
            }

            // ---- apply movement
            if (hover)
            {
                rb.gravityScale = 0f;
                v = new Vector2(hs, 0f);
            }
            else if (launched || !motor.Grounded)
            {
                v.x = hs;
                bool rising = v.y > 0f;
                rb.gravityScale = (rising && !GameInput.JumpHeld && state == State.Normal) ? GravityScale * 2.2f : GravityScale;
            }
            else motor.Move(ref v, hs, GravityScale);
            if (v.y < -MaxFall) v.y = -MaxFall;
            rb.SetVel(v);
            prevVy = v.y;

            if (dropping != null && Time.time > dropUntil)
            {
                Physics2D.IgnoreCollision(col, dropping, false);
                dropping = null;
            }

            // stamina
            if (state != State.Broken && Time.time >= staminaRegenAt)
                Stamina = Mathf.Min(MaxStamina, Stamina + StaminaRegen * dt);

            if (transform.position.y < -14f) // safety net
            {
                rb.position = safePos;
                transform.position = safePos;
                rb.SetVel(Vector2.zero);
                TakeDamage(1, transform.position.x + Facing, true);
            }
            if (grounded && state == State.Normal && Time.frameCount % 20 == 0) safePos = transform.position + Vector3.up * 0.2f;

            anim.Tick(state == State.Parry || (state == State.Wake && holdWake) ? 0f : dt);
            visual.localScale = new Vector3(Facing, 1f, 1f);
            // slopes: the body rests on its uphill probe, so lower the sprite onto the ground under the feet
            float sinkTarget = motor.Grounded && !launched && !hover ? motor.Sink : 0f;
            visualSink = Mathf.MoveTowards(visualSink, sinkTarget, 4f * dt);
            visual.localPosition = new Vector3(0f, -visualSink, 0f);
            if (flashTimer > 0f) flashTimer -= dt;
        }

        void LateUpdate()
        {
            if (anim == null) return;
            bool blink = Time.time < hurtBlinkUntil && state != State.Dead;
            var tint = blink && Mathf.Repeat(Time.time * 14f, 1f) < 0.5f ? new Color(1f, 1f, 1f, 0.35f) : Color.white;
            if (anim.Tint != tint) anim.Tint = tint;
            anim.Flash = flashTimer > 0f;
            if (trail != null)
            {
                // the blade leaves a white crescent while a slash is live
                bool slashing = (state == State.Attack && anim.Frame >= Attacks[combo].activeFrom - 1 && anim.Frame <= Attacks[combo].activeTo)
                                || (state == State.ParryCounter && anim.Frame >= 1 && anim.Frame <= 3)
                                || (state == State.Finisher && anim.Frame >= 1 && anim.Frame <= 3);
                trail.Emit(slashing);
            }
        }

        Collider2D PlatformBelow()
        {
            var g = motor.GroundCollider;
            return g != null && g.GetComponent<PlatformEffector2D>() != null ? g : null;
        }

        // ------------------------------------------------------------------ states
        void UpdateNormal(float dt, float input, ref Vector2 v)
        {
            bool grounded = motor.Grounded;
            hs = Mathf.MoveTowards(hs, input * RunSpeed, (grounded ? 90f : 55f) * dt);
            if (input != 0f) Facing = input > 0 ? 1 : -1;

            if (Consume(ref bJump))
            {
                var plat = grounded && GameInput.DownHeld ? PlatformBelow() : null;
                if (plat != null)
                {
                    dropping = plat;
                    dropUntil = Time.time + 0.35f;
                    Physics2D.IgnoreCollision(col, plat, true);
                    motor.IgnoreCollider(plat, 0.35f);
                    motor.Launch(0.1f);
                    launched = true;
                    v.y = -2f;
                }
                else if (grounded || coyote > 0f)
                {
                    v.y = JumpVel;
                    coyote = 0f;
                    motor.Launch(0.12f);
                    launched = true;
                    Fx.Dust(transform.position, 0.8f);
                    Sfx.Play("jump", 0.45f);
                }
            }

            if (FinisherCandidate != null && Consume(ref bFinish)) { StartFinisher(FinisherCandidate); return; }
            if (Peek(bAttack) && (grounded || !airAttackUsed))
            {
                bAttack = -9f;
                StartAttack(0);
                return;
            }
            if (Time.time >= parryReadyAt && Consume(ref bParry, 0.1f)) { StartParry(); return; }
            if (grounded && Time.time >= rollReadyAt && Consume(ref bRoll)) { StartRoll(input); return; }
            if (grounded && Consume(ref bHeal))
            {
                if (Flasks > 0 && Hearts < MaxHearts) { StartHeal(); return; }
                if (HUD.I != null) HUD.I.Popup(Flasks > 0 ? "Máu đã đầy" : "Hết bình máu", transform.position + Vector3.up * 2.6f, new Color(0.8f, 0.8f, 0.9f));
            }

            if (grounded)
            {
                if (Mathf.Abs(hs) > 0.3f && input != 0f) anim.Play("run", 14f, true);
                else anim.Play("idle", 7f, true);
            }
            else anim.Play(v.y > 0.5f ? "jump" : "fall", 8f, true);
        }

        void StartAttack(int index)
        {
            bool grounded = motor.Grounded;
            state = State.Attack;
            stateTime = 0f;
            combo = index;
            queued = false;
            lungeDone = false;
            hitSet.Clear();
            if (!grounded) airAttackUsed = true;
            float input = GameInput.MoveX;
            if (input != 0f && (GameManager.I == null || GameManager.I.InputEnabled)) Facing = input > 0 ? 1 : -1;
            var d = Attacks[index];
            anim.Play(d.clip, d.fps, false, true);
            Sfx.Play(d.sfx, 0.7f);
            if (grounded) hs *= 0.3f;
            attackStartedAt = Time.unscaledTime;
            bAttack = -9f;
        }
        float attackStartedAt = -9f;

        /// <returns>true while hovering (air attack)</returns>
        bool UpdateAttack(float dt, float input, ref Vector2 v)
        {
            var d = Attacks[combo];
            int f = anim.Frame;
            bool grounded = motor.Grounded;
            bool hover = false;

            // parry cancels the swing at any moment: the attack animation is dropped at once (no recovery frames and no
            // parry cooldown while swinging)
            if (Peek(bParry, 0.2f))
            {
                bParry = -9f;
                queued = false;
                bAttack = -9f;
                StartParry();
                return false;
            }

            if (grounded)
            {
                hs = Mathf.MoveTowards(hs, 0f, 38f * dt);
                if (!lungeDone && f >= d.lungeFrame)
                {
                    lungeDone = true;
                    hs = Facing * d.lunge;
                    if (combo == 2) Fx.Dust(transform.position, 0.9f);
                }
            }
            else
            {
                // air slashes hang in place; one full air combo per jump so you can't fly forever
                hs = Mathf.MoveTowards(hs, input * RunSpeed * 0.5f, 30f * dt);
                hover = true;
            }

            if (f >= d.activeFrom && f <= d.activeTo) DoHitbox(d);

            // queue the next hit only for a NEW press (made after this swing began) — one click = one hit
            if (f >= d.chainFrom - 3 && Peek(bAttack) && bAttack > attackStartedAt + 0.06f) { queued = true; bAttack = -9f; }
            const int maxCombo = 2;
            if (queued && f >= d.chainFrom && combo < maxCombo)
            {
                StartAttack(combo + 1);
                return state == State.Attack && !motor.Grounded;
            }

            if (f > d.activeTo)
            {
                if (FinisherCandidate != null && Consume(ref bFinish)) { StartFinisher(FinisherCandidate); return false; }
                if (grounded && Time.time >= rollReadyAt && Consume(ref bRoll)) { StartRoll(input); return false; }
            }

            if (anim.Finished) ToNormal();
            return hover && state == State.Attack;
        }

        void DoHitbox(AttackDef d)
        {
            Vector2 center = rb.position + new Vector2(d.off.x * Facing, d.off.y);
            var cols = Physics2D.OverlapBoxAll(center, d.size, 0f, Layers.EnemyMask);
            foreach (var c in cols)
            {
                var e = c.GetComponentInParent<EnemyBase>();
                if (e == null || e.IsDead || e.Invulnerable || hitSet.Contains(e)) continue;
                hitSet.Add(e);
                e.TakeHit(d.dmg, d.stDmg * Tuning.I.hitStaminaScale, Facing, d.knock);
                Vector3 hp = new Vector3(Mathf.Lerp(rb.position.x, e.transform.position.x, 0.6f), e.transform.position.y + Mathf.Min(1.4f, e.Height * 0.5f), 0f);
                Fx.Hit(hp, combo == 2);
                Sfx.Play("hit", 0.75f);
                if (combo == 1) Sfx.Play("heavy", 0.45f);
                TimeFx.HitStop(combo == 1 ? 0.08f : 0.05f);
                if (CameraFollow.I != null) CameraFollow.I.Shake(d.shake, 0.16f);
            }
        }

        void StartParry()
        {
            state = State.Parry;
            stateTime = 0f;
            float input = GameInput.MoveX;
            if (input != 0f && (GameManager.I == null || GameManager.I.InputEnabled)) Facing = input > 0 ? 1 : -1;
            anim.Play("parry", 1f, false, true);
            hs *= 0.25f;
            parryReadyAt = Time.time + ParryDelay;
            Sfx.Play("slash3", 0.25f, 0.02f);
        }

        void UpdateParry(float dt, ref Vector2 v)
        {
            hs = Mathf.MoveTowards(hs, 0f, 40f * dt);
            if (!motor.Grounded && v.y < -4f) v.y = -4f;
            float t = stateTime;
            int f = t < 0.03f ? 0 : t < 0.06f ? 1 : t < 0.12f ? 2 : t < GuardEnd ? 3 : t < 0.36f ? 4 : 5;
            anim.SetFrame(f);
            if (Time.time >= parryReadyAt && Consume(ref bParry, 0.1f)) { StartParry(); return; }
            if (t >= ParryEnd) ToNormal();
        }

        void ParrySuccess(EnemyBase src)
        {
            state = State.ParryCounter;
            stateTime = 0f;
            ParryCount++;
            parryReadyAt = Time.time;   // no delay after a perfect parry
            anim.Play("parrycounter", 18f, false, true);
            Vector3 p = new Vector3(transform.position.x + Facing * 0.8f, transform.position.y + 1.5f, 0f);
            Fx.Parry(p);
            Sfx.Play("parry", 1f, 0.03f);
            TimeFx.HitStop(0.09f);
            TimeFx.Slow(0.3f, 0.45f);
            if (CameraFollow.I != null) CameraFollow.I.Shake(0.18f, 0.25f);
            if (HUD.I != null)
            {
                HUD.I.Flash(new Color(1f, 1f, 1f, 0.3f), 0.12f);
                HUD.I.Popup("PARRY!", p + Vector3.up * 1.1f, new Color(0.7f, 0.95f, 1f));
            }
            invulnUntil = Time.time + 0.25f;
            Stamina = Mathf.Min(MaxStamina, Stamina + 10f);
            hs = -Facing * 2.5f;
            if (src != null) src.Parried(Tuning.I.parryStaminaDamage);   // perfect parry drains their stamina (late guard does not)
        }

        void UpdateParryCounter(float dt, ref Vector2 v)
        {
            hs = Mathf.MoveTowards(hs, 0f, 20f * dt);
            if (Consume(ref bParry, 0.1f)) { StartParry(); return; }   // chain parries freely
            if (anim.Frame >= 2)
            {
                if (FinisherCandidate != null && Consume(ref bFinish)) { StartFinisher(FinisherCandidate); return; }
                if (Peek(bAttack)) { bAttack = -9f; StartAttack(0); return; }
                if (motor.Grounded && Time.time >= rollReadyAt && Consume(ref bRoll)) { StartRoll(GameInput.MoveX); return; }
            }
            if (anim.Finished) ToNormal();
        }

        void StartRoll(float input)
        {
            if (input != 0f) Facing = input > 0 ? 1 : -1;
            state = State.Roll;
            stateTime = 0f;
            rollReadyAt = Time.time + RollCooldown;
            rollLatch = true;
            bRoll = -9f;
            anim.Play("roll", 18f, false, true);
            hs = Facing * 11f;
            invulnUntil = Mathf.Max(invulnUntil, Time.time + Tuning.I.rollInvuln);
            Sfx.Play("roll", 0.5f);
            Fx.Dust(transform.position, 0.8f);
        }

        void UpdateRoll()
        {
            hs = Facing * Mathf.Lerp(11f, 5.5f, Mathf.Clamp01(stateTime / 0.38f));
            if (anim.Finished) { state = State.RollRecover; stateTime = 0f; anim.Play("idle", 7f, true, true); }
        }

        void StartHeal()
        {
            state = State.Heal;
            stateTime = 0f;
            healApplied = false;
            hs = 0f;
            anim.Play("heal", 8f, false, true);
        }

        void UpdateHeal(float dt)
        {
            hs = Mathf.MoveTowards(hs, 0f, 40f * dt);
            if (!healApplied && anim.Frame >= 3)
            {
                healApplied = true;
                Flasks--;
                Hearts = Mathf.Min(MaxHearts, Hearts + 2);
                Fx.HealBurst(transform.position);
                Sfx.Play("heal", 0.7f, 0f);
                if (HUD.I != null) HUD.I.Popup("+2 ♥", transform.position + Vector3.up * 2.6f, new Color(1f, 0.6f, 0.65f));
            }
            if (anim.Finished) ToNormal();
        }

        void StartBroken()
        {
            state = State.Broken;
            stateTime = 0f;
            hs *= 0.3f;
            anim.Play("broken", 3f, true, true);
            Fx.Break(transform.position + Vector3.up * 1.2f, 1f);
            Sfx.Play("posture_break", 0.8f, 0.02f);
            if (HUD.I != null) HUD.I.Popup("Kiệt sức!", transform.position + Vector3.up * 2.6f, new Color(1f, 0.8f, 0.4f));
        }

        void StartFinisher(EnemyBase e)
        {
            if (e == null || !e.CanBeFinished) return;
            float dx = e.transform.position.x - transform.position.x;
            Facing = dx >= 0 ? 1 : -1;
            float x = e.transform.position.x - Facing * e.FinisherDistance;
            rb.position = new Vector2(x, rb.position.y);
            transform.position = new Vector3(x, transform.position.y, 0f);
            e.BeginFinisher();
            finisherTarget = e;
            finisherApplied = false;
            lastFinisherFrame = -1;
            state = State.Finisher;
            stateTime = 0f;
            hs = 0f;
            anim.Play("finisher", 15f, false, true);
            Sfx.Play("sheath", 0.4f, 0.05f);
            if (CameraFollow.I != null)
            {
                CameraFollow.I.Punch(0.6f);
                // cinematic close-up on the duel for the whole finisher
                Vector3 mid = (transform.position + e.transform.position) * 0.5f + Vector3.up * Mathf.Clamp(e.Height * 0.45f, 1.2f, 2.6f);
                CameraFollow.I.Focus(mid, e.IsBoss ? 5.2f : 4.2f);
            }
        }

        /// <summary>
        /// Finisher timeline (16 keys @ 15 fps): 0 coil, 1-3 lightning cuts (nothing shows yet), 4 blood-flick,
        /// 5-10 slow sheathe, 11 CLICK -> every cut appears at once on the target (FX + damage), 12-13 hold, 14-15 draw again.
        /// </summary>
        void UpdateFinisher()
        {
            hs = 0f;
            int f = anim.Frame;
            if (f != lastFinisherFrame)
            {
                lastFinisherFrame = f;
                if (f >= 1 && f <= 3) Sfx.Play("slash" + f, 0.5f, 0.1f);
                if (f == 6) Sfx.Play("sheath", 0.3f, 0f);
                if (f >= 11 && !finisherApplied)
                {
                    finisherApplied = true;
                    Sfx.Play("sheath", 1f, 0f);
                    if (finisherTarget != null) finisherTarget.ApplyFinisher();
                    StartCoroutine(FinisherCuts());
                    TimeFx.Slow(0.35f, 0.55f);                     // the cuts bloom in slow motion
                    if (HUD.I != null) HUD.I.Flash(new Color(1f, 1f, 1f, 0.35f), 0.1f);
                    if (CameraFollow.I != null) { CameraFollow.I.Shake(0.3f, 0.35f); CameraFollow.I.Punch(1f); }
                }
            }
            if (anim.Finished)
            {
                if (!finisherApplied && finisherTarget != null) finisherTarget.ApplyFinisher();
                finisherTarget = null;
                if (CameraFollow.I != null) CameraFollow.I.Focus(null);
                ToNormal();
            }
        }

        System.Collections.IEnumerator FinisherCuts()
        {
            for (int i = 0; i < 5; i++)
            {
                Sfx.Play("fin_slash", 0.6f, 0.15f);
                yield return new WaitForSeconds(0.045f);
            }
        }

        /// <summary>Shove the player (boss roar). No damage.</summary>
        public void Push(int dir, float speed)
        {
            if (state == State.Dead || state == State.Finisher) return;
            hs = dir * speed;
            pendingVy = 4f;
        }

        /// <summary>Debug: move instantly (F1 then F2).</summary>
        public void Teleport(Vector3 p)
        {
            rb.position = p;
            transform.position = p;
            rb.SetVel(Vector2.zero);
            safePos = p + Vector3.up * 0.2f;
        }

        /// <summary>Keep lying on the ground (opening text) until released.</summary>
        public void HoldWake(bool hold) { holdWake = hold; }

        /// <summary>Sit down at a bonfire (GameManager handles the fade + respawn).</summary>
        public void BeginRest()
        {
            state = State.Rest;
            stateTime = 0f;
            restWakeFor = 0f;
            hs = 0f;
            anim.Play("rest", 2f, true, true);
        }

        void ToNormal()
        {
            state = State.Normal;
            stateTime = 0f;
            combo = 0;
            queued = false;
        }

        // ------------------------------------------------------------------ damage
        public HitResult ReceiveHit(HitInfo h, EnemyBase src)
        {
            if (state == State.Dead || state == State.Finisher || state == State.Wake || state == State.Rest) return HitResult.Ignored;
            int dir = h.sourceX >= transform.position.x ? 1 : -1;

            // parry / guard work all around the player (360°): turn to face whatever hit us
            if (!h.unparryable && (state == State.Parry || state == State.ParryCounter))
            {
                if ((state == State.Parry && stateTime <= GuardEnd) || (state == State.ParryCounter && stateTime < 0.2f)) Facing = dir;
                if (state == State.Parry && stateTime <= PerfectParry) { ParrySuccess(src); return HitResult.Parried; }
                if (state == State.ParryCounter && stateTime < 0.2f) { ParrySuccess(src); return HitResult.Parried; }
                if (state == State.Parry && stateTime <= GuardEnd)
                {
                    hs = -dir * 4.5f;
                    Fx.Block(new Vector3(transform.position.x + Facing * 0.7f, transform.position.y + 1.4f, 0));
                    Sfx.Play("block", 0.8f);
                    TimeFx.HitStop(0.04f);
                    if (HUD.I != null) HUD.I.Popup("Đỡ", transform.position + Vector3.up * 2.5f, new Color(0.8f, 0.85f, 1f));
                    Spend(Tuning.I.blockCost);
                    return HitResult.Blocked;
                }
            }

            if (Time.time < invulnUntil) return HitResult.Ignored;
            TakeDamage(h.hearts, h.sourceX, false);
            return HitResult.Hit;
        }

        void TakeDamage(int hearts, float sourceX, bool silentKnock)
        {
            if (state == State.Dead) return;
            Hearts -= hearts;
            flashTimer = 0.1f;
            Sfx.Play("hurt", 0.8f);
            TimeFx.HitStop(0.08f);
            if (CameraFollow.I != null) CameraFollow.I.Shake(0.22f, 0.25f);
            if (HUD.I != null) HUD.I.Flash(new Color(0.8f, 0.1f, 0.15f, 0.28f), 0.18f);
            int dir = sourceX >= transform.position.x ? 1 : -1;
            if (Hearts <= 0) { Hearts = 0; Die(); return; }
            if (state == State.Broken) Stamina = MaxStamina * 0.5f;
            state = State.Hurt;
            stateTime = 0f;
            anim.Play("hurt", 10f, false, true);
            if (!silentKnock) { hs = -dir * 5.5f; pendingVy = 4.5f; }
            invulnUntil = Time.time + 0.8f;
            hurtBlinkUntil = invulnUntil;
        }

        void Die()
        {
            Hearts = 0;
            state = State.Dead;
            stateTime = 0f;
            anim.Play("death", 8f, false, true);
            TimeFx.Slow(0.35f, 0.8f);
            if (finisherTarget != null) finisherTarget = null;
        }

        void OnDrawGizmosSelected()
        {
            if (rb == null) return;
            Gizmos.color = Color.cyan;
            foreach (var d in Attacks)
                Gizmos.DrawWireCube(rb.position + new Vector2(d.off.x * Facing, d.off.y), d.size);
        }
    }
}
