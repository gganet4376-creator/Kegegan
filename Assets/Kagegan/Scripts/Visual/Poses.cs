using UnityEngine;

namespace Kagegan
{
    /// <summary>A full-body pose: Euler offsets (degrees) from the rest pose per bone plus a hips translation.</summary>
    public class Pose
    {
        public readonly Vector3[] R = new Vector3[(int)B.Count];
        public Vector3 HipsOffset;
        Quaternion[] q;

        public Pose S(B b, float x, float y = 0f, float z = 0f) { R[(int)b] = new Vector3(x, y, z); q = null; return this; }
        public Pose H(float x, float y, float z) { HipsOffset = new Vector3(x, y, z); return this; }

        public Pose Clone()
        {
            var p = new Pose { HipsOffset = HipsOffset };
            System.Array.Copy(R, p.R, R.Length);
            return p;
        }

        /// <summary>Copy of the pose with left and right swapped (for alternate-side attacks).</summary>
        public Pose Mirror()
        {
            var p = new Pose { HipsOffset = new Vector3(-HipsOffset.x, HipsOffset.y, HipsOffset.z) };
            for (int i = 0; i < R.Length; i++)
            {
                int j = MirrorIndex(i);
                Vector3 e = R[i];
                p.R[j] = new Vector3(e.x, -e.y, -e.z);
            }
            return p;
        }

        static int MirrorIndex(int i)
        {
            switch ((B)i)
            {
                case B.UArmL: return (int)B.UArmR; case B.UArmR: return (int)B.UArmL;
                case B.LArmL: return (int)B.LArmR; case B.LArmR: return (int)B.LArmL;
                case B.HandL: return (int)B.HandR; case B.HandR: return (int)B.HandL;
                case B.ThighL: return (int)B.ThighR; case B.ThighR: return (int)B.ThighL;
                case B.ShinL: return (int)B.ShinR; case B.ShinR: return (int)B.ShinL;
                case B.FootL: return (int)B.FootR; case B.FootR: return (int)B.FootL;
                case B.EyeL: return (int)B.EyeR; case B.EyeR: return (int)B.EyeL;
                case B.LidL: return (int)B.LidR; case B.LidR: return (int)B.LidL;
                default: return i;
            }
        }

        public Quaternion[] Q
        {
            get
            {
                if (q == null)
                {
                    q = new Quaternion[R.Length];
                    for (int i = 0; i < R.Length; i++) q[i] = Quaternion.Euler(R[i]);
                }
                return q;
            }
        }

        public void Invalidate() { q = null; }
    }

    /// <summary>Keyframed action clip; a null pose means "the current base pose" so actions blend in/out of locomotion.</summary>
    public class Clip
    {
        public Pose[] Poses;
        public float[] Times;
        public float Length => Times[Times.Length - 1];

        public static Clip Attack(Pose windup, Pose strike, float startup, float active, float recovery)
        {
            return new Clip
            {
                Poses = new[] { null, windup, strike, strike, null },
                Times = new[] { 0f, startup * 0.7f, Mathf.Max(startup, 0.02f), startup + active, startup + active + recovery }
            };
        }

        public static Clip Hold(Pose p, float blendIn, float hold, float blendOut)
        {
            return new Clip { Poses = new[] { null, p, p, null }, Times = new[] { 0f, blendIn, blendIn + hold, blendIn + hold + blendOut } };
        }

        public static Clip Seq(params object[] keys)
        {
            int n = keys.Length / 2;
            var c = new Clip { Poses = new Pose[n], Times = new float[n] };
            for (int i = 0; i < n; i++) { c.Times[i] = System.Convert.ToSingle(keys[i * 2]); c.Poses[i] = keys[i * 2 + 1] as Pose; }
            return c;
        }
    }

    /// <summary>Hand-authored pose library (degrees). Conventions: arm X- = raise forward, leg X- = raise forward, knee/shin X+ = bend.</summary>
    public static class PoseLib
    {
        public static readonly Pose Neutral = new Pose();

        // ----- stances -----
        public static readonly Pose StanceBrawler = new Pose().H(0, -0.07f, 0)
            .S(B.Hips, 0, 25).S(B.Spine, 8, -10).S(B.Chest, 5, -12).S(B.Neck, 0, -8).S(B.Head, -4)
            .S(B.UArmL, -55, 0, -18).S(B.LArmL, -95).S(B.UArmR, -28, 0, 22).S(B.LArmR, -120)
            .S(B.ThighL, -30, 0, -6).S(B.ShinL, 40).S(B.FootL, -12).S(B.ThighR, 12, 0, 8).S(B.ShinR, 35).S(B.FootR, -12);

