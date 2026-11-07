using UnityEngine;

namespace Kagegan
{
    /// <summary>Short vibration pulses with amplitude on Android 8+, plain vibrate fallback elsewhere.</summary>
    public static class Haptics
    {
        static float lastTime = -1f;
#if UNITY_ANDROID && !UNITY_EDITOR
        static AndroidJavaObject vibrator;
        static AndroidJavaClass effectClass;
        static int sdk;
        static bool init;

        static void Init()
        {
            init = true;
            try
            {
                using (var version = new AndroidJavaClass("android.os.Build$VERSION")) sdk = version.GetStatic<int>("SDK_INT");
                using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                {
                    var activity = player.GetStatic<AndroidJavaObject>("currentActivity");
                    vibrator = activity.Call<AndroidJavaObject>("getSystemService", "vibrator");
                }
                if (sdk >= 26) effectClass = new AndroidJavaClass("android.os.VibrationEffect");
            }
            catch { vibrator = null; }
        }
#endif

        public static void Tick() => Pulse(10, 45);
        public static void Light() => Pulse(18, 70);
        public static void Medium() => Pulse(30, 140);
        public static void Heavy() => Pulse(55, 255);

        public static void Pulse(int ms, int amplitude)
        {
            if (!Save.Data.vibration || !Application.isMobilePlatform) return;
            if (Time.unscaledTime - lastTime < 0.045f) return;
            lastTime = Time.unscaledTime;
#if UNITY_ANDROID && !UNITY_EDITOR
            if (!init) Init();
            if (vibrator == null) { Handheld.Vibrate(); return; }
            try
            {
                if (sdk >= 26 && effectClass != null)
                {
                    using (var effect = effectClass.CallStatic<AndroidJavaObject>("createOneShot", (long)ms, Mathf.Clamp(amplitude, 1, 255)))
                        vibrator.Call("vibrate", effect);
                }
                else vibrator.Call("vibrate", (long)ms);
            }
            catch { }
#elif UNITY_IOS
            Handheld.Vibrate();
#endif
        }
    }
}
