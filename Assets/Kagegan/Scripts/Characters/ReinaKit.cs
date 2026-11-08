using System.Collections;
using UnityEngine;

namespace Kagegan
{
    /// <summary>
    /// Reina — original cyber-netrunner. Monowire whips from the right hand, acrobatic kicks, quickhacks that lock
    /// enemy techniques (System Shock) or overload their reflexes (Neural Overload), a wire capture that pulls the
    /// target into a synced kick, and the "Deep Link" ultimate: wires strike from every direction, launch, finisher.
    /// Bosses get shorter locks/slows (handled in Fighter.ApplyHack / ApplyOverload).
    /// </summary>
    public class ReinaKit : Kit
    {
        public static readonly Color Wire = new Color(0.35f, 1f, 0.55f);    // monowire glow (acid green)
        public static readonly Color Hack = new Color(0.55f, 1f, 0.35f);
        public static readonly Color Over = new Color(1f, 0.3f, 0.75f);     // overload (magenta)

        // captured clips (KayKit Character Animations, CC0); every use falls back to the procedural pose if the clip is missing
        const string KickClip = "KK_Melee_Unarmed_Attack_Kick";
        const string ShootClip = "KK_Ranged_Magic_Shoot", ThrowClip = "KK_Throw";

        public override void Setup(Fighter f)
        {
            base.Setup(f);
            AuraColor = Wire;
            f.HitColor = new Color(0.7f, 1f, 0.75f);
            IntroLine = "Рэйна: «Я уже в твоей системе.»";
            WinLine = "Рэйна: «Соединение разорвано.»";
            UltLine = "Рэйна: «Глубокое погружение!»";

            LightChain = new[]
            {
                MoveLib.Jab(false, 5f, 1.2f),
                MoveLib.Jab(true, 5f, 1.2f),
                WireLash(9f, 1f),
                WithKick(MoveLib.Kick(9f, 3f)),
                SpinKick()
            };
            AirChain = new[] { MoveLib.AirKick(6f), MoveLib.AirPunch(true, 6f), MoveLib.AirKick(7f), WireLashAir() };
            AirFinisher = WireDive();
            Launcher = RisingKick();
            Heavy = CyberKick();
            DashAttack = SlideKick();
            Counter = SpinKick();
            Counter.Name = "counter"; Counter.Damage = 16f; Counter.Unblockable = true; Counter.FxColor = Wire;
            MoveLib.WireGrab(this, 20f, 10f, 6f);
            Grab.OnStart = ff => Lash(ff.transform.position + ff.transform.forward * 1.3f + Vector3.up * 1.1f, Wire, 0.22f, 0.3f);

            Techs = new[]
            {
                new Technique { Name = "Монострунный хлыст", Short = "Хлыст", Icon = "r_wire", Cost = 20f, Cooldown = 4f, Range = 4.8f, Color = Wire, Use = UseWhip },
                new Technique { Name = "Захват струной", Short = "Захват", Icon = "r_capture", Cost = 30f, Cooldown = 9f, Range = 9f, Color = Wire, Use = UseCapture },
                new Technique { Name = "Системный шок", Short = "Шок", Icon = "r_shock", Cost = 35f, Cooldown = 11f, Range = 12f, Color = Hack, Use = UseShock },
                new Technique { Name = "Нейроперегрузка", Short = "Перегруз", Icon = "r_overload", Cost = 40f, Cooldown = 18f, Range = 13f, Color = Over, Use = UseOverload },
            };
            Ult = new Technique { Name = "Глубокое погружение", Short = "УЛЬТА", Icon = "r_ult", Color = Wire, Use = ff => { ff.StartCoroutine(UltRoutine()); return true; } };
        }

        Transform Hand => F.Rig.Bones[(int)B.HandR];

        // the monowire is a physical whip in her right hand (verlet lash); the old VFX line is the fallback
        Whip whip;

        public override void Tick(float dt)
        {
            base.Tick(dt);
            if (whip == null && F != null && F.Rig != null && Hand != null) whip = Whip.Attach(F, Wire);
        }

        void Lash(Vector3 to, Color c, float life, float sweep)
        {
            // called on the contact frame: a fast roll so the tip snaps almost at once (a real cracker breaks the sound barrier)
            if (whip != null) whip.Strike(to, Mathf.Clamp(life * 0.8f, 0.18f, 0.32f), sweep);
            else Fx.Wire(Hand, to, c, life, sweep);
        }