        public static readonly Pose StanceSwift = new Pose().H(0, -0.13f, 0)
            .S(B.Hips, 0, 30).S(B.Spine, 18, -12).S(B.Chest, 6, -12).S(B.Neck, -8, -8).S(B.Head, -8)
            .S(B.UArmL, -50, 0, -30).S(B.LArmL, -70).S(B.HandL, -20).S(B.UArmR, 20, 0, 30).S(B.LArmR, -40)
            .S(B.ThighL, -45, 0, -10).S(B.ShinL, 70).S(B.FootL, -20).S(B.ThighR, 10, 0, 12).S(B.ShinR, 55).S(B.FootR, -15);

        public static readonly Pose StanceHeavy = new Pose().H(0, -0.05f, 0)
            .S(B.Hips, 0, 10).S(B.Spine, -4).S(B.Chest, -4, -6).S(B.Head, -6)
            .S(B.UArmL, -18, 0, -24).S(B.LArmL, -40).S(B.UArmR, -14, 0, 28).S(B.LArmR, -35)
            .S(B.ThighL, -12, 0, -14).S(B.ShinL, 22).S(B.FootL, -6).S(B.ThighR, -6, 0, 14).S(B.ShinR, 18).S(B.FootR, -6);

        public static readonly Pose StanceVoid = new Pose().H(0, -0.02f, 0)
            .S(B.Hips, 0, 15).S(B.Spine, -3).S(B.Chest, -3, -10).S(B.Head, -10)
            .S(B.UArmL, 15, 0, -12).S(B.LArmL, -55).S(B.UArmR, -45, 0, 12).S(B.LArmR, -60).S(B.HandR, -50)
            .S(B.ThighL, -10, 0, -5).S(B.ShinL, 12).S(B.ThighR, 6, 0, 6).S(B.ShinR, 10);

        public static readonly Pose StanceSerpent = new Pose().H(0, -0.12f, 0.05f)
            .S(B.Hips, 0, 15).S(B.Spine, 25).S(B.Chest, 12).S(B.Neck, -15).S(B.Head, -15)
            .S(B.UArmL, -35, 0, -12).S(B.LArmL, -30).S(B.HandL, -30).S(B.UArmR, -40, 0, 12).S(B.LArmR, -35).S(B.HandR, -30)
            .S(B.ThighL, -35, 0, -12).S(B.ShinL, 50).S(B.ThighR, 5, 0, 12).S(B.ShinR, 40);

        public static readonly Pose StanceThunder = new Pose().H(0, -0.09f, 0)
            .S(B.Hips, 0, 35).S(B.Spine, 10, -15).S(B.Chest, 4, -15).S(B.Head, -5)
            .S(B.UArmL, -40, 0, 25).S(B.LArmL, -60).S(B.UArmR, -10, 0, 10).S(B.LArmR, -60).S(B.HandR, -30)
            .S(B.ThighL, -35, 0, -8).S(B.ShinL, 45).S(B.ThighR, 15, 0, 10).S(B.ShinR, 40);

        public static readonly Pose StanceMoon = new Pose().H(0, 0f, 0)
            .S(B.Spine, -4).S(B.Chest, -4).S(B.Head, -8)
            .S(B.UArmL, -20, 0, -38).S(B.LArmL, -25).S(B.HandL, -40).S(B.UArmR, -20, 0, 38).S(B.LArmR, -25).S(B.HandR, -40)
            .S(B.ThighL, -4, 0, -3).S(B.ThighR, 4, 0, 3);

        // ----- strikes -----
        public static readonly Pose JabRWind = new Pose().H(0, -0.08f, 0).S(B.Hips, 0, 20).S(B.Chest, 0, 25).S(B.UArmR, -30, 0, 30).S(B.LArmR, -130).S(B.UArmL, -60, 0, -10).S(B.LArmL, -100)
            .S(B.ThighL, -30).S(B.ShinL, 40).S(B.ThighR, 15).S(B.ShinR, 30);
        public static readonly Pose JabR = new Pose().H(0, -0.1f, 0.08f).S(B.Hips, 0, -10).S(B.Spine, 6).S(B.Chest, 0, -28).S(B.UArmR, -88, 0, 4).S(B.LArmR, -6).S(B.UArmL, -40, 0, -20).S(B.LArmL, -120)
            .S(B.ThighL, -40).S(B.ShinL, 35).S(B.ThighR, 20).S(B.ShinR, 25);
        public static readonly Pose JabLWind = JabRWind.Mirror();
        public static readonly Pose JabL = JabR.Mirror();

