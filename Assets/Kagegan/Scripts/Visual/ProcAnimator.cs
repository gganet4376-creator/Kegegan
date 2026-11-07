using UnityEngine;

namespace Kagegan
{
    public enum AnimMode { Locomotion, Air, Block, Hurt, HurtBack, Dizzy, Down, Grabbed, Dodge, Juggled, Victory, Custom, Dead, Crouch }

    /// <summary>
    /// Code-driven animation for the procedural rig. Layers:
    ///   base (stance / run cycle / air / reactions) -> action clip (attacks, techniques) -> additive
    ///   (breathing, idle variants, flinch, turn lean, head look-at) -> eyes (saccades, blinking).
    /// Every bone is smoothed toward its target with an exponential filter, so state changes never pop.
    /// Off-screen characters update at a reduced rate.
    /// </summary>
    public class ProcAnimator : MonoBehaviour
    {
        public HumanoidRig Rig;
        public StanceKind Stance;
        public RunStyle RunStyle;
        public Pose VictoryPose = PoseLib.WinSwift;
        public Pose CustomPose;
        public Pose HurtPose;          // zone-specific hit-stun pose chosen by the Fighter
        public AnimMode Mode;
        public float MoveSpeed;      // m/s horizontal
        public float VerticalSpeed;
        public bool InCombat;
        public float Tired;          // 0..1, deeper breathing
        public float Squint;         // 0..1
        public bool PlaySteps;
        public float TimeScale = 1f;

        Vector3 lookTarget;
        bool hasLook;
        Clip action;
        float actionTime, actionSpeed = 1f;
        float runPhase, lastStepHalf;
        float flinch, flinchDir = 1f;
        int flinchZone;               // 0 body, 1 head, 2 legs
        float flinchSide = 1f;
        float recoil;
        float idleT, idleVariantTimer = 5f;
        int idleVariant;
        float blinkTimer = 2f, blinkT = -1f;
        Vector2 eyeTarget, eyeCur;
        float saccadeTimer;
        float prevYaw, yawRate;
        int frameSkip;
        float accumulatedDt;

        readonly Quaternion[] baseQ = new Quaternion[(int)B.Count];
        readonly Quaternion[] actQ = new Quaternion[(int)B.Count];
        readonly Quaternion[] outQ = new Quaternion[(int)B.Count];
        readonly Pose runPose = new Pose();
        readonly Vector3[] add = new Vector3[(int)B.Count];
        Vector3 hipsBase, hipsAct;

        public bool ActionPlaying => action != null && actionTime < action.Length;
        public float ActionTime => actionTime;

        public void Init(HumanoidRig rig, StanceKind stance, RunStyle run)
        {
            Rig = rig; Stance = stance; RunStyle = run;
            prevYaw = transform.eulerAngles.y;
            blinkTimer = Random.Range(1f, 4f);
            idleT = Random.value * 10f;
        }

        public void Play(Clip c, float speed = 1f)
        {
            action = c; actionTime = 0f; actionSpeed = speed;
        }

        public void StopAction() { action = null; }

        public void Flinch(float strength, bool fromBehind) => Flinch(strength, fromBehind, 0, 1f);

        /// <summary>Additive hit reaction; zone 0 = body (folds forward), 1 = head (snaps back and twists), 2 = legs (knee buckles).</summary>
        public void Flinch(float strength, bool fromBehind, int zone, float side)
        {
            flinch = Mathf.Max(flinch, strength);
            flinchDir = fromBehind ? -1f : 1f;
            flinchZone = zone;
            flinchSide = side >= 0f ? 1f : -1f;
            Squint = 1f;
        }

        /// <summary>Attacker-side recoil when a blow connects: the body absorbs the impact, sells the weight of the hit.</summary>
        public void Recoil(float strength) { recoil = Mathf.Max(recoil, strength); }

        public void LookAt(Vector3 worldPos) { lookTarget = worldPos; hasLook = true; }
        public void ClearLook() { hasLook = false; }

