using System;
using System.IO;
using System.Text;
using Unity.Profiling;
using UnityEngine;

namespace Kagegan
{
    /// <summary>
    /// On-device profiling without a Unity Profiler connection.
    /// - Frame time stats: average, 1% low (99th percentile frame time), worst frame, spikes over 33 ms.
    /// - CPU main / render thread and GPU frame time from FrameTimingManager (needs "Frame Timing Stats", enabled by KageganSetup),
    ///   so it is visible which side is the bottleneck.
    /// - Draw call counters (when the player exposes them), fighters and live particles.
    /// - Android battery temperature and thermal status / headroom, read every 10 s.
    /// - A per-wave session log in persistentDataPath/perf_log.txt; the settings screen copies it to the clipboard.
    /// The overlay text is rebuilt a few times per second and cached, so the HUD doesn't allocate a string every frame.
    /// </summary>
    public class PerfProbe : MonoBehaviour
    {
        public static string ShortText { get; private set; } = "";
        public static string DetailText { get; private set; } = "";
        public static string LogPath => Path.Combine(Application.persistentDataPath, "perf_log.txt");

        const int Ring = 600;           // ~10 s at 60 fps for the live 1% low
        const float SpikeMs = 33.4f;    // a frame that missed two 60 Hz vsyncs
        const int Buckets = 240;        // 0.5 ms histogram up to 120 ms for the per-wave 1% low
        static readonly FrameTiming[] ft = new FrameTiming[1];

        readonly float[] ring = new float[Ring];
        readonly float[] sorted = new float[Ring];
        int ringPos, ringCount;
        float uiTimer, shortTimer, envTimer;
        readonly StringBuilder sb = new StringBuilder(512);

        // last frame CPU/GPU (ms), smoothed for the overlay
        float cpuMain, cpuRender, gpu;
        bool haveGpu, haveCpu;

        // environment (Android only; NaN = unknown)
        float batteryC = float.NaN, headroom = float.NaN;
        int thermal = -1;
        bool envBroken;
        int sdk = -1;

        ProfilerRecorder batches, setPass, tris;
        bool recording;

        // ---- per-wave segment for the log ----
        class Segment
        {
            public string key;
            public float time;
            public int frames, spikes;
            public double sumMs, sumCpu, sumGpu;
            public int nCpu, nGpu;
            public float worst;
            public readonly int[] hist = new int[Buckets + 1];
            public float tempStart = float.NaN;
            public int thermalMax = -1;
            public float headroomPeak = float.NaN;
            public float scaleMin = 9f;
            public void Reset(string k, float temp)
            {
                key = k; time = 0f; frames = spikes = nCpu = nGpu = 0; sumMs = sumCpu = sumGpu = 0; worst = 0f;
                Array.Clear(hist, 0, hist.Length);
                tempStart = temp; thermalMax = -1; headroomPeak = float.NaN; scaleMin = 9f;
            }
        }
        readonly Segment seg = new Segment();
        float segFlushTimer;
        bool headerWritten;

        static int skipFrames;
        /// <summary>Leave the next frames out of the stats (arena build / respawn hitch, not a combat frame).</summary>
        public static void SkipFrames(int n) { skipFrames = Mathf.Max(skipFrames, n); }

        void OnEnable() { SetRecorders(Save.Data != null && Save.Data.showFps && Save.Data.perfDetail); }
        void OnDisable() { Flush(false); SetRecorders(false); }

        void SetRecorders(bool on)
        {
            if (on == recording) return;
            recording = on;
            if (on)
            {
                try
                {
                    batches = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Batches Count");
                    setPass = ProfilerRecorder.StartNew(ProfilerCategory.Render, "SetPass Calls Count");
                    tris = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Triangles Count");
                }
                catch (Exception) { }
            }
            else
            {
                batches.Dispose(); setPass.Dispose(); tris.Dispose();
            }
        }

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            if (dt <= 0f) return;
            float ms = dt * 1000f;

            // CPU / GPU split for the previous frame
            FrameTimingManager.CaptureFrameTimings();
            if (FrameTimingManager.GetLatestTimings(1, ft) > 0)
            {
                var f = ft[0];
#if UNITY_2022_1_OR_NEWER
                float main = (float)(f.cpuMainThreadFrameTime - f.cpuMainThreadPresentWaitTime);
                float render = (float)f.cpuRenderThreadFrameTime;
#else
                float main = (float)f.cpuFrameTime;
                float render = 0f;
#endif
                float g = (float)f.gpuFrameTime;
                if (main > 0f && main < 500f) { cpuMain = haveCpu ? Mathf.Lerp(cpuMain, main, 0.1f) : main; cpuRender = Mathf.Lerp(cpuRender, render, 0.1f); haveCpu = true; }
                if (g > 0f && g < 500f) { gpu = haveGpu ? Mathf.Lerp(gpu, g, 0.1f) : g; haveGpu = true; }
            }