        public static readonly Pose HookRWind = new Pose().H(0, -0.08f, 0).S(B.Chest, 0, 40).S(B.UArmR, -50, 0, 70).S(B.LArmR, -100).S(B.UArmL, -60, 0, -15).S(B.LArmL, -110).S(B.ThighL, -25).S(B.ShinL, 35);
        public static readonly Pose HookR = new Pose().H(0, -0.1f, 0.05f).S(B.Hips, 0, -20).S(B.Chest, 0, -45).S(B.UArmR, -85, 0, 50).S(B.LArmR, -85).S(B.UArmL, -40, 0, -25).S(B.LArmL, -120).S(B.ThighL, -35).S(B.ShinL, 35).S(B.ThighR, 15).S(B.ShinR, 25);
        public static readonly Pose HookL = HookR.Mirror();
        public static readonly Pose HookLWind = HookRWind.Mirror();

        public static readonly Pose UpperWind = new Pose().H(0, -0.22f, 0).S(B.Spine, 28).S(B.Chest, 0, 20).S(B.UArmR, -10, 0, 20).S(B.LArmR, -130).S(B.UArmL, -50, 0, -20).S(B.LArmL, -100)
            .S(B.ThighL, -55).S(B.ShinL, 80).S(B.ThighR, 10).S(B.ShinR, 70);
        public static readonly Pose Uppercut = new Pose().H(0, 0.06f, 0.05f).S(B.Spine, -15).S(B.Chest, -5, -20).S(B.Head, -20).S(B.UArmR, -170, 0, 10).S(B.LArmR, -20).S(B.UArmL, -20, 0, -30).S(B.LArmL, -60)
            .S(B.ThighL, -50).S(B.ShinL, 60).S(B.ThighR, 10).S(B.ShinR, 5);

        public static readonly Pose KickWind = new Pose().H(0, -0.06f, 0).S(B.Hips, 0, 20).S(B.Spine, -5).S(B.ThighR, -45, 0, 20).S(B.ShinR, 100).S(B.UArmL, -40, 0, -50).S(B.UArmR, -30, 0, 50).S(B.LArmL, -60).S(B.LArmR, -60)
            .S(B.ThighL, -10).S(B.ShinL, 15);
        public static readonly Pose KickHigh = new Pose().H(0, -0.04f, 0).S(B.Hips, 0, -40, 10).S(B.Spine, -12, 0, -20).S(B.ThighR, -100, 0, 40).S(B.ShinR, 5).S(B.FootR, 20)
            .S(B.UArmL, -30, 0, -70).S(B.LArmL, -30).S(B.UArmR, -20, 0, 50).S(B.LArmR, -80).S(B.ThighL, -5).S(B.ShinL, 15);
        public static readonly Pose KickFront = new Pose().H(0, -0.03f, -0.03f).S(B.Spine, -18).S(B.ThighR, -100).S(B.ShinR, 0).S(B.FootR, 30).S(B.UArmL, -40, 0, -40).S(B.UArmR, -40, 0, 40).S(B.LArmL, -90).S(B.LArmR, -90).S(B.ThighL, -5).S(B.ShinL, 15);
        public static readonly Pose KneeR = new Pose().H(0, 0.02f, 0.06f).S(B.Spine, 12).S(B.ThighR, -110).S(B.ShinR, 120).S(B.UArmL, -80, 0, 10).S(B.LArmL, -60).S(B.UArmR, -80, 0, -10).S(B.LArmR, -60);
        public static readonly Pose ElbowR = new Pose().H(0, -0.08f, 0.1f).S(B.Hips, 0, -25).S(B.Chest, 0, -45).S(B.UArmR, -90, 0, 70).S(B.LArmR, -145).S(B.UArmL, -50, 0, -20).S(B.LArmL, -110).S(B.ThighL, -40).S(B.ShinL, 40).S(B.ThighR, 20).S(B.ShinR, 30);
        public static readonly Pose Sweep = new Pose().H(0, -0.45f, 0).S(B.Spine, 30).S(B.ThighL, -90, 0, -20).S(B.ShinL, 125).S(B.ThighR, -55, 0, 65).S(B.ShinR, 10).S(B.UArmL, 20, 0, -30).S(B.LArmL, -20).S(B.UArmR, -60, 0, 10).S(B.LArmR, -30);