        void LateUpdate()
        {
            if (Rig == null) return;
            float dt = Time.deltaTime * TimeScale;
            accumulatedDt += dt;
            // off-screen characters animate at 1/3 rate
            if (Rig.Smr != null && !Rig.Smr.isVisible)
            {
                frameSkip = (frameSkip + 1) % 3;
                if (frameSkip != 0) return;
            }
            dt = accumulatedDt;
            accumulatedDt = 0f;
            if (dt <= 0f) return;

            float yaw = transform.eulerAngles.y;
            yawRate = Mathf.Lerp(yawRate, Mathf.DeltaAngle(prevYaw, yaw) / dt, 1f - Mathf.Exp(-10f * dt));
            prevYaw = yaw;

            BuildBase(dt);

            // ----- action layer -----
            float actWeight = 0f;
            if (action != null)
            {
                actionTime += dt * actionSpeed;
                if (actionTime >= action.Length) action = null;
                else
                {
                    SampleClip(action, actionTime);
                    actWeight = 1f;
                }
            }

            for (int i = 0; i < outQ.Length; i++)
                outQ[i] = actWeight > 0f ? actQ[i] : baseQ[i];
            Vector3 hips = actWeight > 0f ? hipsAct : hipsBase;

            // ----- additive -----
            System.Array.Clear(add, 0, add.Length);
            idleT += dt;
            float breath = Mathf.Sin(idleT * 2f * Mathf.PI * 0.28f);
            add[(int)B.Chest].x += breath * (1.5f + 4f * Tired);
            add[(int)B.UArmL].z -= breath * 1.2f;
            add[(int)B.UArmR].z += breath * 1.2f;
            add[(int)B.Head].x -= breath * 0.8f;
            hips.y += breath * 0.004f;

            if (Mode == AnimMode.Locomotion && actWeight == 0f && MoveSpeed < 0.3f)
            {
                IdleVariants(dt, ref hips);
                // slow weight shift between the feet and shoulder sway
                float shift = Mathf.Sin(idleT * 0.55f);
                hips.x += shift * 0.018f;
                add[(int)B.Hips].z += shift * 1.5f;
                add[(int)B.Spine].z -= shift * 2.2f;
                add[(int)B.UArmL].x += Mathf.Sin(idleT * 0.9f + 1f) * 2.5f;
                add[(int)B.UArmR].x += Mathf.Sin(idleT * 0.8f) * 2.5f;
            }

            flinch *= Mathf.Exp(-7f * dt);
            if (flinch > 0.01f)
            {
                switch (flinchZone)
                {
                    case 1: // head: snaps back and turns away from the blow
                        add[(int)B.Neck].x -= 14f * flinch * flinchDir;
                        add[(int)B.Head].x -= 32f * flinch * flinchDir;
                        add[(int)B.Head].y += 22f * flinch * flinchSide;
                        add[(int)B.Chest].x -= 6f * flinch * flinchDir;
                        break;
                    case 2: // legs: knee buckles, body dips
                        add[(int)B.ThighL].x -= 20f * flinch;
                        add[(int)B.ShinL].x += 35f * flinch;
                        add[(int)B.Spine].z += 8f * flinch * flinchSide;
                        hips.y -= 0.08f * flinch;
                        break;
                    default: // body: folds around the impact
                        add[(int)B.Spine].x += 20f * flinch * flinchDir;
                        add[(int)B.Chest].x += 12f * flinch * flinchDir;
                        add[(int)B.Head].x += 10f * flinch * flinchDir;
                        hips.z -= 0.05f * flinch * flinchDir;
                        break;
                }
            }
            recoil *= Mathf.Exp(-12f * dt);
            if (recoil > 0.01f)
            {
                add[(int)B.Chest].x -= 8f * recoil;
                add[(int)B.Spine].x -= 4f * recoil;
                hips.z -= 0.04f * recoil;
            }
            if (Mode == AnimMode.Locomotion && MoveSpeed > 1f)
                add[(int)B.Spine].z += Mathf.Clamp(-yawRate * 0.04f, -14f, 14f);

            HeadLook();

            for (int i = 0; i < add.Length; i++)
                if (add[i] != Vector3.zero) outQ[i] = outQ[i] * Quaternion.Euler(add[i]);

            // ----- apply with smoothing -----
            // actions track their keys tightly so the limb is really extended on the active (damage) frames
            float rate = actWeight > 0f ? 44f : (Mode == AnimMode.Down || Mode == AnimMode.Dead ? 11f : 15f);
            float k = 1f - Mathf.Exp(-rate * dt);
            var bones = Rig.Bones;
            for (int i = 0; i < (int)B.EyeL; i++)
                bones[i].localRotation = Quaternion.Slerp(bones[i].localRotation, Rig.RestRot[i] * outQ[i], k);
            Vector3 hipsTarget = Rig.HipsRestPos + hips * Rig.Look.height;
            bones[(int)B.Hips].localPosition = Vector3.Lerp(bones[(int)B.Hips].localPosition, hipsTarget, k);

            Eyes(dt);
        }

