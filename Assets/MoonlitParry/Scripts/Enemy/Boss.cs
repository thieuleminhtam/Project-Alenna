using UnityEngine;

namespace MoonlitParry
{
    /// <summary>
    /// The Root-Crowned Warden: mounted knight with a glaive.
    ///  Moves: rising sweep (low behind → under → up) → overhead chop round to behind → thrust aimed at the player;
    ///  galloping charge with the glaive dragged on the ground then ripped up; red leap that homes in and dives;
    ///  horse kick when the player hides behind. Picks moves by weight with anti-repeat, punishes button-mashing.
    ///  Phase 2 (below phaseHp): faster, longer chains, double charge, stomp shockwaves, 2-heart hits. Phase 3: relentless.
    /// </summary>
    public class Boss : EnemyBase
    {
        enum S { Dormant, Approach, BackHop, RearUp, Charge, Upswing, Skid, SwingWindup, Swing, SlamWindup, Slam, ThrustWindup, Thrust, KickWindup, Kick, Crouch, Air, Hang, Dive, Land, Recover, Stagger, Collapsed, Getup, PhaseShift, Dead }
        enum Move { None, Combo, Charge, Leap, Thrust, Kick }

        // rider's hands (glaive pivot) for each move, facing space — matched to the boss sprites
        static readonly Vector2 UpPivot = new Vector2(1.5f, 4.5f);
        static readonly Vector2 SwingPivot = new Vector2(1.5f, 5.0f);
        static readonly Vector2 SlamPivot = new Vector2(1.6f, 4.8f);
        static readonly Vector2 ThrustFrom = new Vector2(2.4f, 4.6f);
        static readonly int[] ThrustAngles = { 20, 0, -20, -40, -60, -78 };   // drawn thrust variants
        float nextSpark;
        // AI memory
        Move last1 = Move.None, last2 = Move.None;
        int hitsTaken;
        float firstHitAt, kickReadyAt;
        bool quickNext, soloThrust;
        float leapTargetX;
        // back-hop before a charge when the player is too close for a real gallop
        float hopTargetX, hopVx;
        int hopPhase;
        const float ChargeRoom = 9.5f, HopMinGain = 2.5f;
        const float LeapGravity = 5f, LeapHeight = 7.2f, DiveSpeed = 34f, HangTime = 0.1f;

        S st;
        // current glaive arc
        bool sweeping, sweepUnparry, chargeSwept, backDustDone;
        string swClip;
        float swT, swDur, swA0, swA1, swR0, swR1;
        Vector2 swPivot;
        // current aimed thrust (facing-space unit direction)
        Vector2 thrustDir;
        bool openerDone;
        float t, cooldownUntil, recoverFor, knockVx, chargeStartX, chargeSpeed, windDur, nextGallopSfx, staggerFor = 0.9f;
        int chargesLeft;
        bool hitDone, impactDone, announcedP2, announcedP3, pushed;
        float arenaMin, arenaMax;

        public override float Height { get { return 6.6f; } }
        public override float FinisherDistance { get { return 3.6f; } }
        public override float FinisherScale { get { return 1.9f; } }
        public override bool IsBoss { get { return true; } }

        public int Phase
        {
            get
            {
                float f = (float)Hp / MaxHp;
                var tu = Tuning.I;
                return f <= tu.phase3At ? 3 : f <= tu.phase2At ? 2 : 1;
            }
        }

        int Dmg { get { return Phase >= 2 ? 2 : 1; } }
        float Spd { get { return Tuning.I.bossSpeed * (Phase == 3 ? 1.35f : Phase == 2 ? 1.1f : 1f); } }
        float Cd(float baseCd) { return baseCd / Mathf.Max(0.3f, Tuning.I.bossAggression); }

        public void Init(Vector3 pos, float arenaMinX, float arenaMaxX)
        {
            DisplayName = Story.BossName;
            var tu = Tuning.I;
            MaxHp = Mathf.Max(50, tu.bossHp); MaxStamina = Mathf.Max(50f, tu.bossStamina);
            StaminaRegen = tu.bossStaminaRegen; RegenDelay = 2.5f; CollapseDuration = 4f;
            arenaMin = arenaMinX; arenaMax = arenaMaxX;
            SetupCommon("boss", "Boss", pos, Order.Boss, 2.2f, 4f);
            var c = gameObject.AddComponent<BoxCollider2D>();
            c.size = new Vector2(4.6f, 4.8f);
            c.offset = new Vector2(0f, 2.4f);
            RegisterCollider(c);
            facing = -1;
            st = S.Dormant;
            anim.Play("idle", 6f, true, true);
        }

        /// <summary>Intro: rear up dramatically.</summary>
        public void Taunt()
        {
            anim.Play("rear", 8f, false, true);
            Sfx.Play("neigh", 0.9f, 0f);
        }