        void Tether(Vector3 to)
        {
            if (whip != null) whip.Hold(to);
            else Fx.Wire(Hand, to, Wire, 0.06f, 0f);
        }

        static Move WithKick(Move m)
        {
            m.Mocap = KickClip; m.MocapContact = MocapLibrary.KickContact;
            return m;
        }

        /// <summary>Plays a captured clip for scripted sequences, or the pose clip when mocap is unavailable.</summary>
        void Act(string clip, float speed, float start, float duration, Clip fallback, float poseSpeed = 1f)
        {
            if (F.Grounded && F.Anim.PlayMocap(clip, speed, start, false, duration, 0.05f)) return;
            F.Anim.Play(fallback, poseSpeed);
        }

        // ---------- basic moves ----------
        Move WireLash(float dmg, float sweep)
        {
            var m = MoveLib.Hook(true, dmg);
            m.Name = "wirelash"; m.Range = 3.2f; m.Reach = new Vector3(0, 0, 1.6f); m.Radius = 0.7f; m.Lunge = 1f;
            m.Knockback = 3.5f; m.FxColor = Wire; m.SlashEuler = new Vector3(0, 0, 75); m.Sfx = SfxId.Whoosh;
            m.OnActive = ff => Lash(ff.transform.position + ff.transform.forward * 3f + Vector3.up * 1.05f, Wire, 0.26f, sweep);
            return m;
        }

        Move WireLashAir()
        {
            var m = WireLash(8f, -1f);
            m.Name = "airwire"; m.Launch = 3.4f; m.Hitstun = 0.45f; m.CancelAt = 0.5f; m.Mocap = null;
            m.Windup = PoseLib.AirUp; m.Strike = PoseLib.HookR;
            return m;
        }

        Move SpinKick()
        {
            var m = MoveLib.Kick(12f, 7f);
            m.Name = "spinkick"; m.Strike = PoseLib.KickHigh.Mirror(); m.Bone = HitBone.FootL; m.Kind = HitKind.Heavy; m.Knockdown = true;
            m.Shake = 0.25f; m.SlashEuler = new Vector3(0, 0, -80); m.Radius = 0.6f; m.FxColor = Wire;
            m.Mocap = null; // no captured left spin kick in the CC0 packs: the procedural mirrored high kick stays
            m.OnStart = ff => Fx.Slash(ff.transform, new Vector3(0, 0.9f, 0.5f), new Vector3(0, 0, -80), Wire, 1.6f, 0.2f);
            return m;
        }

        /// <summary>Cyber Kick: charged cybernetic leg, heavy knockback, breaks guard stamina.</summary>
        Move CyberKick()
        {
            var m = WithKick(MoveLib.FrontKick(17f));
            m.Name = "cyberkick"; m.Knockback = 10f; m.StaminaDamage = 24f; m.FxColor = Wire; m.HitSfx = SfxId.HeavyHit;
            m.OnTick = (ff, t) => { if (ff.InStartup && Random.value < 0.5f) Fx.Electric(ff.Rig.BonePos(B.FootR), Wire, 0.45f, 1); };
            m.OnHit = (a, v) => { Fx.Electric(v.Center, Wire, 1.2f, 3); Fx.Ring(v.Center, Wire, 0.3f, 2f, 0.3f, Quaternion.LookRotation(a.transform.forward)); };
            return m;
        }

        /// <summary>Cyber Kick (launcher variant): rising crescent kick that pops the target up for air combos.</summary>
        Move RisingKick()
        {
            var m = WithKick(MoveLib.Kick(10f, 1f));
            m.Name = "launcher"; m.Launch = 12f; m.Kind = HitKind.Heavy; m.Strike = PoseLib.KickHigh; m.SlashEuler = new Vector3(-90, 0, 0);
            m.Radius = 0.55f; m.FxColor = Wire; m.HitStop = 0.08f; m.StaminaCost = 6f;
            m.OnHit = (a, v) => Fx.Electric(v.Center, Wire, 0.9f, 2);
            return m;
        }

        Move SlideKick()
        {
            var m = MoveLib.Sweep(12f);
            m.Name = "slidekick"; m.Lunge = 7f; m.Knockback = 3f; m.Launch = 6f; m.FxColor = Wire;
            m.Mocap = "Slide_Start"; m.MocapContact = 0.15f;
            return m;
        }

