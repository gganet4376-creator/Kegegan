using System.Collections;
using UnityEngine;

namespace Kagegan
{
    // ======================================================================================
    // Boss 1 — Jakura, the Serpent Sage (Forest of Destruction)
    // ======================================================================================
    public class SerpentKit : Kit
    {
        static readonly Color Purple = new Color(0.65f, 0.35f, 0.85f);
        Move bindThrow;

        public override void Setup(Fighter f)
        {
            base.Setup(f);
            AuraColor = Purple;
            f.HitColor = new Color(0.8f, 0.6f, 1f);
            IntroLine = "Дзякура: «Какое крепкое тело... Оно станет моим.»";
            var fang = new Move
            {
                Name = "fang", Windup = PoseLib.StanceSerpent, Strike = PoseLib.Thrust, Startup = 0.16f, Active = 0.1f, Recovery = 0.25f,
                Damage = 10f, Bone = HitBone.HandR, Reach = new Vector3(0, 0, 0.7f), Radius = 0.45f, Lunge = 4f, Knockback = 3f, Range = 2.3f, FxColor = Purple
            };
            LightChain = new[] { MoveLib.Palm(true, 7f), MoveLib.Palm(false, 7f), fang, MoveLib.Kick(10f, 5f) };
            AirChain = new[] { MoveLib.AirPunch(true, 7f), MoveLib.AirKick(8f) };
            AirFinisher = MoveLib.Slam(14f);
            Heavy = MoveLib.Sweep(16f);
            Launcher = MoveLib.Uppercut(10f, 11f);
            DashAttack = fang.Copy(); DashAttack.Lunge = 11f; DashAttack.Kind = HitKind.Heavy; DashAttack.Damage = 16f; DashAttack.Knockdown = true;
            MoveLib.WireGrab(this, 22f, 9f, 6f);
            bindThrow = MoveLib.ThrowMove(26f, 11f, 8f);
            bindThrow.Startup = 0.9f;
            bindThrow.OnTick = (ff, t) => { if (ff.ThrowVictim != null && Random.value < 0.3f) { Fx.Trail(ff.ThrowVictim.Center + Random.insideUnitSphere * 0.5f, Purple, 0.3f); } };

            Techs = new[]
            {
                new Technique { Name = "Змеиный рой", Cost = 25f, Cooldown = 5f, Range = 14f, Use = SnakeSwarm },
                new Technique { Name = "Змеиные путы", Cost = 30f, Cooldown = 9f, Range = 12f, Use = SnakeBind },
                new Technique { Name = "Призыв", Cost = 35f, Cooldown = 22f, Range = 30f, Offensive = false, Use = Summon },
                new Technique { Name = "Ядовитый туман", Cost = 30f, Cooldown = 12f, Range = 20f, Use = Poison },
                new Technique { Name = "Великий змей", Cost = 45f, Cooldown = 14f, Range = 25f, Use = GreatSnake },
            };
        }

        bool SnakeSwarm(Fighter f)
        {
            f.StartMove(MoveLib.Cast("swarm", PoseLib.Crouch, PoseLib.Beam, 0.4f, 0.4f, ff =>
            {
                for (int i = -1; i <= 1; i++)
                {
                    Vector3 d = Quaternion.Euler(0, i * 22f, 0) * KMath.Flat(MoveLib.AimDir(ff)).normalized;
                    var p = Projectile.Spawn(ff, ProjStyle.Snake, ff.transform.position + d * 1f + Vector3.up * 0.4f, d * 10f, 14f, new Color(0.45f, 0.3f, 0.55f), 1.3f);
                    p.GroundHug = true; p.Homing = ff.Target; p.HomingRate = 0.9f; p.Life = 2.6f; p.Power = 2f; p.Knockback = 3f;
                }
                Sfx.Play(SfxId.Hiss, 1f);
            }));
            return true;
        }