        public void Wake()
        {
            if (st != S.Dormant) return;
            Go(S.Approach);
            cooldownUntil = Time.time + 0.5f;
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
            float dist = Mathf.Abs(dx);
            int f = anim.Frame;
            float sp = Spd;

            if (!IsDead && !announcedP2 && Phase >= 2 && !Collapsed && st != S.Air && st != S.Hang && st != S.Dive && st != S.Dormant)
            {
                announcedP2 = true;
                StartPhaseShift();
            }
            if (!IsDead && !announcedP3 && Phase >= 3)
            {
                announcedP3 = true;
                if (HUD.I != null) HUD.I.Popup("Hơi thở cuối cùng...", Center + Vector3.up * 3f, new Color(1f, 0.6f, 0.6f));
                Fx.Rise(Center, new Color(1f, 0.5f, 0.5f), 30, 2f, 3f);
            }

            switch (st)
            {
                case S.Dormant:
                    if (anim.Name == "rear" && anim.Finished) anim.Play("idle", 6f, true, true);
                    break;

                case S.Approach:
                    if (!alive) { anim.Play("idle", 6f, true); break; }
                    {
                        bool behind = dx * facing < -0.4f;
                        if (openerDone && behind && dist < 4.2f && Time.time >= kickReadyAt && Mathf.Abs(p.transform.position.y - transform.position.y) < 3f)
                        { StartKick(sp); break; }
                        FaceTo(dx);
                        // in reach: don't just stroll up and take hits — strike as soon as the short cooldown allows
                        if (Time.time >= cooldownUntil || (dist < 5.5f && Time.time >= cooldownUntil - 0.25f)) { Decide(dist); break; }
                        if (dist > 4.2f) { vx = facing * 3.6f * sp; anim.Play("walk", 12f * sp, true); }
                        else anim.Play("idle", 6f, true);
                    }
                    break;

                case S.BackHop:
                    if (hopPhase == 0)                     // quick crouch, then spring backwards (still facing the player)
                    {
                        if (t >= 0.16f / sp)
                        {
                            hopPhase = 1;
                            float span = Mathf.Abs(hopTargetX - transform.position.x);
                            float dur = Mathf.Lerp(0.42f, 0.6f, Mathf.InverseLerp(HopMinGain, ChargeRoom, span)) / Mathf.Sqrt(sp);
                            hopVx = (hopTargetX - transform.position.x) / dur;
                            vx = hopVx;
                            v = new Vector2(hopVx, Mathf.Abs(Physics2D.gravity.y) * 4f * dur * 0.5f);   // symmetric arc (gravity x4 in the air)
                            motor.Launch(0.2f);
                            anim.Play("air", 6f, true, true);
                            Sfx.Play("snort", 0.6f, 0.06f);
                            Fx.DustBig(transform.position, 1.0f);
                            t = 0f;
                        }
                    }
                    else if (hopPhase == 1)                // in the air, drifting back
                    {
                        vx = hopVx;
                        if (t > 0.12f && motor.Grounded)
                        {
                            hopPhase = 2;
                            t = 0f;
                            vx = 0f;
                            anim.Play("land", 5f / 0.22f, false, true);
                            Fx.DustBig(transform.position, 1.1f);
                            Sfx.Play("stomp", 0.55f, 0.05f);
                            if (CameraFollow.I != null) CameraFollow.I.Shake(0.1f, 0.2f);
                        }
                    }
                    else if (t >= 0.2f / sp)               // landed at a distance: rear up and charge
                    {
                        FaceTo(dx);
                        StartRear(0.55f / sp);
                    }
                    break;

                case S.RearUp:
                    if (t >= windDur) StartCharge();
                    break;

                case S.Charge:
                    vx = facing * chargeSpeed;
                    if (Time.time >= nextGallopSfx) { Sfx.Play("gallop", 0.6f, 0.05f); nextGallopSfx = Time.time + 0.32f; }
                    if (Time.time >= nextSpark)      // glaive dragging along the ground behind
                    {
                        nextSpark = Time.time + 0.05f;
                        Fx.Sparks(transform.position + new Vector3(-facing * 3.0f, 0.15f, 0f), new Color(1f, 0.75f, 0.3f), 3, 5f);
                    }
                    // the player is just ahead: rip the dragged glaive up out of the ground into them
                    if (!chargeSwept && alive)
                    {
                        float ahead = dx * facing;
                        float dy = p.transform.position.y - transform.position.y;
                        if (ahead > -0.5f && ahead < 5.6f && Mathf.Abs(dy) < 4.5f) { StartUpswing(sp); break; }
                    }
                    {
                        bool passed = alive && (transform.position.x - p.transform.position.x) * facing > 4.5f;
                        float traveled = Mathf.Abs(transform.position.x - chargeStartX);
                        float x = transform.position.x;
                        bool edge = facing > 0 ? x > arenaMax - 4f : x < arenaMin + 4f;
                        if ((passed && traveled > 5f) || edge || t > 2.6f) StartSkid();
                    }
                    break;

                case S.Upswing:
                    vx = facing * Mathf.Max(2.5f, chargeSpeed * 0.6f - t * 18f);
                    TickSweep(dt);
                    if (st != S.Upswing) { v = rb.Vel(); vx = knockVx; break; }
                    if (anim.Finished && !sweeping) StartSkid();
                    break;

                case S.Skid:
                    vx = facing * Mathf.Max(0f, chargeSpeed - t * 40f);
                    TickSweep(dt);
                    if (st != S.Skid) { v = rb.Vel(); vx = knockVx; break; }
                    if (anim.Finished)
                    {
                        if (chargesLeft > 0 && alive)
                        {
                            chargesLeft--;
                            FaceTo(dx);
                            if (!TryBackHop()) StartRear(0.4f / sp);
                        }
                        else StartRecover(0.6f);
                    }
                    break;

                case S.SwingWindup:
                    if (t >= windDur)
                    {
                        Go(S.Swing);
                        hitDone = false;
                        float fps = 16f * sp;
                        anim.Play("swing_up", fps, false, true);
                        Sfx.Play("slash2", 0.95f);
                        // low behind → under the horse → front → over the head: nowhere around the horse is safe
                        BeginSweep(SwingPivot, 0.6f, 6.3f, -160f, 86f, 0.5f / fps, 5f / fps, "slash_rise", -160f, 86f);
                    }
                    break;

                case S.Swing:
                    vx = f >= 2 && f <= 4 ? facing * 2.2f : 0f;
                    TickSweep(dt);
                    if (st != S.Swing) { v = rb.Vel(); vx = knockVx; break; }
                    if (anim.Finished && !sweeping)
                    {
                        // rises, then comes straight back down
                        // phase 1: one rising sweep. phase 2+: sweep → chop → thrust
                        if (Phase >= 2) { FaceTo(dx); StartWindup(S.SlamWindup, "slam_windup", 0.26f / sp); }
                        else StartRecover(0.45f);
                    }
                    break;

                case S.SlamWindup:
                    if (t >= windDur)
                    {
                        Go(S.Slam);
                        hitDone = false; impactDone = false; backDustDone = false;
                        anim.Play("slam", 14f * sp, false, true);
                        Sfx.Play("slash2", 0.9f, 0.1f);
                        BeginSweep(SlamPivot, 0.8f, 6.3f, 78f, -196f, 0.5f / (14f * sp), 5f / (14f * sp), "slash_slam", 76f, -196f);
                    }
                    break;

                case S.Slam:
                    TickSweep(dt);
                    if (st != S.Slam) { v = rb.Vel(); vx = knockVx; break; }
                    if (!backDustDone && swT >= swDur * 0.8f)
                    {
                        backDustDone = true;
                        Fx.DustBig(transform.position - new Vector3(facing * 3.8f, 0f, 0f), 1.3f);
                    }
                    if (!impactDone && f >= 2)
                    {
                        impactDone = true;
                        Fx.DustBig(transform.position + new Vector3(facing * 4.4f, 0f, 0f), 1.6f);
                        Sfx.Play("heavy", 0.9f);
                        if (CameraFollow.I != null) CameraFollow.I.Shake(0.2f, 0.25f);
                    }
                    if (anim.Finished && !sweeping)
                    {
                        StartThrustWindup(0.26f / sp, false);
                    }
                    break;

                case S.ThrustWindup:
                    if (t >= windDur)
                    {
                        Go(S.Thrust);
                        hitDone = false;
                        FaceTo(dx);                    // never safe behind the horse: the rider twists round
                        int aim = AimThrust();
                        anim.Play(ThrustClip(aim), 14f * sp, false, true);
                        Sfx.Play("slash3", 0.9f);
                        SlashFx.Spawn("slash_thrust", transform, ThrustFrom, facing, 0.14f / sp, aim);
                    }
                    break;

                case S.Thrust:
                    vx = f <= 1 ? facing * 5.5f : 0f;
                    if (!hitDone && t <= 0.26f / sp)
                    {
                        float reach = Mathf.Lerp(1.5f, 7.4f, Mathf.Clamp01(t / (0.14f / sp)));
                        Vector2 a = Local(ThrustFrom);
                        Vector2 dirW = new Vector2(thrustDir.x * facing, thrustDir.y);
                        var r = HitPlayerLine(a + dirW * 0.4f, a + dirW * reach, 0.95f, Dmg, false);
                        if (r != HitResult.Ignored) hitDone = true;
                        if (st != S.Thrust) { v = rb.Vel(); vx = knockVx; break; }
                    }
                    if (anim.Finished)
                    {
                        // phase 3 (and sometimes phase 2) keeps the pressure on straight into a charge or a leap
                        float chain = Phase >= 3 ? 0.6f : Phase == 2 && !soloThrust ? 0.3f : 0f;
                        if (Random.value < chain) { if (Random.value < 0.5f) BeginCharge(); else StartCrouch(); }
                        else StartRecover(soloThrust ? 0.4f : 0.55f);
                    }
                    break;

                case S.KickWindup:
                    if (!impactDone && f >= 3) { impactDone = true; Fx.Dust(transform.position - new Vector3(facing * 1.2f, 0f, 0f), 0.6f); }
                    if (t >= windDur)
                    {
                        Go(S.Kick);
                        hitDone = false; impactDone = false;
                        anim.Play("kick", 15f * sp, false, true);
                    }
                    break;

                case S.Kick:
                    if (!hitDone && f >= 2 && f <= 3)
                    {
                        var r = HitPlayer(new Vector2(-3.5f, 1.4f), new Vector2(3.8f, 2.8f), Dmg, false);
                        if (r != HitResult.Ignored) hitDone = true;
                        if (st != S.Kick) { v = rb.Vel(); vx = knockVx; break; }
                    }
                    if (!impactDone && f >= 2)
                    {
                        impactDone = true;
                        Sfx.Play("heavy", 0.7f, 0.08f);
                        Fx.DustBig(transform.position - new Vector3(facing * 3.6f, 0f, 0f), 1.0f);
                    }
                    if (anim.Finished) StartRecover(0.3f);
                    break;

                case S.Crouch:
                    if (t >= windDur)
                    {
                        leapTargetX = LeapTarget(alive, p);
                        float g = Mathf.Abs(Physics2D.gravity.y) * LeapGravity;
                        float v0 = Mathf.Sqrt(2f * g * LeapHeight);
                        motor.Launch(0.3f);
                        v = new Vector2(0f, v0);
                        rb.SetVel(v);
                        rb.gravityScale = LeapGravity;
                        rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
                        Go(S.Air);
                        anim.Play("air", 6f, true, true);
                        Sfx.Play("neigh", 0.7f, 0.05f);
                        Fx.DustBig(transform.position, 1.2f);
                    }
                    break;

                case S.Air:          // rising: keeps steering onto the player's current position
                    {
                        leapTargetX = LeapTarget(alive, p);
                        float g = Mathf.Abs(Physics2D.gravity.y) * LeapGravity;
                        float rise = Mathf.Max(0f, rb.Vel().y / g);
                        float left = Mathf.Max(0.12f, rise + HangTime + LeapHeight / DiveSpeed);
                        vx = Mathf.Clamp((leapTargetX - transform.position.x) / left, -24f, 24f);
                        if (rb.Vel().y <= 0.5f) { Go(S.Hang); v = Vector2.zero; vx = 0f; }
                    }
                    break;

                case S.Hang:         // a heartbeat at the top, locked on…
                    vx = 0f; v = Vector2.zero;
                    leapTargetX = LeapTarget(alive, p);
                    if (t >= HangTime / Mathf.Sqrt(sp)) { Go(S.Dive); Sfx.Play("windup", 0.7f, 0f); }
                    break;

                case S.Dive:         // …then slams straight down, fast
                    {
                        float hgt = Mathf.Max(0.3f, transform.position.y - LevelBuilder.SurfaceAt(transform.position.x));
                        vx = Mathf.Clamp((leapTargetX - transform.position.x) / (hgt / DiveSpeed), -16f, 16f);
                        v.y = -DiveSpeed;
                        if (t > 0.04f && (motor.Grounded || hgt < 0.35f)) StartLand();
                    }
                    break;

                case S.Land:
                    if (!hitDone)
                    {
                        hitDone = true;
                        HitPlayer(new Vector2(0f, 1.2f), new Vector2(7.4f, 2.6f), Dmg, true);   // red attack: cannot be parried
                        if (st != S.Land) { v = rb.Vel(); vx = knockVx; break; }
                    }
                    if (anim.Finished) StartRecover(0.55f);
                    break;

                case S.Recover:
                    anim.Play("idle", 6f, true);
                    if (t >= recoverFor / sp)
                    {
                        Go(S.Approach);
                        // relentless: often goes straight into the next move
                        float chainChance = Phase == 1 ? 0.3f : Phase == 2 ? 0.5f : 0.75f;
                        cooldownUntil = Random.value < chainChance ? Time.time
                                      : Time.time + Cd(Phase == 1 ? Random.Range(0.3f, 0.6f) : Phase == 2 ? Random.Range(0.15f, 0.35f) : 0.08f);
                    }
                    break;

                case S.Stagger:
                    vx = knockVx;
                    knockVx = Mathf.MoveTowards(knockVx, 0f, 10f * dt);
                    if (t >= staggerFor) { Go(S.Approach); cooldownUntil = Time.time + 0.3f; }
                    break;

                case S.PhaseShift:
                    vx = 0f;
                    if (!pushed && t > 0.55f)
                    {
                        pushed = true;
                        Fx.Parry(Center);
                        Fx.Rise(Center, new Color(0.5f, 1f, 0.85f), 40, 3f, 4f);
                        if (CameraFollow.I != null) CameraFollow.I.Shake(0.4f, 0.7f);
                        if (HUD.I != null) HUD.I.Flash(new Color(0.6f, 1f, 0.9f, 0.35f), 0.25f);
                        if (alive && dist < 9f) p.Push(dx >= 0 ? 1 : -1, 9f);
                    }
                    if (anim.Finished)
                    {
                        Invulnerable = false;
                        Go(S.Approach);
                        cooldownUntil = Time.time + 0.3f;
                    }
                    break;

                case S.Collapsed:
                    if (anim.Finished) anim.SetFrame(Mathf.Repeat(Time.time * 3f, 2f) < 1f ? 4 : 5);
                    break;

                case S.Getup:
                    if (anim.Finished) { Go(S.Approach); cooldownUntil = Time.time + 0.5f; }
                    break;

                case S.Dead:
                    if (anim.Finished) { Destroy(gameObject); rb = null; return; }
                    break;
            }

            if (!IsDead)
            {
                if (st == S.Air) { v = new Vector2(vx, rb.Vel().y); rb.gravityScale = LeapGravity; }
                else if (st == S.Hang) { v = Vector2.zero; rb.gravityScale = 0f; }
                else if (st == S.Dive) { v = new Vector2(vx, -DiveSpeed); rb.gravityScale = 0f; }
                else motor.Move(ref v, vx, 4f);
                // never leave the arena
                float x = transform.position.x;
                if ((x < arenaMin + 2.4f && v.x < 0f) || (x > arenaMax - 2.4f && v.x > 0f)) v.x = 0f;
                rb.SetVel(v);
            }
            if (st != S.Collapsed || !anim.Finished) anim.Tick(dt);
            TickCommon(dt);
        }

