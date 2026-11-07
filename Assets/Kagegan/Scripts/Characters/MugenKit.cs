using System.Collections;
using UnityEngine;

namespace Kagegan
{
    /// <summary>
    /// Mugen — technical fighter. Holding "Absorb" raises the right palm: enemy techniques, projectiles and beams that hit
    /// from the front are converted into stored energy (no damage). "Release" fires that energy from the left palm as a beam
    /// whose damage and width scale with what was absorbed. Chakra Rods drain the target's chakra (bosses need chakra for
    /// their techniques), Blink teleports behind the target and grants a counter window.
    /// </summary>
    public class MugenKit : Kit
    {
        static readonly Color Violet = new Color(0.72f, 0.45f, 1f);
        public float Stored;
        public const float MaxStored = 400f;
        bool absorbing;
        float absorbFx;

        public bool Absorbing => absorbing;

        public override void Setup(Fighter f)
        {
            base.Setup(f);
            AuraColor = Violet;
            f.HitColor = new Color(0.85f, 0.7f, 1f);
            IntroLine = "Мугэн: «Твоя сила станет моей.»";
            WinLine = "Мугэн: «Всё, что ты вложил в удар, — теперь моё.»";
            UltLine = "Мугэн: «Небесный разлом.»";

            LightChain = new[] { MoveLib.Palm(true, 7f), MoveLib.Palm(false, 7f), MoveLib.Kick(9f, 3f), DoublePalm() };
            AirChain = new[] { MoveLib.AirPunch(true, 7f), MoveLib.AirPunch(false, 7f), MoveLib.AirKick(8f) };
            AirFinisher = MoveLib.Slam(15f);
            AirFinisher.FxColor = Violet;
            Launcher = MoveLib.Uppercut(11f, 11.5f);
            Heavy = DoublePalm();
            Heavy.Name = "pushwave"; Heavy.Damage = 18f; Heavy.Knockback = 11f; Heavy.Knockdown = true; Heavy.Startup = 0.22f; Heavy.Radius = 0.7f;
            Heavy.OnActive = ff => { Fx.Ring(ff.Center + ff.transform.forward * 0.8f, Violet, 0.3f, 2.4f, 0.25f, Quaternion.LookRotation(ff.transform.up, ff.transform.forward)); };
            DashAttack = MoveLib.Palm(true, 14f);
            DashAttack.Lunge = 7f; DashAttack.Kind = HitKind.Heavy; DashAttack.Knockback = 8f;
            Counter = DoublePalm();
            Counter.Damage = 24f; Counter.Unblockable = true; Counter.Knockdown = true; Counter.Knockback = 12f;
            MoveLib.WireGrab(this, 20f, 10f, 6f);
            Grab.OnHit += (a, v) => { if (!v.InfiniteChakra) { float d = Mathf.Min(v.Chakra, 25f); v.Chakra -= d; a.Chakra = Mathf.Min(a.MaxChakra, a.Chakra + d); } };

            Techs = new[]
            {
                new Technique { Name = "Поглощение (удерживать)", Short = "Поглот.", Icon = "m_absorb", Cost = 5f, Cooldown = 0.6f, Range = 30f, Color = Violet, Hold = true, Offensive = false, AiUsable = false,
                                Use = f2 => true, Held = OnAbsorbHeld },
                new Technique { Name = "Выброс энергии", Short = "Выброс", Icon = "m_release", Cost = 15f, Cooldown = 2.5f, Range = 15f, Color = new Color(1f, 0.6f, 1f), Use = UseRelease },
                new Technique { Name = "Чакровые стержни", Short = "Стержни", Icon = "m_rods", Cost = 20f, Cooldown = 7f, Range = 18f, Color = new Color(0.5f, 0.5f, 0.6f), Use = UseRods },
                new Technique { Name = "Телепортация", Short = "Телепорт", Icon = "m_blink", Cost = 15f, Cooldown = 4f, Range = 20f, Color = new Color(0.6f, 0.4f, 1f), Use = UseBlink, Offensive = false },
            };
            Ult = new Technique { Name = "Небесный разлом", Short = "УЛЬТА", Icon = "m_ult", Color = Violet, Use = f2 => { f2.StartCoroutine(UltRoutine()); return true; } };
        }