        void BuildBase(float dt)
        {
            Pose stance = PoseLib.ForStance(Stance);
            switch (Mode)
            {
                case AnimMode.Locomotion:
                {
                    float run = Mathf.Clamp01(MoveSpeed / 4f);
                    if (run > 0.01f) BuildRun(dt);
                    var sq = stance.Q; var rq = runPose.Q;
                    for (int i = 0; i < baseQ.Length; i++) baseQ[i] = Quaternion.Slerp(sq[i], rq[i], run);
                    hipsBase = Vector3.Lerp(stance.HipsOffset, runPose.HipsOffset, run);
                    if (run < 0.01f) runPhase = 0f;
                    break;
                }
                case AnimMode.Air:
                {
                    float u = Mathf.Clamp01(0.5f - VerticalSpeed * 0.12f);
                    var a = PoseLib.AirUp.Q; var f = PoseLib.AirFall.Q;
                    for (int i = 0; i < baseQ.Length; i++) baseQ[i] = Quaternion.Slerp(a[i], f[i], u);
                    hipsBase = Vector3.zero;
                    break;
                }
                default:
                    SetBase(PoseFor(Mode, stance));
                    break;
            }
        }

        Pose PoseFor(AnimMode m, Pose stance)
        {
            switch (m)
            {
                case AnimMode.Block: return PoseLib.Block;
                case AnimMode.Hurt: return HurtPose ?? PoseLib.Hurt;
                case AnimMode.HurtBack: return PoseLib.HurtBack;
                case AnimMode.Dizzy: return PoseLib.Dizzy;
                case AnimMode.Down: case AnimMode.Dead: return PoseLib.Down;
                case AnimMode.Grabbed: return PoseLib.Grabbed;
                case AnimMode.Dodge: return PoseLib.Dodge;
                case AnimMode.Juggled: return PoseLib.Juggled;
                case AnimMode.Victory: return VictoryPose ?? stance;
                case AnimMode.Crouch: return PoseLib.Crouch;
                case AnimMode.Custom: return CustomPose ?? stance;
                default: return stance;
            }
        }

        void SetBase(Pose p)
        {
            var q = p.Q;
            for (int i = 0; i < baseQ.Length; i++) baseQ[i] = q[i];
            hipsBase = p.HipsOffset;
            if (Mode == AnimMode.Dizzy)
            {
                float w = Mathf.Sin(idleT * 3f);
                baseQ[(int)B.Head] = baseQ[(int)B.Head] * Quaternion.Euler(0, w * 15f, w * 10f);
                baseQ[(int)B.Spine] = baseQ[(int)B.Spine] * Quaternion.Euler(0, 0, w * 6f);
            }
        }

