using System.Collections;
using UnityEngine;

namespace Kagegan
{
    // ======================================================================================
    // Secret boss — Kagura-07, the Cyber Oni (Neon City). Unlocked after the Moon Sovereign falls.
    // Every heavy move is telegraphed with a scan line or a ground ring; phase 2 (50% HP) adds
    // holo-doubles, a wider plasma fan and an overheating aura.
    // ======================================================================================
    public class OniKit : BossKit
    {
        public static readonly Color Neon = new Color(1f, 0.22f, 0.72f);
        public static readonly Color Cyan = new Color(0.25f, 0.95f, 1f);
        Move clawSlash;
        float nextSpark;

        public override void Setup(Fighter f)
        {
            base.Setup(f);
            AuraColor = Neon;
            f.HitColor = Cyan;
            IntroLine = "Кагура-07: «Цель найдена. Протокол уничтожения запущен.»";
            UltLine = "Кагура-07: «Протокол „Они“. Без ограничений.»";
            WinLine = "Кагура-07: «Цель устранена.»";
            var c1 = MoveLib.Jab(true, 8f, 1.25f); c1.FxColor = Cyan; c1.SlashEuler = new Vector3(0, 0, 25);
            var c2 = MoveLib.Hook(false, 9f); c2.FxColor = Neon;
            var c3 = MoveLib.Knee(10f); c3.FxColor = Cyan;
            var c4 = MoveLib.Kick(13f, 5f); c4.FxColor = Neon;
            LightChain = new[] { c1, c2, c3, c4 };
            AirChain = new[] { MoveLib.AirPunch(true, 8f), MoveLib.AirKick(9f), MoveLib.AirPunch(false, 8f) };
            AirFinisher = MoveLib.Slam(18f); AirFinisher.FxColor = Neon;
            // piston fist: long wind-up, armoured, knocks down
            Heavy = KitHelpers.Warned(new Move
            {
                Name = "piston", Windup = PoseLib.Crouch, Strike = PoseLib.Thrust, Startup = 0.26f, Active = 0.12f, Recovery = 0.36f,
                Damage = 21f, Kind = HitKind.Heavy, Lunge = 7f, Reach = new Vector3(0, 0, 0.5f), Knockback = 10f, Knockdown = true,
                FxColor = Neon, Range = 3f, HitSfx = SfxId.HeavyHit
            }, Neon);
            Launcher = MoveLib.Uppercut(12f, 12.5f); Launcher.FxColor = Cyan;
            DashAttack = Heavy.Copy(); DashAttack.Lunge = 11f;
            clawSlash = new Move
            {
                Name = "claw", Windup = PoseLib.HookRWind, Strike = PoseLib.HookR, Startup = 0.2f, Active = 0.12f, Recovery = 0.34f,
                Damage = 17f, Kind = HitKind.Heavy, Lunge = 5f, Reach = new Vector3(0, 0, 0.7f), Radius = 0.75f, Knockback = 7f, Knockdown = true,
                FxColor = Neon, SlashEuler = new Vector3(0, 0, -30), Range = 2.4f, HitSfx = SfxId.WireCrack
            };
            MoveLib.WireGrab(this, 22f, 9f, 6f);

            Techs = new[]
            {
                new Technique { Name = "Плазменный веер", Cost = 20f, Cooldown = 5f, Range = 14f, Color = Cyan, Use = PlasmaFan },
                new Technique { Name = "Лазер-резак", Cost = 30f, Cooldown = 9f, Range = 12f, Color = Neon, Use = LaserSweep },
                new Technique { Name = "Орбитальный удар", Cost = 30f, Cooldown = 11f, Range = 20f, Color = Neon, Use = OrbitalStrike },
                new Technique { Name = "Форсаж", Cost = 15f, Cooldown = 5f, Range = 16f, Color = Cyan, Use = Afterburner },
                new Technique { Name = "Голо-двойники", Cost = 35f, Cooldown = 16f, Range = 14f, Color = Cyan, Use = HoloDoubles },
                new Technique { Name = "Сброс перегрева", Cost = 20f, Cooldown = 7f, Range = 3.5f, Color = Neon, Offensive = false, Use = HeatVent },
            };
            Ult = new Technique { Name = "Протокол «Они»", Color = Neon, Use = ff => { ff.StartCoroutine(OniProtocol()); return true; } };
        }