        /// <summary>Aerial Wire Dive: flip over the target and bring the wire down overhead, slamming it into the ground.</summary>
        Move WireDive()
        {
            var m = MoveLib.Slam(18f);
            m.Name = "wiredive"; m.Startup = 0.2f; m.Reach = new Vector3(0, -0.4f, 1f); m.Radius = 0.8f; m.FxColor = Wire;
            m.Windup = PoseLib.RollBack; m.Strike = PoseLib.Slam; m.Shake = 0.4f;
            m.OnActive = ff =>
            {
                Vector3 p = ff.Target != null ? ff.Target.transform.position : ff.transform.position + ff.transform.forward * 1.5f;
                Lash(p, Wire, 0.28f, 0.2f);
                Fx.Slash(ff.transform, new Vector3(0, 0.8f, 0.8f), new Vector3(90, 0, 0), Wire, 2f, 0.22f);
            };
            m.OnHit = (a, v) => { Fx.Electric(v.transform.position + Vector3.up * 0.3f, Wire, 1.4f, 3); Fx.Crack(v.transform.position, 1.6f); };
            return m;
        }

        // ---------- acrobatics ----------
        public override void OnDodge(Vector3 dir)
        {
            // captured directional dodge (forward keeps the shared dash-slide); afterimage streaks
            float fwd = Vector3.Dot(dir, F.transform.forward), side = Vector3.Dot(dir, F.transform.right);
            bool back = fwd < -0.3f;
            string clip = back ? "KK_Dodge_Backward" : fwd > 0.3f ? null : side >= 0f ? "KK_Dodge_Right" : "KK_Dodge_Left";
            bool played = clip == null ? F.Anim.AnyActionPlaying : F.Anim.PlayMocap(clip, 1.25f, 0f, false, 0.32f, 0.05f);
            if (!played) F.Anim.Play(Clip.Hold(back ? PoseLib.RollBack : PoseLib.Dodge, 0.04f, 0.18f, 0.1f), 1.2f);
            for (int i = 0; i < 3; i++) Fx.Trail(F.Center + Random.insideUnitSphere * 0.3f, Wire, 0.35f);
        }

        public override void OnHitLanded(Fighter target, ref HitInfo h)
        {
            // perfect-dodge counters get an extra wire snap
            if (h.Move != null && h.Move.Name == "counter") Lash(target.Center, Wire, 0.2f, -0.6f);
        }

        // ---------- techniques ----------
        bool UseWhip(Fighter f)
        {
            var m = new Move
            {
                Name = "monowire", Windup = PoseLib.HookRWind, Strike = PoseLib.HookR, Startup = 0.16f, Active = 0.16f, Recovery = 0.3f,
                Damage = 24f, Kind = HitKind.Technique, Knockback = 5f, Hitstun = 0.6f, Radius = 1f, Bone = HitBone.Body,
                Reach = new Vector3(0, 0.1f, 2.6f), Range = 4.8f, Lunge = 1.5f, FxColor = Wire, Shake = 0.3f, HitStop = 0.09f,
                SlashEuler = new Vector3(0, 0, 70), Sfx = SfxId.Whoosh, HitSfx = SfxId.HeavyHit, Mocap = "Melee_Hook", MocapContact = MocapLibrary.HookContact
            };
            m.OnActive = ff =>
            {
                Vector3 p = ff.transform.position + ff.transform.forward * 4.4f + Vector3.up * 1f;
                Lash(p, Wire, 0.32f, 1.3f);
                Fx.Slash(ff.transform, new Vector3(0, 1f, 2f), new Vector3(0, 0, 70), Wire, 3.6f, 0.2f);
            };
            m.OnHit = (a, v) => Fx.Electric(v.Center, Wire, 1f, 2);
            f.StartMove(m);
            return true;
        }

        bool UseCapture(Fighter f)
        {
            var t = f.Target;
            if (t == null || !t.Alive || KMath.Flat(t.transform.position - f.transform.position).magnitude > 9.5f) return false;
            f.StartCoroutine(CaptureRoutine(t));
            return true;
        }

