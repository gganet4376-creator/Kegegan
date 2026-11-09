using System.Collections.Generic;
using UnityEngine;

namespace Kagegan
{
    public enum PadAction { Light, Heavy, Jump, Dodge, Block, Grab, Ult, SlotA, SlotB, Tech1, Tech2, Tech3, Tech4, TechPage, Target, Capri, Pause, Count }

    /// <summary>
    /// Gamepad layer over the legacy Input Manager: Xbox-style defaults, remappable bindings (buttons, triggers,
    /// D-pad and right-stick directions), rest-value calibration for raw axes (triggers that idle at -1 or 0),
    /// and a raw "what was just pressed" probe for the remap wizard.
    /// Raw axes "kpad0".."kpad19" are added to InputManager.asset by KageganSetup; a missing axis is just skipped.
    /// </summary>
    public static class PadInput
    {
        public const int AxisCount = 20, ButtonCount = 20, AxisBase = 100;

        public static readonly string[] ActionNames =
        {
            "Лёгкий удар", "Мощный удар", "Прыжок", "Рывок", "Блок", "Захват", "Ульта",
            "Приём (слот 1)", "Приём (слот 2)", "Приём 1", "Приём 2", "Приём 3", "Приём 4",
            "Сменить приёмы", "Сменить цель", "Капри (Люси)", "Пауза"
        };

        static List<int>[] binds;
        static readonly bool[] cur = new bool[(int)PadAction.Count], prev = new bool[(int)PadAction.Count];
        static readonly float[] axis = new float[AxisCount], rest = new float[AxisCount], stableSince = new float[AxisCount];
        static readonly bool[] axisMissing = new bool[AxisCount], rawPrev = new bool[ButtonCount + AxisCount * 2];
        static int frame = -1;
        static float namesAt = -10f, lastActive = -100f;
        static int connected;

        public static int ConnectedCount { get { RefreshNames(); return connected; } }
        /// <summary>A controller was touched in the last 30 s (phone probably on a stand).</summary>
        public static bool ActiveRecently => connected > 0 && Time.unscaledTime - lastActive < 30f;
        public static float LastActive => lastActive;

        static void RefreshNames()
        {
            if (Time.unscaledTime - namesAt < 1f) return;
            namesAt = Time.unscaledTime;
            int n = 0;
            try { foreach (var s in Input.GetJoystickNames()) if (!string.IsNullOrEmpty(s)) n++; } catch { }
            if (n != connected) { connected = n; for (int i = 0; i < AxisCount; i++) rest[i] = 0f; }
        }

        // ---------------- bindings ----------------
        static int[][] Defaults()
        {
            var d = new int[(int)PadAction.Count][];
            for (int i = 0; i < d.Length; i++) d[i] = new int[0];
            // Xbox layout as Unity reports it: A0 B1 X2 Y3 LB4 RB5, View6 Menu7 (Android: Start 10), L3 8, R3 9
            d[(int)PadAction.Light] = new[] { 0 };
            d[(int)PadAction.Heavy] = new[] { 2 };
            d[(int)PadAction.Jump] = new[] { 3 };
            d[(int)PadAction.Dodge] = new[] { 1 };
            d[(int)PadAction.Block] = new[] { 4 };
            d[(int)PadAction.SlotA] = new[] { 5 };
            d[(int)PadAction.Ult] = new[] { 9 };
            d[(int)PadAction.Target] = new[] { 8 };
            d[(int)PadAction.TechPage] = new[] { 6 };
            d[(int)PadAction.Pause] = Application.platform == RuntimePlatform.Android ? new[] { 10, 7 } : new[] { 7 };
            return d;
        }

        static void EnsureBinds()
        {
            if (binds != null) return;
            binds = new List<int>[(int)PadAction.Count];
            var d = Defaults();
            for (int i = 0; i < binds.Length; i++) binds[i] = new List<int>(d[i]);
            var saved = Save.Data.padBinds;
            if (saved != null && saved.Count > 0)
            {
                // a saved list replaces the defaults action by action
                var touched = new HashSet<int>();
                foreach (var b in saved)
                {
                    if (b.action < 0 || b.action >= binds.Length) continue;
                    if (touched.Add(b.action)) binds[b.action].Clear();
                    if (b.code >= 0) binds[b.action].Add(b.code);
                }
            }
        }

        public static IReadOnlyList<int> Binds(PadAction a) { EnsureBinds(); return binds[(int)a]; }

        /// <summary>Bind one code to an action (code &lt; 0 clears it). A code moves away from any other action.</summary>
        public static void Bind(PadAction a, int code)
        {
            EnsureBinds();
            if (code >= 0) foreach (var l in binds) l.Remove(code);
            binds[(int)a].Clear();
            if (code >= 0) binds[(int)a].Add(code);
            Persist();
        }

        public static void ResetDefaults()
        {
            binds = null;
            Save.Data.padBinds.Clear();
            EnsureBinds();
            Save.Write();
        }