            bool detail = Save.Data.showFps && Save.Data.perfDetail;
            SetRecorders(detail);

            // the pause menu and loading hitches don't count: only live combat frames go into the stats
            var b = Battle.Inst;
            if (skipFrames > 0) skipFrames--;
            bool live = b != null && b.Running && !TimeFx.Paused && dt < 0.5f && skipFrames == 0;
            if (live)
            {
                ring[ringPos] = ms; ringPos = (ringPos + 1) % Ring; if (ringCount < Ring) ringCount++;
                Sample(b, ms, dt);
            }
            else if (seg.frames > 0 && (b == null || !b.Running)) Flush(false);

            envTimer -= dt;
            if (envTimer <= 0f) { envTimer = 10f; ReadEnvironment(); }

            shortTimer -= dt;
            if (shortTimer <= 0f)
            {
                shortTimer = 0.25f;
                ShortText = Save.Data.showFps ? Mathf.RoundToInt(PerfMonitor.Fps) + " FPS  ×" + GraphicsQuality.CurrentRenderScale.ToString("0.00") : "";
            }
            uiTimer -= dt;
            if (uiTimer <= 0f)
            {
                uiTimer = 0.5f;
                DetailText = detail ? BuildDetail() : "";
            }
        }

        // ================= per-wave log =================
        void Sample(Battle b, float ms, float dt)
        {
            string key = KeyFor(b);
            if (seg.key != key) { Flush(false); seg.Reset(key, batteryC); segFlushTimer = 0f; }
            seg.time += dt; seg.frames++; seg.sumMs += ms;
            if (ms > seg.worst) seg.worst = ms;
            if (ms > SpikeMs) seg.spikes++;
            seg.hist[Mathf.Min(Buckets, (int)(ms * 2f))]++;
            if (haveCpu) { seg.sumCpu += Mathf.Max(cpuMain, cpuRender); seg.nCpu++; }
            if (haveGpu) { seg.sumGpu += gpu; seg.nGpu++; }
            if (thermal > seg.thermalMax) seg.thermalMax = thermal;
            if (!float.IsNaN(headroom) && (float.IsNaN(seg.headroomPeak) || headroom > seg.headroomPeak)) seg.headroomPeak = headroom;
            seg.scaleMin = Mathf.Min(seg.scaleMin, GraphicsQuality.CurrentRenderScale);
            // long fights are split every 2 minutes, so heating over a 20 minute session shows as a trend
            segFlushTimer += dt;
            if (segFlushTimer >= 120f) { Flush(true); seg.Reset(key, batteryC); segFlushTimer = 0f; }
        }

        static string KeyFor(Battle b)
        {
            string mode = b.Mode == BattleMode.Training ? "Полигон" : b.Mode == BattleMode.BossOnly ? "Босс" : "Раш";
            return mode + " " + b.Character + " в" + (b.Wave + 1) + " " + b.Diff;
        }

