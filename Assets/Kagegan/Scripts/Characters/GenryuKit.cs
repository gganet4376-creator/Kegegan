using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Kagegan
{
    /// <summary>
    /// Genryu, "Пепельный самурай": a slow-drawing iai slash, a parry stance, a fire fissure, a reflecting crimson whirl,
    /// the "Last Samurai" armour buff and the "Judgement of the Last Sun" ultimate.
    /// Passive "Жар": parries build heat; at full heat the heavy attack becomes a burning two-hand cut with a fire wave.
    /// </summary>
    public class GenryuKit : Kit
    {
        static readonly Color Fire = new Color(1f, 0.5f, 0.15f);
        static readonly Color Ember = new Color(1f, 0.32f, 0.08f);
        static readonly Color Ash = new Color(0.32f, 0.3f, 0.3f);
        const float HeatMax = 100f;

        float heat, heatHoldUntil, counterUntil, buffUntil, burnTick;
        bool buff;
        Move counterStrike, normalHeavy, heatHeavy;

        public float Heat => heat;
        public bool BuffActive => buff;
        public override string GaugeText => heat >= HeatMax ? "ЖАР: МАКС" : heat > 1f ? "Жар " + Mathf.RoundToInt(heat) + "%" : "";
        public override float OutgoingMul(Move m) => buff && m != null && m.Kind != HitKind.Light ? 1.2f : 1f;

        public override void Setup(Fighter f)
        {
            base.Setup(f);
            AuraColor = Fire;
            f.HitColor = new Color(1f, 0.6f, 0.3f);
            f.Weight = 1.3f;
            IntroLine = "Гэнрю: «Мой клинок помнит пепел тысячи битв.»";
            WinLine = "Гэнрю: «Огонь не прощает слабости.»";
            UltLine = "Гэнрю: «Суд последнего солнца!»";

            LightChain = new[]
            {
                KitHelpers.Blade("g_l1", "Sword_Regular_A", MocapLibrary.SwordAContact, 9f, 0.17f, 0.2f, Fire, new Vector3(0, 0, -25)),
                KitHelpers.Blade("g_l2", "Sword_Regular_B", MocapLibrary.SwordBContact, 11f, 0.17f, 0.22f, Fire, new Vector3(0, 0, 155)),
                MoveLib.Elbow(13f),
                KitHelpers.Blade("g_l4", "KK_Melee_2H_Attack_Chop", MocapLibrary.BladeContact, 16f, 0.24f, 0.3f, Ember, new Vector3(0, 0, 90), 0.7f)
            };
            LightChain[2].Knockdown = false; LightChain[2].Knockback = 4f;
            LightChain[3].Kind = HitKind.Heavy; LightChain[3].Knockdown = true; LightChain[3].Knockback = 8f; LightChain[3].Shake = 0.3f;
            LightChain[3].OnActive = ff => { Fx.Crack(ff.transform.position + ff.transform.forward * 1.2f, 1.3f); Fx.Fire(ff.transform.position + ff.transform.forward * 1.2f, 0.5f, 4); };
            AirChain = new[] { MoveLib.AirPunch(true, 8f), MoveLib.AirKick(9f), MoveLib.AirPunch(false, 9f) };
            AirFinisher = MoveLib.Slam(20f);
            Launcher = MoveLib.Uppercut(13f, 11f);
            normalHeavy = KitHelpers.Blade("g_heavy", "Sword_Attack", MocapLibrary.SwordContact, 22f, 0.26f, 0.34f, Fire, new Vector3(0, 0, -35), 0.75f);
            normalHeavy.Kind = HitKind.Heavy; normalHeavy.Knockback = 11f; normalHeavy.Knockdown = true; normalHeavy.Armor = true;
            normalHeavy.Shake = 0.35f; normalHeavy.HitStop = 0.1f; normalHeavy.StaminaCost = 10f; normalHeavy.StaminaDamage = 30f; normalHeavy.HitSfx = SfxId.HeavyHit;
            heatHeavy = KitHelpers.Blade("g_heatheavy", "KK_Melee_2H_Attack_Slice", MocapLibrary.BladeContact, 30f, 0.24f, 0.36f, Ember, new Vector3(0, 0, -10), 0.85f);
            heatHeavy.Name = "Пылающий разрез"; heatHeavy.Kind = HitKind.Heavy; heatHeavy.Knockback = 13f; heatHeavy.Knockdown = true; heatHeavy.Armor = true; heatHeavy.Unblockable = true;
            heatHeavy.Shake = 0.5f; heatHeavy.HitStop = 0.13f; heatHeavy.StaminaCost = 10f; heatHeavy.HitSfx = SfxId.HeavyHit;
            heatHeavy.OnActive = HeatRelease;
            Heavy = normalHeavy;
            DashAttack = KitHelpers.Blade("g_dash", "Sword_Dash", 0.3f, 18f, 0.2f, 0.3f, Fire, new Vector3(0, 0, -15), 0.7f);
            DashAttack.Lunge = 7f; DashAttack.Armor = true; DashAttack.Kind = HitKind.Heavy; DashAttack.Knockback = 8f;
            Counter = KitHelpers.Blade("counter", "Sword_Regular_C", MocapLibrary.SwordCContact, 24f, 0.1f, 0.28f, Fire, new Vector3(0, 0, 20), 0.75f);
            Counter.Unblockable = true; Counter.Kind = HitKind.Heavy; Counter.Knockdown = true; Counter.Knockback = 10f;
            counterStrike = KitHelpers.Blade("Стойка бессмертного", "Sword_Dash", 0.3f, 32f, 0.07f, 0.32f, Ember, new Vector3(0, 0, -5), 0.8f);
            counterStrike.Kind = HitKind.Heavy; counterStrike.Knockback = 14f; counterStrike.Knockdown = true; counterStrike.Unblockable = true;
            counterStrike.Shake = 0.5f; counterStrike.HitStop = 0.14f; counterStrike.Lunge = 8f; counterStrike.HitSfx = SfxId.HeavyHit;
            counterStrike.OnHit = (a, v) => Fx.Fire(v.Center, 0.7f, 8);
            MoveLib.WireGrab(this, 28f, 12f, 7f);

            Techs = new[]
            {
                new Technique { Name = "Пылающий Иай", Short = "Иай", Icon = "g_iai", Cost = 30f, Cooldown = 7f, Range = 7f, Color = Fire, Use = UseIai },
                new Technique { Name = "Стойка бессмертного", Short = "Стойка", Icon = "g_stance", Cost = 15f, Cooldown = 4f, Range = 4f, Color = new Color(1f, 0.85f, 0.5f), Use = UseStance, AiUsable = false },
                new Technique { Name = "Разлом земли", Short = "Разлом", Icon = "g_rift", Cost = 35f, Cooldown = 9f, Range = 10f, Color = Ember, Use = UseRift },
                new Technique { Name = "Алый вихрь", Short = "Вихрь", Icon = "g_whirl", Cost = 30f, Cooldown = 8f, Range = 2.6f, Color = new Color(1f, 0.2f, 0.15f), Use = UseWhirl },
                new Technique { Name = "Последний самурай", Short = "Бафф", Icon = "g_buff", Cost = 40f, Cooldown = 22f, Range = 30f, Color = Fire, Use = UseBuff, Offensive = false },
            };
            Ult = new Technique { Name = "Суд последнего солнца", Short = "УЛЬТА", Icon = "g_ult", Color = Fire, Use = f2 => { f2.StartCoroutine(UltRoutine()); return true; } };
        }

        // ---------- passive: heat ----------
        void AddHeat(float v)
        {
            bool wasMax = heat >= HeatMax;
            heat = Mathf.Min(HeatMax, heat + v);
            heatHoldUntil = Time.time + 5f;
            if (!wasMax && heat >= HeatMax)
            {
                Fx.Burst(F.Center, Fire, 2.2f, 0.4f);
                Fx.Fire(F.Center, 0.8f, 10);
                Sfx.Play(SfxId.Fire, 0.9f, 0.9f);
                if (F.IsPlayer) Hud.Popup("ЖАР НА ПРЕДЕЛЕ", Fire);
            }
        }

        public override void OnParry(Fighter attacker) { AddHeat(34f); }

        void HeatRelease(Fighter f)
        {
            // full heat: the heavy cut throws a fire wave forward and spends the heat
            heat = 0f;
            Vector3 fwd = KMath.Flat(f.transform.forward).normalized;
            for (int i = -1; i <= 1; i++)
            {
                Vector3 d = Quaternion.Euler(0, i * 12f, 0) * fwd;
                var p = Projectile.Spawn(f, ProjStyle.Fire, f.transform.position + Vector3.up * 0.9f + d * 1.2f, d * 15f, 14f, Ember, 1.2f);
                p.Power = 3f; p.Knockback = 5f; p.Life = 0.9f; p.Pierce = true; p.GroundHug = true;
            }
            Fx.Fire(f.transform.position + fwd * 1.3f + Vector3.up, 0.9f, 10);
            WorldLighting.Flash(f.Center + fwd, Fire * 2f, 7f, 0.35f);
            Sfx.Play(SfxId.Fire, 1f, 0.8f);
            CameraRig.Shake(0.35f);
        }

        public override void Tick(float dt)
        {
            if (buff)
            {
                AddHeat(4f * dt);
                if (Time.time >= buffUntil || !F.Alive) SetBuff(false);
                else
                {
                    if (Random.value < 0.35f) Fx.Fire(F.transform.position + Vector3.up * Random.Range(0.3f, 1.6f) + Random.insideUnitSphere * 0.3f, 0.25f, 1);
                    burnTick -= dt;
                    if (burnTick <= 0f)
                    {
                        burnTick = 0.6f;
                        // hot armour scorches anyone pressed against it
                        for (int i = 0; i < Fighter.All.Count; i++)
                        {
                            var e = Fighter.All[i];
                            if (!e.Alive || e.Team == F.Team || e.Team == Team.Neutral) continue;
                            if (KMath.Flat(e.transform.position - F.transform.position).sqrMagnitude > 2.2f * 2.2f) continue;
                            var h = KitHelpers.Hit(4f, 0.5f, 0f, HitKind.Technique, Fire);
                            h.Hitstun = 0.12f; h.Stamina = 4f; h.Shake = 0.05f; h.HitStop = 0f; h.Sfx = SfxId.Fire;
                            KitHelpers.Strike(F, e, h);
                        }
                    }
                }
            }
            else if (Time.time > heatHoldUntil && heat > 0f) heat = Mathf.Max(0f, heat - 3f * dt);

            bool hot = heat >= HeatMax;
            Heavy = hot ? heatHeavy : normalHeavy;
            if (hot || buff) F.Rig.SetEmit(0.18f + Mathf.Sin(Time.time * 7f) * 0.06f, Ember);
            else F.Rig.SetEmit(0f, Ember);
            if (hot && Random.value < 0.25f) Fx.Fire(F.Rig.BonePos(B.HandR), 0.2f, 1);
            if (Time.time < counterUntil && Random.value < 0.4f) Fx.Trail(F.Center + Random.insideUnitSphere * 0.6f, new Color(1f, 0.85f, 0.5f), 0.3f);
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
            AddHeat(34f);
            if (h.Proj != null) { h.Proj.Reflect(F); return true; }
            // unbreakable stance: parry, then lunge through the attacker
            F.FaceInstant(atk.transform.position);
            F.Target = atk;
            F.StartMove(counterStrike);
            F.Shout("Гэнрю: «Предсказуемо.»");
            return true;
        }

        public override void OnDeath() { SetBuff(false); }

        // ---------- 0: Пылающий Иай ----------
        bool UseIai(Fighter f)
        {
            var m = MoveLib.Cast("Пылающий Иай", PoseLib.Charge, PoseLib.Thrust, 0.55f, 0.4f, IaiDraw);
            m.Mocap = "KK_Melee_1H_Attack_Slice_Horizontal"; m.MocapContact = MocapLibrary.BladeContact;
            m.OnTick = (ff, t) =>
            {
                if (ff.MoveTime >= 0.55f) return;
                // the slow sheathe: embers gather along the scabbard
                if (Random.value < 0.5f) Fx.Fire(ff.Rig.BonePos(B.HandR) + Random.insideUnitSphere * 0.2f, 0.18f, 1);
                if (ff.MoveTime > 0.4f) Fx.Glow(ff.Rig.BonePos(B.HandR), Fire, 0.9f, 0.05f);
            };
            f.StartMove(m);
            Sfx.Play(SfxId.Charge, 0.7f, 1.2f);
            return true;
        }

        void IaiDraw(Fighter f)
        {
            Vector3 dir = KMath.Flat(MoveLib.AimDir(f)).normalized;
            if (dir.sqrMagnitude < 0.01f) dir = f.transform.forward;
            float len = 7f;
            if (f.Target != null && f.Target.Alive)
            {
                float d = KMath.Flat(f.Target.transform.position - f.transform.position).magnitude;
                if (d < 6f) len = d + 1.6f;
            }
            Vector3 from = f.transform.position;
            Vector3 to = Arena.ClampInside(from + dir * len, 1f);
            f.SetInvulnerable(0.15f);
            f.Teleport(to);
            var h = KitHelpers.Hit(30f, 8f, 0f, HitKind.Heavy, Fire, true);
            h.Dir = dir; h.Shake = 0.5f; h.HitStop = 0.14f; h.Stamina = 40f;
            int n = KitHelpers.LineHit(f, from, to, 1f, h, new List<Fighter>());
            // a long burning line hangs in the air where the blade went
            float L = KMath.Flat(to - from).magnitude;
            for (float s = 0f; s < L; s += 0.7f)
            {
                Vector3 p = from + dir * s + Vector3.up * 1.05f;
                Fx.Trail(p, Fire, 0.7f);
                if (Random.value < 0.6f) Fx.Fire(p, 0.35f, 2);
            }
            Fx.Slash(f.transform, new Vector3(0, 1.05f, -L * 0.5f), new Vector3(0, 0, 3), Fire, Mathf.Max(3f, L), 0.3f);
            WorldLighting.Flash(f.Center - dir * L * 0.5f, Fire * 2f, 9f, 0.35f);
            Sfx.Play(SfxId.Whoosh, 1f, 0.7f);
            Sfx.Play(SfxId.Fire, 0.8f, 1.1f);
            CameraRig.Shake(n > 0 ? 0.5f : 0.2f);
            if (n > 0) TimeFx.SlowMo(0.3f, 0.25f);
        }

        // ---------- 1: Стойка бессмертного ----------
        bool UseStance(Fighter f)
        {
            counterUntil = Time.time + 0.9f;
            if (!f.Anim.PlayMocap("Sword_Block", 1f, 0f, false, 0.9f, 0.06f)) f.Anim.Play(Clip.Hold(PoseLib.Block, 0.05f, 0.8f, 0.1f));
            Fx.Glow(f.Center, new Color(1f, 0.9f, 0.5f), 1.5f, 0.2f);
            Sfx.Play(SfxId.Block, 0.6f, 0.8f);
            return true;
        }

        // ---------- 2: Разлом земли ----------
        bool UseRift(Fighter f)
        {
            var m = MoveLib.Cast("Разлом земли", PoseLib.SlamWind, PoseLib.Slam, 0.42f, 0.5f, ff => ff.StartCoroutine(RiftRoutine(ff)));
            m.Mocap = "KK_Melee_2H_Attack_Chop"; m.MocapContact = MocapLibrary.BladeContact;
            f.StartMove(m);
            return true;
        }

        IEnumerator RiftRoutine(Fighter f)
        {
            Vector3 dir = KMath.Flat(MoveLib.AimDir(f)).normalized;
            if (dir.sqrMagnitude < 0.01f) dir = f.transform.forward;
            Vector3 origin = f.transform.position + dir * 0.9f;
            origin.y = 0f;
            CameraRig.Shake(0.45f);
            Sfx.Play(SfxId.Crumble, 1f, 0.8f);
            var hitList = new List<Fighter>();
            var h = KitHelpers.Hit(16f, 3f, 7.5f, HitKind.Technique, Ember);
            h.Dir = dir;
            Vector3 prev = origin;
            // rock shards burst up along the fissure
            for (int k = 1; k <= 9; k++)
            {
                Vector3 p = Arena.ClampInside(origin + dir * k * 0.95f, 0.5f);
                Fx.Crack(p, 1.3f);
                Fx.Debris(p + Vector3.up * 0.2f, Ash, 5, 6f);
                Fx.Dust(p, 0.9f, 3);
                if (k % 2 == 0) Fx.Fire(p + Vector3.up * 0.4f, 0.4f, 3);
                KitHelpers.LineHit(f, prev, p, 0.9f, h, hitList);
                Destructible.ImpactAt(p, 1.3f, 25f, f);
                prev = p;
                yield return new WaitForSeconds(0.05f);
            }
            // then a wall of fire runs the same line
            for (int i = -2; i <= 2; i++)
            {
                Vector3 d = Quaternion.Euler(0, i * 6f, 0) * dir;
                var p = Projectile.Spawn(f, ProjStyle.Fire, origin + Vector3.up * 0.6f + d * 0.5f, d * 13f, 9f, Ember, 1.3f);
                p.Power = 3f; p.Knockback = 4f; p.Life = 0.85f; p.Pierce = true; p.GroundHug = true;
            }
            Sfx.Play(SfxId.Fire, 1f, 0.7f);
            WorldLighting.Flash(origin + dir * 4f + Vector3.up, Fire * 1.8f, 9f, 0.5f);
        }

        // ---------- 3: Алый вихрь ----------
        bool UseWhirl(Fighter f)
        {
            var m = MoveLib.Cast("Алый вихрь", PoseLib.HookRWind, PoseLib.HookR, 0.14f, 0.72f, ff => ff.StartCoroutine(WhirlRoutine(ff)));
            m.Mocap = "KK_Melee_2H_Attack_Spinning"; m.MocapContact = MocapLibrary.SpinContact;
            m.Armor = true;
            f.StartMove(m);
            return true;
        }

        IEnumerator WhirlRoutine(Fighter f)
        {
            var mv = f.CurMove;
            Color red = new Color(1f, 0.2f, 0.12f);
            for (int tick = 0; tick < 5; tick++)
            {
                if (!f.Alive || f.State != FState.Attack || f.CurMove != mv) yield break;
                Fx.Slash(f.transform, new Vector3(0, 1f, 0), new Vector3(0, tick * 72f, 90 + Random.Range(-15f, 15f)), tick % 2 == 0 ? red : Fire, 3.6f, 0.18f);
                if (tick % 2 == 0) Fx.Ring(f.transform.position + Vector3.up * 0.9f, red, 0.6f, 3.2f, 0.25f, Quaternion.identity);
                Sfx.Play(SfxId.Whoosh, 0.8f, 1f + tick * 0.06f);
                for (int i = 0; i < Fighter.All.Count; i++)
                {
                    var e = Fighter.All[i];
                    if (!e.Alive || e.Team == f.Team || e.Team == Team.Neutral) continue;
                    if (KMath.Flat(e.transform.position - f.transform.position).magnitude > 2.4f + e.BodyRadius) continue;
                    var h = KitHelpers.Hit(tick == 4 ? 12f : 6f, tick == 4 ? 8f : 1.2f, 0f, tick == 4 ? HitKind.Heavy : HitKind.Light, red, tick == 4);
                    h.Hitstun = 0.35f; h.HitStop = 0.04f;
                    KitHelpers.Strike(f, e, h);
                }
                // weak projectiles are batted back
                for (int i = Projectile.Live.Count - 1; i >= 0; i--)
                {
                    var p = Projectile.Live[i];
                    if (p == null || p.Team == f.Team || p.Harmless || p.Damage > 22f) continue;
                    if ((p.transform.position - f.Center).sqrMagnitude > 2.8f * 2.8f) continue;
                    p.Reflect(f);
                    Fx.Parry(p.transform.position);
                }
                yield return new WaitForSeconds(0.12f);
            }
        }

        // ---------- 4: buff «Последний самурай» ----------
        bool UseBuff(Fighter f)
        {
            SetBuff(true);
            return true;
        }

        void SetBuff(bool on)
        {
            if (on == buff) { if (on) buffUntil = Time.time + 10f; return; }
            buff = on;
            F.DamageReduction = on ? 0.3f : 0f;
            F.SuperArmor = on;
            if (on)
            {
                buffUntil = Time.time + 10f;
                F.Anim.Play(Clip.Hold(PoseLib.Roar, 0.08f, 0.4f, 0.15f));
                Fx.Burst(F.Center, Fire, 3f, 0.5f);
                Fx.Ring(F.transform.position + Vector3.up * 0.1f, Ember, 0.5f, 6f, 0.6f, Quaternion.identity);
                Fx.Fire(F.Center, 1f, 14);
                WorldLighting.Flash(F.Center, Fire * 2f, 8f, 0.6f);
                Sfx.Play(SfxId.Roar, 0.7f, 1.2f);
                Sfx.Play(SfxId.Fire, 0.9f, 0.8f);
                CameraRig.Shake(0.4f);
                F.Shout("Гэнрю: «Я — последний самурай!»");
            }
            else
            {
                Fx.Smoke(F.Center, 1.1f, new Color(0.3f, 0.28f, 0.27f, 0.5f), 8);
                F.Rig.SetEmit(0f, Ember);
            }
        }

        // ---------- ultimate: Суд последнего солнца ----------
        IEnumerator UltRoutine()
        {
            var f = F;
            var t = f.Target;
            f.Shout(UltLine);
            if (t == null || !t.Alive) yield break;
            f.EnterCinematic();
            t.EnterCinematic();
            f.SetInvulnerable(4.5f);
            Vector3 c = t.transform.position;
            Vector3 side = Vector3.Cross(Vector3.up, KMath.Flat(c - f.transform.position).normalized);
            f.Teleport(c - KMath.Flat(c - f.transform.position).normalized * 1.6f);
            f.FaceInstant(c);
            CameraRig.Cinematic(c + Vector3.up * 1.4f, side + Vector3.back * 0.2f, 6.5f, 1.5f, 3.6f);
            WorldLighting.Flash(f.Center, Fire * 2f, 10f, 0.8f);
            Sfx.Play(SfxId.Roar, 1f, 0.75f);
            f.Anim.Play(Clip.Hold(PoseLib.Charge, 0.08f, 0.35f, 0.05f));
            for (int k = 0; k < 8; k++) { Fx.Fire(f.Rig.BonePos(B.HandR), 0.35f, 2); yield return new WaitForSeconds(0.04f); }

            // three huge slashes
            string[] clips = { "Sword_Regular_A", "Sword_Regular_B", "KK_Melee_2H_Attack_Slice" };
            float[] rolls = { -30f, 150f, 0f };
            for (int i = 0; i < 3; i++)
            {
                f.Anim.PlayMocap(clips[i], 2f, 0.12f, false, 0.3f, 0.04f);
                yield return new WaitForSeconds(0.1f);
                Fx.Slash(f.transform, new Vector3(0, 1.1f, 1.1f), new Vector3(0, 0, rolls[i]), i == 2 ? Ember : Fire, 5f + i, 0.3f);
                Fx.Fire(t.Center, 0.7f, 6);
                KitHelpers.DealCinematic(f, t, 14f, Fire);
                WorldLighting.Flash(t.Center, Fire * 1.8f, 6f, 0.2f);
                t.Teleport(t.transform.position + KMath.Flat(t.transform.position - f.transform.position).normalized * 0.35f);
                yield return new WaitForSeconds(0.22f);
            }

            // leap, then the sword into the ground
            Vector3 g = new Vector3(t.transform.position.x, 0f, t.transform.position.z);
            f.Anim.Play(Clip.Hold(PoseLib.AirUp, 0.05f, 0.3f, 0.05f));
            for (int k = 0; k < 6; k++) { f.Teleport(f.transform.position + Vector3.up * 0.55f); yield return new WaitForSeconds(0.03f); }
            f.Anim.Play(Clip.Hold(PoseLib.SlamWind, 0.04f, 0.15f, 0.05f));
            yield return new WaitForSeconds(0.15f);
            f.Teleport(g - KMath.Flat(g - f.transform.position).normalized * 1f);
            f.FaceInstant(g);
            f.Anim.Play(Clip.Hold(PoseLib.Slam, 0.03f, 0.9f, 0.2f));
            Fx.Crack(f.transform.position + f.transform.forward * 0.6f, 2.5f);
            Sfx.Play(SfxId.Land, 1f, 0.7f);
            CameraRig.Shake(0.5f);
            yield return new WaitForSeconds(0.25f);

            // the arena erupts
            Vector3 core = f.transform.position + f.transform.forward * 0.6f;
            Fx.Explosion(core, 9f, Fire);
            Fx.Ring(core + Vector3.up * 0.2f, Color.white, 2f, 24f, 0.9f, Quaternion.identity);
            Fx.Ring(core + Vector3.up * 0.3f, Ember, 1f, 18f, 1.1f, Quaternion.identity);
            for (int i = 0; i < 10; i++)
            {
                float a = i / 10f * Mathf.PI * 2f;
                Vector3 p = core + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * Random.Range(2.5f, 7f);
                Fx.Crack(p, 2f + Random.value * 2f);
                Fx.Fire(p + Vector3.up * 0.3f, 0.9f, 5);
            }
            WorldLighting.Flash(core + Vector3.up * 2f, Fire * 3f, 18f, 0.9f);
            Destructible.ImpactAt(core, 12f, 999f, f);
            Sfx.Play(SfxId.Explosion, 1f, 0.6f);
            Sfx.Play(SfxId.Fire, 1f, 0.6f);
            CameraRig.Shake(1f);
            Haptics.Heavy();
            t.ExitCinematic();
            var h = new HitInfo { Attacker = f, Damage = 55f * f.DamageMul * OutgoingMul(null), Stamina = 60f, Knockback = 14f, Launch = 9f, Kind = HitKind.Ultimate, Knockdown = true, Point = t.Center, Dir = KMath.Flat(t.transform.position - core).normalized, FxColor = Fire, Shake = 0.6f, HitStop = 0.15f };
            t.ReceiveHit(ref h);
            Combat.AreaDamage(f, f.Team, core, 9f, 25f, 10f, 6f, true, Fire, false, HitKind.Ultimate);
            Hazard.Spawn(f, HazardKind.FireField, core, 4f, 0.2f, 20f, Fire, 3f);

            // close-up on the blade buried in the burning ground
            CameraRig.Cinematic(f.Rig.BonePos(B.HandR), f.transform.right + f.transform.forward * 0.6f, 2.4f, 0.1f, 0.9f);
            yield return new WaitForSeconds(0.5f);
            f.ExitCinematic();
            yield return new WaitForSeconds(0.4f);
            CameraRig.EndCinematic();
        }

        public override IEnumerator Finisher(Fighter t)
        {
            var f = F;
            Vector3 c = t.transform.position;
            Vector3 dir = KMath.Flat(c - f.transform.position).normalized;
            CameraRig.Cinematic(c + Vector3.up * 1.2f, Vector3.Cross(Vector3.up, dir), 6f, 1f, 2.4f);
            f.FaceInstant(c);
            f.Anim.Play(Clip.Hold(PoseLib.Charge, 0.1f, 0.5f, 0.05f));
            for (int k = 0; k < 10; k++) { Fx.Fire(f.Rig.BonePos(B.HandR), 0.3f, 2); yield return new WaitForSeconds(0.05f); }
            // one draw: he is past them before the flames catch up
            f.Anim.PlayMocap("KK_Melee_1H_Attack_Slice_Horizontal", 2f, MocapLibrary.BladeContact * 0.8f, false, 0.5f, 0.04f);
            Vector3 from = f.transform.position;
            f.Teleport(Arena.ClampInside(c + dir * 2.2f, 1f));
            Fx.Slash(f.transform, new Vector3(0, 1.05f, -2f), new Vector3(0, 0, 3), Fire, 5f, 0.35f);
            for (float s = 0f; s < 5f; s += 0.6f) Fx.Trail(from + dir * s + Vector3.up * 1.05f, Fire, 0.7f);
            yield return new WaitForSeconds(0.5f);
            t.Kill(f);
            Fx.Explosion(t.Center, 3.5f, Fire);
            Fx.Fire(t.Center, 1f, 12);
            Sfx.Play(SfxId.Explosion, 1f, 0.8f);
            CameraRig.Shake(0.8f);
            yield return new WaitForSeconds(0.6f);
        }
    }
}
