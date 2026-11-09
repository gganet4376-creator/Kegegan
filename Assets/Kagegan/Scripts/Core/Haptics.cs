using System.Collections.Generic;
using UnityEngine;

namespace Kagegan
{
    /// <summary>
    /// Layered haptics. Phone: Android 11+ composition primitives (crisp CLICK / THUD / QUICK_RISE on phones with
    /// a good linear motor), otherwise amplitude waveforms (hit + echo), otherwise plain on/off patterns.
    /// Gamepad: rumble through the controller's own vibrators (Android InputDevice, dual motors on Android 12+).
    /// Stronger effects pre-empt weaker ones inside the throttle window; weaker ones never cut a stronger one short.
    /// Effects are cached per strength setting, so a hit costs one JNI call.
    /// </summary>
    public static class Haptics
    {
        public enum Fx { Tick, Light, Medium, Heavy, Parry, Ult, Ko }

        // timings (ms: off, on, off, on ...) and amplitudes (0..255) of the waveform fallback
        struct Profile { public long[] t; public int[] a; public int[] prim; public float[] primScale; public int[] primDelay; public int priority; public float padLow, padHigh, padSec; }

        // Android VibrationEffect.Composition primitive ids
        const int CLICK = 1, THUD = 2, QUICK_RISE = 4, SLOW_RISE = 5, QUICK_FALL = 6, TICK = 7, LOW_TICK = 8;

        static readonly Dictionary<Fx, Profile> profiles = new Dictionary<Fx, Profile>
        {
            { Fx.Tick,   new Profile { t = new long[] { 0, 9 },                      a = new[] { 0, 60 },                       prim = new[] { TICK },                    primScale = new[] { 0.55f },             primDelay = new[] { 0 },           priority = 0, padLow = 0f,    padHigh = 0.18f, padSec = 0.04f } },
            { Fx.Light,  new Profile { t = new long[] { 0, 16, 18, 10 },             a = new[] { 0, 120, 0, 45 },               prim = new[] { CLICK, LOW_TICK },         primScale = new[] { 0.65f, 0.35f },      primDelay = new[] { 0, 22 },       priority = 1, padLow = 0.18f, padHigh = 0.45f, padSec = 0.07f } },
            { Fx.Medium, new Profile { t = new long[] { 0, 24, 22, 18 },             a = new[] { 0, 190, 0, 80 },               prim = new[] { CLICK, THUD },             primScale = new[] { 1f, 0.45f },         primDelay = new[] { 0, 28 },       priority = 2, padLow = 0.45f, padHigh = 0.6f,  padSec = 0.12f } },
            { Fx.Heavy,  new Profile { t = new long[] { 0, 42, 20, 34, 20, 18 },     a = new[] { 0, 255, 0, 150, 0, 60 },       prim = new[] { THUD, CLICK, QUICK_FALL }, primScale = new[] { 1f, 0.7f, 0.5f },    primDelay = new[] { 0, 10, 30 },   priority = 3, padLow = 0.9f,  padHigh = 0.55f, padSec = 0.22f } },
            { Fx.Parry,  new Profile { t = new long[] { 0, 10, 34, 26 },             a = new[] { 0, 110, 0, 255 },              prim = new[] { QUICK_RISE, CLICK },       primScale = new[] { 0.6f, 1f },          primDelay = new[] { 0, 12 },       priority = 3, padLow = 0.3f,  padHigh = 1f,    padSec = 0.12f } },
            { Fx.Ult,    new Profile { t = new long[] { 0, 40, 0, 40, 0, 40, 0, 90, 40, 50 }, a = new[] { 0, 50, 0, 110, 0, 180, 0, 255, 0, 120 }, prim = new[] { SLOW_RISE, THUD, CLICK, THUD }, primScale = new[] { 0.8f, 1f, 0.8f, 0.6f }, primDelay = new[] { 0, 30, 40, 60 }, priority = 4, padLow = 1f, padHigh = 0.8f, padSec = 0.45f } },
            { Fx.Ko,     new Profile { t = new long[] { 0, 90, 60, 140, 60, 60 },    a = new[] { 0, 255, 0, 200, 0, 90 },       prim = new[] { THUD, THUD, QUICK_FALL },  primScale = new[] { 1f, 0.9f, 0.7f },    primDelay = new[] { 0, 120, 160 }, priority = 5, padLow = 1f,  padHigh = 1f,    padSec = 0.6f } },
        };

        static float busyUntil;
        static int busyPriority = -1;

        public static void Tick() => Play(Fx.Tick);
        public static void Light() => Play(Fx.Light);
        public static void Medium() => Play(Fx.Medium);
        public static void Heavy() => Play(Fx.Heavy);
        public static void Parry() => Play(Fx.Parry);
        public static void Ult() => Play(Fx.Ult);
        public static void Ko() => Play(Fx.Ko);