        bool SnakeBind(Fighter f)
        {
            f.StartMove(MoveLib.Cast("bind", PoseLib.PalmRWind, PoseLib.PalmR, 0.45f, 0.5f, ff =>
            {
                var p = Projectile.Spawn(ff, ProjStyle.Snake, ff.Rig.BonePos(B.HandR), MoveLib.AimDir(ff) * 20f, 4f, new Color(0.55f, 0.35f, 0.65f), 1f);
                p.Unblockable = true; p.Absorbable = false; p.Power = 1f; p.Knockback = 0f; p.Hitstun = 1f; p.Life = 0.8f;
                p.OnHitFighter = (pr, v) =>
                {
                    var o = pr.Owner;
                    if (o == null || !o.Alive || !v.Alive) return;
                    o.BeginThrow(v, bindThrow); // the hold pulls the victim in along the snake
                    Sfx.Play(SfxId.Hiss, 1f, 0.8f);
                    if (v.IsPlayer) Hud.Popup("СХВАЧЕН!", new Color(0.8f, 0.5f, 1f));
                };
                Sfx.Play(SfxId.Hiss, 0.8f, 1.3f);
            }));
            return true;
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

        bool Poison(Fighter f)
        {
            if (f.Target == null) return false;
            f.Anim.Play(Clip.Hold(PoseLib.Overhead, 0.15f, 0.3f, 0.2f));
            Vector3 c = f.Target.transform.position;
            for (int i = 0; i < 3; i++)
                Hazard.Spawn(f, HazardKind.PoisonCloud, c + Quaternion.Euler(0, i * 120f, 0) * Vector3.forward * 2.6f, 2.4f, 0.9f, 28f, new Color(0.55f, 0.25f, 0.65f), 4.5f);
            Sfx.Play(SfxId.Hiss, 1f, 0.6f);
            return true;
        }

        bool GreatSnake(Fighter f)
        {
            if (f.Target == null) return false;
            f.Anim.Play(Clip.Hold(PoseLib.Roar, 0.2f, 0.6f, 0.3f));
            Vector3 a = f.transform.position;
            Vector3 dir = KMath.Flat(f.Target.transform.position - a).normalized;
            for (int i = 1; i <= 12; i++)
                Hazard.Spawn(f, HazardKind.SnakeBurst, a + dir * (i * 2.2f), 1.9f, 0.9f + i * 0.09f, 26f, new Color(0.6f, 0.4f, 0.75f));
            f.Shout("Дзякура: «Великий змей, пожри их!»");
            Sfx.Play(SfxId.Roar, 1f, 0.6f);
            CameraRig.Shake(0.4f);
            return true;
        }
    }

    public class SerpentBrain : BossBrain
    {
        public override void Setup(Fighter f, Archetype type, DifficultyTuning tune)
        {
            base.Setup(f, type, tune);
            Thresholds = new[] { 0.6f, 0.3f };
            PhaseLines = new[] { "Дзякура: «Интересно... Покажи ещё!»", "Дзякура: «Довольно игр! Лес станет твоей могилой!»" };
            patternGap = 2.6f;
        }

        protected override void OnPhase(int p)
        {
            if (p == 2) { Destructible.ImpactAt(F.transform.position, 8f, 999f, F); F.DamageReduction = 0.15f; }
        }

        protected override bool DoPattern(float d)
        {
            float r = Random.value;
            // evasive backflip when pressed at close range
            if (d < 2f && r < 0.18f) { moveDir = -DirToTarget; F.SetMove(moveDir); F.Press(Cmd.Dodge); retreatUntil = Time.time + 0.5f; return true; }
            if (Phase >= 2 && r < 0.3f && Tech(4)) return true;
            if (Phase >= 1 && r < 0.45f && Tech(3)) return true;
            if (Phase >= 1 && r < 0.55f && Tech(2)) return true;
            if (d > 3f && r < 0.75f && Tech(0)) return true;
            if (d < 10f && d > 2.5f && Tech(1)) return true;
            if (d > 4f && d < 9f && r < 0.9f) { F.StartMove(F.Kit.DashAttack); return true; }
            return false;
        }

