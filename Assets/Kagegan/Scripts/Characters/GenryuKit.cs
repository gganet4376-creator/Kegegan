using System.Collections;
using UnityEngine;

namespace Kagegan
{
    /// <summary>
    /// Genryu — heavy hitter. Great Fireball, Flame Wave, Guardian Spirit (giant spectral armour: damage reduction,
    /// super armour, giant-arm area strikes), Counter Stance, and the "Heaven Fall" meteor that levels the arena.
    /// </summary>
    public class GenryuKit : Kit
    {
        static readonly Color Fire = new Color(1f, 0.5f, 0.15f);
        static readonly Color Spirit = new Color(0.45f, 0.55f, 1f);
        bool guardian;
        float counterUntil;
        GameObject guardianGo;
        Move counterStrike;

        public bool GuardianActive => guardian;
        public override float OutgoingMul(Move m) => guardian && m != null && m.Kind != HitKind.Light ? 1.4f : 1f;

        public override void Setup(Fighter f)
        {
            base.Setup(f);
            AuraColor = Spirit;
            f.HitColor = new Color(1f, 0.6f, 0.3f);
            f.Weight = 1.3f;
            IntroLine = "Гэнрю: «Покажи мне, на что ты способен.»";
            WinLine = "Гэнрю: «Слишком рано тебе меня одолеть.»";
            UltLine = "Гэнрю: «Падение небес!»";

            LightChain = new[] { MoveLib.Jab(true, 9f, 0.95f), MoveLib.Hook(false, 11f), MoveLib.Elbow(14f), Stomp() };
            LightChain[2].Knockdown = false; LightChain[2].Knockback = 4f;
            foreach (var m in LightChain) { m.OnActive += GuardianSwing; }
            AirChain = new[] { MoveLib.AirPunch(true, 8f), MoveLib.AirKick(9f), MoveLib.AirPunch(false, 9f) };
            AirFinisher = MoveLib.Slam(20f);
            AirFinisher.OnActive += GuardianSwing;
            Launcher = MoveLib.Uppercut(13f, 11f);
            Launcher.OnActive += GuardianSwing;
            Heavy = MoveLib.Palm(true, 22f);
            Heavy.Name = "heavypalm"; Heavy.Kind = HitKind.Heavy; Heavy.Knockback = 11f; Heavy.Knockdown = true; Heavy.Armor = true;
            Heavy.Startup = 0.24f; Heavy.Recovery = 0.32f; Heavy.Shake = 0.35f; Heavy.HitStop = 0.1f; Heavy.SlashFx = true; Heavy.StaminaCost = 10f; Heavy.StaminaDamage = 30f;
            Heavy.OnActive += GuardianSwing;
            DashAttack = MoveLib.Elbow(18f);
            DashAttack.Lunge = 7f; DashAttack.Armor = true;
            Counter = MoveLib.Elbow(26f);
            Counter.Unblockable = true; Counter.FxColor = Fire;
            counterStrike = MoveLib.Palm(true, 34f);
            counterStrike.Name = "counterstrike"; counterStrike.Startup = 0.06f; counterStrike.Kind = HitKind.Heavy; counterStrike.Knockback = 14f; counterStrike.Knockdown = true;
            counterStrike.Unblockable = true; counterStrike.FxColor = Fire; counterStrike.Shake = 0.5f; counterStrike.HitStop = 0.14f; counterStrike.Lunge = 6f;
            MoveLib.WireGrab(this, 28f, 12f, 7f);

            Techs = new[]
            {
                new Technique { Name = "Огненный шар", Short = "Огонь", Cost = 30f, Cooldown = 6f, Range = 16f, Color = Fire, Use = UseFireball },
                new Technique { Name = "Огненная волна", Short = "Волна", Cost = 35f, Cooldown = 9f, Range = 12f, Color = new Color(1f, 0.35f, 0.1f), Use = UseFlameWave },
                new Technique { Name = "Дух-страж", Short = "Страж", Cost = 25f, Cooldown = 3f, Range = 30f, Color = Spirit, Use = ToggleGuardian, Offensive = false },
                new Technique { Name = "Контрстойка", Short = "Контр", Cost = 15f, Cooldown = 4f, Range = 4f, Color = new Color(1f, 0.9f, 0.5f), Use = UseCounterStance },
            };
            Ult = new Technique { Name = "Падение небес", Short = "УЛЬТА", Color = Fire, Use = f2 => { f2.StartCoroutine(UltRoutine()); return true; } };
        }

