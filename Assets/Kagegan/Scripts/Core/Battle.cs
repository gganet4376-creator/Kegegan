using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Kagegan
{
    public enum BattleMode { BossRush, BossOnly, Training }

    public class BattleResult
    {
        public bool Cleared;
        public BattleMode Mode;
        public CharacterId Character;
        public Difficulty Difficulty;
        public float Time;
        public int MaxCombo, HitsTaken, PerfectDodges, Parries, Finishers, Kills, Absorbs;
        public float DamageDealt, DamageTaken;
        public int Rank; // 0=S..3=C
        public float AvgFps;
        public string Unlocked = "";
        public int WaveReached;
        public static string RankName(int r) => r == 0 ? "S" : r == 1 ? "A" : r == 2 ? "B" : "C";
    }

    /// <summary>
    /// Runs a fight: Boss Rush (3 waves: minion groups then a boss, in three arenas), single boss replay, or the training ground.
    /// Controls spawning with an active-enemy budget, boss intros, defeat/retry, the ending and saving results.
    /// </summary>
    public class Battle : MonoBehaviour
    {
        public static Battle Inst;
        public BattleMode Mode;
        public CharacterId Character;
        public Difficulty Diff;
        public DifficultyTuning Tune;
        public int Wave;
        public Fighter Player, Boss;
        public bool Running;
        readonly List<Fighter> enemies = new List<Fighter>();
        readonly List<Fighter> summons = new List<Fighter>();
        readonly Queue<MinionType> pending = new Queue<MinionType>();
        float runTime;
        int bossOnly;
        Coroutine flow;
        Fighter dummy;
        public System.Action<BattleResult> OnFinished;
        public System.Action OnDefeat;
        public System.Action<BattleResult> OnEnding;

        public int SummonCount { get { summons.RemoveAll(s => s == null || !s.Alive); return summons.Count; } }
        public int EnemiesLeft
        {
            get
            {
                int n = pending.Count;
                for (int i = 0; i < enemies.Count; i++) if (enemies[i] != null && enemies[i].Alive) n++;
                return n;
            }
        }

        public static string WaveName(int w) => w == 0 ? "Лес разрушений" : w == 1 ? "Разрушенный город" : "Лунный храм";
        public static ArenaId ArenaFor(int w) => w == 0 ? ArenaId.Forest : w == 1 ? ArenaId.City : ArenaId.MoonTemple;

        static MinionType[][] Groups(int w, Difficulty d)
        {
            MinionType[][] g;
            switch (w)
            {
                case 0:
                    g = new[] { new[] { MinionType.ForestBandit, MinionType.ForestBandit, MinionType.ForestAssassin },
                                new[] { MinionType.ForestThrower, MinionType.ForestAssassin, MinionType.ForestBandit, MinionType.ForestThrower } };
                    break;
                case 1:
                    g = new[] { new[] { MinionType.CityMerc, MinionType.CityBrute, MinionType.CityBomber },
                                new[] { MinionType.CityMerc, MinionType.CityMerc, MinionType.CityBrute, MinionType.CityBomber } };
                    break;
                default:
                    g = new[] { new[] { MinionType.MoonGuard, MinionType.MoonPriest, MinionType.MoonBone },
                                new[] { MinionType.MoonGuard, MinionType.MoonGuard, MinionType.MoonPriest, MinionType.MoonBone } };
                    break;
            }
            if (d == Difficulty.Novice) { for (int i = 0; i < g.Length; i++) g[i] = Trim(g[i], g[i].Length - 1); }
            if (d == Difficulty.Legend) { for (int i = 0; i < g.Length; i++) g[i] = Extend(g[i], g[i][0]); }
            return g;
        }

        static MinionType[] Trim(MinionType[] a, int n) { var r = new MinionType[n]; System.Array.Copy(a, r, n); return r; }
        static MinionType[] Extend(MinionType[] a, MinionType extra) { var r = new MinionType[a.Length + 1]; a.CopyTo(r, 0); r[a.Length] = extra; return r; }

        // ===================== start =====================
        public void Begin(BattleMode mode, CharacterId c, Difficulty d, int bossIndex)
        {
            Inst = this;
            Mode = mode; Character = c; Diff = d; bossOnly = bossIndex;
            Tune = DifficultyTuning.For(d);
            Combat.AttackTokensInUse = 0;
            runTime = 0f;
            PerfMonitor.ResetAverage();
            Running = true;
            flow = StartCoroutine(mode == BattleMode.Training ? TrainingFlow() : mode == BattleMode.BossOnly ? BossOnlyFlow() : RushFlow(0));
        }

        void Update()
        {
            if (!Running || TimeFx.Paused) return;
            runTime += Time.deltaTime;
            if (Player != null && !Player.Alive && Mode != BattleMode.Training)
            {
                Running = false;
                if (flow != null) StopCoroutine(flow);
                StartCoroutine(DefeatRoutine());
            }
            if (Mode == BattleMode.Training && Player != null)
            {
                Player.Rage = Mathf.Max(Player.Rage, Mathf.Min(100f, Player.Rage + Time.deltaTime * 25f));
                if (!Player.Alive) { Player.Revive(1f); }
                if (dummy != null && !dummy.Alive) StartCoroutine(RespawnDummy());
            }
            Hud.Inst?.SetEnemyCount(Mode == BattleMode.Training ? "" : (Boss != null && Boss.Alive ? "" : "Врагов осталось: " + EnemiesLeft));
        }

        void SpawnPlayer(Vector3 pos)
        {
            if (Player == null)
            {
                Player = Roster.CreatePlayer(Character, pos);
                Player.OnShout = Hud.Line;
                GameManager.Inst.BindPlayer(Player);
            }
            else
            {
                Player.Teleport(pos);
                Player.Revive(Player.HpFrac);
            }
            Player.FaceInstant(Vector3.zero);
            CameraRig.Inst.Player = Player.transform;
            CameraRig.Inst.MenuMode = false;
            CameraRig.LaneMode = true;
            CameraRig.Inst.Snap();
        }

        // ===================== Boss Rush =====================
        IEnumerator RushFlow(int startWave)
        {
            for (Wave = startWave; Wave < 3; Wave++)
            {
                yield return StartCoroutine(LoadWave(Wave));
                var groups = Groups(Wave, Diff);
                for (int g = 0; g < groups.Length; g++)
                {
                    Hud.Banner(g == 0 ? "ВОЛНА " + (Wave + 1) : "ПОДКРЕПЛЕНИЕ!", new Color(1f, 0.85f, 0.5f));
                    if (g == 0) StartCoroutine(Announce(Wave == 2 ? "final_round" : "round_" + (Wave + 1), "fight"));
                    else Sfx.Voice("ready", 0.8f);
                    foreach (var m in groups[g]) pending.Enqueue(m);
                    yield return StartCoroutine(FightUntilClear());
                    yield return new WaitForSeconds(0.8f);
                }
                yield return StartCoroutine(BossFight(Wave));
                if (Wave < 2)
                {
                    Hud.Banner("ВОЛНА ПРОЙДЕНА", new Color(0.6f, 1f, 0.6f));
                    Sfx.Jingle("wave_clear");
                    Player.Hp = Mathf.Min(Player.MaxHp, Player.Hp + Player.MaxHp * 0.45f);
                    Player.Chakra = Player.MaxChakra;
                    yield return new WaitForSeconds(2.5f);
                }
            }
            yield return StartCoroutine(Ending());
        }

        IEnumerator BossOnlyFlow()
        {
            Wave = bossOnly;
            yield return StartCoroutine(LoadWave(Wave));
            yield return StartCoroutine(BossFight(Wave));
            if (Wave == 2) { yield return StartCoroutine(Ending()); yield break; }
            Hud.Banner("БОСС ПОВЕРЖЕН", UIKit.Gold);
            Player.Anim.Mode = AnimMode.Victory;
            yield return new WaitForSeconds(2.5f);
            Finish(true);
        }

        IEnumerator LoadWave(int w)
        {
            GameManager.Inst.SetControlsVisible(false);
            if (Player != null)
            {
                // fade out of the previous location and show where we are going
                ScreenFader.FadeOut();
                Hud.Banner("СЛЕДУЮЩАЯ ЛОКАЦИЯ: " + WaveName(w).ToUpperInvariant(), UIKit.Paper);
                yield return new WaitForSecondsRealtime(0.6f);
            }
            Projectile.ClearAll(); Beam.ClearAll(); Hazard.ClearAll(); Fx.ClearAll();
            var arenaId = ArenaFor(w);
            var arena = Arena.Build(arenaId, GameManager.Inst.WorldRoot, WeatherSystem.Choose(arenaId, true));
            SpawnPlayer(arena.PlayerSpawn);
            Sfx.PlayTrack(w == 0 ? "forest" : w == 1 ? "city" : "moon", 1);
            SetWeatherAmbience(arena);
            WeatherSystem.PlanChange(arena, true);
            Hud.Inst.SetBoss(null);
            Hud.Inst.SetWave("Волна " + (w + 1) + "/3 — " + WaveName(w) + "   •   " + Names.Difficulty(Diff));
            CameraRig.Orbit(arena.PlayerSpawn + Vector3.up * 1.5f, 9f, 3f, 2.2f);
            ScreenFader.FadeIn();
            Hud.Banner(WaveName(w).ToUpperInvariant(), UIKit.Paper);
            if (w == 0 || Mode == BattleMode.BossOnly) Player.Shout(Player.Kit.IntroLine);
            yield return new WaitForSeconds(2.2f);
            CameraRig.EndCinematic();
            GameManager.Inst.SetControlsVisible(true);
        }

        IEnumerator FightUntilClear()
        {
            float spawnCd = 0f;
            while (true)
            {
                for (int i = enemies.Count - 1; i >= 0; i--) if (enemies[i] == null) enemies.RemoveAt(i);
                int alive = 0;
                foreach (var e in enemies) if (e.Alive) alive++;
                if (pending.Count == 0 && alive == 0) yield break;
                spawnCd -= Time.deltaTime;
                if (pending.Count > 0 && alive < GraphicsQuality.MaxActiveEnemies && spawnCd <= 0f)
                {
                    SpawnMinion(pending.Dequeue(), EdgePoint());
                    spawnCd = 0.7f;
                }
                yield return null;
            }
        }

        Vector3 EdgePoint()
        {
            // enter from the far end of the lane (sometimes from behind the player)
            float px = Player != null ? Player.transform.position.x : 0f;
            float side = px < 0f ? 1f : -1f;
            if (Random.value < 0.3f) side = -side;
            return new Vector3(side * (Arena.LaneHalf - 2f), 0f, Random.Range(-Arena.LaneDepth + 1f, Arena.LaneDepth - 1f));
        }

        Fighter SpawnMinion(MinionType t, Vector3 pos)
        {
            var f = Roster.CreateMinion(t, pos, Tune, Wave);
            f.Target = Player;
            f.FaceInstant(Player != null ? Player.transform.position : Vector3.zero);
            Fx.Smoke(f.Center, 1f, new Color(0.85f, 0.85f, 0.85f, 0.6f), 10);
            Sfx.Play(SfxId.Poof, 0.6f);
            f.OnDied += OnEnemyDied;
            enemies.Add(f);
            return f;
        }

        public void SpawnSummon(MinionType t, Vector3 pos)
        {
            var f = SpawnMinion(t, Arena.ClampInside(pos, 1.5f));
            f.MaxHp = f.Hp = f.MaxHp * 0.6f;
            summons.Add(f);
        }

        void OnEnemyDied(Fighter f, Fighter killer)
        {
            StartCoroutine(RemoveCorpse(f, f.IsBoss ? 6f : 2.5f));
        }

        IEnumerator RemoveCorpse(Fighter f, float delay)
        {
            yield return new WaitForSeconds(delay);
            if (f == null) yield break;
            Fx.Smoke(f.transform.position + Vector3.up * 0.3f, 1f, new Color(0.6f, 0.6f, 0.65f, 0.5f), 8);
            Destroy(f.gameObject);
        }

        IEnumerator BossFight(int index)
        {
            if (Mode == BattleMode.BossRush) Save.Data.bossesUnlocked |= 1 << index;
            Save.Write();
            Vector3 pos = Arena.Current.EnemySpawn;
            // entrance: the boss leaps in from behind the treeline and lands hard in front of the player
            Vector3 from = new Vector3(pos.x + 6f, 7f, Arena.LaneDepth + 6f);
            Boss = Roster.CreateBoss(index, from, Tune);
            Boss.Target = Player;
            Boss.Brain.Active = false;
            Boss.OnDied += OnEnemyDied;
            enemies.Add(Boss);
            GameManager.Inst.SetControlsVisible(false);
            Player.SetMove(Vector3.zero);
            Player.FaceInstant(pos);
            Sfx.PlayTrack(index == 0 ? "boss_serpent" : index == 1 ? "boss_thunder" : "boss_moon", 2);
            Boss.EnterCinematic();
            Boss.ForceAnimMode(AnimMode.Custom, PoseLib.AirUp);
            CameraRig.Cinematic(pos + Vector3.up * 1.5f, new Vector3(-0.35f, 0.1f, -1f), 9f, 1.2f, 1.4f);
            Sfx.Play(SfxId.Whoosh, 1f, 0.6f);
            for (float t = 0f; t < 0.9f; t += Time.deltaTime)
            {
                float u = t / 0.9f;
                Vector3 p = Vector3.Lerp(from, pos, u) + Vector3.up * Mathf.Sin(u * Mathf.PI) * 3f;
                Boss.transform.position = p;
                Boss.FaceInstant(Player.transform.position);
                if (u > 0.55f) Boss.ForceAnimMode(AnimMode.Custom, PoseLib.Slam);
                yield return null;
            }
            Boss.Teleport(pos); // re-syncs the CharacterController with the landing spot
            Boss.ForceAnimMode(AnimMode.Custom, PoseLib.LandSquash);
            Fx.Dust(pos, 2.2f, 16);
            Fx.Crack(pos, 3f);
            Fx.Ring(pos + Vector3.up * 0.1f, Boss.Kit.AuraColor, 0.5f, 7f, 0.5f, Quaternion.identity);
            Destructible.ImpactAt(pos, 5f, 60f, Boss);
            Sfx.Play(SfxId.HeavyHit, 1f, 0.6f);
            CameraRig.Shake(0.7f);
            Haptics.Heavy();
            yield return new WaitForSeconds(0.45f);
            Fx.Smoke(Boss.Center, 1.6f, new Color(0.4f, 0.35f, 0.45f, 0.5f), 10);
            Fx.Ring(pos + Vector3.up * 0.1f, Boss.Kit.AuraColor, 0.5f, 9f, 0.7f, Quaternion.identity);
            Sfx.Play(SfxId.Roar, 1f, 0.9f);
            CameraRig.Cinematic(Boss.Center + Vector3.up * 0.3f, KMath.Flat(Player.transform.position - pos).normalized + Vector3.up * 0.05f, 4.2f, 0.5f, 2.8f);
            Boss.EnterCinematic();
            Boss.PlayRoar();
            Hud.Banner(Boss.DisplayName.ToUpperInvariant(), Boss.Kit.AuraColor);
            Hud.Inst.SetBoss(Boss);
            Boss.Shout(Boss.Kit.IntroLine);
            Sfx.Voice("prepare_yourself", 0.9f);
            yield return new WaitForSeconds(2.8f);
            Sfx.Voice("fight");
            Boss.EndRoar();
            Boss.ExitCinematic();
            Boss.Brain.Active = true;
            CameraRig.EndCinematic();
            GameManager.Inst.SetControlsVisible(true);
            while (Boss != null && Boss.Alive) yield return null;
            // boss defeat: slow motion close-up, then the victory pose
            TimeFx.SlowMo(1.4f, 0.2f);
            if (Boss != null) CameraRig.Cinematic(Boss.Center, KMath.Flat(Player.transform.position - Boss.transform.position).normalized + Vector3.up * 0.2f, 5f, 1f, 1.6f);
            Haptics.Heavy();
            yield return new WaitForSecondsRealtime(1.5f);
            Player.Anim.Mode = AnimMode.Victory;
            Player.Anim.PlayMocap("Idle_FoldArms_Loop", 1f, 0f, false, 2.5f, 0.2f);
            CameraRig.Cinematic(Player.Center, Player.transform.forward + Vector3.up * 0.1f, 4f, 0.6f, 2f);
            // clean up summons
            foreach (var s in summons) if (s != null && s.Alive) s.Kill(Player);
            Hud.Banner("ПОБЕДА", UIKit.Gold);
            Sfx.Voice(Player.Hp >= Player.MaxHp * 0.999f ? "flawless_victory" : "you_win");
            Sfx.Jingle("victory");
            Player.Shout(Player.Kit.WinLine);
            Player.SetInvulnerable(4f);
            Projectile.ClearAll(); Hazard.ClearAll(); Beam.ClearAll();
            yield return new WaitForSeconds(2.2f);
        }

        /// <summary>Two announcer lines in sequence ("Round 1" ... "Fight!").</summary>
        IEnumerator Announce(string first, string second)
        {
            Sfx.Voice(first);
            yield return new WaitForSeconds(1.1f);
            Sfx.Voice(second);
        }

        IEnumerator Ending()
        {
            Running = false;
            GameManager.Inst.SetControlsVisible(false);
            var r = BuildResult(true);
            SaveResult(r);
            OnEnding?.Invoke(r);
            yield break;
        }

        IEnumerator DefeatRoutine()
        {
            GameManager.Inst.SetControlsVisible(false);
            Hud.Banner("ПОРАЖЕНИЕ", new Color(1f, 0.35f, 0.3f));
            Sfx.Voice("you_lose");
            yield return new WaitForSeconds(2.4f);
            OnDefeat?.Invoke();
        }

        /// <summary>Restart the current wave (or boss) with a fresh player.</summary>
        public void RetryWave()
        {
            StopAllCoroutines();
            ClearEnemies();
            if (Player != null) { Destroy(Player.gameObject); Player = null; }
            Running = true;
            Combat.AttackTokensInUse = 0;
            flow = StartCoroutine(Mode == BattleMode.BossOnly ? BossOnlyFlow() : RushFlow(Wave));
        }

        void ClearEnemies()
        {
            foreach (var e in enemies) if (e != null) Destroy(e.gameObject);
            enemies.Clear(); summons.Clear(); pending.Clear();
            Boss = null;
            Projectile.ClearAll(); Beam.ClearAll(); Hazard.ClearAll();
        }

        public void Cleanup()
        {
            StopAllCoroutines();
            ClearEnemies();
            if (Player != null) Destroy(Player.gameObject);
            if (dummy != null) Destroy(dummy.gameObject);
            Player = null; dummy = null;
            Running = false;
            if (Inst == this) Inst = null;
        }

        // ===================== Training =====================
        static void SetWeatherAmbience(Arena arena)
        {
            if (arena.WeatherFx != null) { var (n, v) = arena.WeatherFx.Ambience(); Sfx.SetAmbience(n, v); return; }
            switch (arena.Id)
            {
                case ArenaId.Forest: Sfx.SetAmbience("amb_wind", 0.35f); break;
                case ArenaId.MoonTemple: Sfx.SetAmbience("amb_wind", 0.25f); break;
                default: Sfx.SetAmbience(null, 0f); break;
            }
        }

        IEnumerator TrainingFlow()
        {
            var arena = Arena.Build(ArenaId.Dojo, GameManager.Inst.WorldRoot, WeatherSystem.Choose(ArenaId.Dojo, false));
            SetWeatherAmbience(arena);
            SpawnPlayer(arena.PlayerSpawn);
            Player.InfiniteChakra = true;
            Player.NoCooldowns = true;
            Sfx.PlayTrack("menu", 0);
            Hud.Inst.SetWave("Полигон");
            Hud.Inst.SetBoss(null);
            Hud.Inst.SetTraining(true);
            SpawnDummy(arena.EnemySpawn);
            Player.Shout(Player.Kit.IntroLine);
            GameManager.Inst.SetControlsVisible(true);
            yield break;
        }

        void SpawnDummy(Vector3 pos)
        {
            var look = new Look
            {
                skin = new Color(0.88f, 0.7f, 0.48f), top = new Color(0.86f, 0.68f, 0.46f), sleeve = new Color(0.86f, 0.68f, 0.46f), top2 = new Color(0.7f, 0.5f, 0.3f),
                pants = new Color(0.84f, 0.66f, 0.44f), shoes = new Color(0.7f, 0.52f, 0.33f), hair = new Color(0.86f, 0.68f, 0.46f), eye = new Color(0.6f, 0.45f, 0.3f),
                hairStyle = HairStyle.Hood, eyeGlow = 0f
            };
            dummy = FighterFactory.Make("Чучело", look, StanceKind.Brawler, RunStyle.Normal, new MinionKit(MinionType.ForestBandit), Team.Enemy, pos, Roster.FighterRoot, 500f, 5f);
            dummy.IsDummy = true;
            dummy.FaceInstant(Player.transform.position);
            Player.Target = dummy;
        }

        IEnumerator RespawnDummy()
        {
            var d = dummy;
            dummy = null;
            yield return new WaitForSeconds(2f);
            if (d != null) Destroy(d.gameObject);
            if (Mode == BattleMode.Training && Arena.Current != null) SpawnDummy(Arena.Current.EnemySpawn);
        }

        public void ToggleSparring()
        {
            if (dummy == null) return;
            var brain = dummy.GetComponent<AIBrain>();
            if (brain == null)
            {
                brain = dummy.gameObject.AddComponent<AIBrain>();
                brain.Setup(dummy, Archetype.Brawler, DifficultyTuning.For(Difficulty.Warrior));
                dummy.IsDummy = false;
                Hud.Popup("СПАРРИНГ: ВКЛ", new Color(1f, 0.8f, 0.5f));
            }
            else
            {
                Destroy(brain);
                dummy.Brain = null;
                dummy.IsDummy = true;
                dummy.SetMove(Vector3.zero);
                dummy.SetBlock(false);
                Hud.Popup("СПАРРИНГ: ВЫКЛ", new Color(0.8f, 0.8f, 0.8f));
            }
        }

        // ===================== results =====================
        public BattleResult BuildResult(bool cleared)
        {
            var s = Player != null ? Player.Stats : new FighterStats();
            var r = new BattleResult
            {
                Cleared = cleared, Mode = Mode, Character = Character, Difficulty = Diff, Time = runTime, MaxCombo = s.maxCombo, HitsTaken = s.hitsTaken,
                PerfectDodges = s.perfectDodges, Parries = s.parries, Finishers = s.finishers, Kills = s.kills, Absorbs = s.absorbs,
                DamageDealt = s.damageDealt, DamageTaken = s.damageTaken, AvgFps = PerfMonitor.AvgFps, WaveReached = Wave
            };
            float maxHp = Player != null ? Player.MaxHp : 600f;
            float score = 100f - (runTime / 60f) * (Mode == BattleMode.BossRush ? 2.2f : 6f) - r.DamageTaken / maxHp * 14f + r.MaxCombo * 0.6f + r.Finishers * 3f + r.PerfectDodges * 1.2f + r.Parries * 1.5f;
            r.Rank = score >= 85f ? 0 : score >= 68f ? 1 : score >= 50f ? 2 : 3;
            return r;
        }

        void SaveResult(BattleResult r)
        {
            if (Mode == BattleMode.Training) return;
            var rec = Save.Record(Character, Diff);
            if (r.Cleared && Mode == BattleMode.BossRush)
            {
                bool first = !rec.cleared;
                rec.cleared = true;
                if (rec.bestTime <= 0f || r.Time < rec.bestTime) rec.bestTime = r.Time;
                if (rec.bestRank < 0 || r.Rank < rec.bestRank) rec.bestRank = r.Rank;
                int next = (int)Diff + 1;
                if (next <= 2 && Save.Data.unlockedDifficulty < next)
                {
                    Save.Data.unlockedDifficulty = next;
                    r.Unlocked = "Открыта сложность: " + Names.Difficulty((Difficulty)next) + "!";
                }
                else if (first) r.Unlocked = "Первое прохождение за " + Roster.Name(Character) + "!";
            }
            rec.maxCombo = Mathf.Max(rec.maxCombo, r.MaxCombo);
            Save.Write();
        }

        public void Finish(bool cleared)
        {
            Running = false;
            var r = BuildResult(cleared);
            SaveResult(r);
            OnFinished?.Invoke(r);
        }
    }
}