        IEnumerator CaptureRoutine(Fighter t)
        {
            var f = F;
            f.EnterChannel();
            f.FaceInstant(t.transform.position);
            Act(ThrowClip, 2f, 0.45f, 0.4f, Clip.Hold(PoseLib.Throw, 0.06f, 0.2f, 0.1f)); // arm fully out ~0.18 s in
            Sfx.Play(SfxId.Whoosh, 1f, 1.3f);
            yield return new WaitForSeconds(0.12f);
            if (Interrupted(f, t)) yield break;
            Lash(t.Center, Wire, 0.5f, 0.4f);
            yield return new WaitForSeconds(0.14f);
            if (Interrupted(f, t)) yield break;
            bool heavy = t.IsBoss || t.Weight > 1.2f;
            Fx.Electric(t.Center, Wire, 1f, 2);
            t.Flash(Wire);
            // heavy targets don't move: Reina zips to them along the wire instead
            Vector3 from = heavy ? f.transform.position : t.transform.position;
            Vector3 to = heavy ? t.transform.position - KMath.Flat(t.transform.position - f.transform.position).normalized * 1.2f
                               : f.transform.position + f.transform.forward * 1.2f;
            var mover = heavy ? f : t;
            if (!heavy) t.Stun(0.6f);
            for (float k = 0f; k < 1f; k += Time.deltaTime / 0.18f)
            {
                if (Interrupted(f, t)) yield break;
                mover.Teleport(Vector3.Lerp(from, to, k * k));
                Tether(t.Center);
                yield return null;
            }
            if (Interrupted(f, t)) yield break;
            f.ExitChannel();
            f.FaceInstant(t.transform.position);
            // synced kick on arrival
            var kick = WithKick(MoveLib.Kick(18f, 9f));
            kick.Name = "capturekick"; kick.Kind = HitKind.Technique;
            kick.Knockdown = !t.IsBoss; kick.Unblockable = true; kick.Startup = 0.08f; kick.Radius = 0.7f; kick.FxColor = Wire; kick.Shake = 0.35f;
            kick.OnHit = (a, v) => { Fx.Electric(v.Center, Wire, 1.2f, 3); Fx.Hit(v.Center, Wire, true); };
            f.StartMove(kick);
        }

        /// <summary>True when Reina was knocked out of the capture (hit, stunned, killed) or the target is gone; releases the channel if still held.</summary>
        static bool Interrupted(Fighter f, Fighter t)
        {
            if (f == null || !f.Alive || f.State != FState.Channel) return true;
            if (t == null || !t.Alive) { f.ExitChannel(); return true; }
            return false;
        }

        bool UseShock(Fighter f)
        {
            var t = f.Target;
            if (t == null || !t.Alive) return false;
            var m = MoveLib.Cast("sysshock", PoseLib.Seal, PoseLib.PalmR, 0.32f, 0.3f, ff =>
            {
                var v = ff.Target;
                if (v == null || !v.Alive) return;
                Fx.Lightning(ff.Rig.BonePos(B.HandR), v.Center, Hack, 0.1f, 0.3f);
                Fx.HackStream(ff.Rig.BonePos(B.HandR), v.Center, Hack, 10);
                Fx.Electric(v.Center, Hack, 1.4f, 4);
                Sfx.Play(SfxId.Energy, 1f, 1.4f);
                var h = new HitInfo { Attacker = ff, Damage = 16f * ff.DamageMul, Stamina = 20f, Knockback = 0.5f, Hitstun = 0.7f, Kind = HitKind.Technique, Unblockable = true, Parryable = false, Point = v.Center, Dir = ff.transform.forward, FxColor = Hack, Shake = 0.25f, HitStop = 0.06f };
                if (v.ReceiveHit(ref h) == HitResult.Hit)
                {
                    v.ApplyHack(4f);
                    if (!v.IsBoss) v.Stun(0.55f);
                    Hud.Popup("СИСТЕМНЫЙ ШОК", Hack);
                }
            });
            m.Mocap = ShootClip; m.MocapContact = 0.26f; // palm stays thrust out from 0.14 to 0.47 s of the clip
            m.OnTick = (ff, u) =>
            {
                if (!ff.InStartup) return;
                if (Random.value < 0.4f) Fx.Electric(ff.Rig.BonePos(B.HandR), Hack, 0.4f, 1);
                // code spins up around the casting hand before the upload
                if (Random.value < 0.5f) Fx.Glyph(ff.Rig.BonePos(B.HandR) + Random.insideUnitSphere * 0.25f, Vector3.up * 0.4f, Hack, 0.1f, 0.35f);
            };
            f.StartMove(m);
            f.Shout("Рэйна: «Системный шок.»");
            return true;
        }