        public static readonly Pose PalmRWind = new Pose().H(0, -0.06f, 0).S(B.Chest, 0, 25).S(B.UArmR, -20, 0, 20).S(B.LArmR, -110).S(B.HandR, -40).S(B.UArmL, -50, 0, -10).S(B.LArmL, -90).S(B.ThighL, -25).S(B.ShinL, 30);
        public static readonly Pose PalmR = new Pose().H(0, -0.1f, 0.08f).S(B.Hips, 0, -10).S(B.Spine, 5).S(B.Chest, 0, -25).S(B.UArmR, -90).S(B.LArmR, 0).S(B.HandR, -80).S(B.UArmL, -20, 0, -30).S(B.LArmL, -60)
            .S(B.ThighL, -40).S(B.ShinL, 35).S(B.ThighR, 20).S(B.ShinR, 25);
        public static readonly Pose PalmL = PalmR.Mirror();
        public static readonly Pose PalmLWind = PalmRWind.Mirror();

        public static readonly Pose BeamWind = new Pose().H(0, -0.1f, -0.03f).S(B.Spine, 5).S(B.Chest, 0, 30).S(B.UArmL, 20, 0, 20).S(B.LArmL, -100).S(B.UArmR, 20, 0, -10).S(B.LArmR, -100)
            .S(B.ThighL, -35).S(B.ShinL, 40).S(B.ThighR, 15).S(B.ShinR, 35);
        public static readonly Pose Beam = new Pose().H(0, -0.12f, 0.05f).S(B.Spine, -4).S(B.UArmL, -90, 0, 12).S(B.LArmL, 0).S(B.HandL, -80).S(B.UArmR, -90, 0, -12).S(B.LArmR, 0).S(B.HandR, -80)
            .S(B.ThighL, -40).S(B.ShinL, 45).S(B.ThighR, 20).S(B.ShinR, 30);

        public static readonly Pose Overhead = new Pose().H(0, -0.03f, 0).S(B.Spine, -15).S(B.Head, -22).S(B.UArmL, -170, 0, -18).S(B.LArmL, -10).S(B.UArmR, -170, 0, 18).S(B.LArmR, -10)
            .S(B.ThighL, -10, 0, -10).S(B.ShinL, 15).S(B.ThighR, -5, 0, 10).S(B.ShinR, 12);
        public static readonly Pose Seal = new Pose().H(0, -0.08f, 0).S(B.Spine, 6).S(B.Head, 6).S(B.UArmL, -45, 0, 20).S(B.LArmL, -110, 25).S(B.UArmR, -45, 0, -20).S(B.LArmR, -110, -25)
            .S(B.ThighL, -20, 0, -10).S(B.ShinL, 30).S(B.ThighR, -10, 0, 10).S(B.ShinR, 25);

        public static readonly Pose ThrowWind = new Pose().H(0, -0.05f, -0.05f).S(B.Spine, -12).S(B.Chest, 0, 35).S(B.UArmR, -160, 0, 25).S(B.LArmR, -90).S(B.UArmL, -70, 0, -10).S(B.LArmL, -30).S(B.ThighL, -35).S(B.ShinL, 30);
        public static readonly Pose Throw = new Pose().H(0, -0.12f, 0.1f).S(B.Spine, 22).S(B.Chest, 0, -30).S(B.UArmR, -70, 0, 10).S(B.LArmR, -10).S(B.UArmL, 10, 0, -30).S(B.LArmL, -40).S(B.ThighL, -45).S(B.ShinL, 45).S(B.ThighR, 20).S(B.ShinR, 30);

        public static readonly Pose SlamWind = new Pose().H(0, 0, 0).S(B.Spine, -20).S(B.UArmL, -170, 0, -10).S(B.UArmR, -170, 0, 10).S(B.LArmL, -60).S(B.LArmR, -60).S(B.ThighL, -50).S(B.ShinL, 80).S(B.ThighR, -40).S(B.ShinR, 70);
        public static readonly Pose Slam = new Pose().H(0, -0.05f, 0.05f).S(B.Spine, 40).S(B.Head, -20).S(B.UArmL, -60, 0, 10).S(B.UArmR, -60, 0, -10).S(B.LArmL, -5).S(B.LArmR, -5).S(B.ThighL, -60).S(B.ShinL, 80).S(B.ThighR, -60).S(B.ShinR, 80);
        public static readonly Pose AirKick = new Pose().S(B.Spine, -10).S(B.ThighR, -85, 0, 10).S(B.ShinR, 0).S(B.FootR, 20).S(B.ThighL, -30).S(B.ShinL, 90).S(B.UArmL, -40, 0, -50).S(B.UArmR, -20, 0, 60);
        public static readonly Pose Stomp = new Pose().H(0, -0.15f, 0).S(B.Spine, 15).S(B.ThighR, -20, 0, 15).S(B.ShinR, 20).S(B.ThighL, -70, 0, -10).S(B.ShinL, 60).S(B.UArmL, -30, 0, -45).S(B.UArmR, -30, 0, 45).S(B.LArmL, -60).S(B.LArmR, -60);