        // the serpent never trades blows for long: it slips away after being hit and circles at mid range
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
            if (d < 2.8f && t.State != FState.Hitstun && t.State != FState.Juggle && Random.value < 0.14f)
            {
                retreatUntil = Time.time + Random.Range(0.4f, 0.8f);
                return;
            }
            if (d > 3.5f && d < 8f)
            {
                // slithering arc around the target instead of walking straight in
                if (Random.value < 0.04f) strafeSign = -strafeSign;
                moveDir = (Vector3.Cross(Vector3.up, DirToTarget) * strafeSign * 0.85f + DirToTarget * 0.35f).normalized;
            }
        }
    }

    // ======================================================================================
    // Boss 2 — Raiden, the Thunder Avenger (Ruined City)
    // ======================================================================================
    public class ThunderKit : Kit
    {
        static readonly Color Bolt = new Color(0.55f, 0.75f, 1f);
        float counterUntil;
        Move counterMove;
        public bool StormArmor;

        public override void Setup(Fighter f)
        {
            base.Setup(f);
            AuraColor = Bolt;
            f.HitColor = Bolt;
            IntroLine = "Райдэн: «Ты лишь очередное препятствие.»";
            var slash1 = MoveLib.Jab(true, 8f, 1.2f); slash1.Reach = new Vector3(0, 0, 0.55f); slash1.FxColor = Bolt; slash1.SlashEuler = new Vector3(0, 0, 30);
            var slash2 = MoveLib.Hook(false, 9f); slash2.Reach = new Vector3(0, 0, 0.5f); slash2.FxColor = Bolt;
            var slash3 = MoveLib.Kick(10f, 4f); slash3.FxColor = Bolt;
            LightChain = new[] { slash1, slash2, slash3, MoveLib.Elbow(14f) };
            AirChain = new[] { MoveLib.AirPunch(true, 8f), MoveLib.AirKick(8f), MoveLib.AirPunch(false, 8f) };
            AirFinisher = MoveLib.Slam(18f); AirFinisher.FxColor = Bolt;
            Heavy = new Move
            {
                Name = "thrust", Windup = PoseLib.Crouch, Strike = PoseLib.Thrust, Startup = 0.22f, Active = 0.12f, Recovery = 0.32f,
                Damage = 20f, Kind = HitKind.Heavy, Lunge = 8f, Reach = new Vector3(0, 0, 0.5f), Knockback = 9f, Knockdown = true, FxColor = Bolt, Range = 3f,
                Mocap = "Sword_Dash", MocapContact = MocapLibrary.DashContact
            };
            Launcher = MoveLib.Uppercut(12f, 12.5f); Launcher.FxColor = Bolt;
            DashAttack = Heavy.Copy(); DashAttack.Lunge = 12f;
            counterMove = Heavy.Copy(); counterMove.Startup = 0.05f; counterMove.Unblockable = true; counterMove.Damage = 26f;
            MoveLib.WireGrab(this, 24f, 10f, 6f);

            Techs = new[]
            {
                new Technique { Name = "Пронзающая молния", Cost = 30f, Cooldown = 6f, Range = 12f, Use = PiercingBolt },
                new Technique { Name = "Пламя дракона", Cost = 25f, Cooldown = 5f, Range = 16f, Use = Fireball },
                new Technique { Name = "Мгновенный шаг", Cost = 15f, Cooldown = 4f, Range = 20f, Use = FlashStep },
                new Technique { Name = "Контрстойка", Cost = 15f, Cooldown = 7f, Range = 4f, Use = CounterStance },
                new Technique { Name = "Гнев грома", Cost = 50f, Cooldown = 14f, Range = 30f, Use = ThunderRage },
            };
        }

        public override void Tick(float dt)
        {
            if (StormArmor && Random.value < 0.25f)
                Fx.Lightning(F.Center + Random.insideUnitSphere * 0.6f, F.Center + Random.insideUnitSphere * 0.9f, Bolt, 0.04f, 0.08f);
            if (Time.time < counterUntil && Random.value < 0.5f) Fx.Trail(F.Center + Random.insideUnitSphere * 0.6f, Bolt, 0.3f);
        }