        void Go(S s) { st = s; t = 0f; sweeping = false; }

        /// <summary>Start a glaive arc (facing-space degrees: 0 forward, 90 up, 180 behind, -90 down).</summary>
        void BeginSweep(Vector2 pivot, float r0, float r1, float a0, float a1, float delay, float dur, string fxClip, float fxA0, float fxA1)
        {
            sweeping = true;
            hitDone = false;
            swPivot = pivot; swR0 = r0; swR1 = r1; swA0 = a0; swA1 = a1;
            swDur = Mathf.Max(0.05f, dur);
            swT = -delay;
            sweepUnparry = false;
            swClip = fxClip;
            if (delay <= 0f) SpawnSweepTrail();
        }

        void SpawnSweepTrail()
        {
            if (!string.IsNullOrEmpty(swClip)) SlashFx.Spawn(swClip, transform, swPivot, facing, swDur, 0f);
        }

        void StartUpswing(float sp)
        {
            Go(S.Upswing);
            chargeSwept = true;
            float fps = 16f * sp;
            anim.Play("upswing", fps, false, true);
            Sfx.Play("slash2", 1f, 0.08f);
            Fx.DustBig(transform.position + new Vector3(-facing * 3.0f, 0f, 0f), 1.2f);
            BeginSweep(UpPivot, 0.8f, 6.5f, -140f, 82f, 0.5f / fps, 4f / fps, "slash_up", -140f, 82f);
        }