        public static readonly Pose Thrust = new Pose().H(0, -0.22f, 0.12f).S(B.Spine, 32).S(B.Head, -25).S(B.UArmR, -85).S(B.LArmR, 0).S(B.HandR, -70).S(B.UArmL, 35, 0, -30).S(B.LArmL, -20)
            .S(B.ThighL, -70).S(B.ShinL, 60).S(B.ThighR, 35).S(B.ShinR, 40);
        public static readonly Pose GrabReach = new Pose().H(0, -0.1f, 0.08f).S(B.Spine, 15).S(B.UArmL, -85, 0, 8).S(B.LArmL, -25).S(B.UArmR, -85, 0, -8).S(B.LArmR, -25).S(B.ThighL, -40).S(B.ShinL, 40).S(B.ThighR, 15).S(B.ShinR, 30);
        public static readonly Pose GrabToss = new Pose().H(0, -0.05f, 0).S(B.Spine, -20).S(B.Chest, 0, 60).S(B.UArmL, -150, 0, -10).S(B.LArmL, -30).S(B.UArmR, -150, 0, 10).S(B.LArmR, -30);

        // ----- reactions -----
        public static readonly Pose Block = new Pose().H(0, -0.1f, 0).S(B.Spine, 15).S(B.Head, 10).S(B.UArmL, -75, 0, 25).S(B.LArmL, -115).S(B.UArmR, -75, 0, -25).S(B.LArmR, -115)
            .S(B.ThighL, -30, 0, -8).S(B.ShinL, 45).S(B.ThighR, 5, 0, 8).S(B.ShinR, 40);
        public static readonly Pose Hurt = new Pose().H(0, -0.04f, -0.05f).S(B.Spine, -16).S(B.Chest, -10).S(B.Head, -22).S(B.UArmL, -25, 0, -35).S(B.LArmL, -35).S(B.UArmR, -25, 0, 35).S(B.LArmR, -35)
            .S(B.ThighL, -15).S(B.ShinL, 25).S(B.ThighR, 10).S(B.ShinR, 25);
        public static readonly Pose HurtBack = new Pose().H(0, -0.08f, 0.05f).S(B.Spine, 25).S(B.Head, 15).S(B.UArmL, 20, 0, -20).S(B.UArmR, 20, 0, 20).S(B.ThighL, -25).S(B.ShinL, 40);
        public static readonly Pose Dizzy = new Pose().H(0, -0.06f, 0).S(B.Spine, 12).S(B.Head, 25, 0, 15).S(B.UArmL, -8, 0, -8).S(B.LArmL, -10).S(B.UArmR, -8, 0, 8).S(B.LArmR, -10).S(B.ThighL, -15).S(B.ShinL, 30).S(B.ThighR, -10).S(B.ShinR, 30);
        public static readonly Pose Down = new Pose().H(0, -0.8f, -0.2f).S(B.Hips, -86).S(B.Head, -10).S(B.UArmL, 0, 0, -70).S(B.LArmL, -20).S(B.UArmR, 0, 0, 70).S(B.LArmR, -20)
            .S(B.ThighL, -12, 0, -8).S(B.ShinL, 25).S(B.ThighR, -2, 0, 10).S(B.ShinR, 10);
        // face-down (knocked forward / hit from behind): mirror of Down, head turned so the cheek rests on the floor
        public static readonly Pose DownFront = new Pose().H(0, -0.8f, 0.2f).S(B.Hips, 86).S(B.Spine, -6).S(B.Neck, -15).S(B.Head, -25, 65)
            .S(B.UArmL, -20, 0, -75).S(B.LArmL, -70).S(B.UArmR, 10, 0, 70).S(B.LArmR, -25)
            .S(B.ThighL, 4, 0, -12).S(B.ShinL, 45).S(B.ThighR, -6, 0, 6).S(B.ShinR, 10);
        // half on the side: lying on the back with the torso rolled over and the legs stacked and bent
        public static readonly Pose DownSide = new Pose().H(0.05f, -0.78f, -0.18f).S(B.Hips, -84, 0, 0).S(B.Spine, 0, 45, 8).S(B.Chest, 0, 25).S(B.Head, -5, 30)
            .S(B.UArmL, -60, 0, -20).S(B.LArmL, -80).S(B.UArmR, 0, 0, 80).S(B.LArmR, -30)
            .S(B.ThighL, -55, 0, 10).S(B.ShinL, 70).S(B.ThighR, -35, 0, 4).S(B.ShinR, 55);
        // push-up from face-down: hands under the shoulders, one knee drawn under the hips
        public static readonly Pose PushUp = new Pose().H(0, -0.52f, 0.12f).S(B.Hips, 55).S(B.Spine, -12).S(B.Head, -35)
            .S(B.UArmL, -75, 0, -12).S(B.LArmL, -10).S(B.UArmR, -75, 0, 12).S(B.LArmR, -10)
            .S(B.ThighL, -105).S(B.ShinL, 120).S(B.ThighR, -30).S(B.ShinR, 70);
        // tumbling forward through the air after a hit from behind
        public static readonly Pose JuggledFront = new Pose().H(0, 0, 0).S(B.Hips, 40).S(B.Spine, 10).S(B.Head, -30).S(B.UArmL, -70, 0, -50).S(B.UArmR, -70, 0, 50)
            .S(B.LArmL, -40).S(B.LArmR, -40).S(B.ThighL, 15).S(B.ShinL, 60).S(B.ThighR, -10).S(B.ShinR, 40);
        public static readonly Pose GetUpMid = new Pose().H(0, -0.5f, -0.1f).S(B.Hips, -30).S(B.Spine, 40).S(B.Head, -10).S(B.UArmL, 30, 0, -20).S(B.LArmL, -10).S(B.UArmR, 30, 0, 20).S(B.LArmR, -10)
            .S(B.ThighL, -100).S(B.ShinL, 130).S(B.ThighR, -70).S(B.ShinR, 110);
        public static readonly Pose Crouch = new Pose().H(0, -0.32f, 0).S(B.Spine, 30).S(B.Head, -15).S(B.UArmL, -50, 0, -20).S(B.LArmL, -60).S(B.UArmR, -50, 0, 20).S(B.LArmR, -60)
            .S(B.ThighL, -80).S(B.ShinL, 120).S(B.FootL, -30).S(B.ThighR, -60).S(B.ShinR, 110).S(B.FootR, -30);
        public static readonly Pose AirUp = new Pose().S(B.Spine, 12).S(B.UArmL, -30, 0, -40).S(B.UArmR, -30, 0, 40).S(B.LArmL, -60).S(B.LArmR, -60).S(B.ThighL, -70).S(B.ShinL, 95).S(B.ThighR, -40).S(B.ShinR, 80);
        public static readonly Pose AirFall = new Pose().S(B.Spine, -4).S(B.UArmL, -110, 0, -45).S(B.UArmR, -110, 0, 45).S(B.LArmL, -20).S(B.LArmR, -20).S(B.ThighL, -25).S(B.ShinL, 35).S(B.ThighR, -10).S(B.ShinR, 25);
        public static readonly Pose Juggled = new Pose().H(0, 0, 0).S(B.Hips, -45).S(B.Spine, -15).S(B.Head, -20).S(B.UArmL, -150, 0, -40).S(B.UArmR, -150, 0, 40).S(B.ThighL, -30).S(B.ShinL, 50).S(B.ThighR, -10).S(B.ShinR, 30);
        public static readonly Pose Grabbed = new Pose().H(0, 0.05f, 0).S(B.Spine, -12).S(B.Head, -18).S(B.UArmL, -100, 0, -40).S(B.UArmR, -100, 0, 40).S(B.LArmL, -40).S(B.LArmR, -40).S(B.ThighL, -10).S(B.ShinL, 30).S(B.ThighR, 5).S(B.ShinR, 20);
        public static readonly Pose Dodge = new Pose().H(0, -0.3f, 0).S(B.Spine, 35).S(B.Head, -20).S(B.UArmL, -60, 0, 10).S(B.LArmL, -120).S(B.UArmR, -60, 0, -10).S(B.LArmR, -120).S(B.ThighL, -65).S(B.ShinL, 100).S(B.ThighR, -30).S(B.ShinR, 90);
        public static readonly Pose Charge = new Pose().H(0, -0.18f, 0).S(B.Spine, 18).S(B.Head, 12).S(B.UArmL, -30, 0, -45).S(B.LArmL, -70).S(B.HandL, -60).S(B.UArmR, -30, 0, 45).S(B.LArmR, -70).S(B.HandR, -60)
            .S(B.ThighL, -45, 0, -20).S(B.ShinL, 70).S(B.ThighR, -45, 0, 20).S(B.ShinR, 70);
        // grounded roar: wide low stance, chest thrown up, elbows bent and claws pulled down by the hips (no arms-out T shape)
        public static readonly Pose Roar = new Pose().H(0, -0.12f, -0.02f).S(B.Spine, -14).S(B.Chest, -12).S(B.Neck, -8).S(B.Head, -22)
            .S(B.UArmL, -22, 0, -32).S(B.LArmL, -75, 10).S(B.HandL, -35).S(B.UArmR, -22, 0, 32).S(B.LArmR, -75, -10).S(B.HandR, -35)
            .S(B.ThighL, -28, 0, -16).S(B.ShinL, 42).S(B.FootL, -10).S(B.ThighR, -10, 0, 16).S(B.ShinR, 30).S(B.FootR, -10);