        public override bool Intercept(ref HitInfo h)
        {
            if (Time.time >= counterUntil || h.Attacker == null || h.Proj != null) return false;
            counterUntil = 0f;
            var atk = h.Attacker;
            Fx.Parry(h.Point);
            Sfx.Play(SfxId.Parry, 1f, 1.2f);
            F.Teleport(atk.transform.position - atk.transform.forward * 1.2f);
            F.FaceInstant(atk.transform.position);
            F.StartMove(counterMove);
            F.Shout("Райдэн: «Медленно.»");
            if (atk.IsPlayer) Hud.Popup("КОНТРАТАКА ВРАГА", Bolt);
            return true;
        }

        bool PiercingBolt(Fighter f)
        {
            var m = new Move
            {
                Name = "bolt", Windup = PoseLib.Crouch, Strike = PoseLib.Thrust, Startup = 0.6f, Active = 0.3f, Recovery = 0.45f,
                Damage = 34f, Kind = HitKind.Technique, Lunge = 0f, Reach = new Vector3(0, 0, 0.4f), Radius = 0.55f, Knockback = 12f, Knockdown = true,
                FxColor = Bolt, Shake = 0.45f, HitStop = 0.12f, HitSfx = SfxId.Thunder, Range = 12f, Sfx = SfxId.Thunder
            };
            m.OnTick = (ff, t) =>
            {
                Vector3 hand = ff.Rig.BonePos(B.HandR);
                if (ff.MoveTime < m.Startup)
                {
                    Fx.Glow(hand, Bolt, 0.8f, 0.05f);
                    if (Random.value < 0.6f) Fx.Lightning(hand, hand + Random.onUnitSphere * 0.8f, Bolt, 0.05f, 0.06f);
                }
                else if (ff.MoveTime < m.Startup + m.Active)
                {
                    ff.Velocity = ff.transform.forward * 20f + Vector3.up * ff.Velocity.y;
                    Fx.Trail(ff.Center, Bolt, 0.6f);
                    if (ff.Target != null && KMath.Flat(ff.Target.transform.position - ff.transform.position).magnitude < 1f) ff.Velocity = Vector3.zero;
                }
            };
            f.StartMove(m);
            return true;
        }

        bool Fireball(Fighter f)
        {
            f.StartMove(MoveLib.Cast("dragonfire", PoseLib.BeamWind, PoseLib.Beam, 0.35f, 0.35f, ff =>
            {
                var p = Projectile.Spawn(ff, ProjStyle.Fire, ff.Center + ff.transform.forward, MoveLib.AimDir(ff) * 13f, 24f, new Color(1f, 0.45f, 0.15f), 1.8f);
                p.Explode = true; p.ExplodeRadius = 2.6f; p.Power = 4f; p.Knockdown = true;
                Sfx.Play(SfxId.Fire, 1f);
            }));
            return true;
        }

        bool FlashStep(Fighter f)
        {
            if (f.Target == null) return false;
            Fx.Lightning(f.Center, f.Target.Center, Bolt, 0.1f, 0.15f);
            Fx.Smoke(f.Center, 0.5f, new Color(0.7f, 0.8f, 1f, 0.4f), 5);
            f.Teleport(f.Target.transform.position - f.Target.transform.forward * 1.2f);
            f.FaceInstant(f.Target.transform.position);
            Sfx.Play(SfxId.Teleport, 1f, 0.8f);
            f.StartMove(MoveLib.Elbow(16f));
            return true;
        }

        bool CounterStance(Fighter f)
        {
            counterUntil = Time.time + 1.1f;
            f.Anim.Play(Clip.Hold(PoseLib.StanceThunder, 0.05f, 1f, 0.1f));
            return true;
        }

        bool ThunderRage(Fighter f)
        {
            f.StartCoroutine(RageRoutine(f));
            return true;
        }