        Move Stomp()
        {
            var m = MoveLib.Sweep(16f);
            m.Name = "stomp"; m.Windup = PoseLib.KickWind; m.Strike = PoseLib.Stomp; m.Bone = HitBone.FootL; m.Launch = 7f; m.Radius = 0.9f;
            m.OnActive = f => { Fx.Dust(f.transform.position + f.transform.forward, 1.4f, 10); Fx.Crack(f.transform.position + f.transform.forward * 0.8f, 1.6f); CameraRig.Shake(0.3f); };
            return m;
        }

        void GuardianSwing(Fighter f)
        {
            if (!guardian) return;
            Vector3 p = f.transform.position + f.transform.forward * 2.2f + Vector3.up;
            Fx.Slash(f.transform, new Vector3(0, 1.4f, 1.4f), new Vector3(0, 0, Random.Range(-40f, 40f)), Spirit, 3.2f, 0.25f);
            Combat.AreaDamage(f, f.Team, p, 2.2f, 10f, 9f, 2f, false, Spirit, true, HitKind.Heavy);
            Destructible.ImpactAt(p, 2.5f, 40f, f);
        }

        public override void Tick(float dt)
        {
            if (guardian)
            {
                if (!F.InfiniteChakra) F.Chakra -= 7f * dt;
                if (F.Chakra <= 2f || !F.Alive) SetGuardian(false);
                else if (guardianGo != null)
                {
                    guardianGo.transform.position = F.transform.position;
                    guardianGo.transform.rotation = F.transform.rotation;
                    float pulse = 1f + Mathf.Sin(Time.time * 4f) * 0.02f;
                    guardianGo.transform.localScale = Vector3.one * pulse;
                }
            }
            if (Time.time < counterUntil && Random.value < 0.4f) Fx.Trail(F.Center + Random.insideUnitSphere * 0.6f, new Color(1f, 0.9f, 0.5f), 0.3f);
        }

        public override bool Intercept(ref HitInfo h)
        {
            if (Time.time >= counterUntil || h.Attacker == null) return false;
            counterUntil = 0f;
            var atk = h.Attacker;
            Fx.Parry(h.Point);
            Sfx.Play(SfxId.Parry, 1f, 0.8f);
            TimeFx.SlowMo(0.4f, 0.35f);
            F.ExitChannel();
            if (h.Proj == null)
            {
                F.Teleport(atk.transform.position - atk.transform.forward * 0.1f + (F.transform.position - atk.transform.position).normalized * 1.1f);
                F.FaceInstant(atk.transform.position);
                F.Target = atk;
                F.StartMove(counterStrike);
            }
            F.Shout("Гэнрю: «Предсказуемо.»");
            return true;
        }

        public override void OnDeath() { SetGuardian(false); }

        // ---------- techniques ----------
        bool UseFireball(Fighter f)
        {
            var m = MoveLib.Cast("fireball", PoseLib.BeamWind, PoseLib.Beam, 0.4f, 0.35f, ff =>
            {
                Vector3 dir = MoveLib.AimDir(ff); dir.y = Mathf.Clamp(dir.y, -0.15f, 0.15f);
                var p = Projectile.Spawn(ff, ProjStyle.Fire, ff.transform.position + Vector3.up * 1.3f + ff.transform.forward * 1.2f, dir.normalized * 11f, 38f, Fire, guardian ? 3f : 2.3f);
                p.Power = 5f; p.Explode = true; p.ExplodeRadius = 3.2f; p.Knockback = 9f; p.Knockdown = true; p.Life = 2.6f;
                Sfx.Play(SfxId.Fire, 1f, 0.8f);
                WorldLighting.Flash(ff.Center + ff.transform.forward, Fire * 2f, 7f, 0.5f);
            });
            m.OnTick = (ff, t) => { if (ff.MoveTime < 0.4f) Fx.Fire(ff.Center + ff.transform.forward * 0.6f, 0.4f, 1); };
            f.StartMove(m);
            f.Shout("Гэнрю: «Огненный шар!»");
            return true;
        }