        Move DoublePalm()
        {
            var m = new Move
            {
                Name = "doublepalm", Windup = PoseLib.BeamWind, Strike = PoseLib.Beam, Startup = 0.14f, Active = 0.09f, Recovery = 0.26f,
                Damage = 11f, Bone = HitBone.HandR, Radius = 0.55f, Reach = new Vector3(0, 0, 0.2f), Knockback = 6f, Kind = HitKind.Heavy,
                Lunge = 2.5f, FxColor = Violet, SlashFx = false, Range = 1.6f, HitStop = 0.07f
            };
            return m;
        }

        // ---------- absorption ----------
        void OnAbsorbHeld(Fighter f, bool held)
        {
            if (held && !absorbing)
            {
                if (!f.CanAct || (!f.InfiniteChakra && f.Chakra < 5f)) return;
                absorbing = true;
                f.EnterChannel();
                f.ForceAnimMode(AnimMode.Custom, AbsorbPose);
                Sfx.Play(SfxId.Absorb, 0.5f, 1.4f);
            }
            else if (!held && absorbing) StopAbsorb();
        }

        static readonly Pose AbsorbPose = PoseLib.PalmR.Clone().S(B.HandR, -85f).S(B.UArmL, 10, 0, -20).S(B.LArmL, -50);

        void StopAbsorb()
        {
            absorbing = false;
            F.ExitChannel();
            F.ForceAnimMode(AnimMode.Locomotion);
        }

        public override void Tick(float dt)
        {
            if (absorbing)
            {
                if (F.State != FState.Channel) { absorbing = false; F.ForceAnimMode(AnimMode.Locomotion); }
                else
                {
                    if (!F.InfiniteChakra) F.Chakra -= 5f * dt;
                    if (F.Chakra <= 0.5f) StopAbsorb();
                    absorbFx -= dt;
                    if (absorbFx <= 0f)
                    {
                        absorbFx = 0.05f;
                        Vector3 hand = F.Rig.BonePos(B.HandR) + F.transform.forward * 0.15f;
                        Fx.Glow(hand, Violet, 0.7f + Mathf.Sin(Time.time * 20f) * 0.15f, 0.08f);
                    }
                }
            }
            if (Stored > 0f && Random.value < 0.15f + Stored / MaxStored * 0.5f)
                Fx.Trail(F.Rig.BonePos(B.HandL), Color.Lerp(Violet, Color.white, Stored / MaxStored), 0.15f + Stored / MaxStored * 0.35f);
        }

        public override bool TryAbsorb(ref HitInfo h)
        {
            if (!absorbing) return false;
            if (Vector3.Dot(F.transform.forward, -h.Dir) < 0.25f) return false; // only from the front
            float amount = h.Damage;
            Stored = Mathf.Min(MaxStored, Stored + amount);
            F.Chakra = Mathf.Min(F.MaxChakra, F.Chakra + amount * 0.3f);
            Vector3 hand = F.Rig.BonePos(B.HandR);
            Fx.Burst(hand, Violet, 0.9f, 0.25f);
            Fx.Ring(hand, Color.white, 0.1f, 1.4f, 0.25f, Quaternion.LookRotation(F.transform.up, F.transform.forward));
            Sfx.Play(SfxId.Absorb, 0.9f);
            WorldLighting.Flash(hand, Violet * 2f, 5f, 0.3f);
            if (F.IsPlayer) { Haptics.Light(); Hud.Popup("ПОГЛОЩЕНО +" + Mathf.RoundToInt(amount), Violet); }
            return true;
        }