        void Flush(bool partial)
        {
            if (seg.frames < 60) { seg.frames = 0; seg.key = null; return; }
            try
            {
                var w = new StringBuilder(256);
                if (!headerWritten)
                {
                    headerWritten = true;
                    w.Append("=== ").Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm")).Append(" | ").Append(SystemInfo.deviceModel)
                     .Append(" | ").Append(SystemInfo.graphicsDeviceName).Append(" ").Append(SystemInfo.graphicsDeviceType)
                     .Append(" | ").Append(Screen.width).Append('x').Append(Screen.height).Append(" ")
#if UNITY_2022_2_OR_NEWER
                     .Append(Mathf.RoundToInt((float)Screen.currentResolution.refreshRateRatio.value)).Append(" Гц")
#else
                     .Append(Screen.currentResolution.refreshRate).Append(" Гц")
#endif
                     .Append(" | ").Append(SystemInfo.processorCount).Append(" ядер, ").Append(SystemInfo.systemMemorySize).Append(" МБ ===\n");
                }
                float avg = (float)(seg.sumMs / seg.frames);
                w.Append(DateTime.Now.ToString("HH:mm:ss")).Append(" | ").Append(seg.key).Append(partial ? " (2 мин)" : "")
                 .Append(" | ").Append(Names.Preset(GraphicsQuality.Current)).Append(" ×").Append(seg.scaleMin.ToString("0.00"))
                 .Append(" цель ").Append(Application.targetFrameRate)
                 .Append(" | ").Append(Mathf.RoundToInt(seg.time)).Append(" с")
                 .Append(" | ср ").Append((1000f / avg).ToString("0.0")).Append(" fps (").Append(avg.ToString("0.0")).Append(" мс)")
                 .Append(" | 1% ").Append(Percentile99(seg.hist, seg.frames).ToString("0.0")).Append(" мс")
                 .Append(" | макс ").Append(seg.worst.ToString("0")).Append(" мс")
                 .Append(" | >33мс: ").Append(seg.spikes);
                if (seg.nCpu > 0) w.Append(" | CPU ").Append((seg.sumCpu / seg.nCpu).ToString("0.0"));
                if (seg.nGpu > 0) w.Append(" GPU ").Append((seg.sumGpu / seg.nGpu).ToString("0.0"));
                if (!float.IsNaN(seg.tempStart) || !float.IsNaN(batteryC))
                    w.Append(" | ").Append(Temp(seg.tempStart)).Append("→").Append(Temp(batteryC)).Append("°C");
                if (seg.thermalMax >= 0) w.Append(" терм ").Append(seg.thermalMax);
                if (!float.IsNaN(seg.headroomPeak)) w.Append(" нагрев ").Append(seg.headroomPeak.ToString("0.00"));
                w.Append('\n');

                string path = LogPath;
                // keep the log small: when it grows past 64 KB, keep only the newest half
                if (File.Exists(path) && new FileInfo(path).Length > 64 * 1024)
                {
                    string old = File.ReadAllText(path);
                    int cut = old.IndexOf('\n', old.Length / 2);
                    File.WriteAllText(path, cut > 0 ? old.Substring(cut + 1) : "");
                }
                File.AppendAllText(path, w.ToString());
            }
            catch (Exception e) { Debug.LogWarning("Kagegan perf log: " + e.Message); }
            seg.frames = 0; seg.key = null;
        }

        static string Temp(float t) => float.IsNaN(t) ? "?" : t.ToString("0.0");

        static float Percentile99(int[] hist, int total)
        {
            int skip = Mathf.Max(1, Mathf.CeilToInt(total * 0.01f));
            int seen = 0;
            for (int i = hist.Length - 1; i >= 0; i--)
            {
                seen += hist[i];
                if (seen >= skip) return i * 0.5f + 0.25f;
            }
            return 0f;
        }

        void OnApplicationPause(bool paused) { if (paused) Flush(true); }

        /// <summary>The newest lines of the log (for the clipboard / chat).</summary>
        public static string ReadLog(int maxChars = 6000)
        {
            try
            {
                if (!File.Exists(LogPath)) return "";
                string s = File.ReadAllText(LogPath);
                return s.Length > maxChars ? s.Substring(s.Length - maxChars) : s;
            }
            catch (Exception) { return ""; }
        }

        // ================= live overlay =================
        string BuildDetail()
        {
            sb.Length = 0;
            int n = ringCount;
            if (n > 0)
            {
                Array.Copy(ring, sorted, n);
                Array.Sort(sorted, 0, n);
                double sum = 0; int spikes = 0;
                for (int i = 0; i < n; i++) { sum += sorted[i]; if (sorted[i] > SpikeMs) spikes++; }
                float avg = (float)(sum / n);
                float p99 = sorted[Mathf.Clamp(Mathf.FloorToInt(n * 0.99f), 0, n - 1)];
                sb.Append("кадр ").Append(avg.ToString("0.0")).Append(" мс  1% ").Append(p99.ToString("0.0"))
                  .Append("  макс ").Append(sorted[n - 1].ToString("0")).Append("  >33: ").Append(spikes).Append('\n');
            }
            else sb.Append("кадр: нет данных боя\n");

            if (haveCpu)
            {
                sb.Append("CPU ").Append(cpuMain.ToString("0.0"));
                if (cpuRender > 0f) sb.Append(" / рендер ").Append(cpuRender.ToString("0.0"));
            }
            else sb.Append("CPU —");
            sb.Append("  GPU ").Append(haveGpu ? gpu.ToString("0.0") : "—");
            if (haveCpu && haveGpu)
            {
                float c = Mathf.Max(cpuMain, cpuRender);
                sb.Append(gpu > c * 1.1f ? "  — упор в GPU" : c > gpu * 1.1f ? (cpuRender > cpuMain ? "  — упор в рендер-поток" : "  — упор в CPU") : "");
            }
            sb.Append('\n');

            sb.Append("батчи ").Append(Counter(batches)).Append("  SetPass ").Append(Counter(setPass)).Append("  треуг ").Append(Counter(tris, 1000, "k")).Append('\n');
            sb.Append("бойцы ").Append(Fighter.All.Count).Append("  частицы ").Append(Fx.LiveParticles())
              .Append("  тени ").Append(GraphicsQuality.ShadowCasters).Append("  пост ").Append(PostFx.Enabled ? "вкл" : "выкл").Append('\n');
            sb.Append(Names.Preset(GraphicsQuality.Current)).Append(" ×").Append(GraphicsQuality.CurrentRenderScale.ToString("0.00"))
              .Append("  цель ").Append(Application.targetFrameRate);
            if (!float.IsNaN(batteryC)) sb.Append("  ").Append(batteryC.ToString("0.0")).Append("°C");
            if (thermal >= 0) sb.Append("  терм ").Append(thermal);
            if (!float.IsNaN(headroom)) sb.Append("  нагрев ").Append(headroom.ToString("0.00"));
            return sb.ToString();
        }

