using System.Collections.Generic;
using UnityEngine;

namespace MoonlitParry
{
    /// <summary>
    /// Pixel-scaled IMGUI HUD: hearts, flasks, stamina, enemy/boss bars, prompts, hints, dialogue,
    /// opening text, fades, death and end screens, popups.
    /// </summary>
    public class HUD : MonoBehaviour
    {
        public static HUD I;
        float shownSince = -1f;

        class PopupItem { public string text; public Vector3 pos; public Color col; public float t; }

        readonly List<PopupItem> popups = new List<PopupItem>();
        GUIStyle label, center, wrapCenter, big;
        Texture2D heartFull, heartEmpty, flaskFull, flaskEmpty;
        float flashT, flashDur;
        Color flashCol;
        float bossLag = 1f, staminaShown = 100f;

        public float FadeAlpha;

        string hintText; float hintT, hintDur;
        string dlgSpeaker, dlgText; float dlgT, dlgDur; int dlgShown;
        string centerText; float centerT;
        bool death, end; float deathT, endT;
        string bannerText; Color bannerCol; float bannerT = 99f;
        string toastText; float toastT = 99f;

        void Awake()
        {
            shownSince = Time.realtimeSinceStartup;
            I = this;
            heartFull = Tex("UI/heart_full"); heartEmpty = Tex("UI/heart_empty");
            flaskFull = Tex("UI/flask_full"); flaskEmpty = Tex("UI/flask_empty");
        }

        static Texture2D Tex(string path)
        {
            var s = SpriteBank.One(path);
            return s != null ? s.texture : null;
        }

        // ------------------------------------------------------------------ API
        public void Popup(string text, Vector3 world, Color c) { popups.Add(new PopupItem { text = text, pos = world, col = c }); }
        public void Flash(Color c, float dur) { flashCol = c; flashT = flashDur = dur; }
        public void Hint(string text, float seconds) { hintText = text; hintT = 0f; hintDur = seconds; }
        public void Dialog(string speaker, string text, float seconds) { dlgSpeaker = speaker; dlgText = text; dlgT = 0f; dlgDur = seconds; dlgShown = 0; }
        public void CenterText(string text) { centerText = text; centerT = 0f; }
        public void ShowDeath() { death = true; deathT = 0f; }
        public void HideDeath() { death = false; }
        public void ShowEnd() { end = true; endT = 0f; }
        public void Banner(string text, Color c) { bannerText = text; bannerCol = c; bannerT = 0f; }
        /// <summary>Small one-off notice in the bottom-right corner (e.g. the desktop shortcut was created).</summary>
        public void Toast(string text) { toastText = text; toastT = 0f; }

        public void ResetHud()
        {
            popups.Clear();
            flashT = 0f; hintText = null; dlgText = null; centerText = null; death = false; end = false;
        }

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            for (int i = popups.Count - 1; i >= 0; i--)
            {
                popups[i].t += dt;
                popups[i].pos += Vector3.up * dt * 0.8f;
                if (popups[i].t > 1.3f) popups.RemoveAt(i);
            }
            if (flashT > 0f) flashT -= dt;
            if (hintText != null) { hintT += dt; if (hintT > hintDur + 0.5f) hintText = null; }
            if (dlgText != null)
            {
                dlgT += dt;
                int want = Mathf.Min(dlgText.Length, Mathf.FloorToInt(dlgT * 32f));
                while (dlgShown < want)
                {
                    dlgShown++;
                    if (dlgShown % 2 == 0 && dlgText[dlgShown - 1] != ' ') Sfx.Play("blip", 0.22f, 0.08f);
                }
                if (dlgT > dlgDur + 0.4f) dlgText = null;
            }
            if (centerText != null) centerT += dt;
            if (death) deathT += dt;
            bannerT += dt;
            toastT += dt;
            if (DesktopShortcut.Notice != null && GameManager.I != null && GameManager.I.State == GameManager.Flow.Playing)
            {
                Toast(DesktopShortcut.Notice);
                DesktopShortcut.Notice = null;
            }
            if (end) endT += dt;
        }