        IEnumerator RageRoutine(Fighter f)
        {
            f.Anim.Play(Clip.Hold(PoseLib.Overhead, 0.2f, 0.8f, 0.3f));
            f.Shout("Райдэн: «Гнев грома!»");
            Sfx.Play(SfxId.Thunder, 1f, 0.6f);
            for (int i = 0; i < 9; i++)
            {
                if (f.Target == null || !f.Alive) yield break;
                Vector3 p = f.Target.transform.position + new Vector3(Random.Range(-2.5f, 2.5f), 0, Random.Range(-2.5f, 2.5f)) * (i == 0 ? 0f : 1f);
                Hazard.Spawn(f, HazardKind.LightningStrike, p, 2.1f, 0.85f, 24f, Bolt);
                yield return new WaitForSeconds(0.22f);
            }
        }
    }

    public class ThunderBrain : BossBrain
    {
        public override void Setup(Fighter f, Archetype type, DifficultyTuning tune)
        {
            base.Setup(f, type, tune);
            Thresholds = new[] { 0.65f, 0.3f };
            PhaseLines = new[] { "Райдэн: «Хватит сдерживаться.»", "Райдэн: «Я сожгу этот город вместе с тобой!»" };
            patternGap = 2.2f;
        }

        protected override void OnPhase(int p)
        {
            if (p == 2)
            {
                var k = (ThunderKit)F.Kit;
                k.StormArmor = true;
                if (k.Heavy != null) k.Heavy.Armor = true;
                if (k.DashAttack != null) k.DashAttack.Armor = true;
                F.DamageReduction = 0.2f;
                F.SuperArmor = true;
                // the city crumbles around the arena
                for (int i = 0; i < Destructible.All.Count; i++)
                    if (Random.value < 0.6f) Destructible.All[i].Break(Random.onUnitSphere);
                for (int i = 0; i < 8; i++)
                {
                    Vector3 p0 = KMath.Flat(Random.insideUnitSphere * 18f);
                    Fx.Lightning(p0 + Vector3.up * 20f, p0, new Color(0.55f, 0.75f, 1f), 0.25f, 0.4f);
                    Fx.Crack(p0, 2f);
                }
            }
        }

        protected override bool DoPattern(float d)
        {
            float r = Random.value;
            if (Phase >= 2 && r < 0.25f && Tech(4)) return true;
            if (Phase >= 1 && d < 2f && r < 0.35f && Tech(3)) return true;
            if (d < 2.2f && r < 0.55f && F.Kit.Launcher != null) { RunPattern(AirCombo()); return true; }
            if (d > 4f && r < 0.6f && Tech(0)) return true;
            if (d > 5f && Tech(1)) return true;
            if (Phase >= 1 && d > 3f && Tech(2)) return true;
            if (d > 3f && d < 8f) { F.StartMove(F.Kit.DashAttack); return true; }
            return false;
        }

        // Raiden never backs off: he charges straight in, closes distance with a flash step and breaks guards with throws
        protected override void Style(float d)
        {
            var t = F.Target;
            if (!F.CanAct || F.State == FState.Attack) return;
            if (Time.time < retreatUntil) retreatUntil = 0f;
            if (d > PreferredRange + 0.4f) moveDir = DirToTarget;
            if (Phase >= 1 && d > 7f && Random.value < 0.2f && Tech(2)) return;
            if (t.State == FState.Block && d < 2f && Random.value < 0.45f) { F.Press(Cmd.Grab); return; }
            if (JustRecovered && d < 2.5f && Random.value < 0.5f) F.Press(Cmd.Heavy); // answers a combo with a heavy swing
        }
    }

    // ======================================================================================
    // Boss 3 — Seigetsu, the Moon Sovereign (Moon Temple)
    // ======================================================================================
    public class MoonKit : Kit
    {
        static readonly Color Lunar = new Color(0.82f, 0.78f, 1f);
        int dimension;