        void BuildRun(float dt)
        {
            // stride length is derived from leg length and swing angle, so feet travel exactly as far as the body (no sliding)
            float maxAmp = RunStyle == RunStyle.Glide ? 14f : RunStyle == RunStyle.Heavy ? 38f : 46f;
            float speedK = Mathf.Clamp(MoveSpeed / 6f, 0.3f, 1.1f);
            float legAmp = maxAmp * speedK;
            float legLen = 0.84f * Rig.Look.height * Rig.transform.localScale.y;
            float stride = RunStyle == RunStyle.Glide ? 2.2f : Mathf.Max(0.35f, 2f * legLen * Mathf.Sin(legAmp * Mathf.Deg2Rad) * 1.1f);
            runPhase += dt * MoveSpeed / stride * Mathf.PI;
            float s = Mathf.Sin(runPhase), c = Mathf.Cos(runPhase);
            var p = runPose;
            float kneeAmp = (RunStyle == RunStyle.Glide ? 20f : 75f) * Mathf.Lerp(0.6f, 1f, speedK);
            p.S(B.ThighL, -legAmp * s - 12f, 0, -3).S(B.ThighR, legAmp * s - 12f, 0, 3);
            p.S(B.ShinL, 18f + kneeAmp * Mathf.Max(0f, c)).S(B.ShinR, 18f + kneeAmp * Mathf.Max(0f, -c));
            p.S(B.FootL, -10f * s).S(B.FootR, 10f * s);
            p.S(B.Hips, 0, 9f * s, 0).S(B.Neck, 0, 0, 0);
            switch (RunStyle)
            {
                case RunStyle.Ninja:
                    p.S(B.Spine, 32, 0, 0).S(B.Chest, 8, -8f * s).S(B.Head, -28)
                     .S(B.UArmL, 65, 0, -14).S(B.LArmL, -15).S(B.UArmR, 65, 0, 14).S(B.LArmR, -15).S(B.HandL, 20).S(B.HandR, 20);
                    p.H(0, -0.12f + 0.04f * Mathf.Abs(s), 0);
                    break;
                case RunStyle.Heavy:
                    p.S(B.Spine, 10).S(B.Chest, 2, -10f * s).S(B.Head, -8)
                     .S(B.UArmL, 35f * s, 0, -20).S(B.LArmL, -60).S(B.UArmR, -35f * s, 0, 20).S(B.LArmR, -60);
                    p.H(0, -0.07f + 0.045f * Mathf.Abs(s), 0);
                    break;
                case RunStyle.Glide:
                    p.S(B.Spine, 14).S(B.Chest, 0).S(B.Head, -12)
                     .S(B.UArmL, 25, 0, -18).S(B.LArmL, -20).S(B.UArmR, 25, 0, 18).S(B.LArmR, -20);
                    p.H(0, 0.04f + 0.02f * s, 0);
                    break;
                default:
                    p.S(B.Spine, 16).S(B.Chest, 4, -12f * s).S(B.Head, -14)
                     .S(B.UArmL, 45f * s, 0, -10).S(B.LArmL, -75).S(B.UArmR, -45f * s, 0, 10).S(B.LArmR, -75);
                    p.H(0, -0.08f + 0.05f * Mathf.Abs(s), 0);
                    break;
            }
            p.Invalidate();

            if (PlaySteps)
            {
                float half = Mathf.Floor(runPhase / Mathf.PI);
                if (half != lastStepHalf) { lastStepHalf = half; Sfx.Play(SfxId.Step, 0.25f); }
            }
        }

        void IdleVariants(float dt, ref Vector3 hips)
        {
            idleVariantTimer -= dt;
            if (idleVariantTimer <= 0f)
            {
                idleVariant = InCombat ? (Random.value < 0.5f ? 0 : 3) : Random.Range(0, 4);
                idleVariantTimer = Random.Range(3.5f, 7f);
            }
            float t = idleVariantTimer;
            switch (idleVariant)
            {
                case 1: // look around
                    if (!InCombat) add[(int)B.Neck].y += Mathf.Sin(t * 0.9f) * 28f;
                    break;
                case 2: // shoulder roll / stretch
                    add[(int)B.UArmR].x += Mathf.Max(0f, Mathf.Sin(t * 1.6f)) * -20f;
                    add[(int)B.Neck].z += Mathf.Sin(t * 1.6f) * 8f;
                    break;
                case 3: // light bounce on the feet
                    hips.y += Mathf.Abs(Mathf.Sin(t * 5f)) * 0.025f;
                    add[(int)B.ShinL].x += Mathf.Abs(Mathf.Sin(t * 5f)) * -6f;
                    add[(int)B.ShinR].x += Mathf.Abs(Mathf.Sin(t * 5f)) * -6f;
                    break;
            }
        }