        static string ThrustClip(int a)
        {
            return "thrust_" + (a > 0 ? "p" + a : a < 0 ? "m" + (-a) : "0");
        }

        void TickSweep(float dt)
        {
            if (!sweeping) return;
            float prev = swT;
            swT += dt;
            if (prev < 0f && swT >= 0f) SpawnSweepTrail();
            if (swT < 0f) return;
            float k = Mathf.Clamp01(swT / swDur);
            if (!hitDone)
            {
                var r = HitPlayerArc(swPivot, swR0, swR1, swA0, Mathf.Lerp(swA0, swA1, k), Dmg, sweepUnparry);
                if (r != HitResult.Ignored) hitDone = true;
            }
            if (k >= 1f) sweeping = false;
        }

        /// <summary>Lock the thrust onto the player and snap to the nearest drawn thrust pose; returns that angle.</summary>
        int AimThrust()
        {
            var p = Player;
            Vector2 from = Local(ThrustFrom);
            float ang = -20f;
            if (p != null)
            {
                Vector2 d = (Vector2)p.HurtBounds.center - from;
                d.x *= facing;
                if (d.x < 0.3f) d.x = 0.3f;    // behind the horse: stab steeply down instead
                ang = Mathf.Clamp(Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg, -78f, 35f);
            }
            int best = ThrustAngles[0];
            foreach (int a in ThrustAngles) if (Mathf.Abs(a - ang) < Mathf.Abs(best - ang)) best = a;
            float rad = best * Mathf.Deg2Rad;
            thrustDir = new Vector2(Mathf.Cos(rad), Mathf.Sin(rad));
            return best;
        }

