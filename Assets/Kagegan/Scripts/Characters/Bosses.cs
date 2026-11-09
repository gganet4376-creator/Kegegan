using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Kagegan
{
    /// <summary>
    /// Shared boss kit pieces: every heavy attack is telegraphed (flash, ground ring, charge sound) and
    /// the brain flips <see cref="Enraged"/> at 50% HP to unlock the phase-2 versions of the moves.
    /// </summary>
    public abstract class BossKit : Kit
    {
        public bool Enraged;
        protected bool hiddenNow;

        protected void Hide(bool h)
        {
            hiddenNow = h;
            KitHelpers.SetHidden(F, h);
        }

        public override void OnDeath() { if (hiddenNow) Hide(false); }

        /// <summary>The target is a valid, reachable opponent.</summary>
        protected bool HasTarget => F.Target != null && F.Target.Alive;

        protected static bool Dodging(Fighter t) => t.Invulnerable || t.State == FState.Dodge;

        /// <summary>Lifts the victim into the air (used by "lift on the tail" / gravity lift), unblockable.</summary>
        protected void Lift(Fighter t, float dmg, float launch, Color c)
        {
            var h = KitHelpers.Hit(dmg, 2f, launch, HitKind.Technique, c, false, true);
            KitHelpers.Strike(F, t, h);
        }
    }

    // ======================================================================================
    // Boss 1 — Jakura, the Serpent Sage (Forest of Destruction)
    // ======================================================================================
    public class SerpentKit : BossKit
    {
        static readonly Color Purple = new Color(0.65f, 0.35f, 0.85f);
        static readonly Color Venom = new Color(0.45f, 0.85f, 0.3f);
        Move bindThrow, backFang;

        public override void Setup(Fighter f)
        {
            base.Setup(f);
            AuraColor = Purple;
            f.HitColor = new Color(0.8f, 0.6f, 1f);
            IntroLine = "Дзякура: «Какое крепкое тело... Оно станет моим.»";
            UltLine = "Дзякура: «Змеиное затмение!»";
            var fang = new Move
            {
                Name = "fang", Windup = PoseLib.StanceSerpent, Strike = PoseLib.Thrust, Startup = 0.16f, Active = 0.1f, Recovery = 0.25f,
                Damage = 10f, Bone = HitBone.HandR, Reach = new Vector3(0, 0, 0.7f), Radius = 0.45f, Lunge = 4f, Knockback = 3f, Range = 2.3f, FxColor = Purple
            };
            LightChain = new[] { MoveLib.Palm(true, 7f), MoveLib.Palm(false, 7f), fang, MoveLib.Kick(10f, 5f) };
            AirChain = new[] { MoveLib.AirPunch(true, 7f), MoveLib.AirKick(8f) };
            AirFinisher = MoveLib.Slam(14f);
            Heavy = KitHelpers.Warned(MoveLib.Sweep(16f), Purple);
            Launcher = MoveLib.Uppercut(10f, 11f);
            DashAttack = KitHelpers.Warned(fang.Copy(), Purple, 0.1f); DashAttack.Lunge = 11f; DashAttack.Kind = HitKind.Heavy; DashAttack.Damage = 16f; DashAttack.Knockdown = true;
            backFang = fang.Copy(); backFang.Damage = 18f; backFang.Kind = HitKind.Heavy; backFang.Knockdown = true; backFang.Startup = 0.3f; backFang.Lunge = 5f;
            MoveLib.WireGrab(this, 22f, 9f, 6f);
            bindThrow = MoveLib.ThrowMove(20f, 11f, 8f);
            bindThrow.Startup = 0.5f;

            Techs = new[]
            {
                new Technique { Name = "Змеиные клыки", Cost = 25f, Cooldown = 5f, Range = 14f, Color = Purple, Use = Fangs },
                new Technique { Name = "Ядовитое облако", Cost = 30f, Cooldown = 11f, Range = 20f, Color = Venom, Use = Poison },
                new Technique { Name = "Сброс кожи", Cost = 25f, Cooldown = 12f, Range = 12f, Color = Purple, Use = ShedSkin },
                new Technique { Name = "Удар хвостом", Cost = 15f, Cooldown = 6f, Range = 3.4f, Color = Purple, Use = TailSweep },
                new Technique { Name = "Удушающие кольца", Cost = 30f, Cooldown = 13f, Range = 11f, Color = Purple, Use = Bind },
                new Technique { Name = "Призыв", Cost = 35f, Cooldown = 24f, Range = 30f, Offensive = false, Use = Summon },
            };
            Ult = new Technique { Name = "Змеиное затмение", Color = Purple, Use = ff => { ff.StartCoroutine(EclipseUlt()); return true; } };
        }

        public override void Tick(float dt)
        {
            // phase 2: dragon marks glow on the body
            if (Enraged && Random.value < 0.15f && !hiddenNow)
                Fx.Glyph(F.transform.position + Vector3.up * Random.Range(0.4f, F.Height) + Random.insideUnitSphere * 0.25f, Vector3.up * 0.4f, Purple, 0.3f, 0.5f);
        }

        // ---------- 0: snake fangs from both sides ----------
        bool Fangs(Fighter f)
        {
            if (!HasTarget) return false;
            f.StartCoroutine(FangsRoutine(f));
            return true;
        }

        IEnumerator FangsRoutine(Fighter f)
        {
            var t = f.Target;
            f.Anim.Play(Clip.Hold(PoseLib.Seal, 0.08f, 0.5f, 0.2f));
            Vector3 c = t.transform.position;
            Vector3 side = Vector3.Cross(Vector3.up, KMath.Flat(c - f.transform.position).normalized);
            if (side.sqrMagnitude < 0.01f) side = Vector3.right;
            Vector3 a = Arena.ClampInside(c + side * 5f, 1f), b = Arena.ClampInside(c - side * 5f, 1f);
            // the ground cracks where the snakes will burst out
            KitHelpers.Mark(a, Purple, 0.9f, 0.55f);
            KitHelpers.Mark(b, Purple, 0.9f, 0.55f);
            Sfx.Play(SfxId.Hiss, 1f, 0.9f);
            yield return new WaitForSeconds(0.55f);
            if (!f.Alive || t == null) yield break;
            foreach (var p0 in new[] { a, b })
            {
                Fx.Crack(p0, 1.2f);
                Fx.Debris(p0, new Color(0.35f, 0.3f, 0.25f), 6, 4f);
                Vector3 d = KMath.Flat(t.transform.position - p0).normalized;
                var p = Projectile.Spawn(f, ProjStyle.Snake, p0 + Vector3.up * 0.4f, d * 12f, Enraged ? 15f : 12f, new Color(0.45f, 0.3f, 0.55f), 1.5f);
                p.GroundHug = true; p.Homing = t; p.HomingRate = 0.6f; p.Life = 1.6f; p.Power = 2f; p.Knockback = 3f;
            }
            Sfx.Play(SfxId.Hiss, 1f, 1.1f);
            // phase 2: bone spikes trail behind the fangs
            if (Enraged)
            {
                yield return new WaitForSeconds(0.25f);
                for (int i = 0; i < 4 && t != null; i++)
                    Hazard.Spawn(f, HazardKind.BoneSpikes, Arena.ClampInside(t.transform.position + Random.insideUnitSphere * 2.5f, 1f), 1.4f, 0.75f + i * 0.15f, 14f, Purple);
            }
        }

        // ---------- 1: poison cloud ----------
        bool Poison(Fighter f)
        {
            if (!HasTarget) return false;
            f.Anim.Play(Clip.Hold(PoseLib.Overhead, 0.15f, 0.3f, 0.2f));
            Vector3 c = f.Target.transform.position;
            int n = Enraged ? 4 : 3;
            for (int i = 0; i < n; i++)
                Hazard.Spawn(f, HazardKind.PoisonCloud, Arena.ClampInside(c + Quaternion.Euler(0, i * 360f / n, 0) * Vector3.forward * 2.6f, 1f), 2.4f, 0.9f, 24f, new Color(0.45f, 0.75f, 0.3f), 4.5f);
            Sfx.Play(SfxId.Hiss, 1f, 0.6f);
            return true;
        }

        // ---------- 2: shed skin — leaves a decoy, reappears behind ----------
        bool ShedSkin(Fighter f)
        {
            if (!HasTarget) return false;
            f.StartCoroutine(ShedRoutine(f));
            return true;
        }

        IEnumerator ShedRoutine(Fighter f)
        {
            var t = f.Target;
            f.EnterCinematic();
            f.SetInvulnerable(1.6f);
            Vector3 at = f.transform.position;
            Fx.Smoke(f.Center, 0.9f, new Color(0.55f, 0.5f, 0.45f, 0.6f), 10);
            Sfx.Play(SfxId.Hiss, 1f, 0.7f);
            KitHelpers.Decoy(f, at, StanceKind.Serpent, RunStyle.Glide, false, 3.2f, Purple);
            Hide(true);
            f.Teleport(KitHelpers.Behind(f, t, 3.2f));
            yield return new WaitForSeconds(0.85f);
            if (!f.Alive || t == null || !t.Alive) { Hide(false); f.ExitCinematic(); yield break; }
            // a clear tell before the strike from behind: hiss and a glint
            Vector3 p = t.transform.position - KMath.Flat(t.transform.position - f.transform.position).normalized * 1.3f;
            if (p == t.transform.position) p = t.transform.position - t.transform.forward * 1.3f;
            KitHelpers.Mark(p, Purple, 0.7f, 0.35f);
            Sfx.Play(SfxId.Hiss, 1f, 1.4f);
            yield return new WaitForSeconds(0.35f);
            f.Teleport(p);
            f.FaceInstant(t.transform.position);
            Hide(false);
            Fx.Burst(f.Center, Purple, 1.2f, 0.25f);
            f.ExitCinematic();
            f.StartMove(backFang);
        }

        // ---------- 3: tail sweep ----------
        bool TailSweep(Fighter f)
        {
            var m = new Move
            {
                Name = "tail", Windup = PoseLib.Crouch, Strike = PoseLib.Sweep, Startup = 0.5f, Active = 0.12f, Recovery = 0.4f,
                Damage = 0f, SlashFx = false, Lunge = 0f, Range = 3.4f, Sfx = SfxId.Whoosh, NoTracking = true
            };
            m.OnStart = ff => KitHelpers.Telegraph(ff, Purple, 0.5f, 3.4f);
            m.OnActive = ff =>
            {
                Fx.Ring(ff.transform.position + Vector3.up * 0.25f, Purple, 0.5f, 3.6f, 0.3f, Quaternion.identity);
                Fx.Dust(ff.transform.position, 2f, 14);
                Combat.AreaDamage(ff, ff.Team, ff.transform.position + Vector3.up * 0.4f, 3.4f, Enraged ? 20f : 16f, 7f, 2f, true, Purple, false, HitKind.Heavy);
                CameraRig.Shake(0.3f);
            };
            f.StartMove(m);
            return true;
        }

        // ---------- 4: life-drain bind (mash to break free) ----------
        bool Bind(Fighter f)
        {
            f.StartMove(MoveLib.Cast("bind", PoseLib.PalmRWind, PoseLib.PalmR, 0.5f, 0.5f, ff =>
            {
                var p = Projectile.Spawn(ff, ProjStyle.Snake, ff.Rig.BonePos(B.HandR), MoveLib.AimDir(ff) * 18f, 4f, new Color(0.55f, 0.35f, 0.65f), 1f);
                p.Unblockable = true; p.Absorbable = false; p.Power = 1f; p.Knockback = 0f; p.Hitstun = 1f; p.Life = 0.8f;
                p.OnHitFighter = (pr, v) =>
                {
                    var o = pr.Owner;
                    if (o == null || !o.Alive || !v.Alive) return;
                    o.BeginThrow(v, bindThrow);
                    o.StartCoroutine(Drain(o, v));
                    Sfx.Play(SfxId.Hiss, 1f, 0.8f);
                    if (v.IsPlayer) Hud.Popup("КОЛЬЦА ЗМЕЯ!  Вырывайся", new Color(0.8f, 0.5f, 1f));
                };
                Sfx.Play(SfxId.Hiss, 0.8f, 1.3f);
            }));
            return true;
        }

        IEnumerator Drain(Fighter o, Fighter v)
        {
            // the coils squeeze life out of the victim and into Jakura for as long as the hold lasts
            while (o != null && o.Alive && v != null && v.Alive && v.State == FState.Grabbed && v.GrabbedBy == o)
            {
                float d = 9f * Time.deltaTime * o.DamageMul;
                v.Hp = Mathf.Max(1f, v.Hp - d);
                o.Hp = Mathf.Min(o.MaxHp, o.Hp + d * 1.5f);
                if (Random.value < 0.3f) Fx.HackStream(v.Center, o.Center, Venom, 1, 0.35f);
                yield return null;
            }
        }

        bool Summon(Fighter f)
        {
            if (Battle.Inst == null) return false;
            int free = Mathf.Max(1, Battle.Inst.Tune.bossSummons) - Battle.Inst.SummonCount;
            if (free <= 0) return false;
            f.Anim.Play(Clip.Hold(PoseLib.Seal, 0.1f, 0.5f, 0.2f));
            for (int i = 0; i < free; i++)
            {
                Vector3 p = f.transform.position + f.transform.right * (i == 0 ? 2.5f : -2.5f);
                Fx.Smoke(p + Vector3.up, 1.2f, new Color(0.6f, 0.5f, 0.7f, 0.6f), 12);
                Battle.Inst.SpawnSummon(MinionType.SnakeAdept, p);
            }
            Sfx.Play(SfxId.Poof, 1f, 0.8f);
            f.Shout("Дзякура: «Слуги мои, ко мне!»");
            return true;
        }

        // ---------- ultimate: Змеиное затмение ----------
        IEnumerator EclipseUlt()
        {
            var f = F;
            var t = f.Target;
            if (t == null) yield break;
            f.Shout(UltLine);
            f.EnterCinematic();
            f.SuperArmor = true;
            f.SetInvulnerable(1.2f);
            f.PlayRoar();
            Hud.Banner("ЗМЕИНОЕ ЗАТМЕНИЕ", Purple);
            if (Arena.Current != null)
                Arena.Current.DimensionShift(new Color(0.55f, 0.4f, 0.7f), new Color(0.18f, 0.08f, 0.25f), new Color(0.12f, 0.06f, 0.16f), new Color(0.5f, 0.3f, 0.7f), 7f);
            Sfx.Play(SfxId.Roar, 1f, 0.55f);
            CameraRig.Shake(0.5f);
            // the ground splits around the arena
            for (int i = 0; i < 6; i++)
            {
                Vector3 p = Arena.ClampInside(t.transform.position + Quaternion.Euler(0, i * 60f, 0) * Vector3.forward * 7f, 1f);
                Fx.Crack(p, 2.2f);
                Fx.Debris(p, new Color(0.3f, 0.25f, 0.2f), 5, 5f);
            }
            yield return new WaitForSeconds(0.6f);
            f.EndRoar();
            // giant snakes tear out of the cracks towards the target
            for (int s = 0; s < 4 && t != null; s++)
            {
                Vector3 from = Arena.ClampInside(t.transform.position + Quaternion.Euler(0, s * 90f + 45f, 0) * Vector3.forward * 9f, 1f);
                Vector3 dir = KMath.Flat(t.transform.position - from).normalized;
                for (int i = 0; i < 6; i++)
                    Hazard.Spawn(f, HazardKind.SnakeBurst, Arena.ClampInside(from + dir * (i * 2.4f), 1f), 1.7f, 0.9f + i * 0.1f + s * 0.25f, 18f, new Color(0.6f, 0.4f, 0.75f));
                Sfx.Play(SfxId.Hiss, 1f, 0.6f + s * 0.1f);
                yield return new WaitForSeconds(0.25f);
            }
            yield return new WaitForSeconds(1.2f);
            // the tail rises under the target and throws them up
            if (t != null && t.Alive)
            {
                Vector3 c = t.transform.position;
                KitHelpers.Mark(c, Purple, 2.2f, 0.9f);
                Sfx.Play(SfxId.Crumble, 1f, 0.8f);
                yield return new WaitForSeconds(0.9f);
                Fx.Crack(c, 2.5f);
                Fx.Debris(c, new Color(0.35f, 0.3f, 0.25f), 10, 7f);
                CameraRig.Shake(0.5f);
                if (t.Alive && !Dodging(t) && KMath.Flat(t.transform.position - c).magnitude < 2.4f) Lift(t, 16f, 13f, Purple);
            }
            // a wave of poison rolls out from Jakura
            Vector3 o = f.transform.position;
            for (int w = 1; w <= 3; w++)
            {
                for (int i = 0; i < 6 + w * 2; i++)
                    Hazard.Spawn(f, HazardKind.PoisonCloud, Arena.ClampInside(o + Quaternion.Euler(0, i * 360f / (6 + w * 2) + w * 15f, 0) * Vector3.forward * (w * 3.2f), 1f), 1.8f, 0.6f, 12f, new Color(0.45f, 0.75f, 0.3f), 2.5f);
                Sfx.Play(SfxId.Hiss, 0.8f, 0.5f);
                yield return new WaitForSeconds(0.35f);
            }
            f.SuperArmor = false;
            f.ExitCinematic();
        }
    }

    public class SerpentBrain : BossBrain
    {
        public override void Setup(Fighter f, Archetype type, DifficultyTuning tune)
        {
            base.Setup(f, type, tune);
            Thresholds = new[] { 0.5f };
            PhaseLines = new[] { "Дзякура: «Ты разбудил дракона под моей кожей!»" };
            patternGap = 2.5f;
        }

        protected override void OnPhase(int p)
        {
            ((BossKit)F.Kit).Enraged = true;
            Destructible.ImpactAt(F.transform.position, 8f, 999f, F);
            F.DamageReduction = 0.15f;
            F.Rig.SetEmit(0.15f, new Color(0.65f, 0.35f, 0.85f));
            patternGap = 1.8f;
        }

        protected override bool DoPattern(float d)
        {
            float r = Random.value;
            if (d < 2f && r < 0.15f) { moveDir = -DirToTarget; F.SetMove(moveDir); F.Press(Cmd.Dodge); retreatUntil = Time.time + 0.5f; return true; }
            if (d < 3.2f && r < 0.4f && Tech(3)) return true;
            if (Phase >= 1 && r < 0.2f && Tech(5)) return true;
            if (d < 6f && r < 0.5f && Tech(2)) return true;
            if (r < 0.6f && Tech(1)) return true;
            if (d > 2.5f && d < 10f && r < 0.75f && Tech(4)) return true;
            if (d > 3f && Tech(0)) return true;
            if (d > 4f && d < 9f) { F.StartMove(F.Kit.DashAttack); return true; }
            return false;
        }

        protected override void Style(float d)
        {
            if (JustRecovered && d < 3.5f && Random.value < 0.6f)
            {
                moveDir = -DirToTarget + Vector3.Cross(Vector3.up, DirToTarget) * strafeSign;
                F.SetMove(moveDir);
                F.Press(Cmd.Dodge);
                retreatUntil = Time.time + 0.7f;
                return;
            }
            if (F.State != FState.Idle || Time.time < retreatUntil) return;
            var t = F.Target;
            if (d < 2.8f && t.State != FState.Hitstun && t.State != FState.Juggle && Random.value < (Phase >= 1 ? 0.06f : 0.14f))
            {
                retreatUntil = Time.time + Random.Range(0.4f, 0.8f);
                return;
            }
            if (d > 3.5f && d < 8f)
            {
                if (Random.value < 0.04f) strafeSign = -strafeSign;
                moveDir = (Vector3.Cross(Vector3.up, DirToTarget) * strafeSign * 0.85f + DirToTarget * (Phase >= 1 ? 0.6f : 0.35f)).normalized;
            }
        }
    }

    // ======================================================================================
    // Boss 2 — Raiden, the Thunder Avenger (Ruined City)
    // ======================================================================================
    public class ThunderKit : BossKit
    {
        static readonly Color Bolt = new Color(0.55f, 0.75f, 1f);
        static readonly Color Red = new Color(1f, 0.22f, 0.25f);
        float counterUntil, nextSkyBolt;
        Move counterMove, flashSlash;
        public bool StormArmor;

        public override void Setup(Fighter f)
        {
            base.Setup(f);
            AuraColor = Red;
            f.HitColor = Bolt;
            IntroLine = "Райдэн: «Ты лишь очередное препятствие.»";
            UltLine = "Райдэн: «Суд громовержца!»";
            var slash1 = MoveLib.Jab(true, 8f, 1.2f); slash1.Reach = new Vector3(0, 0, 0.55f); slash1.FxColor = Bolt; slash1.SlashEuler = new Vector3(0, 0, 30);
            var slash2 = MoveLib.Hook(false, 9f); slash2.Reach = new Vector3(0, 0, 0.5f); slash2.FxColor = Bolt;
            var slash3 = MoveLib.Kick(10f, 4f); slash3.FxColor = Bolt;
            LightChain = new[] { slash1, slash2, slash3, MoveLib.Elbow(14f) };
            AirChain = new[] { MoveLib.AirPunch(true, 8f), MoveLib.AirKick(8f), MoveLib.AirPunch(false, 8f) };
            AirFinisher = MoveLib.Slam(18f); AirFinisher.FxColor = Bolt;
            Heavy = KitHelpers.Warned(new Move
            {
                Name = "thrust", Windup = PoseLib.Crouch, Strike = PoseLib.Thrust, Startup = 0.22f, Active = 0.12f, Recovery = 0.32f,
                Damage = 20f, Kind = HitKind.Heavy, Lunge = 8f, Reach = new Vector3(0, 0, 0.5f), Knockback = 9f, Knockdown = true, FxColor = Bolt, Range = 3f,
                Mocap = "Sword_Dash", MocapContact = MocapLibrary.DashContact
            }, Red);
            Launcher = MoveLib.Uppercut(12f, 12.5f); Launcher.FxColor = Bolt;
            DashAttack = Heavy.Copy(); DashAttack.Lunge = 12f;
            counterMove = Heavy.Copy(); counterMove.OnStart = null; counterMove.Startup = 0.08f; counterMove.Unblockable = true; counterMove.Damage = 24f;
            flashSlash = new Move
            {
                Name = "redslash", Windup = PoseLib.HookRWind, Strike = PoseLib.HookR, Startup = 0.24f, Active = 0.12f, Recovery = 0.35f,
                Damage = 18f, Kind = HitKind.Heavy, Lunge = 4f, Reach = new Vector3(0, 0, 0.7f), Radius = 0.75f, Knockback = 7f, Knockdown = true,
                FxColor = Red, SlashEuler = new Vector3(0, 0, -35), Range = 2.4f, HitSfx = SfxId.Thunder
            };
            MoveLib.WireGrab(this, 24f, 10f, 6f);

            Techs = new[]
            {
                new Technique { Name = "Алый разряд", Cost = 20f, Cooldown = 5f, Range = 14f, Color = Red, Use = RedFlash },
                new Technique { Name = "Три удара", Cost = 30f, Cooldown = 8f, Range = 20f, Color = Bolt, Use = TripleStrike },
                new Technique { Name = "Глифы-ловушки", Cost = 25f, Cooldown = 14f, Range = 14f, Color = Red, Use = Glyphs },
                new Technique { Name = "Контрстойка", Cost = 15f, Cooldown = 7f, Range = 4f, Color = Bolt, Use = CounterStance },
                new Technique { Name = "Небесное копьё", Cost = 35f, Cooldown = 9f, Range = 22f, Color = Bolt, Use = SkySpear },
                new Technique { Name = "Мгновенный шаг", Cost = 15f, Cooldown = 4f, Range = 20f, Offensive = false, Use = FlashStep },
            };
            Ult = new Technique { Name = "Суд громовержца", Color = Red, Use = ff => { ff.StartCoroutine(JudgementUlt()); return true; } };
        }

        public override void Tick(float dt)
        {
            if (StormArmor && Random.value < 0.25f)
                Fx.Lightning(F.Center + Random.insideUnitSphere * 0.6f, F.Center + Random.insideUnitSphere * 0.9f, Red, 0.04f, 0.08f);
            if (Time.time < counterUntil && Random.value < 0.5f) Fx.Trail(F.Center + Random.insideUnitSphere * 0.6f, Bolt, 0.3f);
            // Буревестник: the dark sky keeps striking near the challenger
            if (Enraged && HasTarget && Time.time >= nextSkyBolt && F.State != FState.Cinematic)
            {
                nextSkyBolt = Time.time + Random.Range(4.5f, 7f);
                Hazard.Spawn(F, HazardKind.LightningStrike, Arena.ClampInside(F.Target.transform.position + KMath.Flat(Random.insideUnitSphere) * 2.5f, 1f), 1.6f, 1f, 12f, Red);
            }
        }

        /// <summary>Phase 2: every dash leaves an electric crack that bursts a moment later.</summary>
        void DashTrail(Vector3 from, Vector3 to)
        {
            if (!Enraged) return;
            Vector3 d = KMath.Flat(to - from);
            int n = Mathf.Clamp(Mathf.RoundToInt(d.magnitude / 2.2f), 1, 5);
            for (int i = 0; i < n; i++)
            {
                Vector3 p = from + d * ((i + 0.5f) / n);
                Fx.Crack(p, 1f);
                Hazard.Spawn(F, HazardKind.LightningStrike, p, 1.1f, 0.7f + i * 0.08f, 8f, Red);
            }
        }

        public override bool Intercept(ref HitInfo h)
        {
            if (Time.time >= counterUntil || h.Attacker == null || h.Proj != null) return false;
            counterUntil = 0f;
            var atk = h.Attacker;
            Fx.Parry(h.Point);
            Sfx.Play(SfxId.Parry, 1f, 1.2f);
            Vector3 from = F.transform.position;
            F.Teleport(atk.transform.position - atk.transform.forward * 1.2f);
            Fx.Lightning(from + Vector3.up, F.Center, Red, 0.08f, 0.2f);
            DashTrail(from, F.transform.position);
            F.FaceInstant(atk.transform.position);
            F.StartMove(counterMove);
            F.Shout("Райдэн: «Медленно.»");
            if (atk.IsPlayer) Hud.Popup("КОНТРАТАКА ВРАГА", Bolt);
            return true;
        }

        // after a parry Raiden answers with a stunning bolt
        public override void OnParry(Fighter attacker)
        {
            if (attacker == null || !attacker.Alive) return;
            F.StartCoroutine(ParryBolt(attacker));
        }

        IEnumerator ParryBolt(Fighter a)
        {
            yield return new WaitForSeconds(0.15f);
            if (a == null || !a.Alive || !F.Alive) yield break;
            Fx.Lightning(F.Rig.BonePos(B.HandR), a.Center, Bolt, 0.1f, 0.25f);
            Fx.Electric(a.Center, Bolt, 1f, 3);
            Sfx.Play(SfxId.Thunder, 0.8f, 1.4f);
            var h = KitHelpers.Hit(8f, 0.5f, 0f, HitKind.Technique, Bolt, false, true);
            if (KitHelpers.Strike(F, a, h) == HitResult.Hit && a.Alive) a.Stun(1f);
            if (a.IsPlayer) Hud.Popup("ОГЛУШЁН МОЛНИЕЙ", Bolt);
        }

        // ---------- 0: red lightning teleport + slash ----------
        bool RedFlash(Fighter f)
        {
            if (!HasTarget) return false;
            f.StartCoroutine(RedFlashRoutine(f));
            return true;
        }

        IEnumerator RedFlashRoutine(Fighter f)
        {
            var t = f.Target;
            f.EnterCinematic();
            f.Anim.Play(Clip.Hold(PoseLib.StanceThunder, 0.05f, 0.35f, 0.1f));
            KitHelpers.Telegraph(f, Red, 0.35f);
            for (int i = 0; i < 4; i++) Fx.Lightning(f.Center, f.Center + Random.insideUnitSphere * 1.2f, Red, 0.05f, 0.1f);
            yield return new WaitForSeconds(0.35f);
            if (t == null || !t.Alive || !f.Alive) { f.ExitCinematic(); yield break; }
            Vector3 from = f.transform.position;
            Vector3 to = t.transform.position + KMath.Flat(t.transform.position - from).normalized * 1.3f;
            Fx.Lightning(from + Vector3.up, to + Vector3.up, Red, 0.16f, 0.3f);
            Fx.Smoke(f.Center, 0.5f, new Color(1f, 0.6f, 0.6f, 0.4f), 5);
            f.Teleport(to);
            f.FaceInstant(t.transform.position);
            DashTrail(from, to);
            Sfx.Play(SfxId.Thunder, 0.9f, 1.5f);
            f.ExitCinematic();
            f.StartMove(flashSlash);
        }

        // ---------- 1: three strikes on marked spots ----------
        bool TripleStrike(Fighter f)
        {
            if (!HasTarget) return false;
            f.StartCoroutine(TripleRoutine(f));
            return true;
        }

        IEnumerator TripleRoutine(Fighter f)
        {
            f.Anim.Play(Clip.Hold(PoseLib.Overhead, 0.15f, 0.9f, 0.3f));
            Sfx.Play(SfxId.Thunder, 0.7f, 0.7f);
            for (int i = 0; i < 3; i++)
            {
                if (!HasTarget || !f.Alive) yield break;
                Hazard.Spawn(f, HazardKind.LightningStrike, f.Target.transform.position, 2.2f, 0.9f, 22f, i == 2 ? Red : Bolt);
                yield return new WaitForSeconds(0.45f);
            }
        }

        // ---------- 2: glyph traps ----------
        bool Glyphs(Fighter f)
        {
            if (!HasTarget) return false;
            f.Anim.Play(Clip.Hold(PoseLib.Seal, 0.08f, 0.4f, 0.2f));
            Vector3 c = f.Target.transform.position;
            Vector3 fwd = KMath.Flat(c - f.transform.position).normalized;
            int n = Enraged ? 4 : 3;
            for (int i = 0; i < n; i++)
            {
                Vector3 p = Arena.ClampInside(c + Quaternion.Euler(0, i * 360f / n + 30f, 0) * fwd * 2.4f, 1f);
                GlyphTrap.Place(f, p, Red, Enraged ? 14f : 10f);
            }
            Sfx.Play(SfxId.WireZap, 0.9f, 0.7f);
            return true;
        }

        bool CounterStance(Fighter f)
        {
            counterUntil = Time.time + 1.1f;
            f.Anim.Play(Clip.Hold(PoseLib.StanceThunder, 0.05f, 1f, 0.1f));
            return true;
        }

        // ---------- 4: sky spear ----------
        bool SkySpear(Fighter f)
        {
            var m = MoveLib.Cast("skyspear", PoseLib.Overhead, PoseLib.Throw, 0.6f, 0.4f, ff =>
            {
                Vector3 d = MoveLib.AimDir(ff);
                var p = Projectile.Spawn(ff, ProjStyle.Spear, ff.Center + Vector3.up * 0.8f + d, d * 26f, 22f, Bolt, 1.6f);
                p.Absorbable = false; p.Power = 4f; p.Knockback = 8f; p.Knockdown = true;
                p.Explode = true; p.ExplodeRadius = 3f;
                Sfx.Play(SfxId.Thunder, 1f, 1.2f);
                Fx.Lightning(ff.Center + Vector3.up * 2f, ff.Center + d * 6f, Bolt, 0.1f, 0.2f);
            });
            m.OnTick = (ff, t) =>
            {
                if (ff.MoveTime < 0.6f)
                {
                    Vector3 hand = ff.Rig.BonePos(B.HandR) + Vector3.up * 0.4f;
                    Fx.Glow(hand, Bolt, 0.6f + ff.MoveTime, 0.05f);
                    if (Random.value < 0.4f) Fx.Lightning(hand + Vector3.up * 6f, hand, Bolt, 0.05f, 0.06f);
                }
            };
            m.OnStart = ff => KitHelpers.Telegraph(ff, Bolt, 0.6f);
            f.StartMove(m);
            return true;
        }

        bool FlashStep(Fighter f)
        {
            if (!HasTarget) return false;
            Vector3 from = f.transform.position;
            Fx.Lightning(f.Center, f.Target.Center, Red, 0.1f, 0.15f);
            Fx.Smoke(f.Center, 0.5f, new Color(1f, 0.7f, 0.7f, 0.4f), 5);
            Vector3 to = f.Target.transform.position - KMath.Flat(f.Target.transform.position - from).normalized * 2.2f;
            f.Teleport(to);
            f.FaceInstant(f.Target.transform.position);
            DashTrail(from, to);
            Sfx.Play(SfxId.Teleport, 1f, 0.8f);
            return true;
        }

        // ---------- ultimate: Суд громовержца ----------
        IEnumerator JudgementUlt()
        {
            var f = F;
            var t = f.Target;
            if (t == null) yield break;
            f.Shout(UltLine);
            f.EnterCinematic();
            f.SetInvulnerable(4.5f);
            Hud.Banner("СУД ГРОМОВЕРЖЦА", Red);
            f.ForceAnimMode(AnimMode.Custom, PoseLib.Overhead);
            Sfx.Play(SfxId.Thunder, 1f, 0.5f);
            WorldLighting.Flash(f.Center, Red * 2f, 14f, 0.6f);
            // warning zones all over the arena, then a bolt on each
            Vector3 c0 = t.transform.position;
            for (int i = 0; i < 7; i++)
            {
                Vector3 p = i == 0 ? c0 : Arena.ClampInside(c0 + Quaternion.Euler(0, i * 51f, 0) * Vector3.forward * Random.Range(3.5f, 7f), 1f);
                Hazard.Spawn(f, HazardKind.LightningStrike, p, 2f, 1.3f + i * 0.12f, 20f, i % 2 == 0 ? Red : Bolt);
            }
            yield return new WaitForSeconds(0.6f);
            // Raiden leaps into the clouds
            Fx.Lightning(f.transform.position, f.transform.position + Vector3.up * 25f, Red, 0.3f, 0.4f);
            Fx.Burst(f.Center, Red, 2f, 0.3f);
            Hide(true);
            yield return new WaitForSeconds(1.4f);
            if (t == null || !t.Alive) { Hide(false); f.ForceAnimMode(AnimMode.Locomotion); f.ExitCinematic(); yield break; }
            // he falls with a giant electric blade where the challenger stands
            Vector3 c = t.transform.position;
            KitHelpers.Mark(c, Red, 3.4f, 1f);
            Hazard.Spawn(f, HazardKind.LightningStrike, c, 3.4f, 1f, 0f, Red);
            Sfx.Play(SfxId.Charge, 1f, 0.6f);
            yield return new WaitForSeconds(1f);
            f.Teleport(c);
            Hide(false);
            f.ForceAnimMode(AnimMode.Locomotion);
            f.Anim.Play(Clip.Hold(PoseLib.Slam, 0.02f, 0.4f, 0.2f));
            for (int k = 0; k < 6; k++) Fx.Lightning(c + Vector3.up * 22f + Random.insideUnitSphere * 2f, c, k % 2 == 0 ? Red : Bolt, 0.25f, 0.4f);
            Fx.Slash(f.transform, new Vector3(0, 1.4f, 0.5f), new Vector3(0, 0, 90), Red, 6f, 0.3f);
            Fx.Explosion(c, 3.5f, Red);
            Fx.Crack(c, 3.5f);
            WorldLighting.Flash(c + Vector3.up * 2f, Red * 3f, 22f, 0.7f);
            Destructible.ImpactAt(c, 6f, 300f, f);
            Combat.AreaDamage(f, f.Team, c + Vector3.up * 0.5f, 3.4f, 34f, 10f, 5f, true, Red, false, HitKind.Ultimate);
            Sfx.Play(SfxId.Explosion, 1f, 0.8f);
            Sfx.Play(SfxId.Thunder, 1f, 0.6f);
            CameraRig.Shake(1f);
            Haptics.Heavy();
            yield return new WaitForSeconds(0.5f);
            f.ExitCinematic();
        }
    }

    /// <summary>Raiden's ground glyph: arms after a moment, then stuns and shocks the first enemy that steps on it.</summary>
    public class GlyphTrap : MonoBehaviour
    {
        Fighter owner;
        Color col;
        float dmg, t, life = 9f, pulse;

        public static void Place(Fighter owner, Vector3 p, Color c, float dmg)
        {
            var go = new GameObject("GlyphTrap");
            go.transform.position = new Vector3(p.x, 0.05f, p.z);
            var g = go.AddComponent<GlyphTrap>();
            g.owner = owner; g.col = c; g.dmg = dmg;
            Fx.Ring(go.transform.position, c, 2.2f, 0.9f, 0.5f, Quaternion.identity);
        }

        void Update()
        {
            t += Time.deltaTime;
            if (t > life || owner == null || !owner.Alive) { Fizzle(); return; }
            pulse -= Time.deltaTime;
            bool armed = t > 0.7f;
            if (pulse <= 0f)
            {
                pulse = 0.35f;
                Fx.Ring(transform.position, armed ? col : col * 0.5f, 1.05f, 0.95f, 0.4f, Quaternion.identity);
                Fx.Glyph(transform.position + Vector3.up * 0.2f + KMath.Flat(Random.insideUnitSphere) * 0.5f, Vector3.up * 0.5f, col, 0.3f, 0.45f);
            }
            if (!armed) return;
            for (int i = 0; i < Fighter.All.Count; i++)
            {
                var f = Fighter.All[i];
                if (!f.Alive || f.Team == owner.Team || f.Team == Team.Neutral || !f.Grounded) continue;
                if (KMath.Flat(f.transform.position - transform.position).magnitude > 1f + f.BodyRadius) continue;
                Trigger(f);
                return;
            }
        }

        void Trigger(Fighter v)
        {
            Fx.Lightning(transform.position + Vector3.up * 6f, v.Center, col, 0.14f, 0.3f);
            Fx.Electric(v.Center, col, 1.2f, 3);
            Sfx.Play(SfxId.Thunder, 0.9f, 1.3f);
            var h = KitHelpers.Hit(dmg, 0.5f, 0f, HitKind.Technique, col, false, true);
            if (KitHelpers.Strike(owner, v, h) == HitResult.Hit && v.Alive) v.Stun(0.9f);
            if (v.IsPlayer) Hud.Popup("ЛОВУШКА!", col);
            Destroy(gameObject);
        }

        void Fizzle()
        {
            Fx.Smoke(transform.position + Vector3.up * 0.2f, 0.4f, new Color(0.6f, 0.6f, 0.7f, 0.3f), 3);
            Destroy(gameObject);
        }
    }

    public class ThunderBrain : BossBrain
    {
        public override void Setup(Fighter f, Archetype type, DifficultyTuning tune)
        {
            base.Setup(f, type, tune);
            Thresholds = new[] { 0.5f };
            PhaseLines = new[] { "Райдэн: «Буревестник. Небо теперь моё.»" };
            patternGap = 2.1f;
        }

        protected override void OnPhase(int p)
        {
            var k = (ThunderKit)F.Kit;
            k.Enraged = true;
            k.StormArmor = true;
            if (k.Heavy != null) k.Heavy.Armor = true;
            if (k.DashAttack != null) k.DashAttack.Armor = true;
            F.DamageReduction = 0.2f;
            patternGap = 1.6f;
            // the sky goes dark and the city crumbles
            if (Arena.Current != null)
                Arena.Current.DimensionShift(new Color(0.55f, 0.5f, 0.7f), new Color(0.12f, 0.1f, 0.18f), new Color(0.1f, 0.08f, 0.14f), new Color(0.35f, 0.25f, 0.4f), 600f);
            for (int i = 0; i < Destructible.All.Count; i++)
                if (Random.value < 0.6f) Destructible.All[i].Break(Random.onUnitSphere);
            for (int i = 0; i < 8; i++)
            {
                Vector3 p0 = KMath.Flat(Random.insideUnitSphere * 18f);
                Fx.Lightning(p0 + Vector3.up * 20f, p0, new Color(1f, 0.25f, 0.3f), 0.25f, 0.4f);
                Fx.Crack(p0, 2f);
            }
        }

        protected override bool DoPattern(float d)
        {
            float r = Random.value;
            if (Phase >= 1 && d < 2f && r < 0.3f && Tech(3)) return true;
            if (d < 2.2f && r < 0.45f && F.Kit.Launcher != null) { RunPattern(AirCombo()); return true; }
            if (d > 3f && r < 0.55f && Tech(0)) return true;
            if (r < 0.7f && Tech(1)) return true;
            if (d < 9f && r < 0.8f && Tech(2)) return true;
            if (d > 5f && Tech(4)) return true;
            if (d > 3f && d < 8f) { F.StartMove(F.Kit.DashAttack); return true; }
            return false;
        }

        protected override void Style(float d)
        {
            var t = F.Target;
            if (!F.CanAct || F.State == FState.Attack) return;
            if (Time.time < retreatUntil) retreatUntil = 0f;
            if (d > PreferredRange + 0.4f) moveDir = DirToTarget;
            if (d > 7f && Random.value < (Phase >= 1 ? 0.2f : 0.08f) && Tech(5)) return;
            if (t.State == FState.Block && d < 2f && Random.value < 0.45f) { F.Press(Cmd.Grab); return; }
            if (JustRecovered && d < 2.5f && Random.value < 0.5f) F.Press(Cmd.Heavy);
        }
    }

    // ======================================================================================
    // Boss 3 — Seigetsu, the Moon Sovereign (Moon Temple)
    // ======================================================================================
    public class MoonKit : BossKit
    {
        static readonly Color Lunar = new Color(0.82f, 0.78f, 1f);
        static readonly Color Blood = new Color(1f, 0.25f, 0.3f);
        Color Main => Enraged ? Blood : Lunar;
        int dimension;
        Move chainThrow, eclipseStrike;

        public override void Setup(Fighter f)
        {
            base.Setup(f);
            AuraColor = Lunar;
            f.HitColor = Lunar;
            IntroLine = "Сэйгэцу: «Вся чакра этого мира принадлежит мне.»";
            UltLine = "Сэйгэцу: «Падение луны.»";
            var palms = new Move
            {
                Name = "heavenpalms", Windup = PoseLib.PalmRWind, Strike = PoseLib.Beam, Startup = 0.22f, Active = 0.6f, Recovery = 0.35f, Hits = 6,
                Damage = 4.5f, Hitstun = 0.3f, Knockback = 0.8f, Radius = 0.6f, Bone = HitBone.HandR, Reach = new Vector3(0, 0, 0.3f), Lunge = 1.5f,
                Kind = HitKind.Technique, FxColor = Lunar, SlashFx = false, Range = 1.8f
            };
            palms.OnTick = (ff, t) => { if (ff.InActiveFrames) { ff.Anim.Play(Clip.Hold(Random.value < 0.5f ? PoseLib.PalmR : PoseLib.PalmL, 0.03f, 0.03f, 0.03f)); } };
            LightChain = new[] { MoveLib.Palm(true, 8f), MoveLib.Palm(false, 8f), MoveLib.Kick(10f, 4f), palms };
            AirChain = new[] { MoveLib.AirPunch(true, 8f), MoveLib.AirKick(9f) };
            AirFinisher = MoveLib.Slam(18f);
            Heavy = KitHelpers.Warned(new Move
            {
                Name = "hairspear", Windup = PoseLib.PalmRWind, Strike = PoseLib.PalmR, Startup = 0.28f, Active = 0.12f, Recovery = 0.35f,
                Damage = 20f, Kind = HitKind.Heavy, Reach = new Vector3(0, 0, 1.4f), Radius = 0.55f, Knockback = 8f, Knockdown = true, Lunge = 2f, FxColor = Lunar, Range = 3.2f
            }, Lunar);
            Launcher = MoveLib.Uppercut(12f, 11f);
            DashAttack = Heavy.Copy(); DashAttack.Lunge = 9f;
            eclipseStrike = Heavy.Copy(); eclipseStrike.OnStart = null; eclipseStrike.Startup = 0.25f; eclipseStrike.Damage = 22f; eclipseStrike.Lunge = 3f;
            MoveLib.WireGrab(this, 26f, 11f, 7f);
            chainThrow = MoveLib.ThrowMove(22f, 9f, 10f);
            chainThrow.Startup = 0.45f;

            Techs = new[]
            {
                new Technique { Name = "Лунные серпы", Cost = 20f, Cooldown = 5f, Range = 16f, Color = Lunar, Use = Crescents },
                new Technique { Name = "Гравитационная печать", Cost = 30f, Cooldown = 10f, Range = 9f, Color = Lunar, Use = GravitySeal },
                new Technique { Name = "Иллюзия", Cost = 30f, Cooldown = 13f, Range = 10f, Color = Lunar, Use = Illusion },
                new Technique { Name = "Затмение", Cost = 30f, Cooldown = 12f, Range = 14f, Color = Lunar, Use = Eclipse },
                new Technique { Name = "Лунные цепи", Cost = 30f, Cooldown = 12f, Range = 11f, Color = Lunar, Use = Chains },
                new Technique { Name = "Пространственный прыжок", Cost = 15f, Cooldown = 5f, Range = 30f, Use = Warp, Offensive = false },
                new Technique { Name = "Смена измерения", Cost = 40f, Cooldown = 18f, Range = 30f, Use = Dimension },
            };
            Ult = new Technique { Name = "Падение луны", Color = Blood, Use = ff => { ff.StartCoroutine(MoonfallUlt()); return true; } };
        }

        public override void Tick(float dt)
        {
            if (hiddenNow) return;
            if (Random.value < 0.2f) Fx.Trail(F.transform.position + Vector3.up * Random.Range(0.2f, 2f) + Random.insideUnitSphere * 0.3f, Main * 0.6f, 0.2f);
            // Кровавая луна: Seigetsu floats above the ground
            if (Enraged && Random.value < 0.12f) Fx.Glow(F.transform.position + Vector3.up * 0.1f, Blood, 0.9f, 0.3f);
        }

        // ---------- 0: crescent blades ----------
        bool Crescents(Fighter f)
        {
            var m = MoveLib.Cast("crescents", PoseLib.BeamWind, PoseLib.Beam, 0.4f, 0.35f, ff =>
            {
                Vector3 fwd = MoveLib.AimDir(ff);
                int n = Enraged ? 5 : 3;
                for (int i = 0; i < n; i++)
                {
                    float a = (i - (n - 1) * 0.5f) * 14f;
                    Vector3 d = Quaternion.Euler(0, a, 0) * fwd;
                    var p = Projectile.Spawn(ff, ProjStyle.Shuriken, ff.Center + d * 0.8f, d * 15f, 13f, Main, 1.9f);
                    p.Homing = ff.Target; p.HomingRate = 0.5f; p.Life = 2.2f; p.Absorbable = false; p.Power = 2f; p.Knockback = 4f;
                }
                Sfx.Play(SfxId.Whoosh, 1f, 0.6f);
            });
            m.OnStart = ff => KitHelpers.Telegraph(ff, Main, 0.4f);
            f.StartMove(m);
            return true;
        }

        // ---------- 1: gravity seal — pulls the challenger in, then implodes ----------
        bool GravitySeal(Fighter f)
        {
            if (!HasTarget) return false;
            f.StartCoroutine(SealRoutine(f));
            return true;
        }

        IEnumerator SealRoutine(Fighter f)
        {
            f.Anim.Play(Clip.Hold(PoseLib.Seal, 0.1f, 1.2f, 0.25f));
            Vector3 c = Arena.ClampInside(f.transform.position + f.transform.forward * 2.5f, 1f);
            float delay = 1.4f;
            Hazard.Spawn(f, HazardKind.VoidImplosion, c, 2.4f, delay, 24f, Main);
            Sfx.Play(SfxId.Absorb, 1f, 0.6f);
            for (float t = 0f; t < delay; t += Time.deltaTime)
            {
                var v = f.Target;
                if (v != null && v.Alive && v.Cc.enabled && v.State != FState.Cinematic && v.State != FState.Grabbed)
                {
                    Vector3 to = KMath.Flat(c - v.transform.position);
                    float dist = to.magnitude;
                    if (dist > 0.6f && dist < 9f) v.Cc.Move(to / dist * 3.3f * Time.deltaTime);
                }
                if (Random.value < 0.3f) Fx.HackStream(c + KMath.Flat(Random.insideUnitSphere) * 6f + Vector3.up * 0.3f, c + Vector3.up * 0.5f, Main, 1, 0.4f);
                yield return null;
            }
        }

        // ---------- 2: illusion copy distracts while the real one strikes from the other side ----------
        bool Illusion(Fighter f)
        {
            if (!HasTarget) return false;
            f.StartCoroutine(IllusionRoutine(f));
            return true;
        }

        IEnumerator IllusionRoutine(Fighter f)
        {
            var t = f.Target;
            Fx.Burst(f.Center, Lunar, 1.4f, 0.3f);
            Sfx.Play(SfxId.Poof, 1f, 0.7f);
            Vector3 toT = KMath.Flat(t.transform.position - f.transform.position).normalized;
            KitHelpers.Decoy(f, t.transform.position - toT * 2.2f, StanceKind.Moon, RunStyle.Glide, true, 4.5f, Lunar);
            f.EnterCinematic();
            f.SetInvulnerable(1.4f);
            Hide(true);
            f.Teleport(t.transform.position + toT * 4f);
            yield return new WaitForSeconds(1f);
            if (t == null || !t.Alive || !f.Alive) { Hide(false); f.ExitCinematic(); yield break; }
            Vector3 p = KitHelpers.Behind(f, t, 1.3f);
            KitHelpers.Mark(p, Lunar, 0.7f, 0.35f);
            Sfx.Play(SfxId.Teleport, 1f, 1.4f);
            yield return new WaitForSeconds(0.35f);
            f.Teleport(p);
            f.FaceInstant(t.transform.position);
            Hide(false);
            f.ExitCinematic();
            f.StartMove(eclipseStrike);
        }

        // ---------- 3: eclipse — darkness, hidden movement, a clear sound cue before the hit ----------
        bool Eclipse(Fighter f)
        {
            if (!HasTarget) return false;
            f.StartCoroutine(EclipseRoutine(f));
            return true;
        }

        IEnumerator EclipseRoutine(Fighter f)
        {
            var t = f.Target;
            if (Arena.Current != null)
                Arena.Current.DimensionShift(new Color(0.25f, 0.22f, 0.35f), new Color(0.05f, 0.04f, 0.09f), new Color(0.03f, 0.02f, 0.05f), new Color(0.15f, 0.1f, 0.25f), 3.2f);
            f.Shout("Сэйгэцу: «Затмение.»");
            Fx.Ring(f.transform.position + Vector3.up * 0.1f, Lunar, 0.5f, 10f, 0.6f, Quaternion.identity);
            f.EnterCinematic();
            f.SetInvulnerable(2.3f);
            Hide(true);
            yield return new WaitForSeconds(Random.Range(1.1f, 1.6f));
            if (t == null || !t.Alive || !f.Alive) { Hide(false); f.ExitCinematic(); yield break; }
            Vector3 p = t.transform.position + KMath.Flat(Random.onUnitSphere).normalized * 1.4f;
            // the cue: a bell-like chime and a glint where she will appear
            Sfx.Play(SfxId.Parry, 1f, 0.55f);
            KitHelpers.Mark(p, Lunar, 0.8f, 0.45f);
            yield return new WaitForSeconds(0.45f);
            f.Teleport(p);
            f.FaceInstant(t.transform.position);
            Hide(false);
            Fx.Burst(f.Center, Lunar, 1.2f, 0.25f);
            f.ExitCinematic();
            f.StartMove(eclipseStrike);
        }

        // ---------- 4: lunar chains — hold until broken ----------
        bool Chains(Fighter f)
        {
            var m = MoveLib.Cast("chains", PoseLib.PalmRWind, PoseLib.PalmR, 0.45f, 0.5f, ff =>
            {
                var p = Projectile.Spawn(ff, ProjStyle.Rod, ff.Rig.BonePos(B.HandR), MoveLib.AimDir(ff) * 20f, 4f, Main, 1f);
                p.Unblockable = true; p.Absorbable = false; p.Power = 1f; p.Knockback = 0f; p.Hitstun = 1f; p.Life = 0.7f;
                p.OnHitFighter = (pr, v) =>
                {
                    var o = pr.Owner;
                    if (o == null || !o.Alive || !v.Alive) return;
                    o.BeginThrow(v, chainThrow);
                    o.StartCoroutine(ChainFx(o, v));
                    Sfx.Play(SfxId.WireCrack, 1f, 0.7f);
                    if (v.IsPlayer) Hud.Popup("ЛУННЫЕ ЦЕПИ!  Вырывайся", Lunar);
                };
                Sfx.Play(SfxId.WireSwing, 0.9f, 0.8f);
            });
            m.OnStart = ff => KitHelpers.Telegraph(ff, Main, 0.45f);
            f.StartMove(m);
            return true;
        }

        IEnumerator ChainFx(Fighter o, Fighter v)
        {
            float next = 0f;
            while (o != null && o.Alive && v != null && v.Alive && v.State == FState.Grabbed && v.GrabbedBy == o)
            {
                if (Time.time >= next) { next = Time.time + 0.12f; Fx.Wire(o.Rig[B.HandR], v.Center, Main, 0.14f, 0f); }
                yield return null;
            }
        }

        bool Warp(Fighter f)
        {
            Vector3 c = f.Target != null ? f.Target.transform.position : Vector3.zero;
            float a = Random.value * Mathf.PI * 2f;
            Vector3 to = Arena.ClampInside(c + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * Random.Range(7f, 11f), 2f);
            Fx.Burst(f.Center, Main, 1.5f, 0.3f);
            Fx.Ring(f.Center, Main, 0.2f, 3f, 0.3f, Quaternion.LookRotation(Vector3.up));
            f.Teleport(to);
            if (f.Target != null) f.FaceInstant(f.Target.transform.position);
            Fx.Burst(f.Center, Main, 1.5f, 0.3f);
            Sfx.Play(SfxId.Teleport, 1f, 0.7f);
            f.SetInvulnerable(0.3f);
            return true;
        }

        bool Dimension(Fighter f)
        {
            var arena = Arena.Current;
            if (arena == null || !HasTarget) return false;
            f.Anim.Play(Clip.Hold(PoseLib.Overhead, 0.2f, 0.6f, 0.3f));
            Fx.Ring(Vector3.up * 0.2f, Main, 30f, 1f, 0.6f, Quaternion.identity);
            Sfx.Play(SfxId.Release, 1f, 0.5f);
            CameraRig.Shake(0.5f);
            int kind = dimension++ % 2;
            Vector3 c = f.Target.transform.position;
            if (kind == 0)
            {
                arena.DimensionShift(new Color(0.75f, 0.9f, 1.1f), new Color(0.2f, 0.3f, 0.45f), new Color(0.75f, 0.85f, 0.95f), new Color(0.8f, 1f, 1.3f), 6f);
                foreach (var x in Fighter.All) if (x.Alive && x.Team != f.Team && x.Team != Team.Neutral) x.ApplyDrain(f, 0f, 5f, 0.65f);
                for (int i = 0; i < 5; i++) Hazard.Spawn(f, HazardKind.BoneSpikes, Arena.ClampInside(c + Random.insideUnitSphere * 5f, 1.5f), 1.7f, 0.9f + i * 0.3f, 20f, new Color(0.8f, 0.95f, 1f));
                f.Shout("Сэйгэцу: «Замри во льдах.»");
            }
            else
            {
                arena.DimensionShift(new Color(0.7f, 0.5f, 1f), new Color(0.15f, 0.05f, 0.25f), new Color(0.05f, 0.02f, 0.1f), new Color(0.6f, 0.3f, 1f), 5f);
                f.StartCoroutine(VoidChase(f));
                f.Shout("Сэйгэцу: «Исчезни в пустоте.»");
            }
            return true;
        }

        IEnumerator VoidChase(Fighter f)
        {
            for (int i = 0; i < 5; i++)
            {
                if (!HasTarget || !f.Alive) yield break;
                Hazard.Spawn(f, HazardKind.VoidImplosion, f.Target.transform.position, 2.1f, 0.8f, 20f, new Color(0.7f, 0.4f, 1f));
                yield return new WaitForSeconds(0.55f);
            }
        }

        // ---------- ultimate: Падение луны ----------
        IEnumerator MoonfallUlt()
        {
            var f = F;
            var t = f.Target;
            if (t == null) yield break;
            f.Shout(UltLine);
            f.EnterCinematic();
            f.SuperArmor = true;
            f.SetInvulnerable(5.5f);
            f.ForceAnimMode(AnimMode.Custom, PoseLib.Overhead);
            Hud.Banner("ПАДЕНИЕ ЛУНЫ", Blood);
            if (Arena.Current != null)
                Arena.Current.DimensionShift(new Color(1f, 0.45f, 0.5f), new Color(0.3f, 0.08f, 0.12f), new Color(0.2f, 0.04f, 0.08f), new Color(1.2f, 0.4f, 0.45f), 8f);
            Sfx.Play(SfxId.Release, 1f, 0.4f);

            // gravity lift: anyone caught inside the ring is thrown up and hangs helpless for a moment
            Vector3 c = t.transform.position;
            KitHelpers.Mark(c, Blood, 3.5f, 1.1f);
            for (float k = 0f; k < 1.1f; k += 0.1f)
            {
                Fx.Debris(c + KMath.Flat(Random.insideUnitSphere) * 3f, new Color(0.45f, 0.4f, 0.5f), 2, 2f);
                yield return new WaitForSeconds(0.1f);
            }
            if (t.Alive && !Dodging(t) && KMath.Flat(t.transform.position - c).magnitude < 3.6f) Lift(t, 8f, 11f, Blood);
            CameraRig.Shake(0.4f);

            // a huge celestial body descends over the challenger
            if (t == null || !t.Alive) { End(f); yield break; }
            Vector3 m = t.transform.position;
            float delay = 2.6f;
            Hazard.Spawn(f, HazardKind.Meteor, m, 6.5f, delay, 55f, Blood);
            Hud.Banner("БЕГИ ИЗ КРУГА!", new Color(1f, 0.5f, 0.6f));
            CameraRig.Cinematic(m + Vector3.up * 4f, new Vector3(0.3f, 0.6f, -1f), 18f, 6f, 1.6f);
            for (float k = 0f; k < delay; k += 0.05f)
            {
                float u = k / delay;
                Vector3 moon = m + Vector3.up * Mathf.Lerp(22f, 3f, u * u);
                Fx.Glow(moon, Color.Lerp(Blood, Color.white, u * 0.5f), 3f + u * 4f, 0.08f);
                if (Random.value < 0.25f) Fx.Debris(m + KMath.Flat(Random.insideUnitSphere) * 7f, new Color(0.4f, 0.35f, 0.45f), 1, 3f);
                yield return new WaitForSeconds(0.05f);
            }
            // the beam and the impact
            Fx.Lightning(m + Vector3.up * 25f, m, Color.white, 0.6f, 0.4f);
            Fx.Burst(m + Vector3.up * 2f, Color.white, 9f, 0.7f);
            Fx.Explosion(m, 6f, Blood);
            for (int i = 0; i < 5; i++) Fx.Crack(m + KMath.Flat(Random.insideUnitSphere) * 4f, 2.5f);
            WorldLighting.Flash(m + Vector3.up * 3f, Blood * 3f, 30f, 0.8f);
            Destructible.ImpactAt(m, 8f, 400f, f);
            CameraRig.Shake(1f);
            Haptics.Heavy();
            End(f);
        }

        void End(Fighter f)
        {
            f.SuperArmor = false;
            f.ForceAnimMode(AnimMode.Locomotion);
            f.ExitCinematic();
        }
    }

    public class MoonBrain : BossBrain
    {
        public override void Setup(Fighter f, Archetype type, DifficultyTuning tune)
        {
            base.Setup(f, type, tune);
            Thresholds = new[] { 0.5f };
            PhaseLines = new[] { "Сэйгэцу: «Взойди, Кровавая луна.»" };
            patternGap = 2f;
            PreferredRange = 1.7f;
        }

        protected override void OnPhase(int p)
        {
            var k = (MoonKit)F.Kit;
            k.Enraged = true;
            k.AuraColor = new Color(1f, 0.25f, 0.3f);
            if (Arena.Current != null) Arena.Current.SetPhase(2);
            F.DamageReduction = 0.15f;
            F.Rig.SetEmit(0.2f, new Color(1f, 0.25f, 0.3f));
            patternGap = 1.6f;
        }

        protected override bool DoPattern(float d)
        {
            float r = Random.value;
            // Кровавая луна: vertical follow-ups — launches far more often
            if (d < 2.5f && r < (Phase >= 1 ? 0.45f : 0.25f)) { RunPattern(AirCombo()); return true; }
            if (Phase >= 1 && r < 0.15f && Tech(6)) return true;
            if (d < 7f && r < 0.35f && Tech(1)) return true;
            if (r < 0.45f && Tech(3)) return true;
            if (d < 9f && r < 0.55f && Tech(2)) return true;
            if (d > 2.5f && d < 10f && r < 0.7f && Tech(4)) return true;
            if (d > 3f && Tech(0)) return true;
            return false;
        }

        protected override void Style(float d)
        {
            var t = F.Target;
            bool pressed = d < 2.4f && (t.State == FState.Attack || JustRecovered);
            if (pressed && F.CanAct && Random.value < 0.35f + Phase * 0.1f && Tech(5)) return;
            if (F.State != FState.Idle || Time.time < retreatUntil) return;
            if (d < 5f && t.State != FState.Hitstun && t.State != FState.Juggle && t.State != FState.Knockdown && Random.value < 0.5f)
            {
                if (Random.value < 0.03f) strafeSign = -strafeSign;
                moveDir = (-DirToTarget * 0.75f + Vector3.Cross(Vector3.up, DirToTarget) * strafeSign * 0.6f).normalized;
            }
        }
    }
}