        /// <summary>Strength multiplier from settings (0.5 .. 1.5).</summary>
        public static float Strength => Mathf.Clamp(Save.Data.vibeStrength <= 0f ? 1f : Save.Data.vibeStrength, 0.25f, 1.5f);

        public static void Play(Fx fx)
        {
            var p = profiles[fx];
            float now = Time.unscaledTime;
            if (now < busyUntil && p.priority < busyPriority) return;          // never cut a stronger effect short
            if (now < busyUntil && p.priority == busyPriority && p.priority < 3 && now - lastStart < 0.05f) return; // rate-limit spam
            lastStart = now;
            long total = 0; foreach (var x in p.t) total += x;
            busyUntil = now + total / 1000f;
            busyPriority = p.priority;

            if (Save.Data.padRumble) PadRumble(p.padLow * Strength, p.padHigh * Strength, p.padSec * Mathf.Lerp(0.8f, 1.2f, Strength - 0.5f));
            if (!Save.Data.vibration || !Application.isMobilePlatform) return;
            // phone on a stand while a controller is used: only the controller rumbles
            if (Save.Data.padRumble && PadInput.ActiveRecently && padOk) return;
            PhonePlay(fx, p);
        }

        static float lastStart;

        /// <summary>Back-compat: a single pulse of the given length and amplitude.</summary>
        public static void Pulse(int ms, int amplitude)
        {
            if (amplitude >= 200) Heavy(); else if (amplitude >= 120) Medium(); else if (amplitude >= 60) Light(); else Tick();
        }

        public static void ClearCache()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            foreach (var e in cache.Values) e?.Dispose();
            cache.Clear();
#endif
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        static AndroidJavaObject vibrator;
        static AndroidJavaClass effectClass;
        static int sdk;
        static bool init, amplitudeControl, primitivesOk;
        static readonly Dictionary<Fx, AndroidJavaObject> cache = new Dictionary<Fx, AndroidJavaObject>();
        static float cacheStrength = -1f;

        static void Init()
        {
            init = true;
            try
            {
                using (var version = new AndroidJavaClass("android.os.Build$VERSION")) sdk = version.GetStatic<int>("SDK_INT");
                using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
                    vibrator = activity.Call<AndroidJavaObject>("getSystemService", "vibrator");
                if (vibrator != null && !vibrator.Call<bool>("hasVibrator")) vibrator = null;
                if (vibrator != null && sdk >= 26)
                {
                    effectClass = new AndroidJavaClass("android.os.VibrationEffect");
                    amplitudeControl = vibrator.Call<bool>("hasAmplitudeControl");
                }
                if (vibrator != null && sdk >= 31)
                {
                    // the full set we use must be backed by the motor, otherwise the waveform sounds better
                    var ok = vibrator.Call<bool[]>("arePrimitivesSupported", (object)new[] { CLICK, THUD, QUICK_RISE, SLOW_RISE, QUICK_FALL, TICK, LOW_TICK });
                    primitivesOk = ok != null && ok.Length == 7;
                    if (primitivesOk) foreach (var b in ok) primitivesOk &= b;
                }
            }
            catch (System.Exception e) { Debug.LogWarning("Kagegan haptics: " + e.Message); vibrator = null; }
        }

        static AndroidJavaObject Build(Profile p)
        {
            float s = Strength;
            if (primitivesOk)
            {
                var comp = effectClass.CallStatic<AndroidJavaObject>("startComposition");
                for (int i = 0; i < p.prim.Length; i++)
                {
                    var next = comp.Call<AndroidJavaObject>("addPrimitive", p.prim[i], Mathf.Clamp01(p.primScale[i] * s), p.primDelay[i]);
                    comp.Dispose(); comp = next;
                }
                var e = comp.Call<AndroidJavaObject>("compose");
                comp.Dispose();
                return e;
            }
            if (amplitudeControl)
            {
                var a = new int[p.a.Length];
                for (int i = 0; i < a.Length; i++) a[i] = p.a[i] == 0 ? 0 : Mathf.Clamp(Mathf.RoundToInt(p.a[i] * s), 1, 255);
                return effectClass.CallStatic<AndroidJavaObject>("createWaveform", p.t, a, -1);
            }
            // on/off motor: keep only the strong segments, scale their length by strength
            var t = (long[])p.t.Clone();
            for (int i = 1; i < t.Length; i += 2) t[i] = p.a[i] < 100 ? 0 : (long)Mathf.Max(8, t[i] * s);
            return effectClass.CallStatic<AndroidJavaObject>("createWaveform", t, -1);
        }

        static void PhonePlay(Fx fx, Profile p)
        {
            if (!init) Init();
            if (vibrator == null) { Handheld.Vibrate(); return; }
            try
            {
                if (sdk < 26 || effectClass == null) { vibrator.Call("vibrate", p.t, -1); return; }
                if (!Mathf.Approximately(cacheStrength, Strength)) { ClearCache(); cacheStrength = Strength; }
                if (!cache.TryGetValue(fx, out var e) || e == null) { e = Build(p); cache[fx] = e; }
                vibrator.Call("cancel");
                vibrator.Call("vibrate", e);
            }
            catch (System.Exception ex) { Debug.LogWarning("Kagegan haptics: " + ex.Message); primitivesOk = false; ClearCache(); }
        }