        float LeapTarget(bool alive, PlayerController p)
        {
            float x = alive ? p.transform.position.x : transform.position.x;
            return Mathf.Clamp(x, arenaMin + 2.6f, arenaMax - 2.6f);
        }

        /// <summary>Pick the next move by weight: range matters, nothing is range-locked, and the same move rarely comes twice.</summary>
        void Decide(float dist)
        {
            if (!openerDone)            // the fight always opens with the charge (rear up, gallop in, rip the spear up)
            {
                openerDone = true;
                last2 = last1; last1 = Move.Charge;
                quickNext = false;
                BeginCharge();
                return;
            }
            var p = Player;
            bool behind = p != null && (p.transform.position.x - transform.position.x) * facing < -0.4f;
            float wCombo = dist < 5.5f ? 3.2f : dist < 8.5f ? 1.0f : 0.25f;
            float wCharge = dist > 8f ? 1.6f : dist > 3.5f ? 1.1f : 0.8f;
            float wLeap = dist > 8f ? 1.2f : dist > 3.5f ? 1.0f : 0.8f;
            float wThrust = Phase < 2 ? 0f : dist > 3f && dist < 8.5f ? 1.1f : 0.35f;   // the thrust belongs to the phase-2 chain
            float wKick = behind && dist < 4.2f ? 2.6f : 0f;
            if (quickNext) { wCombo *= 1.6f; wKick *= 1.6f; wCharge *= 0.5f; wLeap *= 0.5f; }
            float[] w = { 0f, Rep(Move.Combo, wCombo), Rep(Move.Charge, wCharge), Rep(Move.Leap, wLeap), Rep(Move.Thrust, wThrust), Rep(Move.Kick, wKick) };
            float sum = 0f;
            foreach (var x in w) sum += x;
            float r = Random.value * sum;
            Move m = Move.Combo;
            for (int i = 1; i < w.Length; i++) { if (r < w[i]) { m = (Move)i; break; } r -= w[i]; }
            last2 = last1; last1 = m;
            float quick = quickNext ? 0.65f : 1f;
            quickNext = false;
            float sp = Spd;
            switch (m)
            {
                case Move.Combo: StartWindup(S.SwingWindup, "swing_low_windup", 0.5f * quick / sp); break;
                case Move.Charge: BeginCharge(); break;
                case Move.Leap: StartCrouch(); break;
                case Move.Thrust: StartThrustWindup(0.36f * quick / sp, true); break;
                case Move.Kick: StartKick(sp); break;
            }
        }