        // ---------- release ----------
        bool UseRelease(Fighter f)
        {
            if (absorbing) StopAbsorb();
            float s = Stored;
            Stored = 0f;
            var m = MoveLib.Cast("release", PoseLib.PalmLWind, PoseLib.PalmL, 0.22f, s > 0f ? 0.9f : 0.3f, ff =>
            {
                if (s > 0f)
                {
                    float total = 25f + s * 1.6f;
                    var b = Beam.Fire(ff, ff.Rig.Bones[(int)B.HandL], new Vector3(0, 0, 0.25f), 16f, 0.55f + s / 260f, 0.85f, total / 8f, Color.Lerp(Violet, Color.white, 0.3f));
                    b.Knock = 2f + s / 60f;
                    Sfx.Play(SfxId.Release, 1f, 0.9f);
                    CameraRig.Shake(0.3f + s / 500f);
                    if (ff.IsPlayer) Hud.Popup("ВЫБРОС ×" + (total / 25f).ToString("0.0"), Violet);
                }
                else
                {
                    var p = Projectile.Spawn(ff, ProjStyle.Orb, ff.Rig.BonePos(B.HandL) + ff.transform.forward * 0.4f, MoveLib.AimDir(ff) * 18f, 16f, Violet, 0.9f);
                    p.Power = 2f; p.Knockback = 5f;
                    Sfx.Play(SfxId.Energy, 0.9f);
                }
            });
            f.StartMove(m);
            return true;
        }

        bool UseRods(Fighter f)
        {
            if (absorbing) StopAbsorb();
            var m = MoveLib.Cast("rods", PoseLib.ThrowWind, PoseLib.Throw, 0.25f, 0.3f, ff =>
            {
                Vector3 fwd = MoveLib.AimDir(ff);
                for (int i = -1; i <= 1; i++)
                {
                    Vector3 d = Quaternion.Euler(0, i * 9f, 0) * fwd;
                    var p = Projectile.Spawn(ff, ProjStyle.Rod, ff.Rig.BonePos(B.HandR) + d * 0.4f, d * 24f, 9f, new Color(0.6f, 0.55f, 0.75f), 1f);
                    p.Absorbable = false; p.Power = 1f; p.Knockback = 1.5f; p.Hitstun = 0.5f;
                    p.OnHitFighter = (pr, v) =>
                    {
                        v.ApplyDrain(pr.Owner, 10f, 5f, 0.65f);
                        Fx.Burst(v.Center, new Color(0.6f, 0.5f, 0.8f), 1f, 0.3f);
                        if (pr.Owner != null && pr.Owner.IsPlayer) Hud.Popup("ЧАКРА ПОХИЩЕНА", new Color(0.6f, 0.8f, 1f));
                    };
                }
                Sfx.Play(SfxId.Whoosh, 1f, 1.2f);
            });
            f.StartMove(m);
            return true;
        }

        bool UseBlink(Fighter f)
        {
            if (absorbing) StopAbsorb();
            Vector3 from = f.transform.position;
            Vector3 to;
            if (f.Target != null && f.Target.Alive && KMath.Flat(f.Target.transform.position - from).magnitude < 20f)
                to = f.Target.transform.position - f.Target.transform.forward * 1.3f;
            else
                to = from + f.transform.forward * 6f;
            Fx.Smoke(f.Center, 0.7f, new Color(0.5f, 0.3f, 0.8f, 0.5f), 8);
            Fx.Burst(f.Center, Violet, 1.2f, 0.2f);
            f.Teleport(to);
            if (f.Target != null) f.FaceInstant(f.Target.transform.position);
            Fx.Burst(f.Center, Violet, 1.2f, 0.25f);
            f.SetInvulnerable(0.35f);
            f.GrantCounter(0.8f);
            Sfx.Play(SfxId.Teleport, 1f);
            return true;
        }

