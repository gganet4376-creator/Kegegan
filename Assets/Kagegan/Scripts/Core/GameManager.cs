using System.Collections;
using UnityEngine;

namespace Kagegan
{
    /// <summary>
    /// Entry point. Boots all systems, owns the UI, the menu "scene" (dojo + 3D character preview), starts battles,
    /// handles pause, the ending sequence and results. The whole game lives in one Unity scene.
    /// </summary>
    public class GameManager : MonoBehaviour
    {
        public static GameManager Inst;
        public Transform WorldRoot;
        public Canvas Canvas;
        public Hud Hud;
        public TouchControls Touch;
        public Menus Menus;
        public PlayerController PlayerCtl;
        public Battle Battle;
        Fighter showcase;
        float showcaseTimer;
        // main-menu backdrop: a sparring pair in the distance (Medium+) and drifting leaves
        Fighter sparA, sparB;
        float sparTimer, ambientTimer;
        bool sparTurn;
        bool inMenu;
        LoadingScreen loading;
        Coroutine loadingRoutine;
        public bool IsLoading { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap()
        {
            if (Inst != null) return;
            var go = new GameObject("Kagegan");
            DontDestroyOnLoad(go);
            go.AddComponent<GameManager>();
        }

        void Awake()
        {
            Inst = this;
            Input.multiTouchEnabled = true;
            Screen.sleepTimeout = SleepTimeout.NeverSleep;
            Screen.autorotateToLandscapeLeft = true;
            Screen.autorotateToLandscapeRight = true;
            Screen.autorotateToPortrait = false;
            Screen.autorotateToPortraitUpsideDown = false;
            Screen.orientation = ScreenOrientation.AutoRotation;
            Application.runInBackground = false;

            Save.Load();
            if (Save.Data.graphics < 0)
            {
                Save.Data.graphics = (int)GraphicsQuality.AutoDetect();
                Save.Data.targetFps = Save.Data.graphics == 0 ? 30 : 60;
                Save.Write();
            }
            GraphicsQuality.Apply((GraphicsPreset)Save.Data.graphics);
            CameraRig.ShakeScale = Save.Data.shake;

            Mat.Build();
            UIKit.Init();
            Sfx.Init(gameObject);
            WorldLighting.Init(gameObject);
            GrassBend.Init(gameObject);
            gameObject.AddComponent<TimeFx>();
            gameObject.AddComponent<PerfMonitor>();
            gameObject.AddComponent<PerfProbe>();
            Fx.Init(gameObject);

            WorldRoot = new GameObject("World").transform;
            DontDestroyOnLoad(WorldRoot.gameObject);
            Roster.FighterRoot = new GameObject("Fighters").transform;
            Roster.FighterRoot.SetParent(WorldRoot, false);
            var poolRoot = new GameObject("Pools").transform;
            poolRoot.SetParent(WorldRoot, false);
            Projectile.InitPool(poolRoot);
            Beam.InitPool(poolRoot);
            Hazard.InitPool(poolRoot);

            var rig = CameraRig.Create();
            DontDestroyOnLoad(rig.gameObject);
            // remove any default camera from the scene
            foreach (var cam in FindObjectsByType<Camera>(FindObjectsSortMode.None)) if (cam != rig.Cam) cam.gameObject.SetActive(false);

            UIKit.EnsureEventSystem();
            Canvas = UIKit.MakeCanvas("UI", 10);
            DontDestroyOnLoad(Canvas.gameObject);
            Hud = Canvas.gameObject.AddComponent<Hud>();
            Hud.Build(Canvas.transform, () => Pause(true));
            Touch = Canvas.gameObject.AddComponent<TouchControls>();
            Touch.Build(Canvas.transform);
            Menus = Canvas.gameObject.AddComponent<Menus>();
            Menus.Build(Canvas.transform);
            ScreenFader.Create(Canvas.transform);
            loading = LoadingScreen.Create();
            PlayerCtl = gameObject.AddComponent<PlayerController>();
        }

        IEnumerator Start()
        {
            IsLoading = true;
            loading.Show("Подготовка графики");
            yield return null;
            yield return StartCoroutine(ShaderWarmup.Run());
            ShowMenu();
            loading.Hide();
            IsLoading = false;
            if (AutoTest.Requested) gameObject.AddComponent<AutoTest>();
        }

        // ===================== menu =====================
        public void ShowMenu()
        {
            if (loadingRoutine != null)
            {
                StopCoroutine(loadingRoutine);
                loadingRoutine = null;
                loading.Hide();
                IsLoading = false;
            }
            ScreenFader.Flash(1f);
            TimeFx.Clear();
            CleanupBattle();
            inMenu = true;
            Hud.Root.gameObject.SetActive(false);
            Touch.SetVisible(false);
            Hud.SetTraining(false);
            // the menu always gets the bright daytime dojo: a random sunset tinted the whole backdrop orange
            Arena.Build(ArenaId.Dojo, WorldRoot, WeatherType.Clear, Arena.TimeOfDay.Day);
            SetShowcase((CharacterId)Mathf.Clamp(Save.Data.lastCharacter, 0, (int)CharacterId.Reina));
            SpawnSparring();
            CameraRig.Inst.MenuMode = true;
            CameraRig.Inst.Snap();
            Sfx.PlayTrack("menu", 0);
            Sfx.SetAmbience(null, 0f);
            Menus.ShowMain();
        }

        public void SetShowcase(CharacterId id)
        {
            if (showcase != null) Destroy(showcase.gameObject);
            try { showcase = Roster.CreateShowcase(id, Vector3.zero, WorldRoot); }
            catch (System.Exception e) { Debug.LogException(e); showcase = null; }
            // a broken save or a model that failed to load must never leave the menu without a fighter
            if (showcase == null && id != CharacterId.Kaito) showcase = Roster.CreateShowcase(CharacterId.Kaito, Vector3.zero, WorldRoot);
            if (showcase == null) return;
            showcase.FaceInstant(new Vector3(0.9f, 0f, -4f)); // towards the menu camera
            CameraRig.Inst.MenuFocus = Vector3.zero;
            Fx.Smoke(showcase.Center, 0.8f, new Color(1f, 1f, 1f, 0.5f), 6);
            showcaseTimer = 1.5f;
        }

        void Update()
        {
            if (inMenu && showcase != null)
            {
                // keep the preview turned to the camera (moves and poses may rotate the root)
                if (CameraRig.Inst != null && showcase.State != FState.Attack)
                {
                    Vector3 to = KMath.Flat(CameraRig.Inst.transform.position - showcase.transform.position);
                    if (to.sqrMagnitude > 0.01f) showcase.transform.rotation = Quaternion.Slerp(showcase.transform.rotation, Quaternion.LookRotation(to), 1f - Mathf.Exp(-6f * Time.deltaTime));
                }
                // the preview character shows off moves every few seconds
                showcaseTimer -= Time.deltaTime;
                if (showcaseTimer <= 0f)
                {
                    showcaseTimer = Random.Range(2.5f, 4.5f);
                    var k = showcase.Kit;
                    Move m = null;
                    float r = Random.value;
                    if (r < 0.35f) m = k.LightChain[Random.Range(0, k.LightChain.Length)];
                    else if (r < 0.6f) m = k.Heavy;
                    else if (r < 0.8f) m = k.Launcher;
                    if (m != null) { showcase.Anim.Play(m.Clip); Fx.Slash(showcase.transform, new Vector3(0, 1f, 0.6f), m.SlashEuler, k.AuraColor, 1.4f); Sfx.Play(SfxId.Whoosh, 0.4f); }
                    else showcase.Anim.Play(Clip.Hold(PoseLib.Charge, 0.2f, 0.6f, 0.3f));
                }
            }
            if (inMenu) MenuBackdrop(Time.deltaTime);
            bool pausePressed = false;
            try { pausePressed = Input.GetKeyDown(KeyCode.Escape) || PadInput.Down(PadAction.Pause); } catch (System.Exception) { }
            if (!inMenu && !IsLoading && pausePressed && Battle != null) Pause(!TimeFx.Paused);
        }

        void SpawnSparring()
        {
            DespawnSparring();
            if (GraphicsQuality.Current < GraphicsPreset.Medium) return; // Low keeps the menu to one character
            var pairs = new[] { (MinionType.ForestBandit, MinionType.ForestAssassin), (MinionType.CityMerc, MinionType.CityBrute), (MinionType.MoonGuard, MinionType.SnakeAdept) };
            var (ta, tb) = pairs[Random.Range(0, pairs.Length)];
            // well behind the showcase and off to the free side of the frame, so they read as background life
            Vector3 c = new Vector3(2.6f, 0f, 7.5f);
            try
            {
                sparA = Roster.CreateSparring(ta, c + new Vector3(-0.85f, 0f, 0f), WorldRoot);
                sparB = Roster.CreateSparring(tb, c + new Vector3(0.85f, 0f, 0f), WorldRoot);
            }
            catch (System.Exception e) { Debug.LogException(e); DespawnSparring(); return; }
            sparA.FaceInstant(sparB.transform.position);
            sparB.FaceInstant(sparA.transform.position);
            sparTimer = 1.2f;
        }

        void DespawnSparring()
        {
            if (sparA != null) Destroy(sparA.gameObject);
            if (sparB != null) Destroy(sparB.gameObject);
            sparA = sparB = null;
        }

        void MenuBackdrop(float dt)
        {
            // drifting leaves and dust motes across the camera's view of the dojo
            ambientTimer -= dt;
            if (ambientTimer <= 0f && CameraRig.Inst != null)
            {
                ambientTimer = GraphicsQuality.Current == GraphicsPreset.Low ? 0.6f : 0.3f;
                var cam = CameraRig.Inst.transform;
                Vector3 center = cam.position + cam.forward * 5f;
                Fx.Ambient(center, new Vector3(4.5f, 2.2f, 3f), new Vector3(0.45f, 0f, 0.1f), Random.value < 0.4f);
            }
            if (sparA == null || sparB == null) return;
            sparTimer -= dt;
            if (sparTimer > 0f) return;
            sparTimer = Random.Range(1.1f, 1.9f);
            sparTurn = !sparTurn;
            var atk = sparTurn ? sparA : sparB;
            var def = sparTurn ? sparB : sparA;
            atk.FaceInstant(def.transform.position);
            def.FaceInstant(atk.transform.position);
            var k = atk.Kit;
            var m = Random.value < 0.7f && k.LightChain != null && k.LightChain.Length > 0 ? k.LightChain[Random.Range(0, k.LightChain.Length)] : k.Heavy;
            if (m == null || m.Clip == null) return;
            atk.Anim.Play(m.Clip);
            // the partner guards and the blow lands on the block a beat later (quiet: it is far away)
            def.Anim.Play(Clip.Hold(PoseLib.Block, 0.08f, 0.35f, 0.2f));
            StartCoroutine(SparImpact(def, 0.22f));
        }

        IEnumerator SparImpact(Fighter def, float delay)
        {
            yield return new WaitForSeconds(delay);
            if (!inMenu || def == null) yield break;
            Fx.Block(def.Center + def.transform.forward * 0.35f);
            Sfx.Play(SfxId.Block, 0.12f);
        }

        // ===================== battle =====================
        public void StartBattle(BattleMode mode, CharacterId c, Difficulty d, int bossIndex = 0)
        {
            if (IsLoading) return;
            IsLoading = true;
            loadingRoutine = StartCoroutine(LoadBattle(mode, c, d, bossIndex));
        }

        IEnumerator LoadBattle(BattleMode mode, CharacterId c, Difficulty d, int bossIndex)
        {
            TimeFx.Clear();
            Menus.HideAll();
            loading.Show("Подготовка арены");
            // Let the overlay render before the arena and fighters are built on the main thread.
            yield return null;
            yield return null;
            bool ok = true;
            try { StartBattleNow(mode, c, d, bossIndex); }
            catch (System.Exception e)
            {
                ok = false;
                Debug.LogException(e);
                CleanupBattle();
                ShowMenu();
            }
            if (ok)
            {
                // the arena now renders under the opaque overlay: its first frames compile shaders and upload
                // textures, so the hitch stays hidden instead of landing on the intro camera
                for (int i = 0; i < 4; i++) yield return null;
                yield return new WaitForSecondsRealtime(0.15f);
            }
            loading.Hide();
            IsLoading = false;
            loadingRoutine = null;
        }

        void StartBattleNow(BattleMode mode, CharacterId c, Difficulty d, int bossIndex)
        {
            Save.Data.lastCharacter = (int)c;
            Save.Data.lastDifficulty = (int)d;
            Save.Write();
            inMenu = false;
            if (showcase != null) { Destroy(showcase.gameObject); showcase = null; }
            DespawnSparring();
            Menus.HideAll();
            Hud.Root.gameObject.SetActive(true);
            Hud.SetTraining(false);
            TimeFx.Clear();
            ScreenFader.Flash(1f);
            var go = new GameObject("Battle");
            go.transform.SetParent(WorldRoot, false);
            Battle = go.AddComponent<Battle>();
            Battle.OnDefeat = () => Menus.ShowDefeat();
            Battle.OnFinished = r => Menus.ShowResults(r);
            Battle.OnEnding = r => StartCoroutine(EndingSequence(r));
            Battle.Begin(mode, c, d, bossIndex);
        }

        public void BindPlayer(Fighter p)
        {
            Hud.Bind(p);
            PlayerCtl.Bind(p, Touch);
        }

        public void SetControlsVisible(bool v)
        {
            Touch.SetVisible(v);
            PlayerCtl.enabled = v;
            if (!v && Battle != null && Battle.Player != null) { Battle.Player.SetMove(Vector3.zero); Battle.Player.SetBlock(false); }
        }

        public void Pause(bool on)
        {
            if (Battle == null || (on && IsLoading)) return;
            TimeFx.Paused = on;
            if (on) Menus.ShowPause(); else Menus.HideAll();
            AudioListener.pause = false;
        }

        public void Retry()
        {
            TimeFx.Clear();
            Menus.HideAll();
            Hud.Root.gameObject.SetActive(true);
            Battle?.RetryWave();
        }

        void CleanupBattle()
        {
            if (Battle != null) { Battle.Cleanup(); Destroy(Battle.gameObject); Battle = null; }
            FinisherFx.Cancel();
            Projectile.ClearAll(); Beam.ClearAll(); Hazard.ClearAll(); Fx.ClearAll(); MineTrap.ClearAll(); Squad.Reset();
            Hud.SetBoss(null);
            for (int i = Fighter.All.Count - 1; i >= 0; i--)
                if (Fighter.All[i] != null && Fighter.All[i] != showcase) Destroy(Fighter.All[i].gameObject);
            sparA = sparB = null;
            Combat.AttackTokensInUse = 0;
        }

        // ===================== ending =====================
        IEnumerator EndingSequence(BattleResult r)
        {
            var p = Battle.Player;
            SetControlsVisible(false);
            Hud.Root.gameObject.SetActive(false);
            Sfx.PlayTrack("ending", 3);
            Sfx.SetAmbience(null, 0f);
            Arena.Current?.SetPhase(0);
            p.SetInvulnerable(60f);
            p.Teleport(Vector3.zero);
            p.FaceInstant(new Vector3(0, 0, 10f));
            p.Anim.Mode = AnimMode.Victory;
            p.Anim.PlayMocap("Idle_FoldArms_Loop", 1f, 0f, false, 999f, 0.2f);
            // dawn breaks over the moon temple
            float t = 0f;
            CameraRig.Orbit(p.Center + Vector3.up * 0.3f, 6.5f, 1.4f, 14f);
            string[] lines =
            {
                "Луна снова спокойна.",
                "Сила, что пыталась поглотить мир, рассеялась вместе с её владычицей.",
                Roster.Name(r.Character) + " возвращается домой —",
                "но путь воина только начинается.",
                "KAGEGAN"
            };
            Menus.ShowEndingText();
            for (int i = 0; i < lines.Length; i++)
            {
                Menus.SetEndingLine(lines[i]);
                float d = i == lines.Length - 1 ? 2.8f : 2.4f;
                for (float k = 0f; k < d; k += Time.deltaTime)
                {
                    t += Time.deltaTime;
                    float u = Mathf.Clamp01(t / 8f);
                    WorldLighting.SetSun(new Vector3(-0.6f, Mathf.Lerp(0.2f, 0.7f, u), 0.5f), Color.Lerp(new Color(0.75f, 0.8f, 1f), new Color(1f, 0.8f, 0.6f), u), Color.Lerp(new Color(0.22f, 0.22f, 0.36f), new Color(0.45f, 0.35f, 0.38f), u));
                    WorldLighting.SetFog(Color.Lerp(new Color(0.12f, 0.12f, 0.25f), new Color(0.95f, 0.7f, 0.55f), u), 20f, 90f, 0.7f);
                    if (Random.value < 0.08f) Fx.Glow(p.Center + Random.insideUnitSphere * 3f + Vector3.up * 2f, new Color(1f, 0.85f, 0.6f), 0.4f, 1.5f);
                    if (Menus.SkipRequested) break;
                    yield return null;
                }
                if (Menus.SkipRequested) break;
            }
            CameraRig.EndCinematic();
            Menus.ShowResults(r);
        }

        void OnApplicationPause(bool paused)
        {
            if (paused && Battle != null && !TimeFx.Paused && Battle.Running) Pause(true);
            if (paused) Save.Write();
            else GraphicsQuality.ReapplyFps();
        }
    }
}