        public override void Setup(Fighter f)
        {
            base.Setup(f);
            AuraColor = Lunar;
            f.HitColor = Lunar;
            IntroLine = "Сэйгэцу: «Вся чакра этого мира принадлежит мне.»";
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
            Heavy = new Move
            {
                Name = "hairspear", Windup = PoseLib.PalmRWind, Strike = PoseLib.PalmR, Startup = 0.28f, Active = 0.12f, Recovery = 0.35f,
                Damage = 20f, Kind = HitKind.Heavy, Reach = new Vector3(0, 0, 1.4f), Radius = 0.55f, Knockback = 8f, Knockdown = true, Lunge = 2f, FxColor = Lunar, Range = 3.2f
            };
            Launcher = MoveLib.Uppercut(12f, 11f);
            DashAttack = Heavy.Copy(); DashAttack.Lunge = 9f;
            MoveLib.WireGrab(this, 26f, 11f, 7f);

            Techs = new[]
            {
                new Technique { Name = "Костяные копья", Cost = 25f, Cooldown = 5f, Range = 18f, Use = Spears },
                new Technique { Name = "Лунные сферы", Cost = 25f, Cooldown = 6f, Range = 22f, Use = Orbs },
                new Technique { Name = "Пространственный прыжок", Cost = 15f, Cooldown = 5f, Range = 30f, Use = Warp, Offensive = false },
                new Technique { Name = "Смена измерения", Cost = 40f, Cooldown = 16f, Range = 30f, Use = Dimension },
                new Technique { Name = "Небесные ладони", Cost = 20f, Cooldown = 6f, Range = 7f, Use = PalmRush },
                new Technique { Name = "Истина пустоты", Cost = 60f, Cooldown = 20f, Range = 40f, Use = Truth },
            };
        }

        public override void Tick(float dt)
        {
            if (Random.value < 0.2f) Fx.Trail(F.transform.position + Vector3.up * Random.Range(0.2f, 2f) + Random.insideUnitSphere * 0.3f, Lunar * 0.6f, 0.2f);
        }

        bool Spears(Fighter f)
        {
            f.StartMove(MoveLib.Cast("spears", PoseLib.Overhead, PoseLib.Throw, 0.4f, 0.35f, ff =>
            {
                Vector3 fwd = MoveLib.AimDir(ff);
                for (int i = -2; i <= 2; i++)
                {
                    Vector3 d = Quaternion.Euler(0, i * 8f, 0) * fwd;
                    var p = Projectile.Spawn(ff, ProjStyle.Spear, ff.Center + Vector3.up * 0.8f + d, d * 22f, 13f, new Color(0.95f, 0.93f, 0.88f), 0.9f);
                    p.Absorbable = false; p.Power = 2f; p.Knockback = 3f;
                }
                Sfx.Play(SfxId.Whoosh, 1f, 0.7f);
            }));
            return true;
        }

        bool Orbs(Fighter f)
        {
            f.StartMove(MoveLib.Cast("orbs", PoseLib.BeamWind, PoseLib.Beam, 0.45f, 0.4f, ff =>
            {
                for (int i = 0; i < 4; i++)
                {
                    Vector3 d = Quaternion.Euler(-15f, -45f + i * 30f, 0) * ff.transform.forward;
                    var p = Projectile.Spawn(ff, ProjStyle.Orb, ff.Center + d * 1.2f, d * 7f, 16f, Lunar, 1.1f);
                    p.Homing = ff.Target; p.HomingRate = 2.2f; p.Life = 5f; p.Power = 3f; p.Knockback = 5f;
                }
                Sfx.Play(SfxId.Energy, 1f, 0.8f);
            }));
            return true;
        }

        bool Warp(Fighter f)
        {
            Vector3 c = f.Target != null ? f.Target.transform.position : Vector3.zero;
            float a = Random.value * Mathf.PI * 2f;
            Vector3 to = Arena.ClampInside(c + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * Random.Range(7f, 11f), 2f);
            Fx.Burst(f.Center, Lunar, 1.5f, 0.3f);
            Fx.Ring(f.Center, Lunar, 0.2f, 3f, 0.3f, Quaternion.LookRotation(Vector3.up));
            f.Teleport(to);
            if (f.Target != null) f.FaceInstant(f.Target.transform.position);
            Fx.Burst(f.Center, Lunar, 1.5f, 0.3f);
            Sfx.Play(SfxId.Teleport, 1f, 0.7f);
            f.SetInvulnerable(0.3f);
            return true;
        }