        bool UseFlameWave(Fighter f)
        {
            var m = MoveLib.Cast("flamewave", PoseLib.BeamWind, PoseLib.Beam, 0.35f, 0.5f, ff =>
            {
                Vector3 fwd = MoveLib.AimDir(ff); fwd.y = 0f; fwd.Normalize();
                for (int i = -3; i <= 3; i++)
                {
                    Vector3 d = Quaternion.Euler(0, i * 9f, 0) * fwd;
                    var p = Projectile.Spawn(ff, ProjStyle.Fire, ff.transform.position + Vector3.up * 1f + d * 1f, d * 14f, 11f, new Color(1f, 0.45f, 0.1f), 1.1f);
                    p.Power = 2f; p.Knockback = 4f; p.Life = 1f; p.Pierce = true;
                }
                Hazard.Spawn(ff, HazardKind.FireField, ff.transform.position + fwd * 5f, 2.6f, 0.4f, 30f, Fire, 2.5f);
                Sfx.Play(SfxId.Fire, 1f, 0.7f);
                CameraRig.Shake(0.25f);
            });
            f.StartMove(m);
            return true;
        }

        bool ToggleGuardian(Fighter f)
        {
            if (guardian && !f.InfiniteChakra) f.Chakra += 25f; // dismissing the guardian is free
            SetGuardian(!guardian);
            return true;
        }

        void SetGuardian(bool on)
        {
            if (on == guardian) return;
            guardian = on;
            F.DamageReduction = on ? 0.5f : 0f;
            F.SuperArmor = on;
            if (on)
            {
                if (guardianGo == null) guardianGo = BuildGuardian();
                guardianGo.SetActive(true);
                Fx.Burst(F.Center, Spirit, 3f, 0.5f);
                Fx.Ring(F.transform.position + Vector3.up * 0.1f, Spirit, 0.5f, 7f, 0.6f, Quaternion.identity);
                Sfx.Play(SfxId.Roar, 0.7f, 1.3f);
                CameraRig.Shake(0.4f);
                F.Shout("Гэнрю: «Дух-страж, восстань!»");
            }
            else if (guardianGo != null)
            {
                guardianGo.SetActive(false);
                Fx.Smoke(F.Center, 1.2f, new Color(0.5f, 0.6f, 1f, 0.4f), 8);
            }
        }

        GameObject BuildGuardian()
        {
            // translucent giant upper body (ribcage + skull + arms) around the fighter, one additive draw call
            var mb = new MeshBuilder();
            Color32 c = new Color32(110, 140, 255, 200), dim = new Color32(60, 80, 200, 120);
            mb.SetColor(dim);
            mb.Limb(new Vector3(0, 0.6f, -0.2f), new Vector3(0, 3.1f, -0.2f), 0.25f, 0.3f, 8, false);
            mb.SetColor(c);
            for (int i = 0; i < 5; i++)
            {
                float y = 1.3f + i * 0.35f;
                mb.Limb(new Vector3(-0.9f + i * 0.04f, y, 0.25f), new Vector3(0.9f - i * 0.04f, y, 0.25f), 0.06f, 0.06f, 5, false);
            }
            mb.Sphere(new Vector3(0, 3.5f, 0), new Vector3(0.5f, 0.6f, 0.55f), 6, 8);
            mb.SetColor(new Color32(255, 220, 160, 255));
            mb.Sphere(new Vector3(-0.18f, 3.55f, 0.45f), 0.08f, 3, 5);
            mb.Sphere(new Vector3(0.18f, 3.55f, 0.45f), 0.08f, 3, 5);
            mb.SetColor(c);
            mb.Limb(new Vector3(-1.1f, 2.7f, 0), new Vector3(-1.9f, 1.6f, 0.9f), 0.25f, 0.2f, 6, true);
            mb.Limb(new Vector3(-1.9f, 1.6f, 0.9f), new Vector3(-1.5f, 1.5f, 2.2f), 0.2f, 0.32f, 6, true);
            mb.Limb(new Vector3(1.1f, 2.7f, 0), new Vector3(1.9f, 1.6f, 0.9f), 0.25f, 0.2f, 6, true);
            mb.Limb(new Vector3(1.9f, 1.6f, 0.9f), new Vector3(1.5f, 1.5f, 2.2f), 0.2f, 0.32f, 6, true);
            mb.Box(new Vector3(-1.1f, 2.8f, 0), new Vector3(0.8f, 0.25f, 0.8f), Quaternion.Euler(0, 0, 20));
            mb.Box(new Vector3(1.1f, 2.8f, 0), new Vector3(0.8f, 0.25f, 0.8f), Quaternion.Euler(0, 0, -20));
            var go = new GameObject("GuardianSpirit");
            go.AddComponent<MeshFilter>().sharedMesh = mb.ToMesh("guardian");
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = Mat.Ghost;
            var mpb = new MaterialPropertyBlock();
            mpb.SetColor("_Color", new Color(0.55f, 0.6f, 1f, 1f) * (GraphicsQuality.Current == GraphicsPreset.Low ? 0.45f : 0.6f));
            r.SetPropertyBlock(mpb);
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var link = F.gameObject.AddComponent<BlobLink>();
            link.blob = go;
            return go;
        }