        // ---------- ultimate ----------
        IEnumerator UltRoutine()
        {
            var f = F;
            if (absorbing) StopAbsorb();
            var t = f.Target;
            Vector3 c = t != null ? t.transform.position : f.transform.position + f.transform.forward * 6f;
            f.Shout(UltLine);
            f.EnterCinematic();
            f.SetInvulnerable(4f);
            f.Anim.Play(Clip.Hold(PoseLib.Overhead, 0.2f, 2.2f, 0.3f));
            CameraRig.Cinematic(c + Vector3.up * 2f, (f.transform.position - c).normalized + Vector3.up * 0.2f, 14f, 5f, 3.4f);
            Sfx.Play(SfxId.Charge, 1f, 0.6f);
            for (int i = 0; i < 12; i++)
            {
                Vector3 p = c + new Vector3(Random.Range(-4.5f, 4.5f), 0, Random.Range(-4.5f, 4.5f));
                Hazard.Spawn(f, HazardKind.VoidImplosion, p, 1.6f, 0.35f + i * 0.08f, 14f, Violet);
                var rod = Projectile.Spawn(f, ProjStyle.Rod, p + Vector3.up * 14f, Vector3.down * 14f / (0.35f + i * 0.08f), 0f, new Color(0.65f, 0.55f, 0.85f), 1.6f);
                rod.Harmless = true; rod.Life = 0.35f + i * 0.08f;
                yield return new WaitForSeconds(0.04f);
            }
            yield return new WaitForSeconds(0.9f);
            Hazard.Spawn(f, HazardKind.VoidImplosion, c, 5.5f, 0.5f, 95f, Color.white);
            Fx.Lightning(c + Vector3.up * 30f, c, Violet, 1.2f, 0.7f);
            Fx.Lightning(c + Vector3.up * 30f + Vector3.right, c, Color.white, 0.5f, 0.7f);
            yield return new WaitForSeconds(0.5f);
            Fx.Explosion(c, 5.5f, Violet);
            Destructible.ImpactAt(c, 8f, 400f, f);
            CameraRig.Shake(1f);
            Haptics.Heavy();
            f.ExitCinematic();
            yield return new WaitForSeconds(0.4f);
            CameraRig.EndCinematic();
        }

        public override IEnumerator Finisher(Fighter t)
        {
            var f = F;
            Vector3 c = t.transform.position;
            CameraRig.Cinematic(c + Vector3.up * 1.4f, Vector3.Cross(Vector3.up, KMath.Flat(c - f.transform.position).normalized), 5f, 0.8f, 2.6f);
            f.FaceInstant(c);
            for (int i = 0; i < 4; i++)
            {
                f.Anim.Play(Clip.Hold(PoseLib.Throw, 0.04f, 0.06f, 0.04f));
                Projectile.Spawn(f, ProjStyle.Rod, f.Rig.BonePos(B.HandR), (t.Center - f.Rig.BonePos(B.HandR)).normalized * 30f, 0f, new Color(0.65f, 0.55f, 0.85f), 1f).Harmless = true;
                yield return new WaitForSeconds(0.1f);
                Fx.Hit(t.Center, Violet, false);
                t.Anim.Flinch(1f, false);
            }
            f.Anim.Play(Clip.Hold(AbsorbPose, 0.1f, 0.8f, 0.2f));
            for (int i = 0; i < 16; i++)
            {
                t.Teleport(c + Vector3.up * (i * 0.08f));
                Fx.Trail(Vector3.Lerp(t.Center, f.Rig.BonePos(B.HandR), Random.value), Violet, 0.4f);
                yield return new WaitForSeconds(0.04f);
            }
            t.Kill(f);
            Fx.Burst(t.Center, Violet, 3f, 0.4f);
            Fx.Explosion(t.Center, 2.5f, Violet);
            Stored = Mathf.Min(MaxStored, Stored + 120f);
            Sfx.Play(SfxId.Release, 1f, 0.6f);
            CameraRig.Shake(0.7f);
            yield return new WaitForSeconds(0.5f);
        }
    }
}