        // ---------------- gamepad rumble ----------------
        static readonly List<AndroidJavaObject> padVibs = new List<AndroidJavaObject>();   // Vibrator (old API) per pad
        static readonly List<AndroidJavaObject> padManagers = new List<AndroidJavaObject>(); // VibratorManager (31+) per pad
        static readonly List<int[]> padIds = new List<int[]>();
        static float padScanAt = -100f;
        static int padScanCount = -1;
        static bool padOk;
        public static bool PadHasRumble => padOk;

        static void ScanPads()
        {
            int count = PadInput.ConnectedCount;
            if (count == padScanCount && Time.unscaledTime - padScanAt < 10f) return;
            padScanCount = count; padScanAt = Time.unscaledTime;
            foreach (var v in padVibs) v?.Dispose();
            foreach (var m in padManagers) m?.Dispose();
            padVibs.Clear(); padManagers.Clear(); padIds.Clear(); padOk = false;
            if (count == 0) return;
            if (!init) Init();
            try
            {
                using (var dev = new AndroidJavaClass("android.view.InputDevice"))
                {
                    int[] ids = dev.CallStatic<int[]>("getDeviceIds");
                    foreach (int id in ids)
                    {
                        using (var d = dev.CallStatic<AndroidJavaObject>("getDevice", id))
                        {
                            if (d == null) continue;
                            int src = d.Call<int>("getSources");
                            const int SOURCE_GAMEPAD = 0x00000401, SOURCE_JOYSTICK = 0x01000010;
                            if ((src & SOURCE_GAMEPAD) != SOURCE_GAMEPAD && (src & SOURCE_JOYSTICK) != SOURCE_JOYSTICK) continue;
                            if (sdk >= 31)
                            {
                                var vm = d.Call<AndroidJavaObject>("getVibratorManager");
                                int[] vids = vm != null ? vm.Call<int[]>("getVibratorIds") : null;
                                if (vids != null && vids.Length > 0) { padManagers.Add(vm); padIds.Add(vids); padOk = true; continue; }
                                vm?.Dispose();
                            }
                            var v = d.Call<AndroidJavaObject>("getVibrator");
                            if (v != null && v.Call<bool>("hasVibrator")) { padVibs.Add(v); padOk = true; }
                            else v?.Dispose();
                        }
                    }
                }
            }
            catch (System.Exception e) { Debug.LogWarning("Kagegan pad rumble: " + e.Message); }
        }

        static void PadRumble(float low, float high, float sec)
        {
            if (PadInput.ConnectedCount == 0) return;
            ScanPads();
            if (!padOk || effectClass == null) return;
            long ms = (long)Mathf.Clamp(sec * 1000f, 20f, 800f);
            int aLow = Mathf.Clamp(Mathf.RoundToInt(low * 255f), 0, 255), aHigh = Mathf.Clamp(Mathf.RoundToInt(high * 255f), 0, 255);
            try
            {
                for (int i = 0; i < padManagers.Count; i++)
                {
                    int[] vids = padIds[i];
                    using (var cv = new AndroidJavaClass("android.os.CombinedVibration"))
                    {
                        var builder = cv.CallStatic<AndroidJavaObject>("startParallel");
                        for (int k = 0; k < vids.Length; k++)
                        {
                            // Xbox pads list the heavy (left, low-frequency) motor first, the light one second
                            int amp = vids.Length > 1 ? (k == 0 ? aLow : aHigh) : Mathf.Max(aLow, aHigh);
                            if (amp <= 0) continue;
                            using (var e = effectClass.CallStatic<AndroidJavaObject>("createOneShot", ms, amp))
                            {
                                var nb = builder.Call<AndroidJavaObject>("addVibrator", vids[k], e);
                                builder.Dispose(); builder = nb;
                            }
                        }
                        using (var combined = builder.Call<AndroidJavaObject>("combine")) padManagers[i].Call("vibrate", combined);
                        builder.Dispose();
                    }
                }
                foreach (var v in padVibs)
                {
                    int amp = Mathf.Max(1, Mathf.Max(aLow, aHigh));
                    using (var e = effectClass.CallStatic<AndroidJavaObject>("createOneShot", ms, amp)) v.Call("vibrate", e);
                }
            }
            catch (System.Exception e) { Debug.LogWarning("Kagegan pad rumble: " + e.Message); padOk = false; padScanCount = -1; }
        }
#else
        static bool padOk => false;
        public static bool PadHasRumble => false;
        static void PhonePlay(Fx fx, Profile p)
        {
#if UNITY_IOS
            Handheld.Vibrate();
#endif
        }
        static void PadRumble(float low, float high, float sec) { }
#endif
    }
}