        float Rep(Move m, float w)
        {
            if (m == last1 && m == last2) return 0f;          // never three in a row
            return m == last1 ? w * 0.3f : w;
        }

        void StartThrustWindup(float dur, bool solo)
        {
            FaceTo(Player != null ? Player.transform.position.x - transform.position.x : 0f);
            soloThrust = solo;
            StartWindup(S.ThrustWindup, "thrust_windup", dur);
        }

        void StartKick(float sp)
        {
            kickReadyAt = Time.time + 2.4f;
            last2 = last1; last1 = Move.Kick;
            // tell: head dips, weight rocks forward, a hind hoof paws the ground, a quiet snort
            StartWindup(S.KickWindup, "kick_windup", 0.42f / sp);
            hitDone = false; impactDone = false;
            Sfx.Play("snort", 0.45f, 0.08f);
        }

        void StartWindup(S s, string clip, float dur)
        {
            Go(s);
            windDur = dur;
            int n = Mathf.Max(1, anim.CountOf(clip));
            anim.Play(clip, n / dur, false, true);
            if (s == S.SwingWindup) Sfx.Play("windup", 0.6f, 0.05f);
        }

        void BeginCharge()
        {
            chargesLeft = Phase >= 2 ? 1 : 0;
            if (!TryBackHop()) StartRear(0.7f / Spd);
        }

