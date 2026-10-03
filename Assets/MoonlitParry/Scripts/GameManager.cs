using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace MoonlitParry
{
    /// <summary>Story progress that survives deaths / rests (reset on a full restart).</summary>
    public static class Progress
    {
        public static int Checkpoint = -1;          // -1 = start, otherwise bonfire id
        public static bool MoundDone, FirstZombieDead, ArenaCleared, BossIntroDone, FinisherHintShown, ToolsHintShown, BonfireHintShown;
        public static bool[] BonfireLit = new bool[16];

        public static void Reset()
        {
            Checkpoint = -1;
            MoundDone = FirstZombieDead = ArenaCleared = BossIntroDone = FinisherHintShown = ToolsHintShown = BonfireHintShown = false;
            BonfireLit = new bool[16];
        }
    }

    /// <summary>
    /// Entry point & story director. Put it on one GameObject in an empty scene and press Play:
    /// black screen → wake up → plains tutorial → mound cutscene → first undead → bonfire → ruins trap
    /// → road → boss gate → boss intro → fight → demo end. Deaths respawn at the last bonfire.
    /// </summary>
    [DefaultExecutionOrder(-50)]
    public class GameManager : MonoBehaviour
    {
        public static GameManager I;
        public enum Flow { Intro, Playing, Cutscene, Resting, Dead, Victory }

        [Tooltip("Máu boss, tốc độ, cửa sổ parry... (chỉnh ngoài Play mode để lưu lại)")]
        public Tuning tuning = new Tuning();
        LayoutData layout;
        ParallaxBackground parallax;

        public PlayerController Player { get; private set; }
        public LevelData Level { get; private set; }
        public Transform WorldRoot { get; private set; }
        public Flow State { get; private set; }
        public bool Paused { get; private set; }
        public bool BossActive { get; private set; }
        public bool ShowDebug;
        public bool ManualPhysics { get; private set; }
        public bool InputEnabled { get { return State == Flow.Playing && !Paused; } }

        /// <summary>Pause / resume (keyboard Esc-P or the pause menu buttons).</summary>
        public void SetPaused(bool p)
        {
            if (p && !(State == Flow.Playing || State == Flow.Cutscene)) return;
            Paused = p;
            timeFx.Paused = p;
        }

        /// <summary>Pause menu: back to the nearest lit bonfire (applies the tuning).</summary>
        public void RestartFromBonfire()
        {
            if (!Paused) return;
            SetPaused(false);
            Run(Respawn());
        }

        /// <summary>Pause menu: leave the game (stops Play mode in the editor).</summary>
        public void QuitGame()
        {
            PlayerPrefs.Save();
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
        public float DeadSince { get; private set; }

        Camera cam;
        CameraFollow follow;
        TimeFx timeFx;
        HUD hud;
        bool arenaActive, arenaSpawning;
        readonly List<EnemyBase> arenaEnemies = new List<EnemyBase>();
        Coroutine flowRoutine;

        void Awake()
        {
            I = this;
            Application.targetFrameRate = 60;
            QualitySettings.vSyncCount = 1;
            if (Physics2D.gravity.sqrMagnitude < 0.01f) Physics2D.gravity = new Vector2(0f, -9.81f);
#if UNITY_2022_1_OR_NEWER
            ManualPhysics = Physics2D.simulationMode == SimulationMode2D.Script;
#endif
            timeFx = gameObject.AddComponent<TimeFx>();
            gameObject.AddComponent<Capture>();
            gameObject.AddComponent<ImguiKeys>();
            Sfx.Init(gameObject);
            hud = gameObject.AddComponent<HUD>();

            cam = Camera.main;
            if (cam == null)
            {
                var cgo = new GameObject("Main Camera");
                cgo.tag = "MainCamera";
                cam = cgo.AddComponent<Camera>();
                cgo.AddComponent<AudioListener>();
            }
            cam.orthographic = true;
            cam.orthographicSize = CameraFollow.BaseSize;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.02f, 0.027f, 0.04f);
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 100f;
            follow = cam.GetComponent<CameraFollow>();
            if (follow == null) follow = cam.gameObject.AddComponent<CameraFollow>();

            parallax = new GameObject("Parallax").AddComponent<ParallaxBackground>();
            parallax.Build(cam);
            layout = LoadLayout();
            new GameObject("Ambient").AddComponent<AmbientParticles>().Build(cam);

            Sfx.Music("music_moonlit");
            Progress.Reset();
            Run(Intro());
        }

        /// <summary>Use the scene's LevelLayout (drag-and-drop editing) if there is one, else the built-in map.</summary>
        public static string LayoutInfo = "";

        LayoutData LoadLayout()
        {
#if UNITY_2023_1_OR_NEWER
            var lay = Object.FindFirstObjectByType<LevelLayout>(FindObjectsInactive.Include);
#else
            var lay = Object.FindObjectOfType<LevelLayout>(true);
#endif
            if (lay == null)
            {
                LayoutInfo = "Bản đồ: MẶC ĐỊNH (scene chưa có LevelLayout hợp lệ — Stop, chạy menu 3, Ctrl+S)";
                Debug.Log("[Moonlit Parry] " + LayoutInfo);
                return LayoutData.Default();
            }
            var d = lay.Read();
            int decor = d.trees.Count + d.rocks.Count + d.bushes.Count + d.ruinWalls.Count + d.ruinPillars.Count + d.ferns.Count + d.shrooms.Count;
            LayoutInfo = "Bản đồ: LevelLayout của bạn — " + (d.zombies.Count + (float.IsNaN(d.firstZombie) ? 0 : 1)) + " quái, " +
                         d.bonfires.Count + " lửa trại, " + d.platforms.Count + " bục, " + decor + " đồ trang trí";
            Debug.Log("[Moonlit Parry] " + LayoutInfo);
            lay.gameObject.SetActive(false);   // hide the editor previews while playing
            return d;
        }

        void Run(IEnumerator r)
        {
            if (flowRoutine != null) StopCoroutine(flowRoutine);
            flowRoutine = StartCoroutine(r);
        }

        // ------------------------------------------------------------------ world
        void BuildWorld(PlayerController.SpawnMode mode)
        {
            if (WorldRoot != null)
            {
                WorldRoot.gameObject.SetActive(false);
                Destroy(WorldRoot.gameObject);
            }
            Actors.Clear();
            timeFx.ResetAll();
            Time.timeScale = 1f;
            Paused = false;

            WorldRoot = new GameObject("World").transform;
            Level = LevelBuilder.Build(WorldRoot, layout);
            parallax.SetArenaX(Level.bossGateX);
            if (Progress.Checkpoint >= Level.bonfireX.Count) Progress.Checkpoint = -1;
            Vector3 spawn = Progress.Checkpoint < 0
                ? Level.spawn
                : new Vector3(Level.bonfireX[Progress.Checkpoint] + 1.3f, LevelBuilder.StandAt(Level.bonfireX[Progress.Checkpoint] + 1.3f, Level.bonfireY[Progress.Checkpoint]) + 0.02f, 0f);
            Player = new GameObject("Player").AddComponent<PlayerController>();
            Player.transform.SetParent(WorldRoot, false);
            Player.Init(spawn, mode);

            arenaActive = false;
            arenaSpawning = false;
            arenaEnemies.Clear();
            BossActive = false;
            follow.target = Player.transform;
            follow.minX = Level.levelMinX;
            follow.maxX = Level.levelMaxX;
            follow.minY = CameraFollow.RestY;
            follow.maxY = CameraFollow.RestY + 5f;
            follow.Focus(null);
            follow.Snap();
            Sfx.Music("music_moonlit");
            Sfx.MusicVolume(0.3f);
        }

        IEnumerator FadeTo(float target, float seconds)
        {
            float start = hud.FadeAlpha, t = 0f;
            while (t < seconds)
            {
                t += Time.unscaledDeltaTime;
                hud.FadeAlpha = Mathf.Lerp(start, target, Mathf.Clamp01(t / seconds));
                yield return null;
            }
            hud.FadeAlpha = target;
        }

        // ------------------------------------------------------------------ story beats
        IEnumerator Intro()
        {
            State = Flow.Intro;
            hud.ResetHud();
            hud.FadeAlpha = 1f;
            BuildWorld(PlayerController.SpawnMode.Wake);
            Player.HoldWake(true);
            yield return new WaitForSecondsRealtime(0.8f);
            hud.CenterText(Story.Opening);
            yield return new WaitForSecondsRealtime(4.6f);
            hud.CenterText(null);
            yield return new WaitForSecondsRealtime(0.6f);
            Player.HoldWake(false);
            yield return FadeTo(0f, 1.8f);
            State = Flow.Playing;
            hud.Hint(Story.HintMove, 9f);
        }

        IEnumerator MoundScene()
        {
            State = Flow.Cutscene;
            Progress.MoundDone = true;
            var z = Level.firstZombie;
            hud.Dialog("", Story.MoundLine, 3.0f);
            if (z != null) follow.Focus(z.transform.position + new Vector3(0f, 1.4f, 0f), 4.4f);
            yield return new WaitForSecondsRealtime(3.0f);
            follow.Focus(null);
            hud.Hint(Story.HintCombat, 12f);
            State = Flow.Playing;
        }

        IEnumerator ArenaTrap()
        {
            arenaActive = true;
            arenaSpawning = true;
            Level.arenaIn.Close();
            Level.arenaOut.Close();
            follow.minX = Level.arenaMinX - 1.5f;
            follow.maxX = Level.arenaMaxX + 1.5f;
            hud.Hint(Story.HintTrap, 6f);
            yield return new WaitForSecondsRealtime(0.7f);
            float px = Player.transform.position.x;
            float[] xs = { Mathf.Clamp(px - 7f, Level.arenaMinX + 1f, Level.arenaMaxX - 1f), Mathf.Clamp(px + 7f, Level.arenaMinX + 1f, Level.arenaMaxX - 1f) };
            var parent = Level.root.Find("Enemies");
            foreach (var x in xs)
            {
                var z = LevelBuilder.SpawnZombie(parent, x, true);
                arenaEnemies.Add(z);
                Level.enemies.Add(z);
                yield return new WaitForSeconds(0.35f);
            }
            arenaSpawning = false;
        }

        void CheckArena()
        {
            if (!arenaActive || arenaSpawning) return;
            arenaEnemies.RemoveAll(e => e == null || e.IsDead);
            if (arenaEnemies.Count > 0) return;
            arenaActive = false;
            Progress.ArenaCleared = true;
            Level.arenaIn.Open();
            Level.arenaOut.Open();
            follow.minX = Level.levelMinX;
            follow.maxX = Level.levelMaxX;
            hud.Hint(Story.HintArenaClear, 3f);
        }

        IEnumerator BossIntro()
        {
            BossActive = true;
            Level.bossDoor.Close();
            follow.minX = Level.bossArenaMin - 1.6f;
            follow.maxX = Level.bossArenaMax;
            Sfx.Music("music_boss");
            Sfx.MusicVolume(0.5f);
            if (Progress.BossIntroDone)
            {
                Level.boss.Wake();
                yield break;
            }
            State = Flow.Cutscene;
            Progress.BossIntroDone = true;
            yield return new WaitForSecondsRealtime(0.4f);
            follow.Focus(Level.boss.transform.position + new Vector3(-1.5f, 3.6f, 0f), 6.2f);
            yield return new WaitForSecondsRealtime(0.9f);
            Level.boss.Taunt();
            yield return new WaitForSecondsRealtime(0.6f);
            hud.Dialog(Story.BossName, Story.BossLine, 4.0f);
            yield return new WaitForSecondsRealtime(4.2f);
            follow.Focus(null);
            State = Flow.Playing;
            Level.boss.Wake();
            hud.Hint(Story.HintRed, 7f);
        }

        IEnumerator Rest(Bonfire b)
        {
            State = Flow.Resting;
            Player.BeginRest();
            if (!b.Lit)
            {
                b.SetLit(true, true);
                Progress.BonfireLit[b.Id] = true;
            }
            else Sfx.Play("rest", 0.7f, 0f);
            Progress.Checkpoint = b.Id;
            hud.Popup("Đã nghỉ ngơi — điểm hồi sinh được lưu", b.transform.position + Vector3.up * 2.4f, new Color(1f, 0.85f, 0.6f));
            yield return new WaitForSecondsRealtime(1.2f);
            yield return FadeTo(1f, 0.6f);
            BuildWorld(PlayerController.SpawnMode.FromRest);
            yield return new WaitForSecondsRealtime(0.3f);
            yield return FadeTo(0f, 0.8f);
            State = Flow.Playing;
            if (!Progress.ToolsHintShown)
            {
                Progress.ToolsHintShown = true;
                hud.Hint(Story.HintTools, 9f);
            }
        }

        IEnumerator Respawn()
        {
            State = Flow.Resting;
            yield return FadeTo(1f, 0.7f);
            hud.HideDeath();
            BuildWorld(Progress.Checkpoint < 0 ? PlayerController.SpawnMode.Wake : PlayerController.SpawnMode.FromRest);
            yield return new WaitForSecondsRealtime(0.3f);
            yield return FadeTo(0f, 0.9f);
            State = Flow.Playing;
        }

        IEnumerator VictoryRoutine()
        {
            yield return new WaitForSecondsRealtime(3.0f);
            if (Player != null && Player.IsDead) yield break;
            State = Flow.Victory;
            Sfx.Music("music_moonlit");
            Sfx.MusicVolume(0.3f);
            if (Level != null && Level.bossDoor != null) Level.bossDoor.Open();
            hud.ShowEnd();
        }

        // ------------------------------------------------------------------ callbacks
        public void OnPlayerDied()
        {
            if (State == Flow.Victory) return;
            State = Flow.Dead;
            DeadSince = Time.unscaledTime;
            hud.ShowDeath();
            Sfx.Play("death_sting", 1f, 0f);
            Sfx.MusicVolume(0.08f);
        }

        public void OnEnemyDied(EnemyBase e)
        {
            if (Level != null && e == Level.boss) Run(VictoryRoutine());
        }

        public void OnEnemyCollapsed(EnemyBase e)
        {
            if (Progress.FinisherHintShown) return;
            Progress.FinisherHintShown = true;
            hud.Hint(Story.HintFinisher, 6f);
        }

        public EnemyBase FindFinishable(Vector3 pos)
        {
            if (Level == null) return null;
            EnemyBase best = null;
            float bd = float.MaxValue;
            foreach (var e in Level.enemies)
            {
                if (e == null || !e.CanBeFinished) continue;
                float dx = Mathf.Abs(e.transform.position.x - pos.x);
                float dy = Mathf.Abs(e.transform.position.y - pos.y);
                if (dx < e.FinisherDistance + 1.7f && dy < 2.2f && dx < bd) { bd = dx; best = e; }
            }
            return best;
        }

        public Bonfire NearBonfire()
        {
            if (Level == null || Player == null) return null;
            foreach (var b in Level.bonfires)
                if (b != null && Mathf.Abs(b.transform.position.x - Player.transform.position.x) < 1.8f &&
                    Mathf.Abs(b.transform.position.y - Player.transform.position.y) < 1.5f) return b;
            return null;
        }

        // ------------------------------------------------------------------ loop
        void FixedUpdate()
        {
#if UNITY_2022_1_OR_NEWER
            if (ManualPhysics) Physics2D.Simulate(Time.fixedDeltaTime);
#endif
        }

        void Update()
        {
            if (GameInput.DebugDown) ShowDebug = !ShowDebug;
            // test shortcut (0 or F2): jump to just before the boss gate
            if (GameInput.CaptureDown) Capture.Shot("key");
            if (GameInput.WarpDown && Player != null && Level != null && State == Flow.Playing && !BossActive)
            {
                float wx = Level.bossGateX - 3f;
                Player.Teleport(new Vector3(wx, LevelBuilder.SurfaceAt(wx) + 0.05f, 0f));
                follow.Snap();
            }

            if (State == Flow.Dead)
            {
                if (Time.unscaledTime - DeadSince > 1.6f && GameInput.RetryDown) Run(Respawn());
                return;
            }
            if (State == Flow.Victory)
            {
                if (GameInput.RetryDown) { Progress.Reset(); Run(Intro()); }
                return;
            }
            if (GameInput.PauseDown && (State == Flow.Playing || State == Flow.Cutscene))
                SetPaused(!Paused);
            if (Paused)
            {
                if (GameInput.RetryDown) { Paused = false; timeFx.Paused = false; Run(Respawn()); }
                return;
            }
            if (State != Flow.Playing || Player == null || Level == null) return;

            var pp = Player.transform.position;

            // look-out mound → first sight of the undead
            if (!Progress.MoundDone && pp.x > Level.moundMinX && pp.x < Level.moundMaxX && pp.y > Level.moundMinY && Player.Grounded)
            {
                if (Level.firstZombie != null && !Level.firstZombie.IsDead) Run(MoundScene());
                else Progress.MoundDone = true;
                return;
            }

            // bonfire
            var b = NearBonfire();
            if (b != null && !Progress.BonfireHintShown)
            {
                Progress.BonfireHintShown = true;
                hud.Hint(Story.HintBonfire, 6f);
            }
            if (b != null && Player.CanInteract && GameInput.InteractDown)
            {
                Run(Rest(b));
                return;
            }

            // ruins trap
            if (!Progress.ArenaCleared && !arenaActive && pp.x >= Level.arenaTriggerX && pp.x < Level.arenaMaxX)
                Run(ArenaTrap());
            CheckArena();

            // boss
            if (!BossActive && pp.x > Level.bossTriggerX)
                Run(BossIntro());
        }

        void OnDestroy()
        {
            if (I == this) I = null;
        }
    }
}