        public override void Tick(float dt)
        {
            // phase 2: the frame overheats — sparks and neon static crawl over the body
            if (!Enraged || hiddenNow || Time.time < nextSpark) return;
            nextSpark = Time.time + (GraphicsQuality.Current >= GraphicsPreset.Medium ? 0.12f : 0.3f);
            Fx.Electric(F.Center + Random.insideUnitSphere * 0.5f, Random.value < 0.5f ? Neon : Cyan, 0.5f, 1);
        }

        // ---------- 0: plasma fan ----------
        bool PlasmaFan(Fighter f)
        {
            int n = Enraged ? 5 : 3;
            var m = MoveLib.Cast("plasmafan", PoseLib.Crouch, PoseLib.Throw, 0.4f, 0.35f, ff =>
            {
                Vector3 d = MoveLib.AimDir(ff);
                for (int i = 0; i < n; i++)
                {
                    float a = (i - (n - 1) * 0.5f) * 14f;
                    Vector3 v = Quaternion.Euler(0, a, 0) * d;
                    var p = Projectile.Spawn(ff, ProjStyle.Orb, ff.Center + Vector3.up * 0.2f + v * 0.8f, v * 19f, 11f, i % 2 == 0 ? Cyan : Neon, 0.8f);
                    p.Knockback = 4f;
                }
                Sfx.Play(SfxId.Energy, 1f, 1.3f);
                WorldLighting.Flash(ff.Center, Cyan * 1.5f, 6f, 0.2f);
            });
            m.OnStart = ff => KitHelpers.Telegraph(ff, Cyan, 0.4f);
            m.OnTick = (ff, t) => { if (ff.MoveTime < 0.4f) Fx.Glow(ff.Rig.BonePos(B.HandR), Cyan, 0.3f + ff.MoveTime, 0.05f); };
            f.StartMove(m);
            return true;
        }

        // ---------- 1: laser sweep (a scan line warns first) ----------
        bool LaserSweep(Fighter f)
        {
            if (!HasTarget) return false;
            f.StartCoroutine(LaserRoutine(f));
            return true;
        }

        IEnumerator LaserRoutine(Fighter f)
        {
            var t = f.Target;
            f.EnterCinematic();
            f.FaceInstant(t.transform.position);
            f.Anim.Play(Clip.Hold(PoseLib.Thrust, 0.1f, 1.9f, 0.2f));
            Fx.Scan(t.transform, t.Height, 0.9f, Neon, 0.6f);
            KitHelpers.Telegraph(f, Neon, 0.6f);
            Sfx.Play(SfxId.Charge, 0.9f, 1.4f);
            yield return new WaitForSeconds(0.6f);
            if (!f.Alive || t == null) { f.ExitCinematic(); yield break; }
            Vector3 toT = KMath.Flat(t.transform.position - f.transform.position).normalized;
            if (toT.sqrMagnitude < 0.01f) toT = f.transform.forward;
            float sweep = Enraged ? 80f : 60f, dur = Enraged ? 1.5f : 1.2f;
            var b = Beam.Fire(f, f.transform, new Vector3(0, 1.25f, 0.5f), 14f, 0.5f, dur, 4.5f, Neon);
            b.Absorbable = false;
            Sfx.Play(SfxId.WireZap, 1f, 0.7f);
            for (float k = 0f; k < dur && f.Alive; k += Time.deltaTime)
            {
                float a = Mathf.Lerp(-sweep * 0.5f, sweep * 0.5f, k / dur);
                f.transform.rotation = Quaternion.LookRotation(Quaternion.Euler(0, a, 0) * toT);
                yield return null;
            }
            f.ExitCinematic();
        }