        // ------------------------------------------------------------------ drawing helpers
        void Styles(int s)
        {
            if (label == null)
            {
                label = new GUIStyle(GUI.skin.label);
                center = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter };
                wrapCenter = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, wordWrap = true };
                big = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, wordWrap = true };
            }
            label.fontSize = 6 * s;
            center.fontSize = 6 * s;
            wrapCenter.fontSize = Mathf.Max(12, 5 * s);
            big.fontSize = 14 * s;
            label.fontStyle = center.fontStyle = big.fontStyle = FontStyle.Bold;
        }

        static void Box(float x, float y, float w, float h, Color c)
        {
            var old = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(new Rect(x, y, w, h), Texture2D.whiteTexture);
            GUI.color = old;
        }

        static void Icon(Texture2D t, float x, float y, float w, float h, Color tint)
        {
            if (t == null) return;
            var old = GUI.color;
            GUI.color = tint;
            GUI.DrawTexture(new Rect(x, y, w, h), t);
            GUI.color = old;
        }

        /// <summary>
        /// Same colour in every IMGUI state: the default skin gives labels a different hover colour, which turned the
        /// dark drop-shadow light whenever the mouse passed over a text (the text looked bold / glowing).
        /// </summary>
        static void Ink(GUIStyle st, Color c)
        {
            st.normal.textColor = c; st.hover.textColor = c; st.active.textColor = c; st.focused.textColor = c;
            st.onNormal.textColor = c; st.onHover.textColor = c; st.onActive.textColor = c; st.onFocused.textColor = c;
            st.hover.background = st.normal.background; st.onHover.background = st.normal.background;
        }

        static void Text(Rect r, string t, GUIStyle st, Color c, int s)
        {
            var old = st.normal.textColor;
            Ink(st, new Color(0.03f, 0.02f, 0.06f, c.a));
            GUI.Label(new Rect(r.x + Mathf.Max(1, s / 2), r.y + Mathf.Max(1, s / 2), r.width, r.height), t, st);
            Ink(st, c);
            GUI.Label(r, t, st);
            Ink(st, old);
        }

        static void Bar(float x, float y, float w, float h, float frac, Color fill, Color back, int s)
        {
            Box(x - s, y - s, w + 2 * s, h + 2 * s, new Color(0.05f, 0.03f, 0.08f, 0.9f));
            Box(x, y, w, h, back);
            Box(x, y, w * Mathf.Clamp01(frac), h, fill);
        }

        Vector2 Screen2(Camera cam, Vector3 world)
        {
            var sp = cam.WorldToScreenPoint(world);
            return new Vector2(sp.x, Screen.height - sp.y);
        }

        void Prompt(Camera cam, Vector3 world, string key, string text, int s)
        {
            var p = Screen2(cam, world);
            string str = "[" + key + "]  " + text;
            float w = (str.Length * 3.6f + 10) * s, h = 11 * s;
            var r = new Rect(p.x - w * 0.5f, p.y - h, w, h);
            Box(r.x - s, r.y - s, r.width + 2 * s, r.height + 2 * s, new Color(0.85f, 0.72f, 0.45f, 0.85f));
            Box(r.x, r.y, r.width, r.height, new Color(0.07f, 0.05f, 0.1f, 0.9f));
            Text(r, str, center, new Color(1f, 0.95f, 0.85f), s);
        }

        // ------------------------------------------------------------------ OnGUI
        void OnGUI()
        {
            var gm = GameManager.I;
            if (gm == null) return;
            int s = Mathf.Max(1, Mathf.FloorToInt(Screen.height / 216f));
            Styles(s);
            var cam = Camera.main;
            var p = gm.Player;
            bool playing = gm.State == GameManager.Flow.Playing || gm.State == GameManager.Flow.Cutscene || gm.State == GameManager.Flow.Resting;

            if (flashT > 0f)
            {
                var c = flashCol;
                c.a *= flashT / flashDur;
                Box(0, 0, Screen.width, Screen.height, c);
            }

            if (p != null && cam != null && gm.State != GameManager.Flow.Intro)
            {
                // ---- hearts
                float x = 8 * s, y = 8 * s;
                for (int i = 0; i < p.MaxHearts; i++)
                    Icon(i < p.Hearts ? heartFull : heartEmpty, x + i * 15 * s, y, 13 * s, 12 * s, Color.white);
                // ---- stamina
                staminaShown = Mathf.MoveTowards(staminaShown, p.Stamina, Time.unscaledDeltaTime * 200f);
                bool low = p.IsBroken || p.Stamina < 25f;
                Color stc = p.IsBroken ? (Mathf.Repeat(Time.unscaledTime * 6f, 1f) < 0.5f ? new Color(1f, 0.3f, 0.3f) : new Color(0.6f, 0.15f, 0.15f))
                                       : low ? new Color(1f, 0.5f, 0.18f) : new Color(1f, 0.84f, 0.22f);      // stamina = yellow
                Bar(x, y + 15 * s, 58 * s, 3 * s, staminaShown / p.MaxStamina, stc, new Color(0.15f, 0.12f, 0.18f), s);
                // ---- flasks
                for (int i = 0; i < p.MaxFlasks; i++)
                    Icon(i < p.Flasks ? flaskFull : flaskEmpty, x + i * 12 * s, y + 22 * s, 11 * s, 15 * s, Color.white);

                // ---- enemy bars & finisher prompt
                if (gm.Level != null)
                {
                    foreach (var e in gm.Level.enemies)
                    {
                        if (e == null || e.IsDead || e.IsBoss) continue;
                        if (!(e.ShowBarsUntil > Time.time || e.Collapsed)) continue;
                        var sp = Screen2(cam, e.transform.position + Vector3.up * (e.Height + 0.35f));
                        float w = 26 * s;
                        Bar(sp.x - w / 2, sp.y, w, 2 * s, (float)e.Hp / e.MaxHp, new Color(0.85f, 0.2f, 0.25f), new Color(0.15f, 0.1f, 0.12f), s);
                        Bar(sp.x - w / 2, sp.y + 4 * s, w, 2 * s, e.Stamina / e.MaxStamina,
                            e.Collapsed ? new Color(1f, 0.85f, 0.3f, Mathf.Repeat(Time.unscaledTime * 4f, 1f) < 0.5f ? 1f : 0.4f) : new Color(1f, 0.82f, 0.3f),
                            new Color(0.15f, 0.12f, 0.08f), s);
                    }
                    var fc = p.FinisherCandidate;
                    if (fc != null && gm.State == GameManager.Flow.Playing)
                        Prompt(cam, fc.transform.position + Vector3.up * (fc.Height + (fc.IsBoss ? 0.6f : 1.4f)), "F", "Kết liễu", s);
                    var b = gm.NearBonfire();
                    if (b != null && p.CanInteract && gm.State == GameManager.Flow.Playing)
                        Prompt(cam, b.transform.position + new Vector3(Bonfire.SeatDX, 2.7f, 0f), "R", b.Lit ? "Ngồi nghỉ" : "Thắp đèn & ngồi nghỉ", s);

                    // ---- boss bar
                    var boss = gm.Level.boss;
                    if (gm.BossActive && boss != null && !boss.IsDead && gm.State != GameManager.Flow.Victory)
                    {
                        float frac = (float)boss.Hp / boss.MaxHp;
                        bossLag = Mathf.MoveTowards(bossLag, frac, Time.unscaledDeltaTime * 0.35f);
                        if (bossLag < frac) bossLag = frac;
                        float w = Mathf.Min(Screen.width - 40 * s, 230 * s), h = 5 * s;
                        float bx = (Screen.width - w) * 0.5f, by = Screen.height - 24 * s;
                        Text(new Rect(bx, by - 12 * s, w, 10 * s), boss.DisplayName + (boss.Phase == 3 ? "   — tuyệt lộ" : boss.Phase == 2 ? "   — cuồng nộ" : ""), label, new Color(0.85f, 0.95f, 0.9f), s);
                        Box(bx - s, by - s, w + 2 * s, h + 2 * s, new Color(0.05f, 0.03f, 0.08f, 0.95f));
                        Box(bx, by, w, h, new Color(0.15f, 0.1f, 0.15f));
                        Box(bx, by, w * bossLag, h, new Color(1f, 0.95f, 0.85f));
                        Box(bx, by, w * frac, h, boss.Phase == 1 ? new Color(0.75f, 0.2f, 0.25f) : boss.Phase == 2 ? new Color(0.85f, 0.25f, 0.5f) : new Color(1f, 0.35f, 0.2f));
                        float sw = w * 0.6f, sx = bx + w * 0.2f, sy = by + h + 3 * s, sh = 3 * s;
                        float stam = Mathf.Clamp01(boss.Stamina / boss.MaxStamina);
                        Bar(sx, sy, sw, sh, stam,
                            boss.Collapsed ? new Color(1f, 0.85f, 0.3f, Mathf.Repeat(Time.unscaledTime * 4f, 1f) < 0.5f ? 1f : 0.4f) : new Color(1f, 0.82f, 0.3f),
                            new Color(0.15f, 0.12f, 0.08f), s);
                        float since = Time.time - boss.StaminaHitAt;
                        if (since < 0.6f && !boss.Collapsed)        // the chunk a hit / parry just took, flashing white
                        {
                            float before = Mathf.Clamp01(boss.StaminaBefore / boss.MaxStamina);
                            if (before > stam)
                                Box(sx + sw * stam, sy, sw * (before - stam), sh, new Color(1f, 1f, 1f, 1f - since / 0.6f));
                        }
                    }
                }

                // ---- popups
                foreach (var pp in popups)
                {
                    var sp = Screen2(cam, pp.pos);
                    var c = pp.col;
                    c.a = Mathf.Clamp01(1.5f - pp.t * 1.2f);
                    Text(new Rect(sp.x - 120 * s, sp.y - 6 * s, 240 * s, 12 * s), pp.text, center, c, s);
                }
            }

            // ---- hint (top centre)
            if (hintText != null && playing)
            {
                float a = Mathf.Clamp01(Mathf.Min(hintT / 0.3f, (hintDur + 0.5f - hintT) / 0.5f));
                float w = Mathf.Min(Screen.width * 0.86f, 300 * s), h = 22 * s;
                float hx = (Screen.width - w) * 0.5f, hy = 34 * s;
                Box(hx - s, hy - s, w + 2 * s, h + 2 * s, new Color(0.8f, 0.68f, 0.42f, 0.8f * a));
                Box(hx, hy, w, h, new Color(0.06f, 0.05f, 0.1f, 0.88f * a));
                Text(new Rect(hx + 4 * s, hy, w - 8 * s, h), hintText, wrapCenter, new Color(0.96f, 0.94f, 1f, a), s);
            }

            // ---- dialogue (bottom)
            if (dlgText != null)
            {
                float a = Mathf.Clamp01(Mathf.Min(dlgT / 0.2f, (dlgDur + 0.4f - dlgT) / 0.4f));
                float w = Mathf.Min(Screen.width * 0.8f, 260 * s), h = 34 * s;
                float dx = (Screen.width - w) * 0.5f, dy = Screen.height - h - 34 * s;
                Box(dx - s, dy - s, w + 2 * s, h + 2 * s, new Color(0.75f, 0.65f, 0.45f, 0.85f * a));
                Box(dx, dy, w, h, new Color(0.04f, 0.03f, 0.07f, 0.92f * a));
                if (!string.IsNullOrEmpty(dlgSpeaker))
                    Text(new Rect(dx + 6 * s, dy + 2 * s, w, 9 * s), dlgSpeaker, label, new Color(1f, 0.8f, 0.45f, a), s);
                Text(new Rect(dx + 6 * s, dy + 9 * s, w - 12 * s, h - 10 * s), dlgText.Substring(0, Mathf.Clamp(dlgShown, 0, dlgText.Length)), wrapCenter, new Color(0.95f, 0.93f, 1f, a), s);
            }

            // ---- debug (F1)
            if (gm.ShowDebug && p != null)
            {
                string dbg = GameManager.LayoutInfo + "\n[F1]  input x=" + GameInput.MoveX + " | " + GameInput.Backend + " | timeScale " + Time.timeScale.ToString("F2") +
                             " | flow " + gm.State + "\n" + p.DebugInfo;
                var ds = new GUIStyle(GUI.skin.label) { fontSize = Mathf.Max(11, 4 * s), wordWrap = true };
                Box(0, Screen.height - 20 * s, Screen.width, 20 * s, new Color(0f, 0f, 0f, 0.55f));
                Ink(ds, new Color(0.7f, 1f, 0.7f));
                GUI.Label(new Rect(4 * s, Screen.height - 19 * s, Screen.width - 8 * s, 19 * s), dbg, ds);
            }

            // ---- banner (phase change)
            if (bannerText != null && bannerT < 2.6f)
            {
                float a = Mathf.Clamp01(Mathf.Min(bannerT / 0.25f, (2.6f - bannerT) / 0.6f));
                Box(0, Screen.height * 0.26f, Screen.width, 26 * s, new Color(0f, 0f, 0f, 0.45f * a));
                var st = new GUIStyle(big) { fontSize = 12 * s };
                Text(new Rect(0, Screen.height * 0.26f, Screen.width, 26 * s), bannerText, st, new Color(bannerCol.r, bannerCol.g, bannerCol.b, a), s);
            }

            // ---- toast (bottom right)
            if (toastText != null && toastT < 6f)
            {
                float a = Mathf.Clamp01(Mathf.Min(toastT / 0.3f, (6f - toastT) / 0.8f));
                float w = Mathf.Min(Screen.width - 16 * s, (toastText.Length * 3.6f + 14) * s), h = 12 * s;
                float tx = Screen.width - w - 8 * s, ty = Screen.height - h - 8 * s;
                Box(tx - s, ty - s, w + 2 * s, h + 2 * s, new Color(0.8f, 0.68f, 0.42f, 0.8f * a));
                Box(tx, ty, w, h, new Color(0.06f, 0.05f, 0.1f, 0.88f * a));
                Text(new Rect(tx, ty, w, h), toastText, center, new Color(0.96f, 0.94f, 1f, a), s);
            }

            // ---- pause menu: resume, volume, tuning, bonfire, quit
            if (gm.Paused) PauseMenu(gm, s);

            // ---- fade to black
            if (FadeAlpha > 0.001f) Box(0, 0, Screen.width, Screen.height, new Color(0f, 0f, 0f, FadeAlpha));

            // ---- which map is loaded (first seconds in the editor, so editing problems are obvious)
            if (Application.isEditor && Time.realtimeSinceStartup - shownSince < 9f && !string.IsNullOrEmpty(GameManager.LayoutInfo))
            {
                var ls = new GUIStyle(GUI.skin.label) { fontSize = Mathf.Max(11, 4 * s) };
                Ink(ls, GameManager.LayoutInfo.Contains("MẶC ĐỊNH") ? new Color(1f, 0.6f, 0.4f) : new Color(0.6f, 1f, 0.7f));
                GUI.Label(new Rect(4 * s, 2 * s, Screen.width - 8 * s, 8 * s), GameManager.LayoutInfo, ls);
            }


            // ---- opening text (typewriter on black)
            if (centerText != null)
            {
                int n = Mathf.Min(centerText.Length, Mathf.FloorToInt(centerT * 14f));
                var st = new GUIStyle(big) { fontSize = 9 * s, fontStyle = FontStyle.Italic };
                Text(new Rect(Screen.width * 0.1f, Screen.height * 0.38f, Screen.width * 0.8f, 40 * s), centerText.Substring(0, n), st, new Color(0.9f, 0.88f, 0.8f), s);
            }

            // ---- death screen
            if (death)
            {
                float a = Mathf.Clamp01(deathT / 1.2f);
                float bandH = 46 * s, by = Screen.height * 0.5f - bandH * 0.5f;
                Box(0, 0, Screen.width, Screen.height, new Color(0f, 0f, 0f, 0.35f * a));
                for (int i = 0; i < 6; i++)  // soft-edged dark band
                {
                    Box(0, by - (6 - i) * 2 * s, Screen.width, bandH + (6 - i) * 4 * s, new Color(0f, 0f, 0f, 0.12f * a));
                }
                var st = new GUIStyle(big) { fontSize = 16 * s, fontStyle = FontStyle.Normal };
                float scale = 1f + (1f - a) * 0.08f;
                st.fontSize = Mathf.RoundToInt(16 * s * scale);
                Text(new Rect(0, by, Screen.width, bandH), Story.DeathText, st, new Color(0.72f, 0.08f, 0.1f, a), s);
                if (deathT > 1.6f)
                    Text(new Rect(0, by + bandH + 4 * s, Screen.width, 10 * s), Story.DeathSub, center, new Color(0.85f, 0.8f, 0.8f, Mathf.Clamp01((deathT - 1.6f) * 2f)), s);
            }

            // ---- end of demo
            if (end)
            {
                float a = Mathf.Clamp01(endT / 1.5f);
                Box(0, 0, Screen.width, Screen.height, new Color(0f, 0f, 0f, 0.65f * a));
                var st = new GUIStyle(big) { fontSize = 14 * s };
                Text(new Rect(0, Screen.height * 0.36f, Screen.width, 26 * s), Story.EndTitle, st, new Color(0.95f, 0.85f, 0.55f, a), s);
                Text(new Rect(0, Screen.height * 0.36f + 28 * s, Screen.width, 12 * s), Story.EndSub, center, new Color(0.9f, 0.9f, 1f, a), s);
            }
        }

        float SliderRow(float x, float y, float w, int s, string label, float v, float min, float max, string fmt)
        {
            Text(new Rect(x, y, w * 0.45f, 10 * s), label, label_(s), new Color(0.9f, 0.9f, 1f), s);
            Text(new Rect(x + w * 0.45f, y, w * 0.15f, 10 * s), v.ToString(fmt), label_(s), new Color(1f, 0.9f, 0.6f), s);
            return GUI.HorizontalSlider(new Rect(x + w * 0.6f, y + 3 * s, w * 0.4f, 6 * s), v, min, max);
        }

        GUIStyle smallLabel;
        GUIStyle label_(int s)
        {
            if (smallLabel == null) smallLabel = new GUIStyle(GUI.skin.label);
            smallLabel.fontSize = Mathf.Max(11, 5 * s);
            return smallLabel;
        }

        bool showTuning;

        /// <summary>Flat button: the box lightens a little under the mouse, the text never changes (no "pop").</summary>
        bool Button(Rect r, string text, int s, bool accent = false)
        {
            bool over = r.Contains(Event.current.mousePosition);
            Box(r.x - s, r.y - s, r.width + 2 * s, r.height + 2 * s, new Color(0.05f, 0.03f, 0.08f, 0.95f));
            Box(r.x, r.y, r.width, r.height, accent ? (over ? new Color(0.36f, 0.2f, 0.3f, 0.95f) : new Color(0.28f, 0.15f, 0.24f, 0.95f))
                                                   : (over ? new Color(0.2f, 0.18f, 0.28f, 0.95f) : new Color(0.13f, 0.11f, 0.19f, 0.95f)));
            Text(r, text, center, new Color(0.95f, 0.92f, 1f), s);
            return GUI.Button(r, GUIContent.none, GUIStyle.none);
        }

        void PauseMenu(GameManager gm, int s)
        {
            Box(0, 0, Screen.width, Screen.height, new Color(0.02f, 0.02f, 0.06f, 0.72f));
            float w = Mathf.Min(Screen.width * 0.8f, 200 * s), x = (Screen.width - w) * 0.5f, y = Screen.height * 0.12f;
            Text(new Rect(0, y, Screen.width, 20 * s), "TẠM DỪNG", big, new Color(0.9f, 0.9f, 1f), s);
            y += 26 * s;
            float bh = 13 * s, gap = 4 * s;
            if (Button(new Rect(x, y, w, bh), "Tiếp tục", s, true)) gm.SetPaused(false);
            y += bh + gap * 2;

            // ---- volume
            float mv = SliderRow(x, y, w, s, "Âm lượng nhạc", Sfx.UserMusic * 100f, 0f, 100f, "0") / 100f; y += 12 * s;
            float sv = SliderRow(x, y, w, s, "Âm lượng hiệu ứng", Sfx.UserSfx * 100f, 0f, 100f, "0") / 100f; y += 12 * s;
            if (!Mathf.Approximately(mv, Sfx.UserMusic) || !Mathf.Approximately(sv, Sfx.UserSfx)) Sfx.SetUserVolume(mv, sv);
            y += gap;

            if (Button(new Rect(x, y, w, bh), showTuning ? "Ẩn tùy chỉnh độ khó" : "Tùy chỉnh độ khó", s)) showTuning = !showTuning;
            y += bh + gap;
            if (showTuning)
            {
                var tu = gm.tuning;
                float row = 11 * s;
                tu.bossHp = Mathf.RoundToInt(SliderRow(x, y, w, s, "Máu boss", tu.bossHp, 200, 4000, "0") / 50f) * 50; y += row;
                tu.bossSpeed = SliderRow(x, y, w, s, "Tốc độ ra đòn boss", tu.bossSpeed, 0.5f, 2f, "0.00"); y += row;
                tu.bossAggression = SliderRow(x, y, w, s, "Tần suất ra đòn boss", tu.bossAggression, 0.3f, 3f, "0.00"); y += row;
                tu.phase2At = SliderRow(x, y, w, s, "Phase 2 khi máu dưới", tu.phase2At, 0.3f, 0.95f, "0.00"); y += row;
                tu.zombieHp = Mathf.RoundToInt(SliderRow(x, y, w, s, "Máu xác sống", tu.zombieHp, 10, 300, "0") / 5f) * 5; y += row;
                tu.perfectParryWindow = SliderRow(x, y, w, s, "Cửa sổ parry (giây)", tu.perfectParryWindow, 0.04f, 0.35f, "0.00"); y += row;
                tu.rollInvuln = SliderRow(x, y, w, s, "Bất tử khi lộn (giây)", tu.rollInvuln, 0.04f, 0.5f, "0.00"); y += row;
                tu.parryStaminaDamage = SliderRow(x, y, w, s, "Parry chuẩn trừ sức bền quái", tu.parryStaminaDamage, 0f, 150f, "0"); y += row;
                tu.hitStaminaScale = SliderRow(x, y, w, s, "Hệ số sức bền khi chém", tu.hitStaminaScale, 0.1f, 1.5f, "0.00"); y += row;
                tu.hearts = Mathf.RoundToInt(SliderRow(x, y, w, s, "Số tim", tu.hearts, 1, 10, "0")); y += row;
                Text(new Rect(0, y, Screen.width, 9 * s), "(Máu/tim áp dụng khi hồi sinh)", center, new Color(0.65f, 0.65f, 0.75f), s);
                y += 10 * s;
            }
            if (Button(new Rect(x, y, w, bh), "Về ghế nghỉ gần nhất", s)) gm.RestartFromBonfire();
            y += bh + gap;
            if (Button(new Rect(x, y, w, bh), "Thoát game", s)) gm.QuitGame();
            y += bh + gap * 2;
            Text(new Rect(0, y, Screen.width, 10 * s), "Esc / P: tiếp tục", center, new Color(0.75f, 0.75f, 0.85f), s);
        }
    }
}