        // ----- zone-specific hit reactions -----
        public static readonly Pose HurtHead = new Pose().H(0, -0.03f, -0.06f).S(B.Spine, -10).S(B.Chest, -8).S(B.Neck, -15, 12).S(B.Head, -30, 18, 10)
            .S(B.UArmL, -15, 0, -40).S(B.LArmL, -30).S(B.UArmR, -15, 0, 40).S(B.LArmR, -30).S(B.ThighL, -10).S(B.ShinL, 20).S(B.ThighR, 15).S(B.ShinR, 25);
        public static readonly Pose HurtBody = new Pose().H(0, -0.12f, -0.08f).S(B.Spine, 32).S(B.Chest, 18).S(B.Head, 10)
            .S(B.UArmL, -40, 0, 15).S(B.LArmL, -100).S(B.UArmR, -40, 0, -15).S(B.LArmR, -100).S(B.ThighL, -35).S(B.ShinL, 50).S(B.ThighR, -10).S(B.ShinR, 45);
        public static readonly Pose HurtLow = new Pose().H(0, -0.22f, 0).S(B.Hips, 0, 0, 8).S(B.Spine, 10, 0, -10).S(B.Head, -5)
            .S(B.UArmL, -20, 0, -55).S(B.LArmL, -30).S(B.UArmR, -20, 0, 45).S(B.LArmR, -30).S(B.ThighL, -60).S(B.ShinL, 95).S(B.ThighR, 10).S(B.ShinR, 60);
        public static readonly Pose Stagger = new Pose().H(0, -0.05f, -0.12f).S(B.Spine, -22).S(B.Chest, -10).S(B.Head, -25)
            .S(B.UArmL, -60, 0, -70).S(B.LArmL, -20).S(B.UArmR, -60, 0, 70).S(B.LArmR, -20).S(B.ThighL, -35).S(B.ShinL, 20).S(B.ThighR, 25).S(B.ShinR, 30);