        bool UseOverload(Fighter f)
        {
            var t = f.Target;
            if (t == null || !t.Alive) return false;
            var m = MoveLib.Cast("overload", PoseLib.Charge, PoseLib.Beam, 0.45f, 0.35f, ff =>
            {
                Sfx.Play(SfxId.Energy, 1f, 0.8f);
                Fx.Ring(ff.transform.position + Vector3.up * 0.1f, Over, 0.5f, 7f, 0.5f, Quaternion.identity);
                WorldLighting.Flash(ff.Center, Over * 2f, 8f, 0.5f);
                // hits every enemy in the hack radius: slowed reactions + tech lock (shorter on bosses)
                var all = Fighter.All;
                for (int i = 0; i < all.Count; i++)
                {
                    var v = all[i];
                    if (v == null || !v.Alive || v.Team == ff.Team || v.Team == Team.Neutral) continue;
                    if (KMath.Flat(v.transform.position - ff.transform.position).magnitude > 13f) continue;
                    Fx.Lightning(ff.Center, v.Center, Over, 0.08f, 0.35f);
                    Fx.HackStream(ff.Center, v.Center, Over, 5, 0.35f);
                    // goes through the normal hit path: dodges, invulnerability, armor, stats and KO all apply
                    var h = new HitInfo
                    {
                        Attacker = ff, Damage = 10f * ff.DamageMul, Stamina = 10f, Knockback = 0.3f, Hitstun = 0.25f, Kind = HitKind.Technique,
                        Unblockable = true, Parryable = false, Point = v.Center, Dir = KMath.Flat(v.transform.position - ff.transform.position).normalized,
                        FxColor = Over, Shake = 0.15f, HitStop = 0.03f
                    };
                    if (v.ReceiveHit(ref h) != HitResult.Hit) continue;
                    v.ApplyOverload(ff, 5f, 0.6f);
                    v.ApplyHack(3f);
                    v.Flash(Over);
                }
                Hud.Popup("НЕЙРОПЕРЕГРУЗКА", Over);
            });
            m.OnTick = (ff, u) => { if (ff.InStartup) Fx.Glow(ff.Center, Over, 1.2f, 0.05f); };
            f.StartMove(m);
            f.Shout("Рэйна: «Перегрузка нейросети!»");
            return true;
        }

        // ---------- ultimate: Deep Link ----------
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
            CameraRig.Cinematic(c + Vector3.up * 1.6f, view + Vector3.back * 0.25f, 7f, 1.5f, 3.4f);
            f.Anim.Play(Clip.Hold(PoseLib.Charge, 0.05f, 0.45f, 0.1f));
            Sfx.Play(SfxId.Charge, 1f, 1.3f);
            for (int k = 0; k < 8; k++)
            {
                Fx.Electric(f.Center + Random.insideUnitSphere * 0.5f, Wire, 0.7f, 1);
                if (k % 2 == 0) Fx.HackStream(f.Center, t.Center, Hack, 4, 0.3f); // Deep Link: jacking into the target first
                yield return new WaitForSeconds(0.04f);
            }
            t.ApplyHack(5f);