        void HeadLook()
        {
            if (!hasLook || ActionPlaying) return;
            if (Mode != AnimMode.Locomotion && Mode != AnimMode.Block && Mode != AnimMode.Air) return;
            Vector3 local = transform.InverseTransformPoint(lookTarget);
            float targetYaw = Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg;
            Pose stance = PoseLib.ForStance(Stance);
            float stanceYaw = stance.R[(int)B.Hips].y + stance.R[(int)B.Spine].y + stance.R[(int)B.Chest].y + stance.R[(int)B.Neck].y;
            float headYaw = Mathf.Clamp(targetYaw - stanceYaw * (MoveSpeed < 0.5f ? 1f : 0f), -55f, 55f);
            add[(int)B.Head].y += headYaw * 0.7f;
            add[(int)B.Neck].y += headYaw * 0.3f;
            eyeTarget.y = Mathf.Clamp(headYaw * 0.3f, -18f, 18f);
        }

        void Eyes(float dt)
        {
            // saccades
            saccadeTimer -= dt;
            if (saccadeTimer <= 0f)
            {
                saccadeTimer = Random.Range(0.5f, 2.4f);
                if (!hasLook) eyeTarget = new Vector2(Random.Range(-6f, 6f), Random.Range(-16f, 16f));
                else eyeTarget.x = Random.Range(-4f, 4f);
            }
            eyeCur = Vector2.Lerp(eyeCur, eyeTarget, 1f - Mathf.Exp(-35f * dt));
            var eq = Quaternion.Euler(eyeCur.x, eyeCur.y, 0);
            Rig.Bones[(int)B.EyeL].localRotation = Rig.RestRot[(int)B.EyeL] * eq;
            Rig.Bones[(int)B.EyeR].localRotation = Rig.RestRot[(int)B.EyeR] * eq;

            // blinking
            float lids = 0f;
            if (Mode == AnimMode.Dead) lids = 1f;
            else
            {
                blinkTimer -= dt;
                if (blinkTimer <= 0f && blinkT < 0f) { blinkT = 0f; blinkTimer = Random.Range(2f, 5.5f); }
                if (blinkT >= 0f)
                {
                    blinkT += dt / 0.14f;
                    lids = 1f - Mathf.Abs(blinkT * 2f - 1f);
                    if (blinkT >= 1f) blinkT = -1f;
                }
                Squint = Mathf.MoveTowards(Squint, 0f, dt * 2.5f);
                lids = Mathf.Max(lids, Squint * 0.65f);
            }
            Rig.SetLids(lids);
        }

        void SampleClip(Clip c, float t)
        {
            int k = 0;
            while (k < c.Times.Length - 2 && t >= c.Times[k + 1]) k++;
            float t0 = c.Times[k], t1 = c.Times[k + 1];
            float u = t1 > t0 ? Mathf.Clamp01((t - t0) / (t1 - t0)) : 1f;
            Pose a = c.Poses[k], b = c.Poses[k + 1];
            if (b == null) { float v = 1f - u; u = 1f - v * v * v; }            // settle back into stance: ease-out
            else if (a != null && a != b) u = u * u * (2.2f - 1.2f * u);       // strike: accelerates into contact
            else u = u * u * (3f - 2f * u);                                    // anticipation / holds: smooth
            Quaternion[] qa = a != null ? a.Q : null, qb = b != null ? b.Q : null;
            for (int i = 0; i < actQ.Length; i++)
                actQ[i] = Quaternion.Slerp(qa != null ? qa[i] : baseQ[i], qb != null ? qb[i] : baseQ[i], u);
            hipsAct = Vector3.Lerp(a != null ? a.HipsOffset : hipsBase, b != null ? b.HipsOffset : hipsBase, u);
        }
    }
}
