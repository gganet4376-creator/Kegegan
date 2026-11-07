using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Kagegan
{
    /// <summary>
    /// Kaito — fast combo fighter. Spiral Sphere, Shadow Doubles (real AI fighters that deal damage),
    /// Vortex Shuriken, Chakra Surge mode, long air combos, clone-barrage ultimate and finisher.
    /// </summary>
    public class KaitoKit : Kit
    {
        readonly bool isClone;
        float surgeUntil;
        static readonly Color Cyan = new Color(0.45f, 0.8f, 1f);
        static readonly Color Orange = new Color(1f, 0.6f, 0.15f);
        public readonly List<Fighter> Clones = new List<Fighter>();

        public KaitoKit(bool clone = false) { isClone = clone; }

        public bool Surging => Time.time < surgeUntil;
        public override float SpeedMul => Surging ? 1.25f : 1f;
        public override float OutgoingMul(Move m) => Surging ? 1.3f : 1f;

        public override void Setup(Fighter f)
        {
            base.Setup(f);
            AuraColor = Orange;
            f.HitColor = new Color(1f, 0.85f, 0.5f);
            IntroLine = "Кайто: «Я не отступлю — это мой путь!»";
            WinLine = "Кайто: «Говорил же — не сдаюсь!»";
            UltLine = "Кайто: «Ураган теней!»";

            LightChain = new[]
            {
                MoveLib.Jab(false, 5f, 1.15f),
                MoveLib.Jab(true, 6f, 1.15f),
                MoveLib.Kick(8f, 2.5f),
                MoveLib.Hook(false, 8f),
                SpinKick()
            };
            AirChain = new[] { MoveLib.AirPunch(false, 6f), MoveLib.AirPunch(true, 6f), MoveLib.AirKick(7f), MoveLib.AirKick(8f) };
            AirFinisher = MoveLib.Slam(16f);
            Launcher = MoveLib.Uppercut(10f, 12f);
            Heavy = MoveLib.FrontKick(15f);
            DashAttack = MoveLib.Knee(12f);
            DashAttack.Knockback = 7f; DashAttack.Lunge = 7f; DashAttack.Kind = HitKind.Heavy;
            Counter = MoveLib.Uppercut(16f, 13f);
            Counter.Name = "counter"; Counter.Unblockable = true; Counter.FxColor = Orange;
            MoveLib.WireGrab(this, 20f, 10f, 7f);

            if (isClone) { Techs = new Technique[0]; return; }

            Techs = new[]
            {
                new Technique { Name = "Спиральная сфера", Short = "Сфера", Cost = 30f, Cooldown = 5f, Range = 7f, Color = Cyan, Use = UseSphere },
                new Technique { Name = "Теневые двойники", Short = "Клоны", Cost = 35f, Cooldown = 12f, Range = 20f, Color = Orange, Use = UseClones, Offensive = false },
                new Technique { Name = "Вихревой сюрикен", Short = "Сюрикен", Cost = 45f, Cooldown = 10f, Range = 16f, Color = new Color(0.7f, 0.95f, 1f), Use = UseShuriken },
                new Technique { Name = "Режим чакры", Short = "Режим", Cost = 40f, Cooldown = 25f, Range = 30f, Color = new Color(1f, 0.75f, 0.2f), Use = UseSurge, Offensive = false },
            };
            Ult = new Technique { Name = "Ураган теней", Short = "УЛЬТА", Color = Orange, Use = f => { f.StartCoroutine(UltRoutine()); return true; } };
        }

        Move SpinKick()
        {
            var m = MoveLib.Kick(12f, 7f);
            m.Name = "spinkick"; m.Strike = PoseLib.KickHigh.Mirror(); m.Bone = HitBone.FootL; m.Kind = HitKind.Heavy; m.Knockdown = true; m.Shake = 0.25f;
            m.SlashEuler = new Vector3(0, 0, -80); m.Radius = 0.55f;
            return m;
        }

        public override void Tick(float dt)
        {
            if (Surging)
            {
                F.ChakraRegen = 12f;
                if (Random.value < 0.5f) Fx.Trail(F.Center + Random.insideUnitSphere * 0.5f, Orange, 0.45f);
                F.Rig.SetEmit(0.18f + Mathf.Sin(Time.time * 8f) * 0.06f, Orange);
            }
            else if (F.ChakraRegen > 4f)
            {
                F.ChakraRegen = 4f;
                F.Rig.SetEmit(0f, Orange);
            }
            for (int i = Clones.Count - 1; i >= 0; i--) if (Clones[i] == null || !Clones[i].Alive) Clones.RemoveAt(i);
        }

        public override void OnDeath()
        {
            foreach (var c in Clones) if (c != null && c.Alive) c.Kill(null);
        }

        // ---------- techniques ----------
        bool UseSphere(Fighter f)
        {
            var m = new Move
            {
                Name = "sphere", Windup = PoseLib.Seal, Strike = PoseLib.PalmR, Startup = 0.38f, Active = 0.22f, Recovery = 0.35f,
                Damage = 42f, Kind = HitKind.Technique, Knockback = 15f, Launch = 4f, Knockdown = true, Lunge = 0f, Radius = 0.6f,
                Bone = HitBone.HandR, Reach = new Vector3(0, 0, 0.25f), FxColor = Cyan, Shake = 0.55f, HitStop = 0.14f, HitSfx = SfxId.Explosion,
                SlashFx = false, Sfx = SfxId.Energy, StaminaCost = 0f, Range = 7f
            };
            m.OnTick = (ff, t) =>
            {
                Vector3 hand = ff.Rig.BonePos(B.HandR) + ff.transform.forward * 0.2f;
                Fx.Glow(hand, Cyan, 0.9f + Mathf.Sin(Time.time * 50f) * 0.1f, 0.05f);
                if (Random.value < 0.6f) Fx.Trail(hand + Random.insideUnitSphere * 0.25f, Color.white, 0.2f);
                // dash in during the active window
                if (ff.MoveTime >= m.Startup * 0.6f && ff.MoveTime < m.Startup + m.Active && ff.Target != null)
                {
                    float d = KMath.Flat(ff.Target.transform.position - ff.transform.position).magnitude;
                    if (d > 1.1f) ff.Velocity = ff.transform.forward * 14f + Vector3.up * ff.Velocity.y;
                }
            };
            m.OnHit = (a, v) =>
            {
                Fx.Explosion(v.Center, 1.8f, Cyan);
                Fx.Ring(v.Center, Color.white, 0.5f, 3.2f, 0.4f, Quaternion.LookRotation(a.transform.forward));
                WorldLighting.Flash(v.Center, Cyan * 2f, 7f, 0.4f);
            };
            f.StartMove(m);
            return true;
        }

        bool UseClones(Fighter f)
        {
            int count = Surging ? 3 : 2;
            if (Clones.Count >= 4) return false;
            f.Anim.Play(Clip.Hold(PoseLib.Seal, 0.08f, 0.25f, 0.15f));
            Sfx.Play(SfxId.Poof, 1f);
            for (int i = 0; i < count; i++)
            {
                Vector3 side = f.transform.right * (i % 2 == 0 ? 1.4f : -1.4f) + f.transform.forward * (i >= 2 ? -1.2f : 0.3f);
                var c = Roster.CreateClone(f, f.transform.position + side);
                Clones.Add(c);
            }
            f.Shout("Кайто: «Теневые двойники!»");
            return true;
        }

        bool UseShuriken(Fighter f)
        {
            var m = MoveLib.Cast("shuriken", PoseLib.Overhead, PoseLib.Throw, 0.42f, 0.35f, ff =>
            {
                Vector3 dir = MoveLib.AimDir(ff);
                var p = Projectile.Spawn(ff, ProjStyle.Shuriken, ff.Rig.BonePos(B.HandR) + ff.transform.forward * 0.6f, dir * 16f, 26f, new Color(0.75f, 0.95f, 1f), 1.4f);
                p.Power = 5f; p.Knockback = 2f; p.Hitstun = 0.9f; p.Homing = ff.Target; p.HomingRate = 1.2f; p.Life = 2.5f;
                p.OnHitFighter = (pr, target) =>
                {
                    // the shuriken expands into a grinding wind sphere
                    Hazard.Spawn(pr.Owner, HazardKind.VoidImplosion, target.transform.position, 3.2f, 0.25f, 34f, new Color(0.8f, 0.97f, 1f));
                    Fx.Burst(target.Center, new Color(0.8f, 0.97f, 1f), 3.4f, 0.6f);
                    CameraRig.Shake(0.5f);
                };
                Sfx.Play(SfxId.Whoosh, 1f, 0.7f);
            });
            m.OnTick = (ff, t) => { if (ff.MoveTime < 0.42f) Fx.Glow(ff.Rig.BonePos(B.HandR) + Vector3.up * 0.3f, new Color(0.8f, 0.97f, 1f), 1.2f, 0.05f); };
            f.StartMove(m);
            f.Shout("Кайто: «Вихревой сюрикен!»");
            return true;
        }

        bool UseSurge(Fighter f)
        {
            surgeUntil = Time.time + 15f;
            f.Anim.Play(Clip.Hold(PoseLib.Charge, 0.1f, 0.4f, 0.2f));
            Fx.Burst(f.Center, Orange, 2.5f, 0.5f);
            Fx.Ring(f.transform.position + Vector3.up * 0.1f, Orange, 0.5f, 6f, 0.6f, Quaternion.identity);
            WorldLighting.Flash(f.Center, Orange * 2f, 8f, 0.6f);
            Sfx.Play(SfxId.Charge, 1f);
            CameraRig.Shake(0.3f);
            f.Shout("Кайто: «Режим чакры — полная сила!»");
            return true;
        }

        // ---------- ultimate: clone barrage ----------
        IEnumerator UltRoutine()
        {
            var f = F;
            var t = f.Target;
            if (t == null || !t.Alive) yield break;
            f.Shout(UltLine);
            f.EnterCinematic();
            t.EnterCinematic();
            f.SetInvulnerable(4f);
            Vector3 c = t.transform.position;
            Vector3 view = Vector3.Cross(Vector3.up, KMath.Flat(c - f.transform.position).normalized);
            CameraRig.Cinematic(c + Vector3.up * 1.5f, view + Vector3.back * 0.2f, 7f, 1.5f, 3.6f);
            f.Anim.Play(Clip.Hold(PoseLib.Seal, 0.05f, 0.4f, 0.1f));
            Sfx.Play(SfxId.Poof, 1f);
            yield return new WaitForSeconds(0.35f);

            var puppets = new List<Fighter>();
            for (int i = 0; i < 4; i++)
            {
                float a = i / 4f * Mathf.PI * 2f;
                var p = Roster.CreateClone(f, c + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * 2.5f);
                p.Brain.Active = false;
                p.EnterCinematic();
                p.FaceInstant(c);
                puppets.Add(p);
            }
            yield return new WaitForSeconds(0.25f);
            Vector3 air = c;
            for (int i = 0; i < puppets.Count; i++)
            {
                var p = puppets[i];
                air += Vector3.up * 1.1f;
                p.Teleport(t.transform.position - (t.transform.position - p.transform.position).normalized * 0.9f);
                p.FaceInstant(t.transform.position);
                p.Anim.Play(Clip.Hold(i % 2 == 0 ? PoseLib.Uppercut : PoseLib.KickHigh, 0.04f, 0.12f, 0.1f));
                yield return new WaitForSeconds(0.1f);
                DealCinematic(t, 18f, Orange);
                t.Teleport(air);
                Fx.Hit(t.Center, Orange, true);
                yield return new WaitForSeconds(0.16f);
            }
            // final dive with a giant sphere
            f.Teleport(t.transform.position + Vector3.up * 2.2f - f.transform.forward * 0.5f);
            f.FaceInstant(t.transform.position);
            f.Anim.Play(Clip.Hold(PoseLib.SlamWind, 0.05f, 0.3f, 0.05f));
            for (int k = 0; k < 10; k++) { Fx.Glow(f.Rig.BonePos(B.HandR) + Vector3.up * 0.4f, Cyan, 2f + k * 0.15f, 0.06f); yield return new WaitForSeconds(0.03f); }
            f.Anim.Play(Clip.Hold(PoseLib.Slam, 0.04f, 0.4f, 0.2f));
            t.Teleport(new Vector3(t.transform.position.x, 0f, t.transform.position.z));
            f.Teleport(new Vector3(t.transform.position.x, 0f, t.transform.position.z) - f.transform.forward * 0.9f);
            foreach (var p in puppets) if (p != null) p.Kill(null);
            t.ExitCinematic();
            Fx.Explosion(t.transform.position, 4.5f, Cyan);
            Fx.Burst(t.Center, Color.white, 5f, 0.6f);
            Sfx.Play(SfxId.Explosion, 1f, 0.8f);
            CameraRig.Shake(0.9f);
            Haptics.Heavy();
            var h = new HitInfo { Attacker = f, Damage = 70f * f.DamageMul * OutgoingMul(null), Stamina = 50f, Knockback = 12f, Launch = 6f, Kind = HitKind.Ultimate, Knockdown = true, Point = t.Center, Dir = f.transform.forward, FxColor = Cyan, Shake = 0.6f, HitStop = 0.15f };
            t.ReceiveHit(ref h);
            Destructible.ImpactAt(t.transform.position, 6f, 200f, f);
            f.ExitCinematic();
            yield return new WaitForSeconds(0.2f);
            CameraRig.EndCinematic();
        }

        static void DealCinematic(Fighter t, float dmg, Color c)
        {
            if (!t.Alive) return;
            t.Hp = Mathf.Max(1f, t.Hp - dmg);
            t.Stats.damageTaken += dmg;
            t.Anim.Flinch(1f, false);
            t.Flash(Color.white);
            Sfx.Play(SfxId.HeavyHit, 0.9f);
            CameraRig.Shake(0.25f);
            Haptics.Medium();
        }

        public override IEnumerator Finisher(Fighter t)
        {
            var f = F;
            Vector3 c = t.transform.position;
            CameraRig.Cinematic(c + Vector3.up, Vector3.Cross(Vector3.up, KMath.Flat(c - f.transform.position).normalized), 5f, 1f, 2.6f);
            var a = Roster.CreateClone(f, c + f.transform.right * 1.5f);
            var b = Roster.CreateClone(f, c - f.transform.right * 1.5f);
            a.Brain.Active = false; b.Brain.Active = false; a.EnterCinematic(); b.EnterCinematic();
            a.FaceInstant(c); b.FaceInstant(c);
            yield return new WaitForSeconds(0.2f);
            a.Anim.Play(Clip.Hold(PoseLib.KickHigh, 0.05f, 0.15f, 0.1f));
            b.Anim.Play(Clip.Hold(PoseLib.KickHigh.Mirror(), 0.05f, 0.15f, 0.1f));
            yield return new WaitForSeconds(0.1f);
            DealCinematic(t, 5f, Orange);
            t.Teleport(c + Vector3.up * 3f);
            Fx.Hit(t.Center, Orange, true);
            yield return new WaitForSeconds(0.25f);
            f.Teleport(c + Vector3.up * 4.4f - f.transform.forward * 0.6f);
            f.Anim.Play(Clip.Hold(PoseLib.Slam, 0.05f, 0.3f, 0.2f));
            for (int k = 0; k < 6; k++) { Fx.Glow(f.Rig.BonePos(B.HandR), Cyan, 1.6f, 0.06f); yield return new WaitForSeconds(0.03f); }
            t.Teleport(c);
            f.Teleport(c - f.transform.forward * 0.9f);
            a.Kill(null); b.Kill(null);
            t.Kill(f);
            Fx.Explosion(c, 3.5f, Cyan);
            Sfx.Play(SfxId.Explosion, 1f);
            CameraRig.Shake(0.8f);
            yield return new WaitForSeconds(0.5f);
        }
    }
}