        // ----- get-up variants -----
        public static readonly Pose KipTuck = new Pose().H(0, -0.65f, -0.1f).S(B.Hips, -120).S(B.Spine, 30).S(B.Head, 25)
            .S(B.UArmL, -150, 0, -20).S(B.LArmL, -90).S(B.UArmR, -150, 0, 20).S(B.LArmR, -90).S(B.ThighL, -130).S(B.ShinL, 120).S(B.ThighR, -130).S(B.ShinR, 120);
        public static readonly Pose RollBack = new Pose().H(0, -0.6f, -0.2f).S(B.Hips, -160).S(B.Spine, 50).S(B.Head, 40)
            .S(B.UArmL, -150, 0, -10).S(B.LArmL, -120).S(B.UArmR, -150, 0, 10).S(B.LArmR, -120).S(B.ThighL, -140).S(B.ShinL, 140).S(B.ThighR, -140).S(B.ShinR, 140);
        public static readonly Pose OneKnee = new Pose().H(0, -0.42f, 0).S(B.Spine, 20).S(B.Head, -10).S(B.UArmL, -30, 0, -20).S(B.LArmL, -40).S(B.UArmR, -70, 0, 10).S(B.LArmR, -80)
            .S(B.ThighL, -90).S(B.ShinL, 90).S(B.ThighR, 20).S(B.ShinR, 110).S(B.FootR, 40);

