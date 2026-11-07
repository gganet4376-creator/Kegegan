using System;
using UnityEngine;

namespace Kagegan
{
    public enum HitKind { Light = 0, Heavy = 1, Grab = 2, Technique = 3, Projectile = 4, Ultimate = 5, Environment = 6 }
    public enum HitBone { HandR, HandL, FootR, FootL, Body, Head }
    public enum HitResult { Miss, Hit, Blocked, Parried, Absorbed, PerfectDodge, Armored, Clash }

    /// <summary>One attack/technique step: frame data, hitbox, reaction it causes, animation and callbacks.</summary>
    public class Move
    {
        public string Name = "move";
        public float Startup = 0.12f, Active = 0.08f, Recovery = 0.22f;
        public float Damage = 8f, StaminaDamage = 8f, Knockback = 2.5f, Launch, Hitstun = 0.35f;
        public bool Knockdown, Unblockable, Armor, Slam, HitsDowned, Parryable = true, Airborne, NoTracking;
        public HitKind Kind = HitKind.Light;
        public HitBone Bone = HitBone.HandR;
        public float Radius = 0.38f;
        public Vector3 Reach = new Vector3(0, 0, 0.2f);
        public float Lunge = 1.8f;          // forward speed during startup+active
        public float StaminaCost = 3f, ChakraCost;
        public float CancelAt = 0.55f;      // fraction of the move after which the next chain input is accepted
        public int Hits = 1;
        public float AnimSpeed = 1f;
        public Pose Windup, Strike;
        public SfxId Sfx = SfxId.Whoosh, HitSfx = SfxId.Hit;
        public float Shake = 0.12f, HitStop = 0.045f;
        public Color FxColor = new Color(1f, 0.9f, 0.6f);
        public bool SlashFx = true;
        public Vector3 SlashEuler = new Vector3(0, 0, 0);
        public float Range = 1.6f;          // AI hint
        public Action<Fighter> OnStart, OnActive, OnEnd;
        public Action<Fighter, Fighter> OnHit;
        public Action<Fighter, float> OnTick; // called every frame while the move runs (t = normalized)
        Clip clip;

        public float Total => Startup + Active + Recovery;

        public Clip Clip
        {
            get
            {
                if (clip == null) clip = Clip.Attack(Windup ?? Strike ?? PoseLib.StanceBrawler, Strike ?? PoseLib.StanceBrawler, Startup, Active, Recovery);
                return clip;
            }
        }

        public Move Copy()
        {
            var m = (Move)MemberwiseClone();
            m.clip = null;
            return m;
        }

        public static int Power(HitKind k)
        {
            switch (k)
            {
                case HitKind.Light: return 1;
                case HitKind.Heavy: return 2;
                case HitKind.Grab: return 0;
                case HitKind.Technique: case HitKind.Projectile: return 3;
                default: return 5;
            }
        }
    }

    public struct HitInfo
    {
        public Fighter Attacker;
        public Move Move;
        public Projectile Proj;
        public float Damage, Stamina, Knockback, Launch, Hitstun;
        public HitKind Kind;
        public bool Unblockable, Knockdown, Parryable, Slam, Absorbable, IgnoreArmor;
        public Vector3 Point, Dir;
        public Color FxColor;
        public SfxId Sfx;
        public float Shake, HitStop;

        public static HitInfo FromMove(Fighter a, Move m, Vector3 point, Vector3 dir)
        {
            return new HitInfo
            {
                Attacker = a, Move = m, Damage = m.Damage, Stamina = m.StaminaDamage, Knockback = m.Knockback, Launch = m.Launch,
                Hitstun = m.Hitstun, Kind = m.Kind, Unblockable = m.Unblockable, Knockdown = m.Knockdown, Parryable = m.Parryable,
                Slam = m.Slam, Absorbable = m.Kind == HitKind.Technique || m.Kind == HitKind.Ultimate, Point = point, Dir = dir,
                FxColor = m.FxColor, Sfx = m.HitSfx, Shake = m.Shake, HitStop = m.HitStop
            };
        }
    }

    /// <summary>A special technique bound to a button. Use returns false if it could not start (no target, wrong state).</summary>
    public class Technique
    {
        public string Name, Short;
        public float Cost, Cooldown, Range = 8f;
        public float Cd;
        public Color Color = Color.white;
        public bool Hold;           // hold-to-channel (e.g. absorption)
        public bool Offensive = true;
        public bool AiUsable = true;
        public Func<Fighter, bool> Use;
        public Action<Fighter, bool> Held;

        public bool Ready(Fighter f) => Cd <= 0f && (f.InfiniteChakra || f.Chakra >= Cost);
    }
}