        /// <summary>
        /// A charge needs room: when the player is closer than ChargeRoom the Warden first leaps backwards (facing the
        /// player) to that distance, then rears up and gallops. Cornered against the arena edge it charges from where it is.
        /// </summary>
        bool TryBackHop()
        {
            var p = Player;
            if (p == null || p.IsDead) return false;
            float dx = p.transform.position.x - transform.position.x;
            if (Mathf.Abs(dx) >= ChargeRoom - 0.5f) return false;
            FaceTo(dx);
            float target = Mathf.Clamp(p.transform.position.x - facing * ChargeRoom, arenaMin + 2.6f, arenaMax - 2.6f);
            if ((target - transform.position.x) * -facing < HopMinGain) return false;
            hopTargetX = target;
            hopPhase = 0;
            Go(S.BackHop);
            anim.Play("crouch", Mathf.Max(1, anim.CountOf("crouch")) / 0.16f * Spd, false, true);
            return true;
        }

        void StartRear(float dur)
        {
            Go(S.RearUp);
            windDur = dur;
            anim.Play("rear", 6f / dur, false, true);
            Sfx.Play("neigh", 0.8f, 0.08f);
        }

        void StartCharge()
        {
            Go(S.Charge);
            hitDone = false;
            chargeSwept = false;
            nextSpark = 0f;
            chargeStartX = transform.position.x;
            chargeSpeed = (Phase >= 2 ? 15f : 12f) * (Phase == 3 ? 1.1f : 1f);
            anim.Play("gallop_drag", 14f, true, true);
            nextGallopSfx = 0f;
            Fx.DustBig(transform.position, 1.2f);
        }

        void StartSkid()
        {
            bool keep = sweeping;      // let a glaive arc that is already flying finish
            Go(S.Skid);
            sweeping = keep;
            anim.Play("skid", 10f, false, true);
            Fx.DustBig(transform.position + new Vector3(facing * 1.5f, 0f, 0f), 1.2f);
        }

        void StartCrouch()
        {
            StartWindup(S.Crouch, "crouch", 0.6f / Spd);
            Sfx.Play("danger", 0.9f, 0f);
            // unparryable warning: a single red cross glint over the rider, nothing that lingers
            FxAnim.Spawn("FX", "danger_cross", transform.position + new Vector3(facing * 0.6f, 7.2f, 0f), 16f, 2.2f, Order.Fx + 3);
        }

        void StartPhaseShift()
        {
            Go(S.PhaseShift);
            Invulnerable = true;
            pushed = false;
            chargesLeft = 0;
            anim.Tint = Color.white;
            anim.Play("roar", 6f, false, true);
            Sfx.Play("roar", 1f, 0f);
            TimeFx.Slow(0.5f, 0.8f);
            if (CameraFollow.I != null) CameraFollow.I.Punch(0.8f);
            if (HUD.I != null) HUD.I.Banner(DisplayName + " — CUỒNG NỘ", new Color(0.6f, 1f, 0.9f));
        }

        void StartLand()
        {
            rb.collisionDetectionMode = CollisionDetectionMode2D.Discrete;
            Go(S.Land);
            hitDone = false;
            anim.Play("land", 12f, false, true);
            Sfx.Play("stomp", 1f, 0.03f);
            Fx.DustBig(transform.position, 2.0f);
            Fx.DustBig(transform.position + new Vector3(3.6f, 0f, 0f), 1.4f);
            Fx.DustBig(transform.position - new Vector3(3.6f, 0f, 0f), 1.4f);
            if (CameraFollow.I != null) CameraFollow.I.Shake(0.35f, 0.4f);
            if (Phase >= 2)
            {
                Sfx.Play("shock", 0.9f);
                Shockwave.Spawn(transform.position + new Vector3(3.2f, 0f, 0f), 1, Dmg);
                Shockwave.Spawn(transform.position - new Vector3(3.2f, 0f, 0f), -1, Dmg);
            }
        }

        void StartRecover(float dur)
        {
            Go(S.Recover);
            recoverFor = dur;
        }

        /// <summary>Being hit while idling/walking makes the Warden answer quickly instead of soaking up a combo.</summary>
        protected override void OnHurt(int dir, float knock)
        {
            if (st != S.Approach && st != S.Recover) return;
            if (Time.time - firstHitAt > 1.4f) { firstHitAt = Time.time; hitsTaken = 0; }
            hitsTaken++;
            if (hitsTaken >= 2)
            {
                hitsTaken = 0;
                quickNext = true;
                if (st == S.Recover) Go(S.Approach);
                cooldownUntil = Time.time;
            }
        }

        /// <summary>Perfect parry: the Warden is not knocked back — it only loses stamina and keeps attacking.</summary>
        protected override void OnParried()
        {
            flashTimer = 0.06f;
        }

        protected override void OnCollapse()
        {
            anim.Tint = Color.white;
            Go(S.Collapsed);
            chargesLeft = 0;
            anim.Play("collapse", 9f, false, true);
        }