        static string Counter(ProfilerRecorder r, long div = 1, string suffix = "")
        {
            if (!r.Valid) return "—";
            long v = r.LastValue;
            return div > 1 ? (v / (float)div).ToString("0.0") + suffix : v.ToString();
        }

        // ================= Android heat =================
        void ReadEnvironment()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (envBroken) return;
            try
            {
                if (sdk < 0)
                    using (var ver = new AndroidJavaClass("android.os.Build$VERSION")) sdk = ver.GetStatic<int>("SDK_INT");
                using (var up = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var act = up.GetStatic<AndroidJavaObject>("currentActivity"))
                {
                    batteryC = BatteryTemp(act);
                    if (sdk >= 29)
                        using (var pm = act.Call<AndroidJavaObject>("getSystemService", "power"))
                        {
                            thermal = pm.Call<int>("getCurrentThermalStatus");
                            // forecast 10 s ahead; 1.0 = the phone starts throttling
                            if (sdk >= 30) headroom = pm.Call<float>("getThermalHeadroom", 10);
                        }
                }
            }
            catch (Exception e)
            {
                envBroken = true;
                AndroidJNI.ExceptionClear();
                Debug.LogWarning("Kagegan perf: thermal info unavailable (" + e.Message + ")");
            }
#endif
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        /// <summary>Battery temperature from the sticky ACTION_BATTERY_CHANGED intent (registerReceiver with a null receiver).
        /// Raw JNI because AndroidJavaObject can't pass a typed null argument.</summary>
        static float BatteryTemp(AndroidJavaObject act)
        {
            IntPtr intent = IntPtr.Zero, key = IntPtr.Zero;
            try
            {
                using (var filter = new AndroidJavaObject("android.content.IntentFilter", "android.intent.action.BATTERY_CHANGED"))
                {
                    IntPtr actRaw = act.GetRawObject();
                    IntPtr cls = AndroidJNI.GetObjectClass(actRaw);
                    IntPtr reg = AndroidJNI.GetMethodID(cls, "registerReceiver", "(Landroid/content/BroadcastReceiver;Landroid/content/IntentFilter;)Landroid/content/Intent;");
                    AndroidJNI.DeleteLocalRef(cls);
                    if (reg == IntPtr.Zero) { AndroidJNI.ExceptionClear(); return float.NaN; }
                    var a = new jvalue[2];
                    a[0].l = IntPtr.Zero; a[1].l = filter.GetRawObject();
                    intent = AndroidJNI.CallObjectMethod(actRaw, reg, a);
                    if (AndroidJNI.ExceptionOccurred() != IntPtr.Zero) { AndroidJNI.ExceptionClear(); return float.NaN; }
                    if (intent == IntPtr.Zero) return float.NaN;
                    IntPtr icls = AndroidJNI.GetObjectClass(intent);
                    IntPtr get = AndroidJNI.GetMethodID(icls, "getIntExtra", "(Ljava/lang/String;I)I");
                    AndroidJNI.DeleteLocalRef(icls);
                    if (get == IntPtr.Zero) { AndroidJNI.ExceptionClear(); return float.NaN; }
                    key = AndroidJNI.NewStringUTF("temperature");
                    var g = new jvalue[2];
                    g[0].l = key; g[1].i = int.MinValue;
                    int t = AndroidJNI.CallIntMethod(intent, get, g);
                    if (AndroidJNI.ExceptionOccurred() != IntPtr.Zero) { AndroidJNI.ExceptionClear(); return float.NaN; }
                    return t == int.MinValue ? float.NaN : t / 10f; // tenths of a degree
                }
            }
            finally
            {
                if (key != IntPtr.Zero) AndroidJNI.DeleteLocalRef(key);
                if (intent != IntPtr.Zero) AndroidJNI.DeleteLocalRef(intent);
            }
        }
#endif

        void OnDestroy() { SetRecorders(false); }
    }
}
