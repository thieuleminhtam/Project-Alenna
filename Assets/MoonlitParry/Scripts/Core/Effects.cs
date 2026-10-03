using System.Collections.Generic;
using UnityEngine;

namespace MoonlitParry
{
    /// <summary>Hit-stop and slow motion (parry). Runs before everything else.</summary>
    [DefaultExecutionOrder(-100)]
    public class TimeFx : MonoBehaviour
    {
        public static TimeFx I;
        float stopUntil, slowUntil, slowScale = 1f;
        public bool Paused;

        void Awake() { I = this; }

        void OnDestroy()
        {
            if (I == this) { I = null; Time.timeScale = 1f; Time.fixedDeltaTime = 0.02f; }
        }

        public static void HitStop(float seconds)
        {
            if (I == null) return;
            I.stopUntil = Mathf.Max(I.stopUntil, Time.unscaledTime + seconds);
        }

        public static void Slow(float scale, float seconds)
        {
            if (I == null) return;
            I.slowScale = scale;
            I.slowUntil = Time.unscaledTime + seconds;
        }

        public void ResetAll()
        {
            stopUntil = slowUntil = 0f;
            Paused = false;
        }

        void Update()
        {
            float now = Time.unscaledTime;
            float ts = now < stopUntil ? 0f : (now < slowUntil ? slowScale : 1f);
            if (Paused) ts = 0f;
            Time.timeScale = ts;
            Time.fixedDeltaTime = 0.02f * Mathf.Max(0.1f, ts);
        }
    }

    public static class Sfx
    {
        static AudioSource[] pool;
        static AudioSource music, musicOld;      // crossfade between tracks
        static float musicVol = 0.32f;
        static int idx;
        static readonly Dictionary<string, AudioClip> clips = new Dictionary<string, AudioClip>();

        /// <summary>Player's volume settings (0..1), saved between sessions; set from the pause menu.</summary>
        public static float UserMusic { get; private set; }
        public static float UserSfx { get; private set; }

        public static void SetUserVolume(float musicV, float sfxV)
        {
            UserMusic = Mathf.Clamp01(musicV);
            UserSfx = Mathf.Clamp01(sfxV);
            if (music != null) music.volume = musicVol * UserMusic;
            PlayerPrefs.SetFloat("mp_music", UserMusic);
            PlayerPrefs.SetFloat("mp_sfx", UserSfx);
        }

        public static void Init(GameObject host)
        {
            UserMusic = PlayerPrefs.GetFloat("mp_music", 1f);
            UserSfx = PlayerPrefs.GetFloat("mp_sfx", 1f);
            pool = new AudioSource[14];
            for (int i = 0; i < pool.Length; i++)
            {
                var s = host.AddComponent<AudioSource>();
                s.playOnAwake = false;
                pool[i] = s;
            }
            music = host.AddComponent<AudioSource>();
            music.loop = true;
            music.playOnAwake = false;
            music.volume = musicVol * UserMusic;
            musicOld = host.AddComponent<AudioSource>();
            musicOld.loop = true;
            musicOld.playOnAwake = false;
            musicOld.volume = 0f;
            if (host.GetComponent<MusicFader>() == null) host.AddComponent<MusicFader>();
        }

        /// <summary>Called every frame by MusicFader: new track fades in, previous one fades out.</summary>
        public static void TickMusic(float dt)
        {
            if (music == null) return;
            music.volume = Mathf.MoveTowards(music.volume, musicVol * UserMusic, dt / 1.5f);
            if (musicOld != null && musicOld.isPlaying)
            {
                musicOld.volume = Mathf.MoveTowards(musicOld.volume, 0f, dt / 1.2f);
                if (musicOld.volume <= 0f) musicOld.Stop();
            }
        }

        static AudioClip Get(string n)
        {
            AudioClip c;
            if (!clips.TryGetValue(n, out c) || c == null)
            {
                c = Resources.Load<AudioClip>("Audio/" + n);
                clips[n] = c;
            }
            return c;
        }

        public static void Play(string n, float vol = 1f, float pitchVar = 0.06f)
        {
            if (pool == null) return;
            var c = Get(n);
            if (c == null) return;
            var s = pool[idx++ % pool.Length];
            if (s == null) return;
            s.pitch = 1f + Random.Range(-pitchVar, pitchVar);
            s.volume = vol * UserSfx;
            s.clip = c;
            s.Play();
        }

        public static AudioClip Clip(string n) { return Get(n); }

        /// <summary>Looping positional-ish source (volume driven by the owner, e.g. a bonfire).</summary>
        public static AudioSource Loop(GameObject host, string n)
        {
            var c = Get(n);
            var s = host.AddComponent<AudioSource>();
            s.clip = c;
            s.loop = true;
            s.playOnAwake = false;
            s.volume = 0f;
            if (c != null) s.Play();
            return s;
        }