        static void Persist()
        {
            var list = Save.Data.padBinds;
            list.Clear();
            for (int i = 0; i < binds.Length; i++)
            {
                if (binds[i].Count == 0) list.Add(new PadBind { action = i, code = -1 });
                foreach (int c in binds[i]) list.Add(new PadBind { action = i, code = c });
            }
            Save.Write();
        }

        public static string CodeName(int code)
        {
            if (code < 0) return "—";
            if (code < AxisBase)
            {
                switch (code)
                {
                    case 0: return "A"; case 1: return "B"; case 2: return "X"; case 3: return "Y";
                    case 4: return "LB"; case 5: return "RB"; case 8: return "L3"; case 9: return "R3";
                    default: return "Кнопка " + code;
                }
            }
            int ax = (code - AxisBase) / 2;
            bool neg = ((code - AxisBase) & 1) == 1;
            return "Ось " + (ax + 1) + (neg ? " −" : " +");
        }

        public static string BindText(PadAction a)
        {
            var l = Binds(a);
            if (l.Count == 0) return "—";
            var parts = new string[l.Count];
            for (int i = 0; i < l.Count; i++) parts[i] = CodeName(l[i]);
            return string.Join(" / ", parts);
        }

        // ---------------- polling ----------------
        /// <summary>Samples everything once per frame (cheap: a few dozen Input calls). Safe to call repeatedly.</summary>
        public static void Poll()
        {
            if (frame == Time.frameCount) return;
            frame = Time.frameCount;
            RefreshNames();
            EnsureBinds();
            for (int i = 0; i < cur.Length; i++) prev[i] = cur[i];
            if (connected == 0) { for (int i = 0; i < cur.Length; i++) cur[i] = false; return; }

            for (int i = 0; i < AxisCount; i++)
            {
                if (axisMissing[i]) continue;
                float v;
                try { v = Input.GetAxisRaw("kpad" + i); }
                catch { axisMissing[i] = true; continue; }
                // learn the idle value: an axis parked at the same non-zero value for 2.5 s is at rest (e.g. -1 triggers)
                if (Mathf.Abs(v - axis[i]) > 0.02f) stableSince[i] = Time.unscaledTime;
                else if (Time.unscaledTime - stableSince[i] > 2.5f && Mathf.Abs(v) > 0.9f) rest[i] = v;
                axis[i] = v;
            }
            bool any = false;
            for (int a = 0; a < cur.Length; a++)
            {
                bool on = false;
                foreach (int c in binds[a]) if (CodeHeld(c)) { on = true; break; }
                cur[a] = on;
                any |= on;
            }
            if (any) lastActive = Time.unscaledTime;
        }

        static bool CodeHeld(int c)
        {
            if (c < 0) return false;
            if (c < AxisBase) return c < ButtonCount && Input.GetKey(KeyCode.JoystickButton0 + c);
            int ax = (c - AxisBase) / 2;
            if (ax >= AxisCount || axisMissing[ax]) return false;
            float d = axis[ax] - rest[ax];
            return ((c - AxisBase) & 1) == 1 ? d < -0.5f : d > 0.5f;
        }

        public static bool Held(PadAction a) { Poll(); return cur[(int)a]; }
        public static bool Down(PadAction a) { Poll(); return cur[(int)a] && !prev[(int)a]; }
        public static bool Up(PadAction a) { Poll(); return !cur[(int)a] && prev[(int)a]; }

        /// <summary>Note pad use from outside (stick movement).</summary>
        public static void MarkActive() { lastActive = Time.unscaledTime; }

        /// <summary>
        /// Remap wizard probe: returns a code that went from released to pressed since the last call, or -1.
        /// Axis 0/1 (left stick) are ignored so moving the stick never binds an action.
        /// </summary>
        public static int ProbeNewPress()
        {
            Poll();
            int found = -1;
            for (int b = 0; b < ButtonCount; b++)
            {
                bool on = Input.GetKey(KeyCode.JoystickButton0 + b);
                if (on && !rawPrev[b] && found < 0) found = b;
                rawPrev[b] = on;
            }
            for (int ax = 2; ax < AxisCount; ax++)
            {
                for (int s = 0; s < 2; s++)
                {
                    int code = AxisBase + ax * 2 + s, idx = ButtonCount + ax * 2 + s;
                    bool on = CodeHeld(code) && Mathf.Abs(axis[ax] - rest[ax]) > 0.6f;
                    if (on && !rawPrev[idx] && found < 0) found = code;
                    rawPrev[idx] = on;
                }
            }
            return found;
        }

        /// <summary>True while any pad button or bound axis is held (the wizard waits for release).</summary>
        public static bool AnyHeld()
        {
            Poll();
            for (int b = 0; b < ButtonCount; b++) if (Input.GetKey(KeyCode.JoystickButton0 + b)) return true;
            for (int ax = 2; ax < AxisCount; ax++) if (!axisMissing[ax] && Mathf.Abs(axis[ax] - rest[ax]) > 0.4f) return true;
            return false;
        }
    }
}
