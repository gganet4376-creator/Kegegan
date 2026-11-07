using UnityEngine;

namespace Kagegan
{
    /// <summary>Hit-stop and slow motion driven by unscaled time. Pause sets scale to 0 and wins over everything.</summary>
    public class TimeFx : MonoBehaviour
    {
        static float stopUntil, slowUntil, slowScale = 1f;
        public static bool Paused;

        public static void HitStop(float seconds)
        {
            stopUntil = Mathf.Max(stopUntil, Time.unscaledTime + seconds);
        }

        public static void SlowMo(float seconds, float scale)
        {
            slowUntil = Mathf.Max(slowUntil, Time.unscaledTime + seconds);
            slowScale = scale;
        }

        public static void Clear() { stopUntil = slowUntil = 0f; Paused = false; Time.timeScale = 1f; }

        void Update()
        {
            float now = Time.unscaledTime;
            float s = 1f;
            if (now < slowUntil) s = slowScale;
            if (now < stopUntil) s = 0.04f;
            if (Paused) s = 0f;
            Time.timeScale = s;
            Time.fixedDeltaTime = 0.02f * Mathf.Max(0.05f, s);
        }
    }
}