        protected override void OnCollapseEnd()
        {
            Go(S.Getup);
            anim.Play("getup", 8f, false, true);
        }

        protected override void OnDeath()
        {
            Go(S.Dead);
            anim.Play("death", 8f, false, true);
        }
    }

    /// <summary>Ground shockwave from the phase-2 stomp. Cannot be parried — jump over it or roll through.</summary>
    public class Shockwave : MonoBehaviour
    {
        int dir, hearts;
        float age;
        bool hit;
        SpriteAnim anim;
        SpriteRenderer sr;
        const float Speed = 9f, Life = 1.7f;

        public static void Spawn(Vector3 pos, int dir, int hearts)
        {
            var go = new GameObject("Shockwave");
            if (GameManager.I != null && GameManager.I.WorldRoot != null) go.transform.SetParent(GameManager.I.WorldRoot, false);
            go.transform.position = pos;
            var s = go.AddComponent<Shockwave>();
            s.dir = dir;
            s.hearts = hearts;
            s.sr = Gfx.Renderer(go, null, Order.Fx);
            s.anim = new SpriteAnim(s.sr, SpriteBank.Set("FX"));
            s.anim.Play("shock", 16f, true, true);
            go.transform.localScale = new Vector3(dir * 1.3f, 1.3f, 1f);
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            age += dt;
            var p = transform.position;
            p.x += dir * Speed * dt;
            var g = Physics2D.Raycast(new Vector2(p.x, p.y + 1.5f), Vector2.down, 4f, Layers.GroundMask);
            if (g.collider != null) p.y = g.point.y + 0.97f;
            var wall = Physics2D.Raycast(new Vector2(p.x, p.y), new Vector2(dir, 0f), 0.5f, Layers.GroundMask);
            transform.position = p;
            if (!hit)
            {
                var c = Physics2D.OverlapBox(new Vector2(p.x, p.y), new Vector2(1.1f, 1.4f), 0f, Layers.PlayerMask);
                if (c != null)
                {
                    var pc = c.GetComponentInParent<PlayerController>();
                    if (pc != null && pc.ReceiveHit(new HitInfo { hearts = hearts, sourceX = p.x - dir, point = p, unparryable = true }, null) == HitResult.Hit)
                        hit = true;
                }
            }
            if (Random.value < 0.4f) Fx.Sparks(new Vector3(p.x, p.y - 0.6f, 0f), new Color(1f, 0.4f, 0.35f), 1, 3f);
            anim.Tick(dt);
            if (age > Life - 0.3f) sr.color = new Color(1f, 1f, 1f, Mathf.Clamp01((Life - age) / 0.3f));
            if (age >= Life || (wall.collider != null && Mathf.Abs(wall.normal.x) > 0.7f)) Destroy(gameObject);
        }
    }

    /// <summary>
    /// Blazing crescent / thrust slash (sprite frames from Tools/ArtGenerator/fx_slash.py). Drawn around the rider's hands,
    /// follows the boss, grows over the swing (frames 0-4) then burns away (frames 5-7).
    /// </summary>
    public class SlashFx : MonoBehaviour
    {
        Transform anchor;
        Vector3 lastAnchor;
        Vector2 pivot;
        int face;
        SpriteAnim anim;
        string clip;
        float burnFps;

        public static void Spawn(string clip, Transform anchor, Vector2 pivot, int face, float sweepDur, float angleDeg)
        {
            var go = new GameObject("Slash_" + clip);
            if (GameManager.I != null && GameManager.I.WorldRoot != null) go.transform.SetParent(GameManager.I.WorldRoot, false);
            var fx = go.AddComponent<SlashFx>();
            fx.anchor = anchor; fx.pivot = pivot; fx.face = face; fx.clip = clip;
            fx.lastAnchor = anchor != null ? anchor.position : Vector3.zero;
            var sr = Gfx.Renderer(go, null, Order.Fx + 1);
            fx.anim = new SpriteAnim(sr, SpriteBank.Set("FX"));
            float grow = 5f / Mathf.Max(0.05f, sweepDur);       // 5 growing frames across the swing
            fx.burnFps = Mathf.Min(grow, 18f);                    // burn-out a little slower
            fx.anim.Play(clip, grow, false, true);
            go.transform.localScale = new Vector3(face, 1f, 1f);
            go.transform.rotation = Quaternion.Euler(0f, 0f, angleDeg * face);
            fx.Place();
        }

        void Place()
        {
            if (anchor != null) lastAnchor = anchor.position;
            transform.position = lastAnchor + new Vector3(pivot.x * face, pivot.y, 0f);
        }

        void LateUpdate()
        {
            Place();
            if (anim.Frame >= 5) anim.Play(clip, burnFps, false);
            anim.Tick(Time.deltaTime);
            if (anim.Finished) Destroy(gameObject);
        }
    }
}