        bool Dimension(Fighter f)
        {
            var arena = Arena.Current;
            if (arena == null || f.Target == null) return false;
            f.Anim.Play(Clip.Hold(PoseLib.Overhead, 0.2f, 0.6f, 0.3f));
            Fx.Ring(Vector3.up * 0.2f, Lunar, 30f, 1f, 0.6f, Quaternion.identity);
            Sfx.Play(SfxId.Release, 1f, 0.5f);
            CameraRig.Shake(0.5f);
            int kind = dimension++ % 3;
            Vector3 c = f.Target.transform.position;
            switch (kind)
            {
                case 0: // lava dimension
                    arena.DimensionShift(new Color(1f, 0.55f, 0.3f), new Color(0.4f, 0.12f, 0.05f), new Color(0.5f, 0.15f, 0.05f), new Color(1.4f, 0.6f, 0.4f), 7f);
                    for (int i = 0; i < 5; i++) Hazard.Spawn(f, HazardKind.FireField, Arena.ClampInside(c + Random.insideUnitSphere * 7f, 1.5f), 2.4f, 0.8f, 30f, new Color(1f, 0.4f, 0.1f), 6f);
                    f.Shout("Сэйгэцу: «Сгори в пламени иного мира.»");
                    break;
                case 1: // ice dimension: slows the challengers, ice spikes
                    arena.DimensionShift(new Color(0.75f, 0.9f, 1.1f), new Color(0.2f, 0.3f, 0.45f), new Color(0.75f, 0.85f, 0.95f), new Color(0.8f, 1f, 1.3f), 7f);
                    foreach (var x in Fighter.All) if (x.Alive && x.Team != f.Team) x.ApplyDrain(f, 0f, 6f, 0.6f);
                    for (int i = 0; i < 5; i++) Hazard.Spawn(f, HazardKind.BoneSpikes, Arena.ClampInside(c + Random.insideUnitSphere * 5f, 1.5f), 1.7f, 0.9f + i * 0.3f, 22f, new Color(0.8f, 0.95f, 1f));
                    f.Shout("Сэйгэцу: «Замри во льдах.»");
                    break;
                default: // void dimension: implosions follow the target
                    arena.DimensionShift(new Color(0.7f, 0.5f, 1f), new Color(0.15f, 0.05f, 0.25f), new Color(0.05f, 0.02f, 0.1f), new Color(0.6f, 0.3f, 1f), 6f);
                    f.StartCoroutine(VoidChase(f));
                    f.Shout("Сэйгэцу: «Исчезни в пустоте.»");
                    break;
            }
            return true;
        }

        IEnumerator VoidChase(Fighter f)
        {
            for (int i = 0; i < 6; i++)
            {
                if (f.Target == null || !f.Alive) yield break;
                Hazard.Spawn(f, HazardKind.VoidImplosion, f.Target.transform.position, 2.2f, 0.75f, 22f, new Color(0.7f, 0.4f, 1f));
                yield return new WaitForSeconds(0.5f);
            }
        }

        bool PalmRush(Fighter f)
        {
            if (f.Target == null) return false;
            f.Teleport(f.Target.transform.position - KMath.Flat(f.Target.transform.position - f.transform.position).normalized * 1.3f);
            f.FaceInstant(f.Target.transform.position);
            Fx.Burst(f.Center, Lunar, 1f, 0.2f);
            var m = LightChain[3].Copy();
            m.Hits = 8; m.Active = 0.8f; m.Damage = 4.5f;
            m.OnTick = LightChain[3].OnTick;
            f.StartMove(m);
            f.Shout("Сэйгэцу: «Небесные ладони.»");
            return true;
        }

        bool Truth(Fighter f)
        {
            f.StartCoroutine(TruthRoutine(f));
            return true;
        }

