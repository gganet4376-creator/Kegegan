using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Kagegan
{
    /// <summary>
    /// Kaito, "Призрачный ниндзя": katana chains, a ghost double that repeats his attacks and soaks one hit,
    /// a launcher into an air flurry, a shadow-swap counter, returning moon shuriken and the "Thousand Shadows" ultimate.
    /// Passive: a perfect dodge makes the next strikes faster and leaves extra shadow trails.
    /// </summary>
    public class KaitoKit : Kit
    {
        readonly bool isClone;
        static readonly Color Shadow = new Color(0.35f, 0.55f, 1f);
        static readonly Color Steel = new Color(0.8f, 0.92f, 1f);
        static readonly Color Moon = new Color(0.75f, 0.85f, 1f);
        public static readonly Color GhostTint = new Color(0.42f, 0.55f, 0.95f);
        public readonly List<Fighter> Clones = new List<Fighter>();

        Fighter ghost;
        Move seenMove, replayMove;
        float replayAt, hasteUntil, shiftUntil, trailTimer;
        bool skyRunning;
        Move shiftSlash;

        public KaitoKit(bool clone = false) { isClone = clone; }

        public bool Hasted => Time.time < hasteUntil;
        public override float SpeedMul => Hasted ? 1.35f : 1f;

        public override void Setup(Fighter f)
        {
            base.Setup(f);
            AuraColor = Shadow;
            f.HitColor = new Color(0.75f, 0.85f, 1f);
            IntroLine = "Кайто: «Ты даже не увидишь, откуда придёт удар.»";
            WinLine = "Кайто: «Тень всегда на шаг впереди.»";
            UltLine = "Кайто: «Тысяча теней!»";

            LightChain = new[]
            {
                MoveLib.Jab(false, 5f, 1.15f),
                MoveLib.Jab(true, 6f, 1.15f),
                MoveLib.Kick(8f, 2.5f),
                KitHelpers.Blade("k_cut", "Sword_Regular_B", MocapLibrary.SwordBContact, 9f, 0.16f, 0.2f, Steel, new Vector3(0, 0, 160)),
                SpinKick()
            };
            AirChain = new[] { MoveLib.AirPunch(false, 6f), MoveLib.AirPunch(true, 6f), MoveLib.AirKick(7f), MoveLib.AirKick(8f) };
            AirFinisher = MoveLib.Slam(16f);
            Launcher = MoveLib.Uppercut(10f, 12f);
            Heavy = KitHelpers.Blade("k_heavy", "Sword_Attack", MocapLibrary.SwordContact, 15f, 0.26f, 0.3f, Steel, new Vector3(0, 0, -35), 0.7f);
            Heavy.Kind = HitKind.Heavy; Heavy.Knockback = 9f; Heavy.Knockdown = true; Heavy.Shake = 0.3f; Heavy.HitStop = 0.1f; Heavy.StaminaCost = 8f; Heavy.StaminaDamage = 25f;
            DashAttack = MoveLib.Knee(12f);
            DashAttack.Knockback = 7f; DashAttack.Lunge = 7f; DashAttack.Kind = HitKind.Heavy;
            Counter = KitHelpers.Blade("counter", "Sword_Regular_C", MocapLibrary.SwordCContact, 16f, 0.1f, 0.25f, Shadow, new Vector3(0, 0, 20), 0.7f);
            Counter.Unblockable = true; Counter.Kind = HitKind.Heavy; Counter.Knockdown = true; Counter.Knockback = 9f;
            MoveLib.WireGrab(this, 20f, 10f, 7f);

            shiftSlash = KitHelpers.Blade("k_shift", "Sword_Regular_C", MocapLibrary.SwordCContact, 26f, 0.08f, 0.3f, Shadow, new Vector3(0, 0, 25), 0.7f);
            shiftSlash.Unblockable = true; shiftSlash.Kind = HitKind.Heavy; shiftSlash.Knockdown = true; shiftSlash.Knockback = 10f; shiftSlash.Shake = 0.45f; shiftSlash.HitStop = 0.13f;
            shiftSlash.HitSfx = SfxId.HeavyHit;

            if (isClone) { Techs = new Technique[0]; return; }

            Techs = new[]
            {
                new Technique { Name = "Теневой разрез", Short = "Разрез", Icon = "k_cut", Cost = 25f, Cooldown = 5f, Range = 3.5f, Color = Shadow, Use = f2 => Begin(ShadowCut()) },
                new Technique { Name = "Призрачный двойник", Short = "Двойник", Icon = "k_clones", Cost = 30f, Cooldown = 14f, Range = 20f, Color = GhostTint, Use = UseGhost, Offensive = false },
                new Technique { Name = "Небесный шквал", Short = "Шквал", Icon = "k_sky", Cost = 30f, Cooldown = 8f, Range = 2.6f, Color = Steel, Use = UseSkyFlurry },
                new Technique { Name = "Смена тени", Short = "Тень", Icon = "k_shift", Cost = 15f, Cooldown = 5f, Range = 4f, Color = new Color(0.6f, 0.7f, 1f), Use = UseShadowShift, AiUsable = false },
                new Technique { Name = "Лунный сюрикен", Short = "Сюрикен", Icon = "k_shuriken", Cost = 30f, Cooldown = 7f, Range = 14f, Color = Moon, Use = UseMoonShuriken },
            };
            Ult = new Technique { Name = "Тысяча теней", Short = "УЛЬТА", Icon = "k_ult", Color = Shadow, Use = f2 => { f2.StartCoroutine(UltRoutine()); return true; } };
        }

        bool Begin(IEnumerator r) { F.StartCoroutine(r); return true; }

        Move SpinKick()
        {
            var m = MoveLib.Kick(12f, 7f);
            m.Name = "spinkick"; m.Strike = PoseLib.KickHigh.Mirror(); m.Bone = HitBone.FootL; m.Kind = HitKind.Heavy; m.Knockdown = true; m.Shake = 0.25f;
            m.SlashEuler = new Vector3(0, 0, -80); m.Radius = 0.55f;
            return m;
        }

        // ---------- per-frame: ghost double, haste trails ----------
        public override void Tick(float dt)
        {
            for (int i = Clones.Count - 1; i >= 0; i--) if (Clones[i] == null || !Clones[i].Alive) Clones.RemoveAt(i);
            if (isClone) return;

            if (Hasted)
            {
                trailTimer -= dt;
                if (trailTimer <= 0f && F.Velocity.sqrMagnitude > 4f) { trailTimer = 0.06f; KitHelpers.Afterimage(F, Shadow, 2); }
                F.Rig.SetEmit(0.15f + Mathf.Sin(Time.time * 10f) * 0.05f, Shadow);
            }
            else if (hasteUntil > 0f) { hasteUntil = 0f; F.Rig.SetEmit(0f, Shadow); }

            if (ghost != null && !ghost.Alive) ghost = null;
            if (ghost == null) { seenMove = null; replayMove = null; return; }

            // the double keeps a slot at Kaito's side
            ghost.Target = F.Target;
            if (ghost.State == FState.Idle)
            {
                Vector3 slot = F.transform.position + F.transform.right * 1.3f - F.transform.forward * 0.5f;
                Vector3 d = KMath.Flat(slot - ghost.transform.position);
                ghost.SetMove(d.magnitude > 0.4f ? d.normalized * Mathf.Clamp01(d.magnitude / 1.5f) : Vector3.zero);
            }
            if (Random.value < 0.15f) Fx.Glyph(ghost.Center + Random.insideUnitSphere * 0.4f, Vector3.up * 0.4f, GhostTint, 0.25f, 0.5f);

            // remember Kaito's basic strikes and replay them a beat later
            if (F.State == FState.Attack && F.CurMove != seenMove)
            {
                seenMove = F.CurMove;
                if (IsBasic(seenMove)) { replayMove = seenMove; replayAt = Time.time + 0.28f; }
            }
            else if (F.State != FState.Attack) seenMove = null;

            if (replayMove != null && Time.time >= replayAt)
            {
                var m = replayMove;
                replayMove = null;
                var t = F.Target;
                if (ghost.State == FState.Idle && t != null && t.Alive)
                {
                    if (KMath.Flat(t.transform.position - ghost.transform.position).magnitude > 2f)
                    {
                        Fx.Smoke(ghost.Center, 0.5f, new Color(0.4f, 0.5f, 0.9f, 0.6f), 5);
                        Vector3 side = Vector3.Cross(Vector3.up, KMath.Flat(t.transform.position - F.transform.position).normalized);
                        ghost.Teleport(t.transform.position + side * 1.1f);
                    }
                    ghost.SetMove(Vector3.zero);
                    ghost.FaceInstant(t.transform.position);
                    ghost.StartMove(m.Copy());
                }
            }
        }

        bool IsBasic(Move m)
        {
            if (m == null || m.Airborne) return false;
            if (m == Heavy || m == Launcher || m == DashAttack) return true;
            for (int i = 0; i < LightChain.Length; i++) if (LightChain[i] == m) return true;
            return false;
        }

        public override bool Intercept(ref HitInfo h)
        {
            if (isClone) return false;
            var atk = h.Attacker;
            // Смена тени: leave a silhouette, reappear behind the attacker and cut
            if (Time.time < shiftUntil && atk != null && atk.Alive && (h.Proj == null || KMath.Flat(atk.transform.position - F.transform.position).magnitude < 14f))
            {
                shiftUntil = 0f;
                KitHelpers.Silhouette(F, Shadow);
                Sfx.Play(SfxId.Teleport, 1f, 1.1f);
                TimeFx.SlowMo(0.35f, 0.35f);
                F.Teleport(KitHelpers.Behind(F, atk, 1.0f));
                F.FaceInstant(atk.transform.position);
                F.Target = atk;
                F.SetInvulnerable(0.25f);
                F.StartMove(shiftSlash);
                if (F.IsPlayer) Hud.Popup("СМЕНА ТЕНИ", Shadow);
                return true;
            }
            // the ghost double steps in and takes one blow
            if (ghost != null && ghost.Alive && h.Proj == null && atk != null && (ghost.transform.position - F.transform.position).sqrMagnitude < 16f)
            {
                var g = ghost;
                ghost = null;
                Fx.Hit(F.Center, GhostTint, true);
                KitHelpers.Silhouette(F, GhostTint);
                Sfx.Play(SfxId.Poof, 0.9f, 0.9f);
                if (F.IsPlayer) Hud.Popup("ДВОЙНИК ПРИНЯЛ УДАР", GhostTint);
                var life = g.GetComponent<CloneLife>();
                if (life != null) life.Poof(); else g.Kill(null);
                return true;
            }
            return false;
        }

        public override void OnPerfectDodge(Fighter attacker)
        {
            hasteUntil = Time.time + 2.5f;
            KitHelpers.Silhouette(F, Shadow);
            if (F.IsPlayer) Hud.Popup("ТЕНЕВОЙ ШАГ", Shadow);
        }

        public override void OnHitLanded(Fighter target, ref HitInfo h)
        {
            if (!Hasted || isClone) return;
            // the empowered strike leaves an extra shadow slash and spends the haste
            Fx.Slash(target.transform, new Vector3(0, 1f, 0), new Vector3(0, Random.Range(0f, 360f), Random.Range(-40f, 40f)), Shadow, 2.4f, 0.22f);
            KitHelpers.Afterimage(F, Shadow, 6);
            hasteUntil = Time.time + 0.15f;
        }

        public override void OnDeath()
        {
            foreach (var c in Clones) if (c != null && c.Alive) c.Kill(null);
            if (ghost != null && ghost.Alive) ghost.Kill(null);
        }

        // ---------- 0: Теневой разрез ----------
        IEnumerator ShadowCut()
        {
            var a = KitHelpers.Blade("k_sc1", "Sword_Regular_A", MocapLibrary.SwordAContact, 9f, 0.14f, 0.08f, Steel, new Vector3(0, 0, -25));
            a.CancelAt = 1f; a.Name = "Теневой разрез";
            yield return Run(a);
            if (RunInterrupted) yield break;
            var b = KitHelpers.Blade("k_sc2", "Sword_Regular_B", MocapLibrary.SwordBContact, 10f, 0.12f, 0.08f, Steel, new Vector3(0, 0, 155));
            b.CancelAt = 1f; b.Lunge = 2.5f;
            yield return Run(b);
            if (RunInterrupted) yield break;
            var t = F.Target;
            if (t == null || !t.Alive || KMath.Flat(t.transform.position - F.transform.position).magnitude > 6f) yield break;
            // vanish in smoke and cut from behind
            KitHelpers.Silhouette(F, Shadow);
            Fx.Smoke(F.Center, 0.9f, new Color(0.15f, 0.17f, 0.25f, 0.8f), 12);
            Sfx.Play(SfxId.Poof, 0.9f, 1.1f);
            F.SetInvulnerable(0.3f);
            F.Teleport(KitHelpers.Behind(F, t, 1.05f));
            F.FaceInstant(t.transform.position);
            Sfx.Play(SfxId.Teleport, 0.7f, 1.2f);
            var c = KitHelpers.Blade("k_sc3", "Sword_Regular_C", MocapLibrary.SwordCContact, 22f, 0.1f, 0.32f, Shadow, new Vector3(0, 0, 20), 0.7f);
            c.Unblockable = true; c.Kind = HitKind.Heavy; c.Knockdown = true; c.Knockback = 9f; c.Shake = 0.45f; c.HitStop = 0.12f; c.HitSfx = SfxId.HeavyHit; c.Lunge = 1.5f;
            c.OnHit = (atk, v) => { Fx.Slash(v.transform, new Vector3(0, 1f, 0), new Vector3(0, 90, 15), Shadow, 3f, 0.25f); WorldLighting.Flash(v.Center, Shadow * 1.5f, 5f, 0.25f); };
            yield return Run(c);
        }

        // ---------- 1: Призрачный двойник ----------
        bool UseGhost(Fighter f)
        {
            if (ghost != null && ghost.Alive) { var old = ghost.GetComponent<CloneLife>(); if (old != null) old.Poof(); }
            ghost = Roster.CreateClone(f, f.transform.position + f.transform.right * 1.3f, GhostTint);
            ghost.name = "GhostDouble";
            if (ghost.Brain != null) ghost.Brain.Active = false;
            ghost.DamageMul = 0.55f;
            var life = ghost.GetComponent<CloneLife>();
            if (life != null) life.Lifetime = 12f;
            Clones.Add(ghost);
            f.Anim.Play(Clip.Hold(PoseLib.Seal, 0.06f, 0.22f, 0.12f));
            Fx.Smoke(ghost.Center, 0.9f, new Color(0.3f, 0.4f, 0.85f, 0.7f), 12);
            Fx.Ring(ghost.transform.position + Vector3.up * 0.05f, GhostTint, 0.3f, 2.4f, 0.4f, Quaternion.identity);
            Sfx.Play(SfxId.Poof, 1f, 0.85f);
            f.Shout("Кайто: «Призрачный двойник!»");
            return true;
        }

        // ---------- 2: Небесный шквал ----------
        bool UseSkyFlurry(Fighter f)
        {
            var m = MoveLib.Kick(12f, 3f);
            m.Name = "Небесный шквал"; m.Strike = PoseLib.KickHigh; m.Launch = 12.5f; m.Knockdown = false; m.Kind = HitKind.Technique;
            m.Mocap = "KK_Melee_Unarmed_Attack_Kick"; m.MocapContact = MocapLibrary.KickContact; m.FxColor = Steel; m.Startup = 0.2f; m.Lunge = 4f;
            m.OnHit = (a, v) => { if (!skyRunning && v.Alive) a.StartCoroutine(SkyBarrage(v)); };
            f.StartMove(m);
            return true;
        }

        IEnumerator SkyBarrage(Fighter v)
        {
            skyRunning = true;
            yield return new WaitForSeconds(0.14f);
            if (!F.Alive || !v.Alive || v.State != FState.Juggle || F.State == FState.Hitstun || F.State == FState.Knockdown) { skyRunning = false; yield break; }
            F.EnterCinematic();
            v.EnterCinematic();
            F.SetInvulnerable(1.8f);
            Vector3 c = v.transform.position;
            CameraRig.Cinematic(c + Vector3.up * 1.5f, Vector3.Cross(Vector3.up, KMath.Flat(c - F.transform.position).normalized), 6f, 2f, 1.5f);
            Sfx.Play(SfxId.Whoosh, 1f, 0.8f);
            for (int i = 0; i < 6; i++)
            {
                if (!v.Alive) break;
                v.Teleport(v.transform.position + Vector3.up * 0.3f);
                float ang = i * 2.4f;
                Vector3 off = new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * 1.05f;
                KitHelpers.Afterimage(F, Shadow, 3);
                F.Teleport(v.transform.position + off - Vector3.up * 0.25f);
                F.FaceInstant(v.transform.position);
                if (i % 2 == 0) F.Anim.Play(Clip.Hold(PoseLib.AirKick, 0.03f, 0.08f, 0.05f));
                else F.Anim.PlayMocap("KK_Melee_2H_Attack_Spin", 2.2f, 0.25f, false, 0.2f, 0.04f);
                Fx.Slash(F.transform, new Vector3(0, 1f, 0.7f), new Vector3(0, 0, Random.Range(-70f, 70f) + (i % 2) * 180f), i % 2 == 0 ? Steel : Shadow, 2.2f, 0.16f);
                Sfx.Play(SfxId.Whoosh, 0.6f, 1.2f + i * 0.05f);
                KitHelpers.DealCinematic(F, v, 6f, Steel, i == 5);
                yield return new WaitForSeconds(0.11f);
            }
            // spin over the top and drive them into the floor
            F.Teleport(v.transform.position + Vector3.up * 0.9f - F.transform.forward * 0.6f);
            F.FaceInstant(v.transform.position);
            F.Anim.Play(Clip.Hold(PoseLib.Slam, 0.04f, 0.25f, 0.15f));
            Fx.Slash(F.transform, new Vector3(0, 0.8f, 0.6f), new Vector3(0, 0, 90), Shadow, 3f, 0.22f);
            yield return new WaitForSeconds(0.08f);
            v.ExitCinematic();
            F.ExitCinematic();
            var h = KitHelpers.Hit(14f, 4f, 0f, HitKind.Technique, Shadow, true, true);
            h.Slam = true; h.Dir = F.transform.forward; h.Shake = 0.5f; h.HitStop = 0.12f;
            KitHelpers.Strike(F, v, h);
            CameraRig.EndCinematic();
            skyRunning = false;
        }

        // ---------- 3: Смена тени ----------
        bool UseShadowShift(Fighter f)
        {
            shiftUntil = Time.time + 0.6f;
            f.Anim.Play(Clip.Hold(PoseLib.Seal, 0.04f, 0.5f, 0.1f));
            Fx.Glow(f.Center, Shadow, 1.3f, 0.25f);
            Fx.Smoke(f.transform.position + Vector3.up * 0.2f, 0.5f, new Color(0.2f, 0.25f, 0.4f, 0.5f), 5);
            return true;
        }

        // ---------- 4: Лунный сюрикен ----------
        bool UseMoonShuriken(Fighter f)
        {
            var m = MoveLib.Cast("Лунный сюрикен", PoseLib.ThrowWind, PoseLib.Throw, 0.3f, 0.3f, ff =>
            {
                Vector3 dir = MoveLib.AimDir(ff);
                for (int i = -1; i <= 1; i++)
                {
                    Vector3 d = Quaternion.Euler(0f, i * 17f, 0f) * dir;
                    var p = Projectile.Spawn(ff, ProjStyle.Shuriken, ff.Rig.BonePos(B.HandR) + ff.transform.forward * 0.5f, d * 17f, 11f, Moon, 1.1f);
                    p.Power = 2f; p.Knockback = 2.5f; p.Hitstun = 0.45f; p.Life = 2.6f; p.Pierce = true;
                    ff.StartCoroutine(Boomerang(p, ff, i));
                }
                Sfx.Play(SfxId.Whoosh, 1f, 1.3f);
            });
            m.OnTick = (ff, t) => { if (ff.MoveTime < 0.3f) Fx.Glow(ff.Rig.BonePos(B.HandR), Moon, 0.8f, 0.05f); };
            f.StartMove(m);
            return true;
        }

        IEnumerator Boomerang(Projectile p, Fighter owner, int side)
        {
            float t = 0f;
            while (t < 0.42f) { if (!p.gameObject.activeSelf || p.Owner != owner) yield break; t += Time.deltaTime; yield return null; }
            p.ResetHits();
            // swing back along an arc to the thrower's hand
            float spd = p.Vel.magnitude;
            Vector3 curl = Vector3.Cross(Vector3.up, p.Vel.normalized) * (side == 0 ? 1f : side);
            while (p.gameObject.activeSelf && p.Owner == owner && owner != null && owner.Alive)
            {
                Vector3 home = owner.Center - p.transform.position;
                if (home.magnitude < 0.8f) { Fx.Glow(owner.Rig.BonePos(B.HandR), Moon, 0.6f, 0.15f); p.Life = 0f; yield break; }
                Vector3 want = (home.normalized + curl * Mathf.Clamp01(home.magnitude / 8f) * 0.6f).normalized;
                p.Vel = Vector3.Slerp(p.Vel.normalized, want, 5f * Time.deltaTime) * spd;
                p.Life = Mathf.Max(p.Life, 3f);
                if (Random.value < 0.3f) Fx.Trail(p.transform.position, Moon, 0.3f);
                yield return null;
            }
        }

        // ---------- ultimate: Тысяча теней ----------
        IEnumerator UltRoutine()
        {
            var f = F;
            var t = f.Target;
            if (t == null || !t.Alive) yield break;
            f.Shout(UltLine);
            f.EnterCinematic();
            t.EnterCinematic();
            f.SetInvulnerable(5f);
            Vector3 c = t.transform.position;
            Vector3 view = Vector3.Cross(Vector3.up, KMath.Flat(c - f.transform.position).normalized);
            CameraRig.Cinematic(c + Vector3.up * 2f, view + Vector3.back * 0.2f, 8f, 2f, 4.4f);
            f.Anim.Play(Clip.Hold(PoseLib.Seal, 0.05f, 0.45f, 0.1f));
            Fx.Smoke(f.Center, 1.2f, new Color(0.15f, 0.18f, 0.3f, 0.8f), 16);
            Sfx.Play(SfxId.Poof, 1f, 0.8f);
            yield return new WaitForSeconds(0.4f);

            var puppets = new List<Fighter>();
            for (int i = 0; i < 6; i++)
            {
                float a = i / 6f * Mathf.PI * 2f;
                var p = Roster.CreateClone(f, c + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * 3f, GhostTint);
                if (p.Brain != null) p.Brain.Active = false;
                p.EnterCinematic();
                p.FaceInstant(c);
                puppets.Add(p);
            }
            yield return new WaitForSeconds(0.25f);
            // each shadow strikes in turn, carrying the target higher
            for (int i = 0; i < puppets.Count; i++)
            {
                var p = puppets[i];
                Vector3 tp = t.transform.position;
                Vector3 from = KMath.Flat(p.transform.position - tp).normalized;
                if (from.sqrMagnitude < 0.01f) from = Vector3.forward;
                p.Teleport(tp + from * 0.95f + Vector3.up * Mathf.Max(0f, tp.y - 0.3f));
                p.FaceInstant(tp);
                if (i % 2 == 0) p.Anim.PlayMocap("Sword_Regular_A", 2f, 0.1f, false, 0.25f, 0.04f);
                else p.Anim.Play(Clip.Hold(PoseLib.KickHigh, 0.03f, 0.1f, 0.08f));
                Fx.Slash(p.transform, new Vector3(0, 1f, 0.6f), new Vector3(0, 0, i * 63f), GhostTint, 2.6f, 0.18f);
                yield return new WaitForSeconds(0.08f);
                KitHelpers.DealCinematic(f, t, 12f, Shadow);
                t.Teleport(tp + Vector3.up * 0.75f);
                Fx.Smoke(p.Center, 0.5f, new Color(0.3f, 0.4f, 0.85f, 0.6f), 5);
                yield return new WaitForSeconds(0.12f);
            }
            foreach (var p in puppets) if (p != null) { var l = p.GetComponent<CloneLife>(); if (l != null) l.Poof(); else p.Kill(null); }

            // the real Kaito appears above: spin kick, then the falling katana
            Vector3 top = t.transform.position;
            f.Teleport(top + Vector3.up * 1.2f - f.transform.forward * 0.7f);
            f.FaceInstant(top);
            KitHelpers.Silhouette(f, Shadow);
            f.Anim.Play(Clip.Hold(PoseLib.KickHigh.Mirror(), 0.04f, 0.15f, 0.08f));
            Fx.Slash(f.transform, new Vector3(0, 1f, 0.5f), new Vector3(0, 0, -80), Steel, 3f, 0.2f);
            yield return new WaitForSeconds(0.1f);
            KitHelpers.DealCinematic(f, t, 14f, Steel);
            yield return new WaitForSeconds(0.18f);
            f.Anim.Play(Clip.Hold(PoseLib.SlamWind, 0.04f, 0.2f, 0.05f));
            for (int k = 0; k < 6; k++) { Fx.Glow(f.Rig.BonePos(B.HandR) + Vector3.up * 0.3f, Shadow, 1.4f + k * 0.15f, 0.06f); yield return new WaitForSeconds(0.03f); }
            f.Anim.PlayMocap("KK_Melee_2H_Attack_Chop", 1.8f, 0.2f, false, 0.6f, 0.04f);
            Vector3 ground = new Vector3(t.transform.position.x, 0f, t.transform.position.z);
            t.Teleport(ground);
            f.Teleport(ground - f.transform.forward * 1f);
            Fx.Slash(f.transform, new Vector3(0, 1.2f, 1f), new Vector3(0, 0, 90), Shadow, 5f, 0.3f);
            // a deep cut through the floor
            Vector3 fwd = KMath.Flat(f.transform.forward).normalized;
            for (int k = -2; k <= 5; k++) Fx.Crack(ground + fwd * k * 0.75f, 1.3f);
            Fx.Debris(ground, new Color(0.35f, 0.33f, 0.3f), 14, 7f);
            Fx.Burst(t.Center, Steel, 4.5f, 0.5f);
            WorldLighting.Flash(t.Center, Shadow * 2f, 9f, 0.5f);
            Sfx.Play(SfxId.Explosion, 1f, 1.1f);
            CameraRig.Shake(0.9f);
            Haptics.Heavy();
            t.ExitCinematic();
            var h = new HitInfo { Attacker = f, Damage = 60f * f.DamageMul * OutgoingMul(null), Stamina = 50f, Knockback = 10f, Launch = 5f, Kind = HitKind.Ultimate, Knockdown = true, Point = t.Center, Dir = fwd, FxColor = Shadow, Shake = 0.6f, HitStop = 0.15f };
            t.ReceiveHit(ref h);
            Destructible.ImpactAt(ground, 5f, 200f, f);
            f.ExitCinematic();
            yield return new WaitForSeconds(0.2f);
            CameraRig.EndCinematic();
        }

        public override IEnumerator Finisher(Fighter t)
        {
            var f = F;
            Vector3 c = t.transform.position;
            CameraRig.Cinematic(c + Vector3.up, Vector3.Cross(Vector3.up, KMath.Flat(c - f.transform.position).normalized), 5f, 1f, 2.6f);
            var a = Roster.CreateClone(f, c + f.transform.right * 1.5f, GhostTint);
            var b = Roster.CreateClone(f, c - f.transform.right * 1.5f, GhostTint);
            if (a.Brain != null) a.Brain.Active = false;
            if (b.Brain != null) b.Brain.Active = false;
            a.EnterCinematic(); b.EnterCinematic();
            a.FaceInstant(c); b.FaceInstant(c);
            yield return new WaitForSeconds(0.2f);
            a.Anim.PlayMocap("Sword_Regular_A", 2f, 0.1f, false, 0.3f, 0.04f);
            b.Anim.PlayMocap("Sword_Regular_B", 2f, 0.1f, false, 0.3f, 0.04f);
            Fx.Slash(t.transform, new Vector3(0, 1f, 0), new Vector3(0, 0, 30), GhostTint, 2.6f, 0.2f);
            Fx.Slash(t.transform, new Vector3(0, 1f, 0), new Vector3(0, 0, 150), GhostTint, 2.6f, 0.2f);
            yield return new WaitForSeconds(0.1f);
            KitHelpers.DealCinematic(f, t, 5f, Shadow);
            t.Teleport(c + Vector3.up * 3f);
            yield return new WaitForSeconds(0.25f);
            f.Teleport(c + Vector3.up * 4.4f - f.transform.forward * 0.6f);
            f.Anim.Play(Clip.Hold(PoseLib.Slam, 0.05f, 0.3f, 0.2f));
            for (int k = 0; k < 6; k++) { Fx.Glow(f.Rig.BonePos(B.HandR), Shadow, 1.6f, 0.06f); yield return new WaitForSeconds(0.03f); }
            t.Teleport(c);
            f.Teleport(c - f.transform.forward * 0.9f);
            Fx.Slash(f.transform, new Vector3(0, 1.2f, 1f), new Vector3(0, 0, 90), Shadow, 4.5f, 0.3f);
            for (int k = 0; k < 5; k++) Fx.Crack(c + KMath.Flat(f.transform.forward).normalized * (k - 1) * 0.75f, 1.2f);
            a.Kill(null); b.Kill(null);
            t.Kill(f);
            Fx.Burst(c + Vector3.up, Shadow, 3.5f, 0.5f);
            Sfx.Play(SfxId.Explosion, 1f, 1.1f);
            CameraRig.Shake(0.8f);
            yield return new WaitForSeconds(0.5f);
        }
    }
}