        // ---------- 2: orbital strike — the satellite paints three spots ----------
        bool OrbitalStrike(Fighter f)
        {
            if (!HasTarget) return false;
            f.StartCoroutine(OrbitalRoutine(f));
            return true;
        }

        IEnumerator OrbitalRoutine(Fighter f)
        {
            f.Anim.Play(Clip.Hold(PoseLib.Seal, 0.1f, 0.8f, 0.25f));
            Sfx.Play(SfxId.WireZap, 0.8f, 1.2f);
            int n = Enraged ? 4 : 3;
            for (int i = 0; i < n; i++)
            {
                if (!HasTarget || !f.Alive) yield break;
                Vector3 p = Arena.ClampInside(f.Target.transform.position + KMath.Flat(Random.insideUnitSphere) * (i == 0 ? 0f : 1.5f), 1f);
                Fx.Scan(f.Target.transform, f.Target.Height, 0.8f, Cyan, 0.35f);
                Hazard.Spawn(f, HazardKind.LightningStrike, p, 2f, 1.1f, 20f, i % 2 == 0 ? Neon : Cyan);
                yield return new WaitForSeconds(0.5f);
            }
        }

        // ---------- 3: afterburner dash + claw ----------
        bool Afterburner(Fighter f)
        {
            if (!HasTarget) return false;
            f.StartCoroutine(AfterburnerRoutine(f));
            return true;
        }

        IEnumerator AfterburnerRoutine(Fighter f)
        {
            var t = f.Target;
            f.EnterCinematic();
            f.Anim.Play(Clip.Hold(PoseLib.Crouch, 0.05f, 0.3f, 0.1f));
            KitHelpers.Telegraph(f, Cyan, 0.3f);
            Fx.Glow(f.transform.position + Vector3.up * 0.3f - f.transform.forward * 0.4f, Cyan, 1.2f, 0.3f);
            yield return new WaitForSeconds(0.3f);
            if (t == null || !t.Alive || !f.Alive) { f.ExitCinematic(); yield break; }
            Vector3 from = f.transform.position;
            Vector3 side = Vector3.Cross(Vector3.up, KMath.Flat(t.transform.position - from).normalized) * (Random.value < 0.5f ? 1f : -1f);
            Vector3 to = Arena.ClampInside(t.transform.position + side * 1.4f, 1f);
            KitHelpers.Afterimage(f, Cyan, 4);
            Fx.Dust(from, 1f, 6);
            f.Teleport(to);
            f.FaceInstant(t.transform.position);
            Fx.Lightning(from + Vector3.up * 0.8f, to + Vector3.up * 0.8f, Cyan, 0.1f, 0.2f);
            Sfx.Play(SfxId.Whoosh, 1f, 1.3f);
            f.ExitCinematic();
            f.StartMove(clawSlash);
        }

        // ---------- 4: holo-doubles (phase 2 only) ----------
        bool HoloDoubles(Fighter f)
        {
            if (!Enraged || !HasTarget) return false;
            var t = f.Target;
            f.Anim.Play(Clip.Hold(PoseLib.Seal, 0.08f, 0.5f, 0.2f));
            Vector3 toT = KMath.Flat(t.transform.position - f.transform.position).normalized;
            Vector3 side = Vector3.Cross(Vector3.up, toT);
            for (int i = -1; i <= 1; i += 2)
            {
                Vector3 p = t.transform.position + side * i * 2.4f - toT * 0.6f;
                Fx.Scan(f.transform, f.Height, 0.8f, Cyan, 0.4f);
                KitHelpers.Decoy(f, p, StanceKind.Swift, RunStyle.Ninja, true, 5f, Cyan);
            }
            Sfx.Play(SfxId.Teleport, 1f, 1.4f);
            return true;
        }