        IEnumerator TruthRoutine(Fighter f)
        {
            // the large final attack: an enormous orb over the arena centre; get out of the circle or perfect-dodge it
            Vector3 c = Vector3.zero;
            f.Teleport(c);
            f.EnterCinematic();
            f.SuperArmor = true;
            f.ForceAnimMode(AnimMode.Custom, PoseLib.Overhead);
            f.Shout("Сэйгэцу: «Узри истину пустоты!»");
            Hud.Banner("БЕГИ ИЗ КРУГА!", new Color(1f, 0.5f, 0.6f));
            float delay = 3.2f;
            Hazard.Spawn(f, HazardKind.Meteor, c, 9f, delay, 115f, new Color(0.85f, 0.6f, 1f));
            CameraRig.Cinematic(c + Vector3.up * 4f, new Vector3(0.3f, 0.6f, -1f), 20f, 6f, 1.4f);
            for (float t = 0f; t < delay; t += 0.05f)
            {
                Fx.Glow(c + Vector3.up * 6f, Color.Lerp(Lunar, Color.white, t / delay), 2f + t * 2.6f, 0.08f);
                if (Random.value < 0.3f) Fx.Lightning(c + Vector3.up * 6f, c + Vector3.up * 6f + Random.onUnitSphere * (2f + t), Lunar, 0.08f, 0.1f);
                yield return new WaitForSeconds(0.05f);
            }
            Fx.Burst(c + Vector3.up * 2f, Color.white, 10f, 0.7f);
            WorldLighting.Flash(c + Vector3.up * 3f, Color.white * 3f, 30f, 0.8f);
            CameraRig.Shake(1f);
            Haptics.Heavy();
            f.SuperArmor = false;
            f.ForceAnimMode(AnimMode.Locomotion);
            f.ExitCinematic();
        }
    }

    public class MoonBrain : BossBrain
    {
        float lastTruth = -100f;

        public override void Setup(Fighter f, Archetype type, DifficultyTuning tune)
        {
            base.Setup(f, type, tune);
            Thresholds = new[] { 0.7f, 0.35f };
            PhaseLines = new[] { "Сэйгэцу: «Этот храм больше не выдержит нас обоих.»", "Сэйгэцу: «Довольно. Я сотру тебя вместе с луной!»" };
            patternGap = 2f;
            PreferredRange = 1.7f;
        }

        protected override void OnPhase(int p)
        {
            if (Arena.Current != null) Arena.Current.SetPhase(p);
            if (p == 2) { F.DamageReduction = 0.15f; lastTruth = Time.time - 12f; }
        }

        protected override bool DoPattern(float d)
        {
            float r = Random.value;
            if (Phase >= 2 && Time.time - lastTruth > 22f && Tech(5)) { lastTruth = Time.time; return true; }
            if (Phase >= 1 && r < 0.22f && Tech(3)) return true;
            if (d < 2.5f && r < 0.35f && Tech(4)) return true;
            if (d < 2.5f && r < 0.5f) { RunPattern(AirCombo()); return true; }
            if (r < 0.65f && (d < 2f || d > 12f) && Tech(2)) return true;
            if (d > 4f && r < 0.8f && Tech(1)) return true;
            if (d > 4f && Tech(0)) return true;
            if (d > 6f && Tech(4)) return true;
            return false;
        }

        // Seigetsu fights from range: backs away while casting and teleports out when cornered or after a combo
        protected override void Style(float d)
        {
            var t = F.Target;
            bool pressed = d < 2.4f && (t.State == FState.Attack || JustRecovered);
            if (pressed && F.CanAct && Random.value < 0.4f + Phase * 0.1f && Tech(2)) return;
            if (F.State != FState.Idle || Time.time < retreatUntil) return;
            if (d < 5f && t.State != FState.Hitstun && t.State != FState.Juggle && t.State != FState.Knockdown && Random.value < 0.55f)
            {
                if (Random.value < 0.03f) strafeSign = -strafeSign;
                moveDir = (-DirToTarget * 0.75f + Vector3.Cross(Vector3.up, DirToTarget) * strafeSign * 0.6f).normalized;
            }
        }
    }
}
