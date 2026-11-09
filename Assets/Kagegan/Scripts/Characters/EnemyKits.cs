using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Kagegan
{
    public enum MinionType { ForestBandit, ForestAssassin, ForestThrower, CityBrute, CityMerc, CityBomber, MoonGuard, MoonPriest, MoonBone, SnakeAdept, CityCyber }

    /// <summary>
    /// Minion move sets. Each design type (bandit, assassin, mercenary, bomber, cyber soldier, bone warrior) has its own
    /// telegraphed techniques plus a weakness the player can read and punish: a failed counter stumbles, a broken shield
    /// staggers, an overheated cyborg vents, a bone warrior without its spear is fragile.
    /// Technique indices are fixed per type so <see cref="MinionBrain"/> can pick them by name.
    /// </summary>
    public class MinionKit : Kit
    {
        public readonly MinionType Type;
        public MinionKit(MinionType t) { Type = t; }

        // counter stance (assassin, bandit)
        float counterUntil;
        bool counterFired;
        // mercenary energy shield
        float shieldUntil, shieldHp;
        // cyber soldier heat
        public float Heat;
        float overheatUntil;
        // bone warrior
        bool reassembled;
        float unarmedUntil;
        bool hiddenNow;

        public bool ShieldUp => Time.time < shieldUntil && shieldHp > 0f;
        public bool Overheated => Time.time < overheatUntil;
        public bool Unarmed => Time.time < unarmedUntil;
        public bool CounterActive => Time.time < counterUntil;

        static readonly Color Steel = new Color(0.85f, 0.88f, 0.95f);
        static readonly Color Shadow = new Color(0.55f, 0.35f, 0.6f);
        static readonly Color Rifle = new Color(1f, 0.82f, 0.35f);
        static readonly Color Shield = new Color(0.4f, 0.75f, 1f);
        static readonly Color Blast = new Color(1f, 0.5f, 0.15f);
        static readonly Color Cyber = new Color(0.3f, 1f, 0.85f);
        static readonly Color Bone = new Color(0.95f, 0.92f, 0.85f);

        public override void Setup(Fighter f)
        {
            base.Setup(f);
            bool heavy = Type == MinionType.CityBrute || Type == MinionType.MoonBone;
            float spd = Type == MinionType.ForestAssassin || Type == MinionType.CityMerc || Type == MinionType.MoonGuard || Type == MinionType.CityCyber ? 1.15f : heavy ? 0.85f : 1f;
            LightChain = new[] { MoveLib.Jab(false, 5f, spd), MoveLib.Jab(true, 6f, spd), heavy ? MoveLib.Elbow(12f) : MoveLib.Kick(8f, 3.5f) };
            if (heavy) { LightChain[2].Armor = true; LightChain[2].Knockdown = false; LightChain[2].Knockback = 6f; }
            AirChain = new[] { MoveLib.AirPunch(true, 5f), MoveLib.AirKick(6f) };
            AirFinisher = MoveLib.Slam(10f);
            Heavy = MoveLib.FrontKick(heavy ? 18f : 12f);
            Heavy.Armor = heavy;
            Launcher = MoveLib.Uppercut(8f, 10f);
            MoveLib.WireGrab(this, heavy ? 20f : 14f, 8f, 5f);
            Counter = MoveLib.Elbow(14f);

            switch (Type)
            {
                case MinionType.ForestBandit:
                    // combo, grab-and-hold for allies, running strike, counter
                    LightChain = new[] { MoveLib.Jab(false, 5f, 1f), MoveLib.Jab(true, 6f, 1f), MoveLib.Hook(false, 7f), MoveLib.Kick(9f, 4f) };
                    Techs = new[] { HoldTech(), DashSlash("Удар с разбега", 12f, new Color(0.9f, 0.8f, 0.6f), false), CounterTech(), Kunai(1, 8f, 9f) };
                    break;
                case MinionType.SnakeAdept:
                    Techs = new[] { Kunai(1, 8f, 9f) };
                    break;
                case MinionType.ForestAssassin:
                    // shadow strike from behind, throwing blades, smoke, counter
                    Techs = new[] { ShadowStrike(), Kunai(3, 6f, 6f, "Метательные клинки"), SmokeBomb(), CounterTech() };
                    break;
                case MinionType.ForestThrower:
                    Techs = new[] { Kunai(3, 7f, 6f), SmokeBomb() };
                    break;
                case MinionType.CityBrute:
                    Techs = new[] { Quake() };
                    break;
                case MinionType.CityMerc:
                    // rifle burst, energy shield, rifle butt (rolls are done by the brain with a real dodge)
                    Techs = new[] { RifleBurst(), ShieldTech(), RifleButt() };
                    break;
                case MinionType.CityBomber:
                    Techs = new[] { StickyBomb(), MineTech(), RocketVolley(), BlastJump() };
                    break;
                case MinionType.CityCyber:
                    LightChain = new[] { BladeJab(true), BladeJab(false), BladeJab(true), MoveLib.Kick(9f, 4f) };
                    Techs = new[] { DashSlash("Рывок", 14f, Cyber, true, 30f), ArmBlades(), Emp(), Cloak() };
                    break;
                case MinionType.MoonGuard:
                    Techs = new[] { MoonStep() };
                    break;
                case MinionType.MoonPriest:
                    Techs = new[] { MoonOrbs() };
                    break;
                case MinionType.MoonBone:
                    Techs = new[] { SpearThrust(), Spin(), BoneThrow(), BoneSpikes() };
                    break;
            }
        }

        // ====================================================================================
        // hooks
        // ====================================================================================
        public override bool TechsLocked => Overheated;
        public override float OutgoingMul(Move m) => Unarmed ? 0.65f : 1f;

        public override void Tick(float dt)
        {
            if (Type == MinionType.CityCyber)
            {
                Heat = Mathf.Max(0f, Heat - (Overheated ? 0f : 7f) * dt);
                if (Overheated && Random.value < 0.35f) Fx.Smoke(F.Center + Vector3.up * 0.3f + Random.insideUnitSphere * 0.3f, 0.35f, new Color(0.85f, 0.9f, 0.9f, 0.45f), 2);
                if (Heat > 70f && !Overheated && Random.value < 0.15f) Fx.Electric(F.Center, Cyber, 0.5f, 1);
            }
            if (ShieldUp && Random.value < 0.3f)
                Fx.Ring(F.Center, Shield * 0.8f, 1.15f, 1.25f, 0.15f, Quaternion.LookRotation(F.transform.forward));
            if (CounterActive && Random.value < 0.4f) Fx.Trail(F.Rig.BonePos(B.HandR), Steel, 0.25f);
            if (Unarmed && Random.value < 0.05f) Fx.Glow(F.Center, Bone * 0.5f, 0.4f, 0.2f);
        }

        public override void OnDeath()
        {
            if (hiddenNow) { hiddenNow = false; KitHelpers.SetHidden(F, false); }
        }

        public override bool Intercept(ref HitInfo h)
        {
            if (!CounterActive || h.Attacker == null || h.Proj != null || h.Attacker.IsBoss) return false;
            counterUntil = 0f;
            counterFired = true;
            var atk = h.Attacker;
            Fx.Parry(h.Point);
            Sfx.Play(SfxId.Parry, 1f, 1.3f);
            if (Type == MinionType.ForestAssassin)
            {
                // slips behind the attacker and stabs
                Fx.Smoke(F.Center, 0.5f, new Color(0.3f, 0.25f, 0.35f, 0.6f), 6);
                F.Teleport(atk.transform.position - atk.transform.forward * 1.1f);
                F.FaceInstant(atk.transform.position);
            }
            F.StartMove(Counter);
            if (atk.IsPlayer) Hud.Popup("КОНТРАТАКА", Steel);
            return true;
        }

        public override void OnDamaged(ref HitInfo h)
        {
            // energy shield soaks frontal hits until it breaks
            if (ShieldUp)
            {
                bool frontal = Vector3.Dot(F.transform.forward, -h.Dir) > -0.1f;
                if (frontal)
                {
                    shieldHp -= h.Damage;
                    h.Damage *= 0.15f; h.Knockback *= 0.3f; h.Launch = 0f; h.Knockdown = false;
                    Fx.Ring(F.Center, Shield, 1f, 1.5f, 0.2f, Quaternion.LookRotation(-h.Dir));
                    if (shieldHp <= 0f) F.StartCoroutine(ShieldBreak());
                }
            }
            if (Overheated) h.Damage *= 1.6f;
            if (Unarmed) h.Damage *= 1.3f;
            // bone warrior: the first lethal blow scatters the bones, which pull back together once
            if (Type == MinionType.MoonBone && !reassembled && h.Damage * F.DefenseMul * (1f - F.DamageReduction) >= F.Hp)
            {
                reassembled = true;
                h.Damage = 0f; h.Launch = 0f; h.Knockdown = false;
                F.StartCoroutine(Reassemble());
            }
        }

        // ====================================================================================
        // shared techniques
        // ====================================================================================
        Technique Kunai(int count, float dmg, float cd, string name = "Кунай") => new Technique
        {
            Name = name, Cost = 10f, Cooldown = cd, Range = 14f,
            Use = f =>
            {
                var m = MoveLib.Cast("kunai", PoseLib.ThrowWind, PoseLib.Throw, 0.3f, 0.3f, ff =>
                {
                    Vector3 fwd = MoveLib.AimDir(ff);
                    for (int i = 0; i < count; i++)
                    {
                        Vector3 d = Quaternion.Euler(0, (i - (count - 1) * 0.5f) * 10f, 0) * fwd;
                        var p = Projectile.Spawn(ff, ProjStyle.Kunai, ff.Rig.BonePos(B.HandR), d * 22f, dmg, new Color(0.8f, 0.85f, 0.9f), 1.2f);
                        p.Power = 1f; p.Knockback = 1.5f; p.Hitstun = 0.3f; p.Absorbable = true;
                    }
                    Sfx.Play(SfxId.Whoosh, 0.6f, 1.6f);
                });
                m.OnStart = ff => ff.Flash(Steel, 0.5f);
                f.StartMove(m);
                return true;
            }
        };

        Technique DashSlash(string name, float dmg, Color c, bool lightning, float heat = 0f) => new Technique
        {
            Name = name, Cost = 15f, Cooldown = 6f, Range = 9f,
            Use = f =>
            {
                var m = new Move
                {
                    Name = "dashslash", Windup = PoseLib.Crouch, Strike = PoseLib.Thrust, Startup = 0.35f, Active = 0.25f, Recovery = 0.45f,
                    Damage = dmg, Kind = HitKind.Technique, Lunge = 15f, Knockback = 6f, Radius = 0.5f, Reach = new Vector3(0, 0, 0.3f),
                    FxColor = c, Shake = 0.25f, Range = 9f,
                    Mocap = "Sword_Dash", MocapContact = MocapLibrary.DashContact
                };
                m.OnStart = ff => { ff.Flash(c, 0.6f); AddHeat(heat); };
                m.OnTick = (ff, t) =>
                {
                    if (ff.MoveTime < m.Startup && lightning && Random.value < 0.4f)
                        Fx.Lightning(ff.Rig.BonePos(B.HandR), ff.Rig.BonePos(B.HandR) + Random.onUnitSphere * 0.6f, c, 0.05f, 0.08f);
                    if (ff.MoveTime >= m.Startup && ff.MoveTime < m.Startup + m.Active) Fx.Trail(ff.Center, c, 0.4f);
                };
                f.StartMove(m);
                return true;
            }
        };

        Technique SmokeBomb() => new Technique
        {
            Name = "Дым", Cost = 15f, Cooldown = 10f, Range = 3f, Offensive = true,
            Use = f =>
            {
                Fx.Smoke(f.Center, 1.6f, new Color(0.7f, 0.7f, 0.72f, 0.7f), 14);
                Sfx.Play(SfxId.Poof, 0.8f);
                Vector3 away = f.Target != null ? KMath.Flat(f.transform.position - f.Target.transform.position).normalized : -f.transform.forward;
                f.Teleport(f.transform.position + away * 6f);
                f.SetInvulnerable(0.4f);
                return true;
            }
        };

        /// <summary>A short guard stance with a visible glint. A hit during it is countered; if nobody bites, the fighter stumbles.</summary>
        Technique CounterTech() => new Technique
        {
            Name = "Встречный удар", Cost = 10f, Cooldown = 7f, Range = 2.6f,
            Use = f =>
            {
                counterUntil = Time.time + 0.75f;
                counterFired = false;
                f.Anim.Play(Clip.Hold(PoseLib.Block, 0.05f, 0.7f, 0.1f));
                Fx.Glow(f.Rig.BonePos(B.HandR), Steel, 0.7f, 0.25f);
                Sfx.Play(SfxId.Clash, 0.5f, 1.6f);
                f.StartCoroutine(CounterWhiff(f));
                return true;
            }
        };

        IEnumerator CounterWhiff(Fighter f)
        {
            yield return new WaitForSeconds(0.8f);
            if (!counterFired && f.Alive && (f.State == FState.Idle || f.State == FState.Block)) f.Stun(0.5f); // the opening after a wasted stance
        }

        // ====================================================================================
        // Разбойник
        // ====================================================================================
        /// <summary>Grabs and pins the target in place so allies can hit them. Whiffing leaves the bandit off balance.</summary>
        Technique HoldTech() => new Technique
        {
            Name = "Захват", Cost = 15f, Cooldown = 12f, Range = 2.2f,
            Use = f =>
            {
                if (f.Target == null || !Squad.CanHold) return false;
                var m = new Move
                {
                    Name = "hold", Windup = PoseLib.GrabReach, Strike = PoseLib.GrabReach, Startup = 0.42f, Active = 0.1f, Recovery = 0.3f,
                    Damage = 0f, Lunge = 5f, SlashFx = false, Range = 2.2f, Sfx = SfxId.Whoosh
                };
                m.OnStart = ff => { ff.Flash(new Color(1f, 0.6f, 0.4f), 0.6f); Sfx.Play(SfxId.Charge, 0.4f, 1.5f); };
                m.OnActive = ff =>
                {
                    var t = ff.Target;
                    bool caught = t != null && t.Alive && !t.Invulnerable && t.Grounded && t.State != FState.Dodge && t.State != FState.Cinematic
                                  && KMath.Flat(t.transform.position - ff.transform.position).magnitude < 1.8f && !t.IsBoss;
                    if (!caught) { ff.StartCoroutine(Stumble(ff)); return; }
                    t.Stun(1.1f);
                    Squad.Pin(t, ff, 1.1f);
                    ff.Anim.Play(Clip.Hold(PoseLib.GrabReach, 0.02f, 1.05f, 0.15f));
                    Sfx.Play(SfxId.Block, 0.9f, 0.7f);
                    Fx.Hit(t.Center, new Color(1f, 0.7f, 0.5f), false);
                    if (t.IsPlayer) { Haptics.Medium(); Hud.Popup("СХВАЧЕН!", new Color(1f, 0.55f, 0.45f)); }
                };
                f.StartMove(m);
                return true;
            }
        };

        IEnumerator Stumble(Fighter f)
        {
            yield return new WaitForSeconds(0.32f);
            if (f.Alive && f.State == FState.Idle) f.Stun(0.6f);
        }

        // ====================================================================================
        // Убийца
        // ====================================================================================
        Technique ShadowStrike() => new Technique
        {
            Name = "Теневой удар", Cost = 20f, Cooldown = 9f, Range = 10f,
            Use = f =>
            {
                if (f.Target == null) return false;
                f.StartCoroutine(ShadowRoutine(f));
                return true;
            }
        };

        IEnumerator ShadowRoutine(Fighter f)
        {
            var t = f.Target;
            Fx.Smoke(f.Center, 0.8f, new Color(0.25f, 0.2f, 0.3f, 0.7f), 9);
            Sfx.Play(SfxId.Poof, 0.7f, 1.3f);
            f.EnterCinematic();
            f.SetInvulnerable(1.1f);
            hiddenNow = true;
            KitHelpers.SetHidden(f, true);
            yield return new WaitForSeconds(0.6f);
            if (t == null || !t.Alive || !f.Alive) { Unhide(f); f.ExitCinematic(); yield break; }
            Vector3 p = Arena.ClampInside(t.transform.position - t.transform.forward * 1.2f, 1f);
            // tell: a shadow pools on the ground and a blade hisses just before the stab
            KitHelpers.Mark(p, Shadow, 0.6f, 0.35f);
            Sfx.Play(SfxId.Whoosh, 0.8f, 1.8f);
            yield return new WaitForSeconds(0.35f);
            f.Teleport(p);
            f.FaceInstant(t.transform.position);
            Unhide(f);
            Fx.Smoke(f.Center, 0.5f, new Color(0.25f, 0.2f, 0.3f, 0.6f), 6);
            f.ExitCinematic();
            var stab = new Move
            {
                Name = "backstab", Windup = PoseLib.Crouch, Strike = PoseLib.Thrust, Startup = 0.14f, Active = 0.1f, Recovery = 0.45f,
                Damage = 15f, Kind = HitKind.Heavy, Lunge = 3f, Reach = new Vector3(0, 0, 0.5f), Radius = 0.45f, Knockback = 5f, FxColor = Shadow, Range = 2f
            };
            f.StartMove(stab);
        }

        void Unhide(Fighter f)
        {
            if (!hiddenNow) return;
            hiddenNow = false;
            KitHelpers.SetHidden(f, false);
        }

        // ====================================================================================
        // Наёмник
        // ====================================================================================
        Technique RifleBurst() => new Technique
        {
            Name = "Очередь", Cost = 12f, Cooldown = 4.5f, Range = 15f,
            Use = f =>
            {
                if (f.Target == null) return false;
                f.StartCoroutine(BurstRoutine(f));
                return true;
            }
        };

        IEnumerator BurstRoutine(Fighter f)
        {
            var t = f.Target;
            f.Anim.Play(Clip.Hold(PoseLib.Beam, 0.1f, 0.95f, 0.2f));
            // laser sight: the aim line is visible for half a second before the shots
            for (float k = 0f; k < 0.5f; k += 0.05f)
            {
                if (t == null || !f.Alive || f.State == FState.Hitstun || f.State == FState.Stunned || f.State == FState.Knockdown) yield break;
                f.FaceInstant(t.transform.position);
                Fx.Lightning(f.Rig.BonePos(B.HandR), t.Center, new Color(1f, 0.2f, 0.2f), 0.012f, 0.06f);
                yield return new WaitForSeconds(0.05f);
            }
            for (int i = 0; i < 3; i++)
            {
                if (t == null || !f.Alive || f.State == FState.Hitstun || f.State == FState.Stunned || f.State == FState.Knockdown) yield break;
                Vector3 from = f.Rig.BonePos(B.HandR);
                Vector3 d = (t.Center - from).normalized;
                var p = Projectile.Spawn(f, ProjStyle.Kunai, from, d * 32f, 5f, Rifle, 0.6f);
                p.Power = 1f; p.Knockback = 1f; p.Hitstun = 0.22f; p.Life = 1.2f;
                Fx.Glow(from + d * 0.3f, Rifle, 0.5f, 0.06f);
                Sfx.Play(SfxId.Hit, 0.55f, 1.9f);
                yield return new WaitForSeconds(0.12f);
            }
        }

        Technique ShieldTech() => new Technique
        {
            Name = "Энергощит", Cost = 20f, Cooldown = 14f, Range = 12f, Offensive = false,
            Use = f =>
            {
                shieldUntil = Time.time + 5f;
                shieldHp = 35f;
                Fx.Ring(f.Center, Shield, 0.3f, 1.4f, 0.3f, Quaternion.LookRotation(f.transform.forward));
                Sfx.Play(SfxId.Energy, 0.8f, 1.1f);
                return true;
            }
        };

        IEnumerator ShieldBreak()
        {
            shieldUntil = 0f;
            Fx.Burst(F.Center, Shield, 1.4f, 0.3f);
            Fx.Electric(F.Center, Shield, 1f, 3);
            Sfx.Play(SfxId.Parry, 0.9f, 0.7f);
            yield return null;
            if (F.Alive) F.Stun(0.9f);
            Hud.Popup("ЩИТ СЛОМАН", Shield);
        }

        Technique RifleButt() => new Technique
        {
            Name = "Удар прикладом", Cost = 10f, Cooldown = 5f, Range = 2.3f,
            Use = f =>
            {
                var m = KitHelpers.Warned(new Move
                {
                    Name = "butt", Windup = PoseLib.HookRWind, Strike = PoseLib.ElbowR, Startup = 0.3f, Active = 0.1f, Recovery = 0.4f,
                    Damage = 11f, Kind = HitKind.Heavy, Lunge = 3f, Reach = new Vector3(0, 0, 0.45f), Radius = 0.5f, Knockback = 11f, FxColor = Rifle, Range = 2.3f
                }, Rifle);
                f.StartMove(m);
                return true;
            }
        };

        // ====================================================================================
        // Подрывник
        // ====================================================================================
        Technique StickyBomb() => new Technique
        {
            Name = "Липкая бомба", Cost = 15f, Cooldown = 7f, Range = 12f,
            Use = f =>
            {
                f.StartMove(MoveLib.Cast("sticky", PoseLib.ThrowWind, PoseLib.Throw, 0.35f, 0.35f, ff =>
                {
                    var p = Lob(ff, ff.Target != null ? ff.Target.Center : ff.transform.position + ff.transform.forward * 7f, 0.7f, 4f, Blast, 0.5f);
                    p.Explode = false; p.Power = 0.5f; p.Knockback = 0.5f; p.Hitstun = 0.1f;
                    p.OnHitFighter = (pr, v) => { if (pr.Owner != null) pr.Owner.StartCoroutine(Fuse(pr.Owner, v)); };
                }));
                return true;
            }
        };

        static Projectile Lob(Fighter ff, Vector3 to, float T, float dmg, Color c, float size)
        {
            Vector3 from = ff.Rig.BonePos(B.HandR);
            float g = 14f;
            Vector3 v = (to - from) / T; v.y = (to.y - from.y + 0.5f * g * T * T) / T;
            var p = Projectile.Spawn(ff, ProjStyle.Fire, from, v, dmg, c, size);
            p.Gravity = g; p.Life = T + 0.8f;
            Sfx.Play(SfxId.Whoosh, 0.6f, 0.9f);
            return p;
        }

        /// <summary>The bomb sticks to the victim and beeps; a dodge roll shakes it off before it blows.</summary>
        static IEnumerator Fuse(Fighter owner, Fighter v)
        {
            if (v.IsPlayer) Hud.Popup("БОМБА!  Уклонись, чтобы стряхнуть", Blast);
            for (float t = 0f; t < 1.6f; t += 0.2f)
            {
                if (v == null || !v.Alive) yield break;
                if (v.State == FState.Dodge)
                {
                    Vector3 drop = v.transform.position;
                    Fx.Glow(drop + Vector3.up * 0.2f, Blast, 0.6f, 0.5f);
                    Sfx.Play(SfxId.Click, 0.8f, 0.8f);
                    yield return new WaitForSeconds(0.5f);
                    Detonate(owner, drop, 1.6f, 14f);
                    yield break;
                }
                Fx.Glow(v.Center + Vector3.up * 0.2f, t > 1f ? Color.red : Blast, 0.35f + t * 0.15f, 0.12f);
                Sfx.Play(SfxId.Click, 0.6f, 1.4f + t * 0.4f);
                yield return new WaitForSeconds(0.2f);
            }
            if (v != null && v.Alive) Detonate(owner, v.transform.position, 2f, 18f);
        }

        static void Detonate(Fighter owner, Vector3 p, float r, float dmg)
        {
            Fx.Explosion(p, r, Blast);
            WorldLighting.Flash(p + Vector3.up, Blast * 2f, 8f, 0.3f);
            Sfx.Play(SfxId.Explosion, 0.9f, 1.1f);
            CameraRig.Shake(0.35f);
            Destructible.ImpactAt(p, r + 1f, 60f, owner);
            if (owner != null) Combat.AreaDamage(owner, owner.Team, p + Vector3.up * 0.5f, r, dmg, 7f, 5f, true, Blast, false, HitKind.Heavy);
        }

        Technique MineTech() => new Technique
        {
            Name = "Мина", Cost = 15f, Cooldown = 8f, Range = 14f, Offensive = false,
            Use = f =>
            {
                if (f.Target == null || MineTrap.CountOf(f) >= 2) return false;
                // between the bomber and the target, a step in front of the target, so allies can push them onto it
                Vector3 t = f.Target.transform.position;
                Vector3 p = Arena.ClampInside(t + KMath.Flat(f.transform.position - t).normalized * 2.4f + KMath.Flat(Random.insideUnitSphere) * 1.2f, 1f);
                f.StartMove(MoveLib.Cast("mine", PoseLib.ThrowWind, PoseLib.Throw, 0.3f, 0.3f, ff =>
                {
                    var pr = Lob(ff, p, 0.6f, 0f, Blast, 0.35f);
                    pr.Harmless = true; pr.Explode = false;
                    ff.StartCoroutine(PlaceMine(ff, p, 0.6f));
                }));
                return true;
            }
        };

        static IEnumerator PlaceMine(Fighter owner, Vector3 p, float delay)
        {
            yield return new WaitForSeconds(delay);
            if (owner != null && owner.Alive) MineTrap.Place(owner, p);
        }

        Technique RocketVolley() => new Technique
        {
            Name = "Ракетный залп", Cost = 25f, Cooldown = 11f, Range = 16f,
            Use = f =>
            {
                if (f.Target == null) return false;
                f.StartCoroutine(VolleyRoutine(f));
                return true;
            }
        };

        IEnumerator VolleyRoutine(Fighter f)
        {
            var t = f.Target;
            Vector3 c = t.transform.position;
            Vector3 side = Vector3.Cross(Vector3.up, KMath.Flat(c - f.transform.position).normalized);
            var pts = new[] { c, Arena.ClampInside(c + side * 2.2f, 1f), Arena.ClampInside(c - side * 2.2f, 1f) };
            f.Anim.Play(Clip.Hold(PoseLib.Overhead, 0.12f, 0.8f, 0.25f));
            foreach (var p in pts) KitHelpers.Mark(p, Blast, 1.5f, 1.25f);
            Sfx.Play(SfxId.Charge, 0.7f, 0.8f);
            yield return new WaitForSeconds(0.35f);
            for (int i = 0; i < pts.Length; i++)
            {
                if (!f.Alive) yield break;
                var pr = Lob(f, pts[i], 0.9f, 15f, Blast, 0.55f);
                pr.Explode = true; pr.ExplodeRadius = 1.5f; pr.Knockdown = true; pr.Power = 2f;
                Fx.Smoke(f.Rig.BonePos(B.HandR), 0.4f, new Color(0.8f, 0.8f, 0.8f, 0.5f), 3);
                yield return new WaitForSeconds(0.12f);
            }
        }

        Technique BlastJump() => new Technique
        {
            Name = "Взрывной прыжок", Cost = 15f, Cooldown = 9f, Range = 3f,
            Use = f =>
            {
                Vector3 away = f.Target != null ? KMath.Flat(f.transform.position - f.Target.transform.position).normalized : -f.transform.forward;
                Vector3 o = f.transform.position;
                KitHelpers.Telegraph(f, Blast, 0.3f, 2f);
                f.StartCoroutine(BlastJumpRoutine(f, o, away));
                return true;
            }
        };

        IEnumerator BlastJumpRoutine(Fighter f, Vector3 o, Vector3 away)
        {
            yield return new WaitForSeconds(0.3f);
            if (!f.Alive || !f.CanAct) yield break;
            Detonate(f, o, 2f, 10f);
            f.SetInvulnerable(0.35f);
            f.SetMove(away);
            f.Press(Cmd.Jump);
            Fx.Smoke(o, 1f, new Color(0.4f, 0.35f, 0.3f, 0.6f), 8);
        }

        // ====================================================================================
        // Киберсолдат
        // ====================================================================================
        void AddHeat(float h)
        {
            if (h <= 0f || Type != MinionType.CityCyber) return;
            Heat += h;
            if (Heat < 100f) return;
            Heat = 0f;
            overheatUntil = Time.time + 3f;
            F.StartCoroutine(OverheatRoutine());
        }

        IEnumerator OverheatRoutine()
        {
            yield return new WaitForSeconds(0.5f);
            if (!F.Alive) yield break;
            F.Stun(1.4f);
            Fx.Smoke(F.Center, 1f, new Color(0.9f, 0.95f, 0.95f, 0.6f), 12);
            Fx.Electric(F.Center, Cyber, 1.2f, 4);
            Sfx.Play(SfxId.Hiss, 0.9f, 1.6f);
            Hud.Popup("ПЕРЕГРЕВ — бей!", Cyber);
        }

        Move BladeJab(bool right)
        {
            var m = MoveLib.Jab(right, 6f, 1.2f);
            m.FxColor = Cyber; m.Reach = new Vector3(0, 0, 0.5f);
            return m;
        }

        Technique ArmBlades() => new Technique
        {
            Name = "Клинки-руки", Cost = 15f, Cooldown = 5f, Range = 2.4f,
            Use = f =>
            {
                var m = new Move
                {
                    Name = "armblades", Windup = PoseLib.HookRWind, Strike = PoseLib.HookR, Startup = 0.3f, Active = 0.45f, Recovery = 0.4f, Hits = 4,
                    Damage = 4.5f, Hitstun = 0.3f, Knockback = 1f, Radius = 0.6f, Bone = HitBone.HandR, Reach = new Vector3(0, 0, 0.5f), Lunge = 3f,
                    Kind = HitKind.Technique, FxColor = Cyber, Range = 2.4f
                };
                m.OnStart = ff => { KitHelpers.Telegraph(ff, Cyber, 0.3f, 1.4f); AddHeat(30f); };
                m.OnTick = (ff, t) =>
                {
                    if (ff.InActiveFrames && Random.value < 0.5f)
                        Fx.Slash(ff.transform, new Vector3(0, 1.2f, 0.6f), new Vector3(0, 0, Random.Range(-60f, 60f)), Cyber, 1.2f, 0.12f);
                };
                f.StartMove(m);
                return true;
            }
        };

        Technique Emp() => new Technique
        {
            Name = "ЭМИ", Cost = 20f, Cooldown = 10f, Range = 3.2f,
            Use = f =>
            {
                var m = new Move
                {
                    Name = "emp", Windup = PoseLib.Charge, Strike = PoseLib.Roar, Startup = 0.6f, Active = 0.05f, Recovery = 0.5f,
                    Damage = 0f, Lunge = 0f, SlashFx = false, NoTracking = true, Sfx = SfxId.WireZap
                };
                m.OnStart = ff => { KitHelpers.Telegraph(ff, Cyber, 0.6f, 3.2f); AddHeat(35f); };
                m.OnActive = ff =>
                {
                    Fx.Ring(ff.Center, Cyber, 0.4f, 7f, 0.35f, Quaternion.identity);
                    Fx.Electric(ff.Center, Cyber, 2f, 5);
                    WorldLighting.Flash(ff.Center, Cyber * 2f, 8f, 0.25f);
                    Combat.AreaDamage(ff, ff.Team, ff.Center, 3.2f, 6f, 3f, 0f, false, Cyber, false, HitKind.Technique);
                    foreach (var x in Fighter.All)
                    {
                        if (!x.Alive || x.Team == ff.Team || x.Team == Team.Neutral || x.Invulnerable) continue;
                        if (KMath.Flat(x.transform.position - ff.transform.position).magnitude > 3.2f) continue;
                        x.ApplyDrain(ff, 12f, 2.5f, 0.6f);
                        if (x.IsPlayer) Hud.Popup("ЭМИ: чакра сбита", Cyber);
                    }
                };
                f.StartMove(m);
                return true;
            }
        };

        Technique Cloak() => new Technique
        {
            Name = "Маскировка", Cost = 20f, Cooldown = 12f, Range = 12f,
            Use = f =>
            {
                if (f.Target == null) return false;
                AddHeat(25f);
                f.StartCoroutine(CloakRoutine(f));
                return true;
            }
        };

        IEnumerator CloakRoutine(Fighter f)
        {
            Fx.Electric(f.Center, Cyber, 1f, 3);
            Sfx.Play(SfxId.Energy, 0.7f, 1.6f);
            hiddenNow = true;
            KitHelpers.SetHidden(f, true);
            // the cloak is not perfect: a faint shimmer gives the position away, and hits land normally
            float end = Time.time + 2.2f;
            while (Time.time < end && f.Alive && f.Target != null && f.State != FState.Hitstun && f.State != FState.Knockdown && f.State != FState.Stunned)
            {
                if (Random.value < 0.45f) Fx.Trail(f.Center + Random.insideUnitSphere * 0.4f, Cyber * 0.5f, 0.3f);
                if (KMath.Flat(f.Target.transform.position - f.transform.position).magnitude < 1.8f) break;
                yield return null;
            }
            Unhide(f);
            if (!f.Alive || f.Target == null || !f.CanAct) yield break;
            Fx.Electric(f.Center, Cyber, 1f, 3);
            Sfx.Play(SfxId.Teleport, 0.7f, 1.6f);
            f.FaceInstant(f.Target.transform.position);
            f.StartMove(Heavy);
        }

        // ====================================================================================
        // Костяной воин
        // ====================================================================================
        Technique SpearThrust() => new Technique
        {
            Name = "Удар копьём", Cost = 10f, Cooldown = 4f, Range = 3.4f,
            Use = f =>
            {
                if (Unarmed) return false;
                f.StartMove(KitHelpers.Warned(new Move
                {
                    Name = "spear", Windup = PoseLib.Crouch, Strike = PoseLib.Thrust, Startup = 0.38f, Active = 0.12f, Recovery = 0.45f,
                    Damage = 15f, Kind = HitKind.Heavy, Lunge = 4f, Reach = new Vector3(0, 0, 1.6f), Radius = 0.4f, Knockback = 7f, FxColor = Bone, Range = 3.4f
                }, Bone));
                return true;
            }
        };

        Technique Spin() => new Technique
        {
            Name = "Вихрь костей", Cost = 15f, Cooldown = 7f, Range = 2.6f,
            Use = f =>
            {
                var m = new Move
                {
                    Name = "spin", Windup = PoseLib.Crouch, Strike = PoseLib.Sweep, Startup = 0.45f, Active = 0.3f, Recovery = 0.5f,
                    Damage = 0f, Lunge = 0f, SlashFx = false, NoTracking = true, Range = 2.6f, Armor = true
                };
                m.OnStart = ff => KitHelpers.Telegraph(ff, Bone, 0.45f, 2.6f);
                m.OnActive = ff =>
                {
                    Fx.Ring(ff.transform.position + Vector3.up * 0.9f, Bone, 0.5f, 3f, 0.25f, Quaternion.identity);
                    Fx.Slash(ff.transform, new Vector3(0, 1f, 0), new Vector3(90, 0, 0), Bone, 2.6f, 0.2f);
                    Combat.AreaDamage(ff, ff.Team, ff.Center, 2.6f, Unarmed ? 8f : 14f, 6f, 0f, false, Bone, true, HitKind.Heavy);
                    Sfx.Play(SfxId.Whoosh, 1f, 0.7f);
                };
                f.StartMove(m);
                return true;
            }
        };

        /// <summary>Throws its own bone spear. Until it regrows one it is weaker and takes more damage.</summary>
        Technique BoneThrow() => new Technique
        {
            Name = "Бросок кости", Cost = 15f, Cooldown = 10f, Range = 14f,
            Use = f =>
            {
                if (Unarmed) return false;
                var m = MoveLib.Cast("bonethrow", PoseLib.ThrowWind, PoseLib.Throw, 0.45f, 0.35f, ff =>
                {
                    Vector3 d = MoveLib.AimDir(ff);
                    var p = Projectile.Spawn(ff, ProjStyle.Spear, ff.Rig.BonePos(B.HandR), d * 24f, 16f, Bone, 1.2f);
                    p.Power = 3f; p.Knockback = 6f; p.Knockdown = true;
                    unarmedUntil = Time.time + 5f;
                    Sfx.Play(SfxId.Whoosh, 0.9f, 0.8f);
                });
                m.OnStart = ff => KitHelpers.Telegraph(ff, Bone, 0.45f, 1.4f);
                f.StartMove(m);
                return true;
            }
        };

        IEnumerator Reassemble()
        {
            yield return null; // let the killing blow's reaction play first
            var f = F;
            if (!f.Alive) yield break;
            f.CancelMove();
            f.EnterCinematic();
            f.SetInvulnerable(2.8f);
            hiddenNow = true;
            KitHelpers.SetHidden(f, true);
            Fx.Debris(f.Center, Bone, 14, 5f);
            Fx.Smoke(f.transform.position, 0.8f, new Color(0.8f, 0.78f, 0.72f, 0.5f), 8);
            Sfx.Play(SfxId.Crumble, 1f, 1.2f);
            Hud.Popup("Кости собираются...", Bone);
            yield return new WaitForSeconds(1.6f);
            // tell: the bones rattle and glow before it stands back up
            for (int i = 0; i < 6; i++)
            {
                Fx.Glow(f.transform.position + Vector3.up * 0.2f + KMath.Flat(Random.insideUnitSphere) * 1.2f, new Color(1f, 0.3f, 0.3f), 0.4f, 0.4f);
                Sfx.Play(SfxId.Click, 0.7f, 0.6f + i * 0.1f);
                yield return new WaitForSeconds(0.12f);
            }
            Unhide(f);
            f.Hp = f.MaxHp * 0.45f;
            Fx.Burst(f.Center, Bone, 1.2f, 0.25f);
            Sfx.Play(SfxId.Roar, 0.6f, 1.4f);
            f.ExitCinematic();
        }

        // ====================================================================================
        // existing simple types
        // ====================================================================================
        Technique Quake() => new Technique
        {
            Name = "Сотрясение", Cost = 25f, Cooldown = 8f, Range = 3.5f,
            Use = f =>
            {
                var m = new Move
                {
                    Name = "quake", Windup = PoseLib.SlamWind, Strike = PoseLib.Stomp, Startup = 0.55f, Active = 0.05f, Recovery = 0.5f, Damage = 0f,
                    Armor = true, Lunge = 0f, SlashFx = false, Kind = HitKind.Technique
                };
                m.OnStart = ff => KitHelpers.Telegraph(ff, new Color(0.9f, 0.8f, 0.6f), 0.55f, 3.5f);
                m.OnActive = ff =>
                {
                    Combat.AreaDamage(ff, ff.Team, ff.transform.position + Vector3.up * 0.5f, 3.5f, 20f, 7f, 6f, true, new Color(0.8f, 0.7f, 0.5f), false, HitKind.Heavy);
                    Fx.Ring(ff.transform.position + Vector3.up * 0.1f, new Color(0.9f, 0.8f, 0.6f), 0.5f, 8f, 0.4f, Quaternion.identity);
                    Fx.Dust(ff.transform.position, 2.4f, 16);
                    Fx.Crack(ff.transform.position, 3f);
                    Destructible.ImpactAt(ff.transform.position, 4f, 60f, ff);
                    Sfx.Play(SfxId.HeavyHit, 1f, 0.6f);
                    CameraRig.Shake(0.4f);
                };
                f.StartMove(m);
                return true;
            }
        };

        Technique MoonStep() => new Technique
        {
            Name = "Лунный шаг", Cost = 20f, Cooldown = 7f, Range = 12f,
            Use = f =>
            {
                if (f.Target == null) return false;
                Fx.Smoke(f.Center, 0.6f, new Color(0.85f, 0.85f, 1f, 0.5f), 6);
                f.Teleport(f.Target.transform.position - f.Target.transform.forward * 1.2f);
                f.FaceInstant(f.Target.transform.position);
                Sfx.Play(SfxId.Teleport, 0.7f);
                f.StartMove(Heavy);
                return true;
            }
        };

        Technique MoonOrbs() => new Technique
        {
            Name = "Сферы", Cost = 20f, Cooldown = 5f, Range = 16f,
            Use = f =>
            {
                f.StartMove(MoveLib.Cast("orbs", PoseLib.BeamWind, PoseLib.Beam, 0.4f, 0.4f, ff =>
                {
                    for (int i = 0; i < 2; i++)
                    {
                        Vector3 d = Quaternion.Euler(0, i == 0 ? -25f : 25f, 0) * ff.transform.forward;
                        var p = Projectile.Spawn(ff, ProjStyle.Orb, ff.Center + d, d * 7f, 13f, new Color(0.75f, 0.7f, 1f), 0.8f);
                        p.Homing = ff.Target; p.HomingRate = 2f; p.Life = 4f; p.Power = 2f;
                    }
                    Sfx.Play(SfxId.Energy, 0.6f, 1.2f);
                }));
                return true;
            }
        };

        Technique BoneSpikes() => new Technique
        {
            Name = "Шипы", Cost = 25f, Cooldown = 7f, Range = 12f,
            Use = f =>
            {
                if (f.Target == null) return false;
                f.Anim.Play(Clip.Hold(PoseLib.Stomp, 0.15f, 0.3f, 0.2f));
                Hazard.Spawn(f, HazardKind.BoneSpikes, f.Target.transform.position, 1.8f, 0.8f, 20f, Bone);
                return true;
            }
        };
    }

    /// <summary>Shared minion coordination state: who is pinned by a bandit, and when the next hold is allowed.</summary>
    public static class Squad
    {
        public static Fighter Pinned;
        public static Fighter PinnedBy;
        static float pinnedUntil, nextHoldAt;

        public static bool CanHold => Time.time >= nextHoldAt;
        public static bool IsPinned(Fighter t) => t != null && t == Pinned && Time.time < pinnedUntil;

        public static void Pin(Fighter t, Fighter by, float secs)
        {
            Pinned = t; PinnedBy = by;
            pinnedUntil = Time.time + secs;
            nextHoldAt = Time.time + 9f; // one hold at a time across the whole group
        }

        public static void Reset() { Pinned = null; PinnedBy = null; pinnedUntil = 0f; nextHoldAt = 0f; }
    }

    /// <summary>The bomber's proximity mine: blinks while arming, then blows up under the first enemy that steps on it.</summary>
    public class MineTrap : MonoBehaviour
    {
        public static readonly List<MineTrap> Live = new List<MineTrap>();
        public Fighter Owner;
        float t, blink;
        const float Life = 14f, Arm = 0.9f, Radius = 0.9f;

        public static void Place(Fighter owner, Vector3 p)
        {
            var go = new GameObject("Mine");
            go.transform.position = new Vector3(p.x, 0.05f, p.z);
            var m = go.AddComponent<MineTrap>();
            m.Owner = owner;
            Live.Add(m);
            Fx.Dust(go.transform.position, 0.5f, 4);
            Sfx.Play(SfxId.Click, 0.8f, 0.7f);
        }

        public static void ClearAll()
        {
            for (int i = Live.Count - 1; i >= 0; i--) if (Live[i] != null) Destroy(Live[i].gameObject);
            Live.Clear();
        }

        public static int CountOf(Fighter owner)
        {
            int n = 0;
            for (int i = 0; i < Live.Count; i++) if (Live[i] != null && Live[i].Owner == owner) n++;
            return n;
        }

        /// <summary>Nearest armed mine to a point, or null.</summary>
        public static MineTrap Near(Vector3 p, float maxDist)
        {
            MineTrap best = null; float bd = maxDist;
            for (int i = 0; i < Live.Count; i++)
            {
                var m = Live[i];
                if (m == null || m.t < Arm) continue;
                float d = KMath.Flat(m.transform.position - p).magnitude;
                if (d < bd) { bd = d; best = m; }
            }
            return best;
        }

        void Update()
        {
            t += Time.deltaTime;
            if (t > Life || Owner == null || !Owner.Alive) { Fizzle(); return; }
            blink -= Time.deltaTime;
            bool armed = t > Arm;
            if (blink <= 0f)
            {
                blink = armed ? 0.5f : 0.15f;
                Fx.Glow(transform.position + Vector3.up * 0.12f, new Color(1f, 0.15f, 0.1f), 0.35f, 0.12f);
                Fx.Ring(transform.position, new Color(1f, 0.35f, 0.15f) * 0.7f, Radius, Radius * 1.05f, 0.3f, Quaternion.identity);
            }
            if (!armed) return;
            for (int i = 0; i < Fighter.All.Count; i++)
            {
                var f = Fighter.All[i];
                if (!f.Alive || f.Team == Owner.Team || f.Team == Team.Neutral || !f.Grounded) continue;
                if (KMath.Flat(f.transform.position - transform.position).magnitude > Radius + f.BodyRadius) continue;
                Boom();
                return;
            }
        }

        void Boom()
        {
            Vector3 p = transform.position;
            Fx.Explosion(p, 2f, new Color(1f, 0.5f, 0.15f));
            Fx.Debris(p, new Color(0.35f, 0.3f, 0.25f), 8, 6f);
            WorldLighting.Flash(p + Vector3.up, new Color(2f, 1f, 0.3f), 8f, 0.3f);
            Sfx.Play(SfxId.Explosion, 1f, 1f);
            CameraRig.Shake(0.4f);
            Combat.AreaDamage(Owner, Owner.Team, p + Vector3.up * 0.4f, 2f, 20f, 6f, 8f, true, new Color(1f, 0.5f, 0.15f), false, HitKind.Heavy);
            Kill();
        }

        void Fizzle()
        {
            Fx.Smoke(transform.position + Vector3.up * 0.1f, 0.3f, new Color(0.6f, 0.6f, 0.6f, 0.3f), 3);
            Kill();
        }

        void Kill() { Live.Remove(this); Destroy(gameObject); }
        void OnDestroy() { Live.Remove(this); }
    }
}
