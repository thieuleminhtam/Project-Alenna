using System.Collections.Generic;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace MoonlitParry
{
    /// <summary>
    /// Keyboard + mouse + gamepad. Reads the new Input System, the old Input Manager AND IMGUI events,
    /// so it works whatever "Active Input Handling" is set to (even when the Input System has no keyboard).
    ///   Move A/D ←→   Jump Space   Attack LMB/Z/J   Parry RMB/X/K   Roll Shift/C/L
    ///   Flask 1 (V/H)   Finisher F   Rest/Retry R (Enter)   Drop S+Space   Pause Esc/P   Debug F1
    /// </summary>
    public static class GameInput
    {
        static readonly KeyCode[] kLeft = { KeyCode.A, KeyCode.LeftArrow };
        static readonly KeyCode[] kRight = { KeyCode.D, KeyCode.RightArrow };
        static readonly KeyCode[] kDown = { KeyCode.S, KeyCode.DownArrow };
        static readonly KeyCode[] kJump = { KeyCode.Space, KeyCode.UpArrow, KeyCode.JoystickButton0 };   // W is not jump; S+Space drops through ledges
        static readonly KeyCode[] kAttack = { KeyCode.Mouse0, KeyCode.Z, KeyCode.J, KeyCode.JoystickButton2 };
        static readonly KeyCode[] kParry = { KeyCode.Mouse1, KeyCode.X, KeyCode.K, KeyCode.JoystickButton3, KeyCode.JoystickButton5 };
        static readonly KeyCode[] kRoll = { KeyCode.LeftShift, KeyCode.C, KeyCode.L, KeyCode.JoystickButton1 };
        static readonly KeyCode[] kHeal = { KeyCode.Alpha1, KeyCode.Keypad1, KeyCode.V, KeyCode.H, KeyCode.JoystickButton4 };
        static readonly KeyCode[] kFinish = { KeyCode.F, KeyCode.Q };
        static readonly KeyCode[] kInteract = { KeyCode.R, KeyCode.E };
        static readonly KeyCode[] kRetry = { KeyCode.R, KeyCode.Return, KeyCode.JoystickButton7 };
        static readonly KeyCode[] kPause = { KeyCode.Escape, KeyCode.P };
        static readonly KeyCode[] kDebug = { KeyCode.F1 };
        static readonly KeyCode[] kWarp = { KeyCode.F2, KeyCode.Alpha0 };
        static readonly KeyCode[] kCapture = { KeyCode.F12, KeyCode.Alpha9 };
        static readonly KeyCode[] kBench = { KeyCode.F3 };

#if ENABLE_INPUT_SYSTEM
        static readonly Dictionary<KeyCode, Key> map = new Dictionary<KeyCode, Key>
        {
            { KeyCode.A, Key.A }, { KeyCode.D, Key.D }, { KeyCode.S, Key.S }, { KeyCode.W, Key.W }, { KeyCode.Z, Key.Z },
            { KeyCode.J, Key.J }, { KeyCode.X, Key.X }, { KeyCode.K, Key.K }, { KeyCode.C, Key.C }, { KeyCode.L, Key.L },
            { KeyCode.V, Key.V }, { KeyCode.H, Key.H }, { KeyCode.F, Key.F }, { KeyCode.Q, Key.Q }, { KeyCode.R, Key.R },
            { KeyCode.E, Key.E }, { KeyCode.P, Key.P }, { KeyCode.Space, Key.Space }, { KeyCode.LeftArrow, Key.LeftArrow },
            { KeyCode.RightArrow, Key.RightArrow }, { KeyCode.UpArrow, Key.UpArrow }, { KeyCode.DownArrow, Key.DownArrow },
            { KeyCode.LeftShift, Key.LeftShift }, { KeyCode.Alpha1, Key.Digit1 }, { KeyCode.Keypad1, Key.Numpad1 }, { KeyCode.Return, Key.Enter }, { KeyCode.Escape, Key.Escape }, { KeyCode.F1, Key.F1 }, { KeyCode.F2, Key.F2 }, { KeyCode.Alpha0, Key.Digit0 }, { KeyCode.F12, Key.F12 }, { KeyCode.Alpha9, Key.Digit9 }, { KeyCode.F3, Key.F3 },
        };

        static bool NewHeld(KeyCode[] keys)
        {
            var kb = Keyboard.current;
            var ms = Mouse.current;
            foreach (var k in keys)
            {
                Key nk;
                if (kb != null && map.TryGetValue(k, out nk) && kb[nk].isPressed) return true;
                if (ms != null && k == KeyCode.Mouse0 && ms.leftButton.isPressed) return true;
                if (ms != null && k == KeyCode.Mouse1 && ms.rightButton.isPressed) return true;
            }
            return false;
        }

        static bool NewDown(KeyCode[] keys)
        {
            var kb = Keyboard.current;
            var ms = Mouse.current;
            foreach (var k in keys)
            {
                Key nk;
                if (kb != null && map.TryGetValue(k, out nk) && kb[nk].wasPressedThisFrame) return true;
                if (ms != null && k == KeyCode.Mouse0 && ms.leftButton.wasPressedThisFrame) return true;
                if (ms != null && k == KeyCode.Mouse1 && ms.rightButton.wasPressedThisFrame) return true;
            }
            return false;
        }
#endif

#if ENABLE_LEGACY_INPUT_MANAGER
        static bool OldHeld(KeyCode[] keys)
        {
            try { foreach (var k in keys) if (Input.GetKey(k)) return true; }
            catch (System.InvalidOperationException) { }
            return false;
        }

        static bool OldDown(KeyCode[] keys)
        {
            try { foreach (var k in keys) if (Input.GetKeyDown(k)) return true; }
            catch (System.InvalidOperationException) { }
            return false;
        }
#endif

        static bool Held(KeyCode[] keys)
        {
            bool b = ImguiKeys.Held(keys);
#if ENABLE_INPUT_SYSTEM
            b |= NewHeld(keys);
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
            b |= OldHeld(keys);
#endif
            return b;
        }

        static readonly Dictionary<KeyCode[], int> lastDown = new Dictionary<KeyCode[], int>();

        /// <summary>
        /// The Input System reports a press in the frame it happens, the IMGUI fallback one frame later — without this
        /// guard a single press of Esc/P toggled the pause menu twice (open + close) and other one-shot actions fired twice.
        /// </summary>
        static bool Down(KeyCode[] keys)
        {
            int last;
            bool seen = lastDown.TryGetValue(keys, out last);
            if (seen && last == Time.frameCount) return true;                // asked again in the same frame
            bool b = ImguiKeys.Down(keys);
#if ENABLE_INPUT_SYSTEM
            b |= NewDown(keys);
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
            b |= OldDown(keys);
#endif
            if (!b) return false;
            if (seen && Time.frameCount - last <= 2) return false;          // the same press echoed by another backend
            lastDown[keys] = Time.frameCount;
            return true;
        }

        public static string Backend
        {
            get
            {
                string s = "";
#if ENABLE_INPUT_SYSTEM
                s += "InputSystem" + (Keyboard.current != null ? "(kb)" : "(no kb)") + (Mouse.current != null ? "(mouse)" : "");
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
                s += (s.Length > 0 ? "+" : "") + "Legacy";
#endif
                s += (s.Length > 0 ? "+" : "") + "IMGUI" + (ImguiKeys.Seen ? "(ok)" : "");
                return s;
            }
        }

        public static float MoveX
        {
            get
            {
                bool l = Held(kLeft), r = Held(kRight);
#if ENABLE_INPUT_SYSTEM
                var g = Gamepad.current;
                if (g != null)
                {
                    float gx = g.leftStick.ReadValue().x;
                    if (gx < -0.35f || g.dpad.left.isPressed) l = true;
                    if (gx > 0.35f || g.dpad.right.isPressed) r = true;
                }
#endif
                return (r ? 1f : 0f) - (l ? 1f : 0f);
            }
        }

        public static bool DownHeld
        {
            get
            {
                bool b = Held(kDown);
#if ENABLE_INPUT_SYSTEM
                var g = Gamepad.current;
                if (g != null && (g.leftStick.ReadValue().y < -0.5f || g.dpad.down.isPressed)) b = true;
#endif
                return b;
            }
        }

        public static bool JumpHeld
        {
            get
            {
                bool b = Held(kJump);
#if ENABLE_INPUT_SYSTEM
                var g = Gamepad.current;
                if (g != null && g.buttonSouth.isPressed) b = true;
#endif
                return b;
            }
        }

        static bool PadDown(int which)
        {
#if ENABLE_INPUT_SYSTEM
            var g = Gamepad.current;
            if (g == null) return false;
            switch (which)
            {
                case 0: return g.buttonSouth.wasPressedThisFrame;
                case 1: return g.buttonWest.wasPressedThisFrame;
                case 2: return g.buttonNorth.wasPressedThisFrame || g.rightShoulder.wasPressedThisFrame;
                case 3: return g.buttonEast.wasPressedThisFrame;
                case 4: return g.leftShoulder.wasPressedThisFrame;
                case 5: return g.rightTrigger.wasPressedThisFrame;
                case 6: return g.startButton.wasPressedThisFrame;
                case 7: return g.selectButton.wasPressedThisFrame;
            }
#endif
            return false;
        }

        public static bool JumpDown { get { return Down(kJump) || PadDown(0); } }
        public static bool AttackDown { get { return Down(kAttack) || PadDown(1); } }
        public static bool ParryDown { get { return Down(kParry) || PadDown(2); } }
        public static bool RollDown { get { return Down(kRoll) || PadDown(3); } }
        public static bool RollHeld
        {
            get
            {
                bool b = Held(kRoll);
#if ENABLE_INPUT_SYSTEM
                var g = Gamepad.current;
                if (g != null && g.buttonEast.isPressed) b = true;
#endif
                return b;
            }
        }
        public static bool HealDown { get { return Down(kHeal) || PadDown(4); } }
        public static bool FinisherDown { get { return Down(kFinish) || PadDown(5); } }
        public static bool InteractDown { get { return Down(kInteract) || PadDown(7); } }
        public static bool RetryDown { get { return Down(kRetry) || PadDown(6); } }
        public static bool PauseDown { get { return Down(kPause); } }
        public static bool DebugDown { get { return Down(kDebug); } }
        public static bool WarpDown { get { return Down(kWarp); } }
        public static bool BenchWarpDown { get { return Down(kBench); } }
        public static bool CaptureDown { get { return Down(kCapture); } }
    }

    /// <summary>
    /// Fallback keyboard/mouse reader using IMGUI events. These arrive whatever the Input System / Input Manager
    /// settings are (e.g. projects where the Input System has no Keyboard device).
    /// </summary>
    public class ImguiKeys : MonoBehaviour
    {
        static readonly HashSet<KeyCode> held = new HashSet<KeyCode>();
        static readonly Dictionary<KeyCode, int> pressFrame = new Dictionary<KeyCode, int>();
        public static bool Seen { get; private set; }
        static float shiftSeen;

        void OnEnable() { held.Clear(); pressFrame.Clear(); }

        static void Press(KeyCode k)
        {
            if (k == KeyCode.None || held.Contains(k)) return;
            held.Add(k);
            pressFrame[k] = Time.frameCount;
        }

        void OnGUI()
        {
            var e = Event.current;
            if (e == null) return;
            // modifier keys do not always send KeyDown events — poll the flag instead.
            // Some events (layout/repaint) carry no modifier state, so only treat Shift as released
            // after it has been missing for a short while; otherwise holding Shift would re-press it.
            if (e.shift) { Press(KeyCode.LeftShift); shiftSeen = Time.realtimeSinceStartup; }
            else if (held.Contains(KeyCode.LeftShift) && Time.realtimeSinceStartup - shiftSeen > 0.12f
                     && (e.type == EventType.KeyDown || e.type == EventType.KeyUp || e.isMouse || Time.realtimeSinceStartup - shiftSeen > 0.25f))
                held.Remove(KeyCode.LeftShift);

            if (e.type == EventType.KeyDown && e.keyCode != KeyCode.None) { Seen = true; Press(e.keyCode); }
            else if (e.type == EventType.KeyUp) held.Remove(e.keyCode);
            else if (e.type == EventType.MouseDown) { Seen = true; Press(MouseKey(e.button)); }
            else if (e.type == EventType.MouseUp) held.Remove(MouseKey(e.button));
        }

        static KeyCode MouseKey(int button)
        {
            return button == 0 ? KeyCode.Mouse0 : button == 1 ? KeyCode.Mouse1 : button == 2 ? KeyCode.Mouse2 : KeyCode.None;
        }

        void OnApplicationFocus(bool focus)
        {
            if (!focus) held.Clear();
        }

        public static bool Held(KeyCode[] keys)
        {
            foreach (var k in keys) if (held.Contains(k)) return true;
            return false;
        }

        /// <summary>True during the first Update after the key went down (IMGUI runs after Update).</summary>
        public static bool Down(KeyCode[] keys)
        {
            int f;
            foreach (var k in keys)
                if (pressFrame.TryGetValue(k, out f) && f == Time.frameCount - 1) return true;
            return false;
        }
    }
}