        // ---------- 5: heat vent — pushes everyone off when cornered ----------
        bool HeatVent(Fighter f)
        {
            f.StartCoroutine(VentRoutine(f));
            return true;
        }

        IEnumerator VentRoutine(Fighter f)
        {
            f.EnterCinematic();
            f.Anim.Play(Clip.Hold(PoseLib.Crouch, 0.05f, 0.45f, 0.2f));
            KitHelpers.Mark(f.transform.position, Neon, 3.2f, 0.45f);
            Sfx.Play(SfxId.Hiss, 1f, 0.8f);
            yield return new WaitForSeconds(0.45f);
            if (!f.Alive) { f.ExitCinematic(); yield break; }
            Vector3 c = f.transform.position;
            Fx.Smoke(f.Center, 2f, new Color(0.95f, 0.9f, 1f, 0.55f), 14);
            Fx.Ring(c + Vector3.up * 0.1f, Neon, 0.5f, 6.5f, 0.45f, Quaternion.identity);
            WorldLighting.Flash(f.Center, Neon * 2f, 9f, 0.35f);
            Combat.AreaDamage(f, f.Team, c + Vector3.up * 0.8f, 3.2f, Enraged ? 16f : 12f, 10f, 3f, true, Neon, false);
            Sfx.Play(SfxId.Release, 1f, 0.9f);
            CameraRig.Shake(0.4f);
            f.ExitCinematic();
        }

        // ---------- ultimate: Протокол «Они» ----------
        IEnumerator OniProtocol()
        {
            var f = F;
            var t = f.Target;
            if (t == null) yield break;
            f.Shout(UltLine);
            f.EnterCinematic();
            f.SetInvulnerable(5f);
            Hud.Banner("ПРОТОКОЛ «ОНИ»", Neon);
            f.ForceAnimMode(AnimMode.Custom, PoseLib.Overhead);
            WorldLighting.Flash(f.Center, Neon * 2f, 14f, 0.6f);
            Sfx.Play(SfxId.Charge, 1f, 0.6f);
            // satellite lock-on: a ring of strikes closes in on the challenger
            Vector3 c0 = t.transform.position;
            for (int i = 0; i < 6; i++)
            {
                Vector3 p = Arena.ClampInside(c0 + Quaternion.Euler(0, i * 60f, 0) * Vector3.forward * 4.5f, 1f);
                Hazard.Spawn(f, HazardKind.LightningStrike, p, 1.8f, 1.1f + i * 0.1f, 18f, i % 2 == 0 ? Neon : Cyan);
            }
            Fx.Scan(t.transform, t.Height, 1f, Neon, 0.8f);
            yield return new WaitForSeconds(1f);
            if (t == null || !t.Alive || !f.Alive) { f.ForceAnimMode(AnimMode.Locomotion); f.ExitCinematic(); yield break; }
            // full-circle laser
            f.ForceAnimMode(AnimMode.Locomotion);
            f.Anim.Play(Clip.Hold(PoseLib.Thrust, 0.05f, 2.2f, 0.2f));
            var b = Beam.Fire(f, f.transform, new Vector3(0, 1.25f, 0.5f), 15f, 0.6f, 2.2f, 5f, Neon);
            b.Absorbable = false;
            Sfx.Play(SfxId.WireZap, 1f, 0.6f);
            Vector3 start = KMath.Flat(t.transform.position - f.transform.position).normalized;
            if (start.sqrMagnitude < 0.01f) start = f.transform.forward;
            for (float k = 0f; k < 2.2f && f.Alive; k += Time.deltaTime)
            {
                f.transform.rotation = Quaternion.LookRotation(Quaternion.Euler(0, k / 2.2f * 360f, 0) * start);
                if (Random.value < 0.15f) Fx.Electric(f.Center + Random.insideUnitSphere * 0.5f, Cyan, 0.6f, 1);
                yield return null;
            }
            if (!f.Alive) { f.ExitCinematic(); yield break; }
            // core overload
            Vector3 c = f.transform.position;
            KitHelpers.Mark(c, Neon, 4f, 0.5f);
            yield return new WaitForSeconds(0.5f);
            if (!f.Alive) { f.ExitCinematic(); yield break; }
            Fx.Explosion(c + Vector3.up * 0.8f, 4f, Neon);
            Fx.Crack(c, 3.5f);
            WorldLighting.Flash(c + Vector3.up * 2f, Neon * 3f, 20f, 0.7f);
            Destructible.ImpactAt(c, 6f, 250f, f);
            Combat.AreaDamage(f, f.Team, c + Vector3.up * 0.8f, 4f, 30f, 11f, 5f, true, Neon, false, HitKind.Ultimate);
            Sfx.Play(SfxId.Explosion, 1f, 0.8f);
            CameraRig.Shake(1f);
            Haptics.Heavy();
            yield return new WaitForSeconds(0.4f);
            f.ExitCinematic();
        }
    }

