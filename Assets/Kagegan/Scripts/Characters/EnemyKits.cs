using UnityEngine;

namespace Kagegan
{
    public enum MinionType { ForestBandit, ForestAssassin, ForestThrower, CityBrute, CityMerc, CityBomber, MoonGuard, MoonPriest, MoonBone, SnakeAdept }

    /// <summary>Minion move sets: shared basics plus one or two type-specific techniques.</summary>
    public class MinionKit : Kit
    {
        readonly MinionType type;
        public MinionKit(MinionType t) { type = t; }

        public override void Setup(Fighter f)
        {
            base.Setup(f);
            bool heavy = type == MinionType.CityBrute || type == MinionType.MoonBone;
            float spd = type == MinionType.ForestAssassin || type == MinionType.CityMerc || type == MinionType.MoonGuard ? 1.15f : heavy ? 0.85f : 1f;
            LightChain = new[] { MoveLib.Jab(false, 5f, spd), MoveLib.Jab(true, 6f, spd), heavy ? MoveLib.Elbow(12f) : MoveLib.Kick(8f, 3.5f) };
            if (heavy) { LightChain[2].Armor = true; LightChain[2].Knockdown = false; LightChain[2].Knockback = 6f; }
            AirChain = new[] { MoveLib.AirPunch(true, 5f), MoveLib.AirKick(6f) };
            AirFinisher = MoveLib.Slam(10f);
            Heavy = MoveLib.FrontKick(heavy ? 18f : 12f);
            Heavy.Armor = heavy;
            Launcher = MoveLib.Uppercut(8f, 10f);
            MoveLib.WireGrab(this, heavy ? 20f : 14f, 8f, 5f);
            Counter = MoveLib.Elbow(14f);

            switch (type)
            {
                case MinionType.ForestBandit:
                case MinionType.SnakeAdept:
                    Techs = new[] { Kunai(1, 8f, 9f) };
                    break;
                case MinionType.ForestAssassin:
                    Techs = new[] { DashSlash(14f, new Color(0.9f, 0.95f, 1f), false) };
                    break;
                case MinionType.ForestThrower:
                    Techs = new[] { Kunai(3, 7f, 6f), SmokeBomb() };
                    break;
                case MinionType.CityBrute:
                    Techs = new[] { Quake() };
                    break;
                case MinionType.CityMerc:
                    Techs = new[] { DashSlash(18f, new Color(0.55f, 0.75f, 1f), true) };
                    break;
                case MinionType.CityBomber:
                    Techs = new[] { FireBomb(), SmokeBomb() };
                    break;
                case MinionType.MoonGuard:
                    Techs = new[] { MoonStep() };
                    break;
                case MinionType.MoonPriest:
                    Techs = new[] { MoonOrbs() };
                    break;
                case MinionType.MoonBone:
                    Techs = new[] { BoneSpikes() };
                    break;
            }
        }

        Technique Kunai(int count, float dmg, float cd) => new Technique
        {
            Name = "Кунай", Cost = 10f, Cooldown = cd, Range = 14f,
            Use = f => { f.StartMove(MoveLib.Cast("kunai", PoseLib.ThrowWind, PoseLib.Throw, 0.3f, 0.3f, ff =>
            {
                Vector3 fwd = MoveLib.AimDir(ff);
                for (int i = 0; i < count; i++)
                {
                    Vector3 d = Quaternion.Euler(0, (i - (count - 1) * 0.5f) * 10f, 0) * fwd;
                    var p = Projectile.Spawn(ff, ProjStyle.Kunai, ff.Rig.BonePos(B.HandR), d * 22f, dmg, new Color(0.8f, 0.85f, 0.9f), 1.2f);
                    p.Power = 1f; p.Knockback = 1.5f; p.Hitstun = 0.3f; p.Absorbable = true;
                }
                Sfx.Play(SfxId.Whoosh, 0.6f, 1.6f);
            })); return true; }
        };

        Technique DashSlash(float dmg, Color c, bool lightning) => new Technique
        {
            Name = "Рывок", Cost = 15f, Cooldown = 6f, Range = 9f,
            Use = f =>
            {
                var m = new Move
                {
                    Name = "dashslash", Windup = PoseLib.Crouch, Strike = PoseLib.Thrust, Startup = 0.35f, Active = 0.25f, Recovery = 0.4f,
                    Damage = dmg, Kind = HitKind.Technique, Lunge = 15f, Knockback = 6f, Radius = 0.5f, Reach = new Vector3(0, 0, 0.3f),
                    FxColor = c, Shake = 0.25f, Range = 9f
                };
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

        Technique FireBomb() => new Technique
        {
            Name = "Бомба", Cost = 20f, Cooldown = 5f, Range = 13f,
            Use = f =>
            {
                f.StartMove(MoveLib.Cast("bomb", PoseLib.ThrowWind, PoseLib.Throw, 0.35f, 0.35f, ff =>
                {
                    Vector3 to = ff.Target != null ? ff.Target.transform.position : ff.transform.position + ff.transform.forward * 8f;
                    Vector3 from = ff.Rig.BonePos(B.HandR);
                    float T = 0.9f, g = 14f;
                    Vector3 v = (to - from) / T; v.y = (to.y - from.y + 0.5f * g * T * T) / T;
                    var p = Projectile.Spawn(ff, ProjStyle.Fire, from, v, 18f, new Color(1f, 0.5f, 0.15f), 0.8f);
                    p.Gravity = g; p.Explode = true; p.ExplodeRadius = 2.4f; p.Knockdown = true; p.Life = 1.6f; p.Power = 2f;
                    Sfx.Play(SfxId.Whoosh, 0.6f, 0.9f);
                }));
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
                Hazard.Spawn(f, HazardKind.BoneSpikes, f.Target.transform.position, 1.8f, 0.8f, 20f, new Color(0.95f, 0.92f, 0.85f));
                return true;
            }
        };
    }
}