        public static void MusicVolume(float v)
        {
            musicVol = v;
        }

        /// <summary>Switch the background music (crossfades; does nothing if that track is already playing).</summary>
        public static void Music(string n)
        {
            if (music == null) return;
            var c = Get(n);
            if (c == null || music.clip == c) return;
            var t = musicOld; musicOld = music; music = t;
            music.clip = c;
            music.volume = musicOld.clip == null || !musicOld.isPlaying ? musicVol * UserMusic : 0f;
            music.Play();
        }
    }

    /// <summary>Drives the music crossfade (unscaled time so it keeps going during slow-mo / pause).</summary>
    public class MusicFader : MonoBehaviour
    {
        void Update() { Sfx.TickMusic(Time.unscaledDeltaTime); }
    }

    /// <summary>One-shot animated effect or simple particle.</summary>
    public class FxAnim : MonoBehaviour
    {
        SpriteAnim anim;
        SpriteRenderer sr;
        Vector2 vel;
        float gravity, life = -1f, age, drag, spin;
        Color baseCol = Color.white;
        bool fade;
        bool unscaled;
        bool align;

        public static FxAnim Spawn(string folder, string clip, Vector3 pos, float fps, float scale = 1f, int order = Order.Fx, bool flip = false)
        {
            var go = new GameObject("fx_" + clip);
            go.transform.position = pos;
            go.transform.localScale = new Vector3(flip ? -scale : scale, scale, 1f);
            var fx = go.AddComponent<FxAnim>();
            fx.sr = Gfx.Renderer(go, null, order);
            fx.anim = new SpriteAnim(fx.sr, SpriteBank.Set(folder));
            fx.anim.Play(clip, fps, false, true);
            if (GameManager.I != null && GameManager.I.WorldRoot != null) go.transform.SetParent(GameManager.I.WorldRoot, true);
            return fx;
        }

        public static FxAnim Particle(Sprite s, Vector3 pos, Vector2 vel, float life, Color col, float gravity = 0f, float scale = 1f, int order = Order.Fx)
        {
            var go = new GameObject("p");
            go.transform.position = pos;
            go.transform.localScale = new Vector3(scale, scale, 1f);
            var fx = go.AddComponent<FxAnim>();
            fx.sr = Gfx.Renderer(go, s, order);
            fx.sr.color = col;
            fx.baseCol = col;
            fx.vel = vel;
            fx.life = life;
            fx.gravity = gravity;
            fx.fade = true;
            fx.drag = 2f;
            if (GameManager.I != null && GameManager.I.WorldRoot != null) go.transform.SetParent(GameManager.I.WorldRoot, true);
            return fx;
        }

        public FxAnim Tint(Color c) { baseCol = c; sr.color = c; return this; }
        /// <summary>Rotate the sprite along its velocity every frame (streak sparks).</summary>
        public FxAnim Align() { align = true; return this; }
        public FxAnim Unscaled() { unscaled = true; return this; }
        public FxAnim Spin(float degPerSec) { spin = degPerSec; return this; }