    public class OniBrain : BossBrain
    {
        public override void Setup(Fighter f, Archetype type, DifficultyTuning tune)
        {
            base.Setup(f, type, tune);
            Thresholds = new[] { 0.5f };
            PhaseLines = new[] { "Кагура-07: «Ограничители сняты. Перегрев допустим.»" };
            patternGap = 2f;
            PreferredRange = 2.2f;
        }

        protected override void OnPhase(int p)
        {
            var k = (OniKit)F.Kit;
            k.Enraged = true;
            k.AuraColor = OniKit.Cyan;
            if (k.Heavy != null) k.Heavy.Armor = true;
            if (k.DashAttack != null) k.DashAttack.Armor = true;
            F.DamageReduction = 0.2f;
            F.Rig.SetEmit(0.25f, OniKit.Neon);
            patternGap = 1.5f;
            // the city's power grid fails: neon night
            if (Arena.Current != null)
                Arena.Current.DimensionShift(new Color(0.75f, 0.45f, 0.9f), new Color(0.12f, 0.08f, 0.2f), new Color(0.1f, 0.05f, 0.16f), new Color(0.6f, 0.2f, 0.6f), 600f);
            for (int i = 0; i < 6; i++)
            {
                Vector3 p0 = KMath.Flat(Random.insideUnitSphere * 16f);
                Fx.Lightning(p0 + Vector3.up * 18f, p0, i % 2 == 0 ? OniKit.Neon : OniKit.Cyan, 0.2f, 0.35f);
            }
        }

        protected override bool DoPattern(float d)
        {
            float r = Random.value;
            if (Phase >= 1 && r < 0.18f && Tech(4)) return true;
            if (d < 2.3f && r < 0.4f && F.Kit.Launcher != null) { RunPattern(AirCombo()); return true; }
            if (d > 4f && r < 0.5f && Tech(3)) return true;
            if (d > 3f && d < 11f && r < 0.65f && Tech(1)) return true;
            if (r < 0.75f && Tech(2)) return true;
            if (d > 3.5f && Tech(0)) return true;
            if (d > 3f && d < 8f) { F.StartMove(F.Kit.DashAttack); return true; }
            return false;
        }

        protected override void Style(float d)
        {
            var t = F.Target;
            if (!F.CanAct || F.State == FState.Attack) return;
            // cornered and under pressure: vent heat
            if (d < 2.2f && (t.State == FState.Attack || F.Hp < F.MaxHp * 0.6f) && Random.value < 0.06f + Phase * 0.06f && Tech(5)) return;
            if (d > PreferredRange + 0.4f) moveDir = DirToTarget;
            if (t.State == FState.Block && d < 2f && Random.value < 0.4f) { F.Press(Cmd.Grab); return; }
            if (JustRecovered && d < 2.5f && Random.value < 0.45f) F.Press(Cmd.Heavy);
        }
    }
}
