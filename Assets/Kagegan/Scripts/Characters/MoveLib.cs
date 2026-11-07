using UnityEngine;

namespace Kagegan
{
    /// <summary>Shared building blocks for move sets. Every call returns a fresh Move so kits can customise callbacks.</summary>
    public static class MoveLib
    {
        public static Move Jab(bool right, float dmg = 6f, float speed = 1f) => new Move
        {
            Name = right ? "jabR" : "jabL", Windup = right ? PoseLib.JabRWind : PoseLib.JabLWind, Strike = right ? PoseLib.JabR : PoseLib.JabL,
            Startup = 0.09f / speed, Active = 0.07f, Recovery = 0.16f / speed, Damage = dmg, Bone = right ? HitBone.HandR : HitBone.HandL,
            Knockback = 1.6f, Hitstun = 0.32f, Lunge = 2.4f, SlashEuler = new Vector3(0, 0, right ? 20 : -20), Range = 1.5f
        };

        public static Move Hook(bool right, float dmg = 8f) => new Move
        {
            Name = "hook", Windup = right ? PoseLib.HookRWind : PoseLib.HookLWind, Strike = right ? PoseLib.HookR : PoseLib.HookL,
            Startup = 0.12f, Active = 0.08f, Recovery = 0.2f, Damage = dmg, Bone = right ? HitBone.HandR : HitBone.HandL,
            Knockback = 2.4f, Hitstun = 0.38f, Lunge = 2f, Radius = 0.42f, SlashEuler = new Vector3(0, 0, right ? 70 : -70), Range = 1.5f
        };

        public static Move Kick(float dmg = 11f, float knock = 4.5f) => new Move
        {
            Name = "kick", Windup = PoseLib.KickWind, Strike = PoseLib.KickHigh, Startup = 0.14f, Active = 0.09f, Recovery = 0.26f,
            Damage = dmg, Bone = HitBone.FootR, Knockback = knock, Hitstun = 0.45f, Lunge = 2.2f, Radius = 0.45f, SlashEuler = new Vector3(0, 0, 80),
            Kind = HitKind.Light, Range = 1.8f, Shake = 0.15f
        };

        public static Move FrontKick(float dmg = 16f) => new Move
        {
            Name = "frontkick", Windup = PoseLib.KickWind, Strike = PoseLib.KickFront, Startup = 0.2f, Active = 0.1f, Recovery = 0.32f,
            Damage = dmg, Bone = HitBone.FootR, Knockback = 8f, Hitstun = 0.55f, Lunge = 3f, Radius = 0.48f, Kind = HitKind.Heavy, Range = 1.9f,
            StaminaDamage = 18f, Shake = 0.25f, HitStop = 0.07f, StaminaCost = 8f
        };

        public static Move Uppercut(float dmg = 10f, float launch = 11.5f) => new Move
        {
            Name = "launcher", Windup = PoseLib.UpperWind, Strike = PoseLib.Uppercut, Startup = 0.15f, Active = 0.1f, Recovery = 0.28f,
            Damage = dmg, Bone = HitBone.HandR, Launch = launch, Knockback = 1f, Kind = HitKind.Heavy, Lunge = 2f, Radius = 0.48f,
            SlashEuler = new Vector3(-90, 0, 0), Range = 1.5f, Shake = 0.25f, HitStop = 0.08f, StaminaCost = 6f
        };

        public static Move Knee(float dmg = 9f) => new Move
        {
            Name = "knee", Windup = PoseLib.KickWind, Strike = PoseLib.KneeR, Startup = 0.1f, Active = 0.08f, Recovery = 0.2f,
            Damage = dmg, Bone = HitBone.FootR, Reach = new Vector3(0, 0.35f, 0.15f), Knockback = 2f, Hitstun = 0.4f, Lunge = 2.6f, Radius = 0.45f, Range = 1.3f
        };

        public static Move Elbow(float dmg = 18f) => new Move
        {
            Name = "elbow", Windup = PoseLib.HookRWind, Strike = PoseLib.ElbowR, Startup = 0.1f, Active = 0.1f, Recovery = 0.3f,
            Damage = dmg, Bone = HitBone.HandR, Reach = new Vector3(0, 0.2f, 0.1f), Knockback = 9f, Knockdown = true, Kind = HitKind.Heavy, Lunge = 4f,
            Radius = 0.5f, Range = 1.5f, Shake = 0.3f, HitStop = 0.09f, SlashEuler = new Vector3(0, 0, 60)
        };

        public static Move Sweep(float dmg = 12f) => new Move
        {
            Name = "sweep", Windup = PoseLib.Crouch, Strike = PoseLib.Sweep, Startup = 0.16f, Active = 0.1f, Recovery = 0.3f,
            Damage = dmg, Bone = HitBone.FootR, Knockback = 2f, Launch = 5f, Knockdown = true, Kind = HitKind.Heavy, Lunge = 2f, Radius = 0.55f,
            Range = 1.8f, SlashEuler = new Vector3(0, 0, 0)
        };

