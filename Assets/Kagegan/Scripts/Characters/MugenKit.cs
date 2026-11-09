using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Kagegan
{
    /// <summary>
    /// Mugen, "Кибернетический призрак": a neural dash with afterimages (an exact dodge slows the world), forearm mono-blades,
    /// a Kiroshi weak-point lock, an EMP that breaks charged techniques, the Overclock buff and the "Neural Collapse" ultimate.
    /// Passive: every implant heats the system; at 100% the implants shut down for a few seconds.
    /// </summary>
    public class MugenKit : Kit
    {
        static readonly Color Cyan = new Color(0.25f, 0.95f, 1f);
        static readonly Color Magenta = new Color(1f, 0.22f, 0.72f);
        static readonly Color Chrome = new Color(0.82f, 0.9f, 1f);
        const float HeatMax = 100f;

        float heat, lockedUntil, dashUntil, overUntil, markUntil, glitchTimer;
        bool over;
        Fighter marked;

        public float Heat => heat;
        public bool Overheated => Time.time < lockedUntil;
        public override bool TechsLocked => Overheated;
        public override float SpeedMul => over ? 1.3f : 1f;
        public override float OutgoingMul(Move m) => over ? 1.15f : 1f;
        public override string GaugeText => Overheated ? "ПЕРЕГРЕВ " + Mathf.CeilToInt(lockedUntil - Time.time) + "с" : heat > 1f ? "Нагрев " + Mathf.RoundToInt(heat) + "%" : "";

        public override void Setup(Fighter f)
        {
            base.Setup(f);
            AuraColor = Cyan;
            f.HitColor = new Color(0.6f, 0.95f, 1f);
            IntroLine = "Мугэн: «Сканирование завершено. Слабых мест: три.»";
            WinLine = "Мугэн: «Протокол выполнен.»";
            UltLine = "Мугэн: «Нейронный коллапс.»";

            LightChain = new[]
            {
                MoveLib.Jab(false, 6f, 1.2f),
                MoveLib.Jab(true, 6f, 1.2f),
                KitHelpers.Blade("m_l3", "KK_Melee_Dualwield_Attack_Slice", MocapLibrary.BladeContact, 9f, 0.13f, 0.18f, Cyan, new Vector3(0, 0, -20)),
                KitHelpers.Blade("m_l4", "KK_Melee_Dualwield_Attack_Stab", MocapLibrary.BladeContact, 12f, 0.16f, 0.24f, Magenta, new Vector3(0, 0, 5), 0.7f)
            };
            LightChain[3].Kind = HitKind.Heavy; LightChain[3].Knockback = 7f; LightChain[3].Knockdown = true;
            AirChain = new[] { MoveLib.AirPunch(true, 7f), MoveLib.AirPunch(false, 7f), MoveLib.AirKick(8f) };
            AirFinisher = MoveLib.Slam(15f);
            AirFinisher.FxColor = Cyan;
            Launcher = MoveLib.Uppercut(11f, 11.5f);
            Heavy = KitHelpers.Blade("m_heavy", "KK_Melee_Dualwield_Attack_Chop", MocapLibrary.BladeContact, 18f, 0.22f, 0.3f, Cyan, new Vector3(0, 0, 90), 0.7f);
            Heavy.Kind = HitKind.Heavy; Heavy.Knockback = 10f; Heavy.Knockdown = true; Heavy.Shake = 0.3f; Heavy.HitStop = 0.1f; Heavy.HitSfx = SfxId.HeavyHit;
            DashAttack = KitHelpers.Blade("m_dashatk", "Sword_Dash", 0.3f, 14f, 0.16f, 0.26f, Cyan, new Vector3(0, 0, -10), 0.7f);
            DashAttack.Lunge = 8f; DashAttack.Kind = HitKind.Heavy; DashAttack.Knockback = 8f;
            Counter = KitHelpers.Blade("counter", "KK_Melee_Dualwield_Attack_Chop", MocapLibrary.BladeContact, 22f, 0.1f, 0.26f, Magenta, new Vector3(0, 0, 90), 0.7f);
            Counter.Unblockable = true; Counter.Knockdown = true; Counter.Knockback = 11f; Counter.Kind = HitKind.Heavy;
            MoveLib.WireGrab(this, 20f, 10f, 6f);

            Techs = new[]
            {
                new Technique { Name = "Neural Dash", Short = "Рывок", Icon = "m_dash", Cost = 10f, Cooldown = 2.5f, Range = 9f, Color = Cyan, Use = Implant(UseNeuralDash, 14f), Offensive = false },
                new Technique { Name = "Mono Edge", Short = "Клинки", Icon = "m_edge", Cost = 25f, Cooldown = 6f, Range = 3f, Color = Magenta, Use = Implant(f2 => { f2.StartCoroutine(MonoEdge()); return true; }, 20f) },
                new Technique { Name = "Kiroshi Lock", Short = "Скан", Icon = "m_lock", Cost = 15f, Cooldown = 10f, Range = 20f, Color = new Color(1f, 0.3f, 0.3f), Use = Implant(UseKiroshi, 15f), Offensive = false },
                new Technique { Name = "EMP Breaker", Short = "ЭМИ", Icon = "m_emp", Cost = 30f, Cooldown = 10f, Range = 5f, Color = Chrome, Use = Implant(UseEmp, 25f) },
                new Technique { Name = "Overclock", Short = "Разгон", Icon = "m_over", Cost = 35f, Cooldown = 20f, Range = 30f, Color = new Color(1f, 0.75f, 0.2f), Use = Implant(UseOverclock, 15f), Offensive = false },
            };
            Ult = new Technique { Name = "Нейронный коллапс", Short = "УЛЬТА", Icon = "m_ult", Color = Cyan, Use = f2 => { f2.StartCoroutine(UltRoutine()); return true; } };
        }

        /// <summary>Wraps a technique so that using it heats the implants.</summary>
        Func<Fighter, bool> Implant(Func<Fighter, bool> use, float cost) => f =>
        {
            if (!use(f)) return false;
            AddHeat(cost);
            return true;
        };

        void AddHeat(float v)
        {
            if (Overheated) return;
            heat = Mathf.Min(HeatMax, heat + v);
            if (heat < HeatMax) return;
            // overheat: implants shut down
            heat = HeatMax;
            lockedUntil = Time.time + 4f;
            if (over) SetOverclock(false);
            Fx.Smoke(F.Center, 0.9f, new Color(0.6f, 0.62f, 0.66f, 0.6f), 10);
            for (int i = 0; i < 4; i++) Fx.Lightning(F.Center, F.Center + Random.insideUnitSphere * 1.2f, Cyan, 0.05f, 0.2f);
            Sfx.Play(SfxId.WireZap, 0.9f, 0.7f);
            F.Shout("Мугэн: «Импланты перегреты…»");
            if (F.IsPlayer) { Hud.Popup("ПЕРЕГРЕВ ИМПЛАНТОВ", new Color(1f, 0.45f, 0.2f)); Haptics.Medium(); }
        }

        public override void Tick(float dt)
        {
            if (over)
            {
                heat = Mathf.Min(HeatMax, heat + 10f * dt);
                if (heat >= HeatMax) AddHeat(1f);
                if (Time.time >= overUntil || !F.Alive) SetOverclock(false);
                else if (F.Velocity.sqrMagnitude > 4f && Random.value < 0.3f) KitHelpers.Afterimage(F, Random.value < 0.5f ? Cyan : Magenta, 1);
            }
            if (Overheated)
            {
                if (Random.value < 0.12f) Fx.Lightning(F.Center, F.Center + Random.insideUnitSphere * 0.8f, Cyan, 0.03f, 0.12f);
                if (Random.value < 0.08f) Fx.Smoke(F.Center + Vector3.up * 0.3f, 0.35f, new Color(0.6f, 0.62f, 0.66f, 0.4f), 1);
            }
            else if (!over) heat = Mathf.Max(0f, heat - 12f * dt);
            if (!Overheated && lockedUntil > 0f) { lockedUntil = 0f; heat = 35f; if (F.IsPlayer) Hud.Popup("СИСТЕМЫ В НОРМЕ", Cyan); }

            if (marked != null)
            {
                if (!marked.Alive || Time.time >= markUntil) marked = null;
                else
                {
                    glitchTimer -= dt;
                    if (glitchTimer <= 0f)
                    {
                        glitchTimer = 0.12f;
                        Vector3 wp = marked.Center + Vector3.up * 0.2f + Random.insideUnitSphere * 0.15f;
                        Fx.Glyph(wp, Vector3.zero, new Color(1f, 0.25f, 0.25f), 0.35f, 0.15f);
                    }
                }
            }
            if (Time.time < dashUntil && Random.value < 0.8f) KitHelpers.Afterimage(F, Random.value < 0.6f ? Cyan : Magenta, 1);
        }

        public override void BeforeHitLanded(Fighter target, ref HitInfo h)
        {
            if (target != marked || marked == null) return;
            // the scanned weak point: the next precise hit lands for much more
            h.Damage *= 1.8f;
            h.Stamina *= 1.5f;
            marked = null;
            Fx.Burst(target.Center, new Color(1f, 0.25f, 0.25f), 1.6f, 0.25f);
            WorldLighting.Flash(target.Center, new Color(1f, 0.3f, 0.3f) * 1.6f, 5f, 0.25f);
            if (F.IsPlayer) Hud.Popup("СЛАБОЕ МЕСТО ×1.8", new Color(1f, 0.35f, 0.35f));
        }

        public override void OnPerfectDodge(Fighter attacker)
        {
            if (Time.time >= dashUntil + 0.15f) return;
            NeuralSync();
        }

        void NeuralSync()
        {
            TimeFx.SlowMo(0.9f, 0.3f);
            KitHelpers.Afterimage(F, Cyan, 8);
            WorldLighting.Flash(F.Center, Cyan * 1.5f, 6f, 0.3f);
            F.GrantCounter(1f);
            if (F.IsPlayer) Hud.Popup("НЕЙРОСИНХРОН", Cyan);
        }

        public override bool Intercept(ref HitInfo h)
        {
            // a blow that arrives mid-dash passes through an afterimage
            if (Time.time >= dashUntil || h.Kind == HitKind.Ultimate) return false;
            NeuralSync();
            Fx.Glyph(h.Point, Vector3.up, Cyan, 0.5f, 0.3f);
            return true;
        }

        public override void OnDeath() { SetOverclock(false); marked = null; }

        // ---------- 0: Neural Dash ----------
        bool UseNeuralDash(Fighter f)
        {
            if (!f.CanAct) return false;
            Vector3 dir = f.MoveInput.sqrMagnitude > 0.05f ? f.MoveInput.normalized : Vector3.zero;
            float dist = 5f;
            if (dir == Vector3.zero && f.Target != null && f.Target.Alive)
            {
                Vector3 to = KMath.Flat(f.Target.transform.position - f.transform.position);
                dir = to.normalized;
                dist = Mathf.Clamp(to.magnitude - 1.2f, 1.5f, 6f);
            }
            if (dir == Vector3.zero) dir = KMath.Flat(f.transform.forward).normalized;
            f.StartCoroutine(DashRoutine(f, dir, dist));
            return true;
        }

        IEnumerator DashRoutine(Fighter f, Vector3 dir, float dist)
        {
            dashUntil = Time.time + 0.32f;
            f.SetInvulnerable(0.12f);
            f.Anim.PlayMocap("Slide_Start", 2.4f, 0.05f, false, 0.3f, 0.04f);
            Sfx.Play(SfxId.Teleport, 0.8f, 1.4f);
            Vector3 from = f.transform.position;
            Vector3 to = Arena.ClampInside(from + dir * dist, 1f);
            const int steps = 6;
            for (int i = 1; i <= steps; i++)
            {
                if (!f.Alive) yield break;
                KitHelpers.Afterimage(f, i % 2 == 0 ? Cyan : Magenta, 2);
                f.Teleport(Vector3.Lerp(from, to, i / (float)steps));
                yield return null;
            }
            Fx.Lightning(from + Vector3.up, to + Vector3.up, Cyan, 0.04f, 0.15f);
            if (f.Target != null && f.Target.Alive) f.FaceInstant(f.Target.transform.position);
        }

        // ---------- 1: Mono Edge ----------
        IEnumerator MonoEdge()
        {
            Sfx.Play(SfxId.WireSwing, 0.8f, 1.4f);
            Fx.Glow(F.Rig.BonePos(B.HandR), Magenta, 0.8f, 0.15f);
            Fx.Glow(F.Rig.BonePos(B.HandL), Magenta, 0.8f, 0.15f);
            var a = KitHelpers.Blade("Mono Edge", "KK_Melee_Dualwield_Attack_Slice", MocapLibrary.BladeContact, 8f, 0.1f, 0.06f, Magenta, new Vector3(0, 0, -20));
            a.CancelAt = 1f; a.AnimSpeed = 1.2f;
            yield return Run(a);
            if (RunInterrupted) yield break;
            var b = KitHelpers.Blade("m_me2", "KK_Melee_Dualwield_Attack_Chop", MocapLibrary.BladeContact, 8f, 0.1f, 0.06f, Cyan, new Vector3(0, 0, 150));
            b.CancelAt = 1f; b.AnimSpeed = 1.2f;
            yield return Run(b);
            if (RunInterrupted) yield break;
            var c = KitHelpers.Blade("m_me3", "KK_Melee_Dualwield_Attack_Stab", MocapLibrary.BladeContact, 15f, 0.12f, 0.28f, Magenta, new Vector3(0, 0, 5), 0.75f);
            c.Kind = HitKind.Heavy; c.Knockback = 9f; c.Knockdown = true; c.Shake = 0.35f; c.HitStop = 0.1f; c.Lunge = 5f; c.HitSfx = SfxId.HeavyHit;
            c.OnHit = (atk, v) =>
            {
                Fx.Slash(v.transform, new Vector3(0, 1f, 0), new Vector3(0, 0, 30), Magenta, 2.4f, 0.18f);
                Fx.Slash(v.transform, new Vector3(0, 1f, 0), new Vector3(0, 0, -30), Cyan, 2.4f, 0.18f);
            };
            yield return Run(c);
        }

        // ---------- 2: Kiroshi Lock ----------
        bool UseKiroshi(Fighter f)
        {
            var t = f.Target;
            if (t == null || !t.Alive) return false;
            marked = t;
            markUntil = Time.time + 6f;
            f.Anim.Play(Clip.Hold(PoseLib.Seal, 0.05f, 0.2f, 0.1f));
            Vector3 eye = f.transform.position + Vector3.up * (f.Height - 0.1f) + f.transform.forward * 0.12f;
            Fx.Glow(eye, new Color(1f, 0.3f, 0.3f), 0.6f, 0.25f);
            // scan lines sweep the target top to bottom
            for (int i = 0; i < 5; i++)
                Fx.Lightning(eye, t.transform.position + Vector3.up * (t.Height * (1f - i / 4f)), new Color(1f, 0.25f, 0.25f, 0.8f), 0.02f, 0.25f);
            Fx.Ring(t.Center, new Color(1f, 0.25f, 0.25f), 0.2f, 1.6f, 0.35f, Quaternion.LookRotation(f.transform.up, f.transform.forward));
            Sfx.Play(SfxId.Click, 1f, 1.3f);
            if (f.IsPlayer) Hud.Popup("СЛАБОЕ МЕСТО НАЙДЕНО", new Color(1f, 0.35f, 0.35f));
            return true;
        }

        // ---------- 3: EMP Breaker ----------
        bool UseEmp(Fighter f)
        {
            var m = MoveLib.Cast("EMP Breaker", PoseLib.Charge, PoseLib.Beam, 0.18f, 0.35f, EmpBurst);
            f.StartMove(m);
            Sfx.Play(SfxId.Charge, 0.6f, 1.6f);
            return true;
        }

        void EmpBurst(Fighter f)
        {
            Vector3 c = f.Center;
            Fx.Ring(f.transform.position + Vector3.up * 0.15f, Chrome, 0.4f, 10f, 0.4f, Quaternion.identity);
            Fx.Ring(c, Cyan, 0.3f, 8f, 0.35f, Quaternion.LookRotation(Vector3.up, f.transform.forward));
            Fx.Burst(c, Cyan, 2.4f, 0.3f);
            WorldLighting.Flash(c, Cyan * 2f, 9f, 0.35f);
            Sfx.Play(SfxId.WireZap, 1f, 0.8f);
            Sfx.Play(SfxId.Thunder, 0.5f, 1.6f);
            CameraRig.Shake(0.35f);
            for (int i = 0; i < Fighter.All.Count; i++)
            {
                var e = Fighter.All[i];
                if (!e.Alive || e.Team == f.Team || e.Team == Team.Neutral) continue;
                if (KMath.Flat(e.transform.position - f.transform.position).magnitude > 5f) continue;
                Fx.Lightning(c, e.Center, Cyan, 0.07f, 0.25f);
                bool charging = e.InStartup || e.State == FState.Channel;
                var h = KitHelpers.Hit(charging ? 14f : 8f, 2.5f, 0f, HitKind.Technique, Cyan);
                h.Unblockable = charging; h.Parryable = false; h.Hitstun = 0.4f; h.Sfx = SfxId.WireZap;
                KitHelpers.Strike(f, e, h);
                e.ApplyHack(3f);
                if (charging && e.Alive)
                {
                    // a charged technique is shorted out
                    if (e.State == FState.Channel) e.ExitChannel(); else e.CancelMove();
                    e.Stun(1.2f);
                    if (f.IsPlayer || e.IsPlayer) Hud.Popup("ТЕХНИКА ПРЕРВАНА", Cyan);
                }
            }
        }

        // ---------- 4: Overclock ----------
        bool UseOverclock(Fighter f)
        {
            SetOverclock(true);
            return true;
        }

        void SetOverclock(bool on)
        {
            if (on == over) { if (on) overUntil = Time.time + 8f; return; }
            over = on;
            if (on)
            {
                overUntil = Time.time + 8f;
                F.Rig.SetEmit(0.25f, Cyan);
                F.Anim.Play(Clip.Hold(PoseLib.Charge, 0.06f, 0.25f, 0.1f));
                Fx.Burst(F.Center, Cyan, 2.2f, 0.35f);
                for (int i = 0; i < 5; i++) Fx.Lightning(F.Center, F.Center + Random.insideUnitSphere * 1.6f, Cyan, 0.05f, 0.2f);
                Sfx.Play(SfxId.Energy, 1f, 1.3f);
                F.Shout("Мугэн: «Разгон. Все ограничители сняты.»");
            }
            else
            {
                F.Rig.SetEmit(0f, Cyan);
                Fx.Smoke(F.Center, 0.6f, new Color(0.6f, 0.62f, 0.66f, 0.4f), 5);
            }
        }

        // ---------- ultimate: Нейронный коллапс ----------
        IEnumerator UltRoutine()
        {
            var f = F;
            var t = f.Target;
            f.Shout(UltLine);
            if (t == null || !t.Alive) yield break;
            if (over) SetOverclock(false);
            heat = 0f;
            f.EnterCinematic();
            t.EnterCinematic();
            f.SetInvulnerable(5f);
            Vector3 c = t.transform.position;
            Vector3 side = Vector3.Cross(Vector3.up, KMath.Flat(c - f.transform.position).normalized);
            CameraRig.Cinematic(c + Vector3.up * 1.4f, side + Vector3.back * 0.3f, 7f, 1.5f, 4f);
            // the world slows: everything but Mugen drags
            f.Anim.Play(Clip.Hold(PoseLib.Seal, 0.05f, 0.4f, 0.1f));
            Fx.Ring(f.transform.position + Vector3.up * 0.1f, Cyan, 0.5f, 14f, 0.8f, Quaternion.identity);
            WorldLighting.Flash(f.Center, Cyan * 2f, 12f, 0.8f);
            Sfx.Play(SfxId.Charge, 1f, 0.7f);
            TimeFx.SlowMo(2.6f, 0.45f);
            yield return new WaitForSecondsRealtime(0.45f);

            // eight cuts from every side, each leaving a digital afterimage
            Vector3 last = f.transform.position;
            for (int i = 0; i < 8; i++)
            {
                if (!t.Alive) break;
                float a = i * 2.35f;
                Vector3 p = t.transform.position + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 1.1f;
                Fx.Lightning(last + Vector3.up, p + Vector3.up, i % 2 == 0 ? Cyan : Magenta, 0.05f, 0.25f);
                KitHelpers.Afterimage(f, i % 2 == 0 ? Cyan : Magenta, 4);
                f.Teleport(p);
                f.FaceInstant(t.transform.position);
                f.Anim.PlayMocap(i % 2 == 0 ? "KK_Melee_Dualwield_Attack_Slice" : "KK_Melee_Dualwield_Attack_Chop", 2.4f, 0.15f, false, 0.2f, 0.03f);
                Fx.Slash(t.transform, new Vector3(0, 1f, 0), new Vector3(0, a * Mathf.Rad2Deg, Random.Range(-60f, 60f)), i % 2 == 0 ? Cyan : Magenta, 2.6f, 0.16f);
                KitHelpers.DealCinematic(f, t, 7f, i % 2 == 0 ? Cyan : Magenta, i % 3 == 2);
                last = p;
                yield return new WaitForSecondsRealtime(0.11f);
            }

            // the EMP collapse
            f.Teleport(t.transform.position - KMath.Flat(t.transform.position - f.transform.position).normalized * 1.4f);
            f.FaceInstant(t.transform.position);
            f.Anim.Play(Clip.Hold(PoseLib.Beam, 0.05f, 0.5f, 0.2f));
            for (int k = 0; k < 6; k++) { Fx.Glow(f.Rig.BonePos(B.HandR), Cyan, 1f + k * 0.2f, 0.06f); yield return new WaitForSecondsRealtime(0.04f); }
            TimeFx.Clear();
            Vector3 core = t.Center;
            Fx.Explosion(core, 5f, Cyan);
            Fx.Ring(core, Color.white, 0.5f, 16f, 0.6f, Quaternion.identity);
            Fx.Ring(t.transform.position + Vector3.up * 0.1f, Magenta, 1f, 12f, 0.8f, Quaternion.identity);
            for (int k = 0; k < 8; k++) Fx.Lightning(core, core + Random.onUnitSphere * 5f, k % 2 == 0 ? Cyan : Chrome, 0.08f, 0.35f);
            WorldLighting.Flash(core, Cyan * 3f, 14f, 0.7f);
            Destructible.ImpactAt(t.transform.position, 7f, 300f, f);
            Sfx.Play(SfxId.Explosion, 1f, 1.2f);
            Sfx.Play(SfxId.WireZap, 1f, 0.6f);
            CameraRig.Shake(0.9f);
            Haptics.Heavy();
            t.ExitCinematic();
            var h = new HitInfo { Attacker = f, Damage = 45f * f.DamageMul * OutgoingMul(null), Stamina = 50f, Knockback = 11f, Launch = 6f, Kind = HitKind.Ultimate, Knockdown = true, Point = core, Dir = KMath.Flat(t.transform.position - f.transform.position).normalized, FxColor = Cyan, Shake = 0.6f, HitStop = 0.15f };
            t.ReceiveHit(ref h);
            if (t.Alive) t.ApplyHack(5f);
            Combat.AreaDamage(f, f.Team, core, 6f, 20f, 8f, 4f, true, Cyan, false, HitKind.Ultimate);
            f.ExitCinematic();
            yield return new WaitForSeconds(0.4f);
            CameraRig.EndCinematic();
        }

        public override IEnumerator Finisher(Fighter t)
        {
            var f = F;
            Vector3 c = t.transform.position;
            CameraRig.Cinematic(c + Vector3.up * 1.3f, Vector3.Cross(Vector3.up, KMath.Flat(c - f.transform.position).normalized), 5f, 0.8f, 2.6f);
            f.FaceInstant(c);
            Vector3 eye = f.transform.position + Vector3.up * (f.Height - 0.1f);
            for (int i = 0; i < 4; i++) Fx.Lightning(eye, t.transform.position + Vector3.up * (t.Height * (1f - i / 3f)), new Color(1f, 0.25f, 0.25f), 0.02f, 0.3f);
            Sfx.Play(SfxId.Click, 1f, 1.3f);
            yield return new WaitForSeconds(0.35f);
            for (int i = 0; i < 5; i++)
            {
                float a = i * 2.5f;
                KitHelpers.Afterimage(f, i % 2 == 0 ? Cyan : Magenta, 3);
                f.Teleport(c + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 1.1f);
                f.FaceInstant(c);
                f.Anim.PlayMocap("KK_Melee_Dualwield_Attack_Slice", 2.4f, 0.15f, false, 0.2f, 0.03f);
                Fx.Slash(t.transform, new Vector3(0, 1f, 0), new Vector3(0, a * Mathf.Rad2Deg, Random.Range(-50f, 50f)), i % 2 == 0 ? Cyan : Magenta, 2.4f, 0.16f);
                Fx.Hit(t.Center, Cyan, false);
                t.Anim.Flinch(1f, false);
                yield return new WaitForSeconds(0.1f);
            }
            t.Kill(f);
            Fx.Explosion(t.Center, 3f, Cyan);
            for (int k = 0; k < 5; k++) Fx.Lightning(t.Center, t.Center + Random.onUnitSphere * 3f, Cyan, 0.06f, 0.3f);
            Sfx.Play(SfxId.WireZap, 1f, 0.7f);
            CameraRig.Shake(0.7f);
            yield return new WaitForSeconds(0.5f);
        }
    }
}