        void Update()
        {
            float dt = unscaled ? Time.unscaledDeltaTime : Time.deltaTime;
            age += dt;
            if (anim != null)
            {
                anim.Tick(dt);
                if (anim.Finished) { Destroy(gameObject); return; }
            }
            else
            {
                vel.y -= gravity * dt;
                vel *= Mathf.Max(0f, 1f - drag * dt);
                transform.position += (Vector3)(vel * dt);
                if (spin != 0f) transform.Rotate(0f, 0f, spin * dt);
                if (align && vel.sqrMagnitude > 1e-4f) transform.rotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(vel.y, vel.x) * Mathf.Rad2Deg);
                if (fade && life > 0f)
                {
                    float a = 1f - age / life;
                    sr.color = new Color(baseCol.r, baseCol.g, baseCol.b, baseCol.a * Mathf.Clamp01(a));
                }
                if (life > 0f && age >= life) Destroy(gameObject);
            }
        }
    }

    public static class Fx
    {
        static Sprite pix, glow, spark;
        static bool sparkChecked;
        static Sprite Pix { get { if (pix == null) pix = SpriteBank.One("FX/pixel"); return pix; } }
        /// <summary>Small round ember / mote (HD pollen dot, or the pixel).</summary>
        public static Sprite EmberSprite { get { return Spark != null ? SpriteBank.One("FX/pollen") : Pix; } }
        public static float EmberScale { get { return Spark != null ? 0.7f : 1f; } }

        /// <summary>HD streak spark (null when only the pixel art is present).</summary>
        static Sprite Spark
        {
            get
            {
                if (!sparkChecked) { sparkChecked = true; spark = SpriteBank.HDAllowed("FX/spark") ? Resources.Load<Sprite>("HD/FX/spark") : null; }
                return spark;
            }
        }
        public static Sprite Glow { get { if (glow == null) glow = SpriteBank.One("FX/glow"); return glow; } }

        public static void Hit(Vector3 p, bool big)
        {
            FxAnim.Spawn("FX", "hit", p, 22f, big ? 1.6f : 1f);
            Sparks(p, new Color(1f, 0.86f, 0.7f), big ? 12 : 6, big ? 9f : 6f);
        }

        public static void Parry(Vector3 p)
        {
            FxAnim.Spawn("FX", "parry", p, 20f, 1.2f, Order.Fx + 2).Unscaled();
            Sparks(p, new Color(1f, 0.92f, 0.55f), 16, 11f);
            Sparks(p, new Color(0.7f, 0.92f, 1f), 10, 7f);
            var g = FxAnim.Particle(Glow, p, Vector2.zero, 0.35f, new Color(0.75f, 0.92f, 1f, 0.8f), 0f, 3f, Order.Fx + 1);
            g.Unscaled();
        }

        public static void Finisher(Vector3 p, float scale)
        {
            FxAnim.Spawn("FX", "finisher", p, 18f, scale, Order.Fx + 3).Unscaled();
            Sparks(p, new Color(0.8f, 0.95f, 1f), 18, 12f);
            Sparks(p, new Color(0.85f, 0.15f, 0.2f), 10, 7f);
        }

        public static void Break(Vector3 p, float scale)
        {
            FxAnim.Spawn("FX", "break", p, 18f, scale, Order.Fx + 2);
            Sparks(p, new Color(1f, 0.85f, 0.4f), 12, 8f);
        }

        public static void DustBig(Vector3 p, float scale = 1f)
        {
            FxAnim.Spawn("FX", "dustbig", p + new Vector3(0f, 0.6f * scale, 0f), 14f, scale, Order.Player + 1);
        }

        public static void Block(Vector3 p)
        {
            Sparks(p, new Color(0.85f, 0.9f, 1f), 6, 5f);
        }

        public static void Dust(Vector3 p, float scale = 1f)
        {
            FxAnim.Spawn("FX", "dust", p + new Vector3(0f, 0.37f * scale, 0f), 16f, scale, Order.Player - 1);
        }

        public static void Sparks(Vector3 p, Color c, int n, float speed)
        {
            var sp = Spark;
            for (int i = 0; i < n; i++)
            {
                float a = Random.value * Mathf.PI * 2f;
                var v = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * speed * Random.Range(0.4f, 1f);
                if (sp != null) FxAnim.Particle(sp, p, v, Random.Range(0.18f, 0.4f), c, 18f, Random.Range(0.5f, 1.0f)).Align();
                else FxAnim.Particle(Pix, p, v, Random.Range(0.18f, 0.4f), c, 18f, Random.value < 0.3f ? 2f : 1f);
            }
        }

        public static void Rise(Vector3 p, Color c, int n, float spread, float speed)
        {
            for (int i = 0; i < n; i++)
            {
                var pos = p + new Vector3(Random.Range(-spread, spread), Random.Range(-0.3f, spread), 0f);
                if (Spark != null) FxAnim.Particle(SpriteBank.One("FX/pollen"), pos, new Vector2(Random.Range(-0.3f, 0.3f), speed * Random.Range(0.5f, 1.2f)), Random.Range(0.5f, 1.1f), c, -1f, Random.Range(0.6f, 1.1f));
                else FxAnim.Particle(Pix, pos, new Vector2(Random.Range(-0.3f, 0.3f), speed * Random.Range(0.5f, 1.2f)), Random.Range(0.5f, 1.1f), c, -1f, Random.value < 0.4f ? 2f : 1f);
            }
        }

        public static void HealBurst(Vector3 p)
        {
            for (int i = 0; i < 10; i++)
            {
                var pos = p + new Vector3(Random.Range(-0.8f, 0.8f), Random.Range(0.2f, 2.2f), 0f);
                var fx = FxAnim.Spawn("FX", "heal", pos, Random.Range(6f, 10f), 1f);
                fx.Tint(new Color(0.8f, 1f, 0.85f));
            }
            Rise(p + new Vector3(0, 0.8f, 0), new Color(0.75f, 1f, 0.8f), 14, 0.7f, 2.5f);
            FxAnim.Particle(Glow, p + new Vector3(0, 1.2f, 0), Vector2.zero, 0.6f, new Color(0.6f, 1f, 0.75f, 0.5f), 0f, 4f, Order.Fx);
        }
    }
}