        public static Move Palm(bool right, float dmg = 7f) => new Move
        {
            Name = "palm", Windup = right ? PoseLib.PalmRWind : PoseLib.PalmLWind, Strike = right ? PoseLib.PalmR : PoseLib.PalmL,
            Startup = 0.1f, Active = 0.08f, Recovery = 0.18f, Damage = dmg, Bone = right ? HitBone.HandR : HitBone.HandL,
            Knockback = 2.2f, Hitstun = 0.35f, Lunge = 2.2f, Radius = 0.42f, Range = 1.5f, SlashFx = false
        };

        public static Move AirKick(float dmg = 7f) => new Move
        {
            Name = "airkick", Windup = PoseLib.AirUp, Strike = PoseLib.AirKick, Startup = 0.08f, Active = 0.09f, Recovery = 0.15f,
            Damage = dmg, Bone = HitBone.FootR, Knockback = 1.2f, Launch = 3.4f, Hitstun = 0.4f, Lunge = 1f, Radius = 0.5f, Range = 1.6f, CancelAt = 0.5f
        };

        public static Move AirPunch(bool right, float dmg = 6f) => new Move
        {
            Name = "airpunch", Windup = right ? PoseLib.JabRWind : PoseLib.JabLWind, Strike = right ? PoseLib.JabR : PoseLib.JabL,
            Startup = 0.07f, Active = 0.08f, Recovery = 0.14f, Damage = dmg, Bone = right ? HitBone.HandR : HitBone.HandL,
            Knockback = 1f, Launch = 3.2f, Hitstun = 0.4f, Lunge = 1f, Radius = 0.48f, Range = 1.5f, CancelAt = 0.5f
        };

        public static Move Slam(float dmg = 15f) => new Move
        {
            Name = "slam", Windup = PoseLib.SlamWind, Strike = PoseLib.Slam, Startup = 0.14f, Active = 0.12f, Recovery = 0.3f,
            Damage = dmg, Bone = HitBone.HandR, Reach = new Vector3(0, -0.2f, 0.2f), Slam = true, Knockback = 2f, Kind = HitKind.Heavy, Lunge = 0.5f,
            Radius = 0.6f, Range = 1.6f, Shake = 0.35f, HitStop = 0.1f, SlashEuler = new Vector3(90, 0, 0)
        };

        public static Move GrabMove() => new Move
        {
            Name = "grab", Windup = PoseLib.GrabReach, Strike = PoseLib.GrabReach, Startup = 0.16f, Active = 0.1f, Recovery = 0.4f,
            Damage = 0.01f, Kind = HitKind.Grab, Unblockable = true, Parryable = false, Bone = HitBone.Body, Reach = new Vector3(0, 0, 0.55f), Radius = 0.45f,
            Lunge = 3f, Range = 1.2f, SlashFx = false, Knockback = 0f, Hitstun = 0f, StaminaCost = 6f
        };

        public static Move ThrowMove(float dmg, float knock, float launch) => new Move
        {
            Name = "throw", Windup = PoseLib.GrabReach, Strike = PoseLib.GrabToss, Startup = 0.38f, Active = 0.05f, Recovery = 0.35f,
            Damage = 0f, Lunge = 0f, SlashFx = false, NoTracking = true,
            OnActive = f => f.ReleaseThrow(dmg, knock, launch)
        };

        public static void WireGrab(Kit k, float dmg = 22f, float knock = 9f, float launch = 6f)
        {
            k.Throw = ThrowMove(dmg, knock, launch);
            k.Grab = GrabMove();
            var toss = k.Throw;
            k.Grab.OnHit = (a, v) => a.BeginThrow(v, toss);
        }

        /// <summary>Basic projectile-casting move: windup + strike pose, spawns on active frame.</summary>
        public static Move Cast(string name, Pose windup, Pose strike, float startup, float recovery, System.Action<Fighter> spawn) => new Move
        {
            Name = name, Windup = windup, Strike = strike, Startup = startup, Active = 0.05f, Recovery = recovery, Damage = 0f, Lunge = 0f,
            SlashFx = false, Kind = HitKind.Technique, OnActive = spawn, Sfx = SfxId.Count
        };

        public static Vector3 AimDir(Fighter f)
        {
            if (f.Target != null && f.Target.Alive)
            {
                Vector3 d = f.Target.Center - (f.transform.position + Vector3.up * 1.2f);
                return d.normalized;
            }
            return f.transform.forward;
        }
    }

    /// <summary>Creates complete fighters (rig + animator + controller + kit).</summary>
    public static class FighterFactory
    {
        public static Fighter Make(string name, Look look, StanceKind stance, RunStyle run, Kit kit, Team team, Vector3 pos, Transform parent,
                                   float hp, float speed, float scale = 1f)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = pos;
            var cc = go.AddComponent<CharacterController>();
            var f = go.AddComponent<Fighter>();
            f.DisplayName = name;
            f.Team = team;
            f.MaxHp = hp;
            f.MoveSpeed = speed;
            f.Height = 1.75f * look.height * scale;
            f.BodyRadius = 0.34f * look.bulk * scale;
            var rig = HumanoidRig.Build(go.transform, look, name);
            rig.transform.localScale = Vector3.one * scale;
            var anim = go.AddComponent<ProcAnimator>();
            anim.Init(rig, stance, run);
            f.Init(kit, rig, anim);
            return f;
        }
    }
}