        bool UseCounterStance(Fighter f)
        {
            counterUntil = Time.time + 0.9f;
            f.Anim.Play(Clip.Hold(PoseLib.Block, 0.05f, 0.8f, 0.1f));
            Fx.Glow(f.Center, new Color(1f, 0.9f, 0.5f), 1.5f, 0.2f);
            return true;
        }

        // ---------- ultimate ----------
        IEnumerator UltRoutine()
        {
            var f = F;
            Vector3 target = f.Target != null ? f.Target.transform.position : f.transform.position + f.transform.forward * 6f;
            f.Shout(UltLine);
            f.EnterCinematic();
            f.SetInvulnerable(3f);
            f.Anim.Play(Clip.Hold(PoseLib.Overhead, 0.15f, 1.4f, 0.3f));
            CameraRig.Cinematic(f.Center + Vector3.up * 2f, -f.transform.forward + Vector3.up * 0.3f, 6.5f, -0.5f, 1.2f);
            Sfx.Play(SfxId.Roar, 1f, 0.7f);
            WorldLighting.Flash(f.Center + Vector3.up * 3f, Fire * 2f, 10f, 1.2f);
            yield return new WaitForSeconds(0.9f);
            f.ExitCinematic();
            float delay = 1.6f;
            Hazard.Spawn(f, HazardKind.Meteor, target, 7f, delay, 130f, Fire);
            Vector3 start = target + new Vector3(-18f, 34f, 10f);
            var p = Projectile.Spawn(f, ProjStyle.Meteor, start, (target - start) / delay, 0f, Fire, 7f);
            p.Harmless = true; p.Life = delay;
            CameraRig.Cinematic(target, (f.transform.position - target).normalized + Vector3.back * 0.3f, 16f, 6f, delay + 0.8f);
            yield return new WaitForSeconds(delay);
            Fx.Explosion(target, 7f, Fire);
            Fx.Ring(target + Vector3.up * 0.2f, Color.white, 2f, 26f, 0.9f, Quaternion.identity);
            for (int i = 0; i < 6; i++) Fx.Crack(target + Random.insideUnitSphere * 5f, 3f + Random.value * 2f);
            Destructible.ImpactAt(target, 14f, 999f, f);
            CameraRig.Shake(1f);
            Haptics.Heavy();
            Sfx.Play(SfxId.Explosion, 1f, 0.6f);
            yield return new WaitForSeconds(0.6f);
            CameraRig.EndCinematic();
        }

        public override IEnumerator Finisher(Fighter t)
        {
            var f = F;
            bool had = guardian;
            if (!had) SetGuardian(true);
            Vector3 c = t.transform.position;
            CameraRig.Cinematic(c + Vector3.up * 1.5f, Vector3.Cross(Vector3.up, KMath.Flat(c - f.transform.position).normalized), 7f, 1.5f, 2.4f);
            f.FaceInstant(c);
            f.Anim.Play(Clip.Hold(PoseLib.SlamWind, 0.2f, 0.3f, 0.1f));
            t.Teleport(c + Vector3.up * 1.2f);
            Fx.Burst(t.Center, Spirit, 2f, 0.4f);
            yield return new WaitForSeconds(0.55f);
            f.Anim.Play(Clip.Hold(PoseLib.Slam, 0.05f, 0.4f, 0.2f));
            t.Teleport(c);
            t.Kill(f);
            Fx.Explosion(c, 4f, Spirit);
            Fx.Slash(f.transform, new Vector3(0, 2.5f, 1.5f), new Vector3(90, 0, 0), Spirit, 4f, 0.3f);
            Destructible.ImpactAt(c, 6f, 300f, f);
            CameraRig.Shake(0.9f);
            Sfx.Play(SfxId.Explosion, 1f, 0.7f);
            yield return new WaitForSeconds(0.6f);
            if (!had) SetGuardian(false);
        }
    }
}