            // wires from every direction while Reina blinks around the target
            for (int i = 0; i < 6; i++)
            {
                if (!t.Alive) break;
                float a = i / 6f * Mathf.PI * 2f + 0.4f;
                Vector3 p = c + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * 2.6f;
                f.Teleport(p);
                f.FaceInstant(t.transform.position);
                if (i % 2 == 0) Act("Melee_Hook", 2.2f, MocapLibrary.HookContact - 0.08f * 2.2f, 0.16f, Clip.Hold(PoseLib.HookR, 0.03f, 0.1f, 0.06f));
                else Act(KickClip, 2.6f, MocapLibrary.KickContact - 0.08f * 2.6f, 0.16f, Clip.Hold(PoseLib.KickHigh, 0.03f, 0.1f, 0.06f));
                Fx.Trail(f.Center, Wire, 0.5f);
                Lash(t.Center + Random.insideUnitSphere * 0.3f, Wire, 0.22f, i % 2 == 0 ? 1f : -1f);
                yield return new WaitForSeconds(0.08f);
                DealCinematic(t, 9f);
                Fx.Slash(t.transform, new Vector3(0, 1f, 0), new Vector3(0, a * Mathf.Rad2Deg, Random.Range(-60f, 60f)), Wire, 2.2f, 0.16f);
                yield return new WaitForSeconds(0.08f);
            }
            // launch
            t.Teleport(c + Vector3.up * 3.2f);
            Fx.Electric(t.Center, Wire, 1.6f, 4);
            Sfx.Play(SfxId.Whoosh, 1f, 0.7f);
            TimeFx.SlowMo(0.5f, 0.35f);
            yield return new WaitForSeconds(0.2f);
            // finisher: flip above and bring both wires down
            f.Teleport(c + Vector3.up * 4.6f - f.transform.forward * 0.6f);
            f.FaceInstant(t.transform.position);
            f.Anim.Play(Clip.Hold(PoseLib.SlamWind, 0.05f, 0.25f, 0.05f));
            yield return new WaitForSeconds(0.15f);
            f.Anim.Play(Clip.Hold(PoseLib.Slam, 0.04f, 0.4f, 0.2f));
            Lash(new Vector3(c.x, 0f, c.z), Wire, 0.3f, 0.6f);
            Fx.Wire(f.Rig.Bones[(int)B.HandL], new Vector3(c.x, 0f, c.z), Wire, 0.3f, -0.6f);
            t.Teleport(new Vector3(c.x, 0f, c.z));
            f.Teleport(new Vector3(c.x, 0f, c.z) - f.transform.forward * 1f);
            t.ExitCinematic();
            Fx.Electric(t.Center, Wire, 2.2f, 6);
            Fx.Ring(t.transform.position + Vector3.up * 0.1f, Wire, 0.5f, 6f, 0.5f, Quaternion.identity);
            Fx.Crack(t.transform.position, 2.4f);
            WorldLighting.Flash(t.Center, Wire * 2.5f, 9f, 0.5f);
            Sfx.Play(SfxId.Explosion, 1f, 1.1f);
            CameraRig.Shake(0.8f);
            Haptics.Heavy();
            var h = new HitInfo { Attacker = f, Damage = 55f * f.DamageMul, Stamina = 50f, Knockback = 11f, Launch = 6f, Kind = HitKind.Ultimate, Knockdown = true, Point = t.Center, Dir = f.transform.forward, FxColor = Wire, Shake = 0.6f, HitStop = 0.15f };
            t.ReceiveHit(ref h);
            Destructible.ImpactAt(t.transform.position, 5f, 160f, f);
            f.ExitCinematic();
            yield return new WaitForSeconds(0.2f);
            CameraRig.EndCinematic();
        }

        static void DealCinematic(Fighter t, float dmg)
        {
            if (!t.Alive) return;
            t.Hp = Mathf.Max(1f, t.Hp - dmg);
            t.Stats.damageTaken += dmg;
            t.Anim.Flinch(1f, false);
            t.Flash(Color.white);
            Fx.Hit(t.Center, Wire, false);
            Sfx.Play(SfxId.Hit, 0.9f, 1.2f);
            CameraRig.Shake(0.2f);
            Haptics.Medium();
        }

        public override IEnumerator Finisher(Fighter t)
        {
            var f = F;
            Vector3 c = t.transform.position;
            CameraRig.Cinematic(c + Vector3.up, Vector3.Cross(Vector3.up, KMath.Flat(c - f.transform.position).normalized), 5f, 1f, 2.4f);
            f.FaceInstant(c);
            // wrap the target in wire, yank it up, spin kick through
            for (int i = 0; i < 4; i++) { Lash(t.Center + Random.insideUnitSphere * 0.4f, Wire, 0.2f, i % 2 == 0 ? 1f : -1f); yield return new WaitForSeconds(0.07f); }
            t.Teleport(c + Vector3.up * 1.8f);
            Fx.Electric(t.Center, Wire, 1.4f, 3);
            yield return new WaitForSeconds(0.18f);
            f.Teleport(c - f.transform.forward * 0.9f + Vector3.up * 1.2f);
            f.Anim.Play(Clip.Hold(PoseLib.KickHigh.Mirror(), 0.04f, 0.3f, 0.2f));
            Fx.Slash(f.transform, new Vector3(0, 1f, 0.6f), new Vector3(0, 0, -80), Wire, 2.4f, 0.22f);
            yield return new WaitForSeconds(0.08f);
            t.Teleport(c);
            f.Teleport(c - f.transform.forward * 1.1f);
            t.Kill(f);
            Fx.Electric(c + Vector3.up, Wire, 2f, 5);
            Sfx.Play(SfxId.Explosion, 1f, 1.2f);
            CameraRig.Shake(0.7f);
            yield return new WaitForSeconds(0.5f);
        }
    }
}
