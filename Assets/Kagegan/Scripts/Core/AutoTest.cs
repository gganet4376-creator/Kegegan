using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace Kagegan
{
    /// <summary>
    /// Hidden self-test, only active when the game is launched with "-kagetest".
    /// Walks every menu screen, every arena x weather x graphics preset, runs bot-vs-AI Boss Rush fights
    /// (the player is driven by an AIBrain), records frame times and every error/exception, takes screenshots,
    /// writes report.txt / frames.csv / errors.txt and quits. Normal play never creates this component.
    ///
    /// Args: -kagetest            run the full test
    ///       -kagetestquick       shorter run (fewer combos, shorter fights)
    ///       -kagetestout DIR     output folder (default: persistentDataPath/kagetest)
    /// </summary>
    public class AutoTest : MonoBehaviour
    {
        public static bool Active { get; private set; }
        public static bool Requested => HasArg("-kagetest") || HasArg("-kagetestquick") || UrlHas("kagetest");
        static bool Web => Application.platform == RuntimePlatform.WebGLPlayer;

        // WebGL: no command line, so the test is switched on by the page URL (index.html?kagetest or ?kagetestquick)
        static bool UrlHas(string key)
        {
            try { return !string.IsNullOrEmpty(Application.absoluteURL) && Application.absoluteURL.IndexOf(key, StringComparison.OrdinalIgnoreCase) >= 0; }
            catch (Exception) { return false; }
        }

        string outDir;
        bool quick;
        string scenario = "boot";
        string saveBackup;
        readonly StringBuilder report = new StringBuilder();
        readonly StringBuilder csv = new StringBuilder("scenario,frames,avg_fps,low1_fps,worst_ms,errors\n");
        readonly List<string> errors = new List<string>();
        readonly Dictionary<string, int> errorCounts = new Dictionary<string, int>();
        readonly Dictionary<string, int> errorsPerScenario = new Dictionary<string, int>();
        readonly List<float> frameMs = new List<float>(4096);
        int shots;
        float startTime;

        static bool HasArg(string a)
        {
            foreach (var s in Environment.GetCommandLineArgs()) if (string.Equals(s, a, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        static string ArgValue(string a)
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++) if (string.Equals(args[i], a, StringComparison.OrdinalIgnoreCase)) return args[i + 1];
            return null;
        }

        void Awake()
        {
            Active = true;
            quick = HasArg("-kagetestquick") || UrlHas("kagetestquick");
            outDir = ArgValue("-kagetestout");
            if (string.IsNullOrEmpty(outDir)) outDir = Path.Combine(Application.persistentDataPath, "kagetest");
            if (!Web)
            {
                Directory.CreateDirectory(outDir);
                Directory.CreateDirectory(Path.Combine(outDir, "shots"));
            }
            Application.runInBackground = true;
            Application.logMessageReceivedThreaded += OnLog;
            saveBackup = JsonUtility.ToJson(Save.Data);
            startTime = Time.realtimeSinceStartup;
            Line("KAGEGAN auto-test " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            Line("Unity " + Application.unityVersion + " | " + Application.platform + " | " + SystemInfo.operatingSystem);
            Line("GPU: " + SystemInfo.graphicsDeviceName + " (" + SystemInfo.graphicsDeviceType + ", " + SystemInfo.graphicsMemorySize + " MB)");
            Line("CPU: " + SystemInfo.processorType + " x" + SystemInfo.processorCount + " | RAM " + SystemInfo.systemMemorySize + " MB");
            Line("Screen: " + Screen.width + "x" + Screen.height + " | quick=" + quick);
            Line("NOTE: desktop software rendering; FPS here is NOT phone performance, only relative.");
            Line("");
        }

        void OnDestroy() { Application.logMessageReceivedThreaded -= OnLog; }

        void OnLog(string msg, string stack, LogType type)
        {
            if (type == LogType.Log || type == LogType.Warning) return;
            lock (errors)
            {
                string key = type + ": " + msg;
                if (errorCounts.TryGetValue(key, out int n)) { errorCounts[key] = n + 1; }
                else
                {
                    errorCounts[key] = 1;
                    errors.Add("[" + scenario + "] " + key + "\n" + stack);
                }
                errorsPerScenario.TryGetValue(scenario, out int s);
                errorsPerScenario[scenario] = s + 1;
            }
        }

        void Line(string s) { report.AppendLine(s); Debug.Log("[kagetest] " + s); }

        void Update() { frameMs.Add(Time.unscaledDeltaTime * 1000f); }

        // ===================== helpers =====================
        void Begin(string name) { scenario = name; frameMs.Clear(); }

        void EndScenario(string extra = "")
        {
            int frames = frameMs.Count;
            float avgFps = 0f, low1 = 0f, worst = 0f;
            if (frames > 2)
            {
                // drop the first frames (loading hitch) from the averages but keep it in "worst"
                double sum = 0; for (int i = 0; i < frames; i++) { sum += frameMs[i]; worst = Mathf.Max(worst, frameMs[i]); }
                avgFps = (float)(1000.0 * frames / sum);
                var sorted = new List<float>(frameMs); sorted.Sort();
                float p99 = sorted[Mathf.Clamp(Mathf.CeilToInt(frames * 0.99f) - 1, 0, frames - 1)];
                low1 = p99 > 0f ? 1000f / p99 : 0f;
            }
            errorsPerScenario.TryGetValue(scenario, out int errs);
            csv.AppendLine(scenario + "," + frames + "," + avgFps.ToString("0.0") + "," + low1.ToString("0.0") + "," + worst.ToString("0") + "," + errs);
            Line(string.Format("{0,-34} fps {1,5:0.0}  1%low {2,5:0.0}  worst {3,5:0}ms  errors {4}{5}", scenario, avgFps, low1, worst, errs, extra == "" ? "" : "  " + extra));
        }

        IEnumerator Shot(string name)
        {
            yield return new WaitForEndOfFrame();
            if (Web)
            {
                // the frame itself goes to the console as JPEG base64 chunks; the browser harness reassembles them.
                // Grabbing the page from outside is far too slow under software rendering and lands on later frames.
                string id = (++shots).ToString("000") + "_" + name;
                Debug.Log("[kagetest] SHOT " + id);
                string b64 = null;
                Texture2D tex = null;
                try
                {
                    tex = ScreenCapture.CaptureScreenshotAsTexture();
                    b64 = Convert.ToBase64String(tex.EncodeToJPG(80));
                }
                catch (Exception e) { Debug.LogWarning("[kagetest] capture failed: " + e.Message); }
                if (tex != null) Destroy(tex);
                if (b64 != null)
                {
                    const int chunk = 48000;
                    int n = (b64.Length + chunk - 1) / chunk;
                    for (int i = 0; i < n; i++)
                        Debug.Log("[kagetest] IMG " + id + " " + i + "/" + n + " " + b64.Substring(i * chunk, Mathf.Min(chunk, b64.Length - i * chunk)));
                }
                yield return Wait(0.3f);
                yield break;
            }
            string file = Path.Combine(outDir, "shots", (++shots).ToString("000") + "_" + name + ".png");
            try { ScreenCapture.CaptureScreenshot(file); } catch (Exception e) { Debug.LogError("screenshot failed: " + e.Message); }
            yield return null;
            yield return null;
        }

        static IEnumerator Wait(float seconds)
        {
            float end = Time.realtimeSinceStartup + seconds;
            while (Time.realtimeSinceStartup < end) yield return null;
        }

        static GameManager GM => GameManager.Inst;

        void Driver()
        {
            var b = GM.Battle;
            if (b == null || b.Player == null) return;
            var p = b.Player;
            var brain = p.GetComponent<PlayerBot>();
            if (brain == null) p.gameObject.AddComponent<PlayerBot>().Init(p);
        }

        // ===================== main =====================
        IEnumerator Start()
        {
            yield return Wait(2f);
            yield return Step("menus", Menus());
            yield return Step("arenas", Arenas());
            yield return Step("fights", Fights());
            Finish();
        }

        IEnumerator Step(string name, IEnumerator body)
        {
            Line("== " + name + " ==");
            while (true)
            {
                object cur;
                try { if (!body.MoveNext()) break; cur = body.Current; }
                catch (Exception e) { Debug.LogException(e); Line("!! step " + name + " aborted: " + e.Message); break; }
                yield return cur;
            }
            Line("");
        }

        IEnumerator Menus()
        {
            var m = GM.Menus;
            GraphicsQuality.Apply(GraphicsPreset.High);
            GM.ShowMenu();
            Begin("menu_main"); yield return Wait(2.5f); yield return Shot("menu_main"); EndScenario();
            Begin("menu_characters"); m.ShowCharSelect(); yield return Wait(1.5f); yield return Shot("menu_characters"); EndScenario();
            for (int c = 0; c < 4; c++)
            {
                Begin("showcase_" + (CharacterId)c); GM.SetShowcase((CharacterId)c); yield return Wait(4f); yield return Shot("showcase_" + (CharacterId)c); EndScenario();
            }
            Begin("menu_difficulty"); m.ShowDifficulty(); yield return Wait(1f); yield return Shot("menu_difficulty"); EndScenario();
            Begin("menu_bosses"); m.ShowBossSelect(); yield return Wait(1f); yield return Shot("menu_bosses"); EndScenario();
            Begin("menu_settings"); m.ShowSettings(m.ShowMain); yield return Wait(1f); yield return Shot("menu_settings"); EndScenario();
            Begin("menu_layout"); m.ShowLayout(); yield return Wait(1f); yield return Shot("menu_layout"); EndScenario();
            GM.Touch.SetEditMode(false); GM.Touch.SetVisible(false);
            GM.ShowMenu();

            // in-battle screens: HUD, pause, defeat, results
            Begin("training_hud");
            GM.StartBattle(BattleMode.Training, CharacterId.Kaito, Difficulty.Warrior);
            yield return Wait(3f); yield return Shot("training_hud");
            Driver(); yield return Wait(5f); yield return Shot("training_bot");
            EndScenario();
            Begin("menu_pause"); GM.Pause(true); yield return Wait(0.6f); yield return Shot("menu_pause"); GM.Pause(false); EndScenario();
            Begin("menu_defeat"); m.ShowDefeat(); yield return Wait(0.8f); yield return Shot("menu_defeat"); EndScenario();
            Begin("menu_results"); m.ShowResults(GM.Battle.BuildResult(true)); yield return Wait(0.8f); yield return Shot("menu_results"); EndScenario();
            GM.ShowMenu();
            yield return Wait(1f);
        }

        IEnumerator Arenas()
        {
            var presets = new[] { GraphicsPreset.Low, GraphicsPreset.Medium, GraphicsPreset.High, GraphicsPreset.Ultra };
            var arenas = new[] { ArenaId.Dojo, ArenaId.Forest, ArenaId.City, ArenaId.MoonTemple };
            foreach (var p in presets)
            {
                if (quick && (p == GraphicsPreset.Medium || p == GraphicsPreset.High)) continue;
                GraphicsQuality.Apply(p);
                foreach (var a in arenas)
                {
                    // every weather on High (or Ultra in quick mode), clear + storm elsewhere
                    bool full = p == (quick ? GraphicsPreset.Ultra : GraphicsPreset.High);
                    var weathers = full
                        ? new[] { WeatherType.Clear, WeatherType.Rain, WeatherType.Storm, WeatherType.Snow, WeatherType.Fog, WeatherType.Downpour }
                        : new[] { WeatherType.Clear, WeatherType.Storm };
                    foreach (var w in weathers)
                    {
                        string name = p + "_" + a + "_" + w;
                        Begin(name);
                        Save.Data.weather = (int)w + 2;
                        if (a == ArenaId.Dojo) GM.StartBattle(BattleMode.Training, CharacterId.Kaito, Difficulty.Warrior);
                        else GM.StartBattle(BattleMode.BossOnly, CharacterId.Kaito, Difficulty.Warrior, a == ArenaId.Forest ? 0 : a == ArenaId.City ? 1 : 2);
                        yield return Wait(3.5f);
                        yield return Shot(name + "_a");
                        Driver();
                        // the player is invulnerable here: this pass is about visuals, not the fight result
                        var pl = GM.Battle != null ? GM.Battle.Player : null;
                        if (pl != null) pl.SetInvulnerable(60f);
                        yield return Wait(quick ? 3f : 5f);
                        yield return Shot(name + "_b");
                        string kind = Arena.Current != null ? Arena.Current.WeatherKind.ToString() : "none";
                        EndScenario(kind != w.ToString() ? "WEATHER MISMATCH (got " + kind + ")" : "");
                        GM.ShowMenu();
                        yield return Wait(0.5f);
                    }
                }
            }
            Save.Data.weather = 0;
        }

        IEnumerator Fights()
        {
            GraphicsQuality.Apply(GraphicsPreset.Medium);
            Save.Data.weather = 0;
            var runs = new List<(CharacterId c, Difficulty d)>
            {
                (CharacterId.Kaito, Difficulty.Warrior), (CharacterId.Genryu, Difficulty.Warrior), (CharacterId.Mugen, Difficulty.Warrior), (CharacterId.Reina, Difficulty.Warrior),
            };
            if (!quick) { runs.Add((CharacterId.Kaito, Difficulty.Novice)); runs.Add((CharacterId.Genryu, Difficulty.Legend)); }
            float limit = quick ? 90f : 240f;
            foreach (var r in runs)
            {
                string name = "rush_" + r.c + "_" + r.d;
                Begin(name);
                GM.StartBattle(BattleMode.BossRush, r.c, r.d);
                float t0 = Time.realtimeSinceStartup, nextShot = 15f;
                int lastWave = -1;
                string outcome = "timeout";
                while (Time.realtimeSinceStartup - t0 < limit)
                {
                    Driver();
                    var b = GM.Battle;
                    if (b == null) { outcome = "battle vanished"; break; }
                    if (b.Wave != lastWave) { lastWave = b.Wave; Line("   " + name + ": wave " + (b.Wave + 1) + " at " + (Time.realtimeSinceStartup - t0).ToString("0") + "s"); }
                    if (!b.Running) { outcome = b.Player != null && !b.Player.Alive ? "defeat" : "finished"; break; }
                    float el = Time.realtimeSinceStartup - t0;
                    if (el > nextShot) { nextShot += 30f; yield return Shot(name + "_" + Mathf.RoundToInt(el) + "s"); }
                    yield return null;
                }
                yield return Shot(name + "_end");
                var bb = GM.Battle;
                string info = outcome;
                if (bb != null)
                {
                    var res = bb.BuildResult(false);
                    var bot = bb.Player != null ? bb.Player.GetComponent<PlayerBot>() : null;
                    info += string.Format(" | wave {0} | dmg {1:0}/{2:0} | kills {3} | maxCombo {4} | parries {5} | dodges {6} | finishers {7} | techs {8} | ults {9}",
                        bb.Wave + 1, res.DamageDealt, res.DamageTaken, res.Kills, res.MaxCombo, res.Parries, res.PerfectDodges, res.Finishers,
                        bot != null ? bot.TechsUsed : 0, bot != null ? bot.UltsUsed : 0);
                    if (bb.Boss != null) info += " | boss " + bb.Boss.DisplayName + " " + Mathf.RoundToInt(bb.Boss.HpFrac * 100) + "%";
                }
                EndScenario(info);
                GM.ShowMenu();
                yield return Wait(1f);
            }
        }

        void Finish()
        {
            scenario = "finish";
            Line("== summary ==");
            Line("total time " + (Time.realtimeSinceStartup - startTime).ToString("0") + "s, screenshots " + shots + ", unique errors " + errors.Count);
            foreach (var kv in errorCounts) Line("  x" + kv.Value + "  " + kv.Key.Split('\n')[0]);
            lock (errors) foreach (var e in errors) Debug.Log("[kagetest] ERROR " + e.Replace("\n", "\n[kagetest]   "));
            if (Web) { JsonUtility.FromJsonOverwrite(saveBackup, Save.Data); Save.Write(); Debug.Log("[kagetest] DONE"); return; }
            try
            {
                File.WriteAllText(Path.Combine(outDir, "report.txt"), report.ToString());
                File.WriteAllText(Path.Combine(outDir, "frames.csv"), csv.ToString());
                lock (errors) File.WriteAllText(Path.Combine(outDir, "errors.txt"), errors.Count == 0 ? "no errors\n" : string.Join("\n\n", errors));
            }
            catch (Exception e) { Debug.LogError("report write failed: " + e.Message); }
            // put the player's settings back exactly as they were
            if (!string.IsNullOrEmpty(saveBackup)) { JsonUtility.FromJsonOverwrite(saveBackup, Save.Data); Save.Write(); }
            StartCoroutine(Quit());
        }

        IEnumerator Quit()
        {
            yield return Wait(1.5f);
            Application.Quit(errors.Count > 0 ? 2 : 0);
        }
    }

    /// <summary>Drives the player fighter in the auto-test: an AIBrain plus periodic techniques and ultimates.</summary>
    public class PlayerBot : MonoBehaviour
    {
        Fighter f;
        AIBrain brain;
        float techTimer = 3f;
        public int TechsUsed, UltsUsed;

        public void Init(Fighter fighter)
        {
            f = fighter;
            brain = f.gameObject.AddComponent<AIBrain>();
            brain.Setup(f, Archetype.Striker, DifficultyTuning.For(Difficulty.Legend));
            brain.UsesTokens = false;
            f.Brain = null; // keep the player flagged as the human for kits/HUD
        }

        void Update()
        {
            if (f == null || !f.Alive || TimeFx.Paused) return;
            if (f.Target == null || !f.Target.Alive) f.Target = Combat.Nearest(f.transform.position, f.Team);
            techTimer -= Time.deltaTime;
            if (techTimer > 0f || f.Target == null) return;
            techTimer = UnityEngine.Random.Range(2f, 4f);
            float d = KMath.Flat(f.Target.transform.position - f.transform.position).magnitude;
            if (d > 9f) return;
            if (f.Rage >= 100f && UnityEngine.Random.value < 0.6f) { f.Press(Cmd.Ult); UltsUsed++; return; }
            var techs = f.Kit.Techs;
            if (techs.Length == 0) return;
            int i = UnityEngine.Random.Range(0, techs.Length);
            var t = techs[i];
            if (t == null) return;
            if (t.Hold) StartCoroutine(HoldTech(i));
            else f.Press(Cmd.Tech, i);
            TechsUsed++;
        }

        IEnumerator HoldTech(int i)
        {
            f.SetTechHeld(i, true);
            yield return new WaitForSeconds(UnityEngine.Random.Range(0.6f, 1.6f));
            if (f != null) f.SetTechHeld(i, false);
        }
    }
}