        // ----- mount (sitting on a downed opponent) -----
        public static readonly Pose MountSit = new Pose().H(0, -0.5f, 0.05f).S(B.Spine, 18).S(B.Head, 20)
            .S(B.UArmL, -40, 0, -20).S(B.LArmL, -110).S(B.UArmR, -40, 0, 20).S(B.LArmR, -110)
            .S(B.ThighL, -60, 0, -35).S(B.ShinL, 140).S(B.FootL, 30).S(B.ThighR, -60, 0, 35).S(B.ShinR, 140).S(B.FootR, 30);
        public static readonly Pose MountWindR = new Pose().H(0, -0.45f, 0.02f).S(B.Spine, 5, 25).S(B.Head, 25).S(B.UArmR, -150, 0, 30).S(B.LArmR, -120).S(B.UArmL, -50, 0, -20).S(B.LArmL, -100)
            .S(B.ThighL, -60, 0, -35).S(B.ShinL, 140).S(B.ThighR, -60, 0, 35).S(B.ShinR, 140);
        public static readonly Pose MountPunchR = new Pose().H(0, -0.55f, 0.1f).S(B.Spine, 38, -20).S(B.Head, 30).S(B.UArmR, -60, 0, 0).S(B.LArmR, -10).S(B.UArmL, -40, 0, -25).S(B.LArmL, -110)
            .S(B.ThighL, -60, 0, -35).S(B.ShinL, 140).S(B.ThighR, -60, 0, 35).S(B.ShinR, 140);
        public static readonly Pose MountWindL = MountWindR.Mirror();
        public static readonly Pose MountPunchL = MountPunchR.Mirror();
        public static readonly Pose MountHammerWind = new Pose().H(0, -0.38f, 0).S(B.Spine, -15).S(B.Head, 15).S(B.UArmL, -170, 0, 10).S(B.LArmL, -70).S(B.UArmR, -170, 0, -10).S(B.LArmR, -70)
            .S(B.ThighL, -60, 0, -35).S(B.ShinL, 140).S(B.ThighR, -60, 0, 35).S(B.ShinR, 140);
        public static readonly Pose MountHammer = new Pose().H(0, -0.58f, 0.12f).S(B.Spine, 45).S(B.Head, 35).S(B.UArmL, -70, 0, 15).S(B.LArmL, -10).S(B.UArmR, -70, 0, -15).S(B.LArmR, -10)
            .S(B.ThighL, -60, 0, -35).S(B.ShinL, 140).S(B.ThighR, -60, 0, 35).S(B.ShinR, 140);

        // ----- landing / jumping -----
        public static readonly Pose LandSquash = new Pose().H(0, -0.2f, 0).S(B.Spine, 18).S(B.Head, -8).S(B.UArmL, -30, 0, -35).S(B.LArmL, -50).S(B.UArmR, -30, 0, 35).S(B.LArmR, -50)
            .S(B.ThighL, -55).S(B.ShinL, 85).S(B.FootL, -25).S(B.ThighR, -45).S(B.ShinR, 80).S(B.FootR, -25);

        // ----- victory poses -----
        public static readonly Pose WinSwift = new Pose().S(B.Head, -10).S(B.UArmR, -150, 0, 10).S(B.LArmR, -70).S(B.HandR, -20).S(B.UArmL, -10, 0, -12).S(B.LArmL, -60).S(B.ThighL, -5, 0, -10).S(B.ThighR, 0, 0, 10);
        public static readonly Pose WinHeavy = new Pose().S(B.Head, -12).S(B.Chest, -6).S(B.UArmL, -60, 0, 40).S(B.LArmL, -100).S(B.UArmR, -60, 0, -40).S(B.LArmR, -100).S(B.ThighL, 0, 0, -12).S(B.ThighR, 0, 0, 12);
        public static readonly Pose WinVoid = new Pose().S(B.Head, -18).S(B.UArmR, -120, 0, 30).S(B.LArmR, -30).S(B.HandR, -80).S(B.UArmL, 10, 0, -8).S(B.LArmL, -40);

        public static Pose ForStance(StanceKind k)
        {
            switch (k)
            {
                case StanceKind.Swift: return StanceSwift;
                case StanceKind.Heavy: return StanceHeavy;
                case StanceKind.Void: return StanceVoid;
                case StanceKind.Serpent: return StanceSerpent;
                case StanceKind.Thunder: return StanceThunder;
                case StanceKind.Moon: return StanceMoon;
                default: return StanceBrawler;
            }
        }
    }

    public enum StanceKind { Brawler, Swift, Heavy, Void, Serpent, Thunder, Moon }
    public enum RunStyle { Normal, Ninja, Heavy, Glide }
}
