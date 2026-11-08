using UnityEngine;

namespace Kagegan
{
    /// <summary>
    /// Physical whip held in a fighter's hand: a short handle plus a tapered lash simulated as a verlet chain
    /// (follow-the-leader constraints, so the hand's own motion travels down the lash like a real whip).
    /// Idle, it hangs and drags on the floor. A strike rolls a loop from the handle to the tip (the base turns first,
    /// the tip last), the tip snaps at the target, then the chain is released and follows through under gravity.
    /// </summary>
    [DefaultExecutionOrder(1000)] // after the animator and procedural pose, so the handle sits in the final hand
    public class Whip : MonoBehaviour
    {
        const int N = 20;
        const float RestLen = 2.2f, HandleLen = 0.24f, MaxLen = 5.2f;

        Fighter f;
        Transform hand, forearm;
        Color glowColor;
        LineRenderer lash, glow, handle;
        readonly Vector3[] p = new Vector3[N], prev = new Vector3[N], aim = new Vector3[N];
        float seg = RestLen / (N - 1);
        bool ready;

        // strike
        float strikeT = -1f, strikeDur, sweep, strikeLen;
        Vector3 strikeTarget, startDir, endDir;
        bool cracked;
        float glowAmt;

        // tether (tip pinned to a point while held)
        float holdUntil = -1f;
        Vector3 holdPos;

        float floorY, floorCheck;

        public static Whip Attach(Fighter fighter, Color glow)
        {
            var go = new GameObject("Whip");
            go.transform.SetParent(fighter.transform, false);
            var w = go.AddComponent<Whip>();
            w.f = fighter; w.glowColor = glow;
            w.hand = fighter.Rig.Bones[(int)B.HandR];
            w.forearm = fighter.Rig.Bones[(int)B.LArmR];
            w.Build();
            return w;
        }

        LineRenderer Line(string name, Material m, int count, AnimationCurve width, Color c)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.sharedMaterial = m;
            lr.positionCount = count;
            lr.useWorldSpace = true;
            lr.alignment = LineAlignment.View;
            lr.numCapVertices = 2;
            lr.numCornerVertices = 1;
            lr.widthCurve = width;
            lr.startColor = c; lr.endColor = c;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
            return lr;
        }

        void Build()
        {
            // braided leather: dark at the handle, slightly lighter towards the fall and cracker
            lash = Line("Lash", Mat.FxAlphaSolid, N, new AnimationCurve(new Keyframe(0, 0.034f), new Keyframe(0.6f, 0.02f), new Keyframe(1, 0.007f)), Color.white);
            lash.startColor = new Color(0.09f, 0.07f, 0.06f, 1f);
            lash.endColor = new Color(0.2f, 0.17f, 0.14f, 1f);
            handle = Line("Handle", Mat.FxAlphaSolid, 4, new AnimationCurve(new Keyframe(0, 0.05f), new Keyframe(0.15f, 0.06f), new Keyframe(0.2f, 0.045f), new Keyframe(1, 0.04f)), new Color(0.06f, 0.06f, 0.07f, 1f));
            handle.startColor = new Color(0.25f, 0.9f, 0.45f, 1f); // lit pommel ring
            glow = Line("LashGlow", Mat.FxAddSolid, N, new AnimationCurve(new Keyframe(0, 0.09f), new Keyframe(1, 0.04f)), Color.clear);
            glow.enabled = false;
        }

        // ---------------- API ----------------
        /// <summary>Lash at <paramref name="target"/>: contact at ~40% of <paramref name="duration"/>; sweep bends the swing (+ = from the right, 0 = overhead).</summary>
        public void Strike(Vector3 target, float duration, float sweepSide)
        {
            if (!ready) return;
            Vector3 b = p[0];
            Vector3 d = target - b;
            float dist = d.magnitude;
            if (dist < 0.3f) return;
            strikeTarget = target;
            strikeDur = Mathf.Clamp(duration, 0.16f, 0.6f);
            strikeT = 0f; cracked = false;
            sweep = Mathf.Clamp(sweepSide, -1.5f, 1.5f);
            strikeLen = Mathf.Clamp(dist, RestLen, MaxLen);
            endDir = d / dist;
            Vector3 side = Vector3.Cross(Vector3.up, endDir); if (side.sqrMagnitude < 0.01f) side = f.transform.right; side.Normalize();
            float s = Mathf.Clamp(sweep, -1f, 1f);
            // wound up behind and above the shoulder (overhead) or out to the side (horizontal swing)
            Vector3 wind = (-endDir * 0.55f + Vector3.up * (1f - Mathf.Abs(s) * 0.6f) + side * s * 0.9f).normalized;
            // start from where the lash actually is (no pop), leaning into the wind-up side
            Vector3 cur = p[N / 2] - p[0];
            startDir = cur.sqrMagnitude > 0.01f ? (cur.normalized * 0.6f + wind * 0.4f).normalized : wind;
            if (Vector3.Dot(startDir, endDir) > 0.7f) startDir = wind; // already pointing at the target: still wind up
            glowAmt = 1f;
        }

        /// <summary>Keeps the tip on <paramref name="point"/> this frame (taut tether, e.g. while pulling).</summary>
        public void Hold(Vector3 point)
        {
            holdPos = point; holdUntil = Time.time + 0.08f;
            glowAmt = Mathf.Max(glowAmt, 0.6f);
        }

        public Vector3 Tip => p[N - 1];

        // ---------------- simulation ----------------
        Vector3 HandleDir()
        {
            Vector3 d = hand.position - forearm.position;
            return d.sqrMagnitude > 1e-4f ? d.normalized : f.transform.forward;
        }

        void ResetChain(Vector3 basePos)
        {
            for (int i = 0; i < N; i++) { p[i] = basePos + Vector3.down * Mathf.Min(i * seg, 0.9f) - f.transform.forward * Mathf.Max(0f, i * seg - 0.9f); prev[i] = p[i]; }
            ready = true;
        }

        void LateUpdate()
        {
            if (f == null || hand == null) return;
            float dt = Time.deltaTime;
            Vector3 hdir = HandleDir();
            Vector3 grip = hand.position - hdir * 0.02f;
            Vector3 basePos = grip + hdir * HandleLen;
            if (!ready || (basePos - p[0]).sqrMagnitude > 16f) ResetChain(basePos);

            if (Time.time > floorCheck)
            {
                floorCheck = Time.time + 0.15f;
                floorY = Physics.Raycast(f.transform.position + Vector3.up * 0.5f, Vector3.down, out var hit, 4f, ~0, QueryTriggerInteraction.Ignore)
                    ? hit.point.y : f.transform.position.y;
            }

            if (dt > 0f)
            {
                int steps = dt > 0.021f ? 2 : 1;
                float h = Mathf.Min(dt, 0.05f) / steps;
                for (int s = 0; s < steps; s++) Step(h, basePos);
            }
            else p[0] = basePos; // hit-stop: freeze the lash, keep it in the hand

            Render(grip, basePos, dt);
        }

        void Step(float h, Vector3 basePos)
        {
            bool holding = Time.time < holdUntil;
            bool striking = strikeT >= 0f;
            float targetSeg = (holding ? Mathf.Clamp((holdPos - basePos).magnitude, RestLen, MaxLen + 5f) : striking ? strikeLen : RestLen) / (N - 1);
            // extends at once for a strike, reels back in smoothly afterwards
            seg = targetSeg > seg ? targetSeg : Mathf.MoveTowards(seg, targetSeg, h * 6f / (N - 1));

            // verlet integration (light air drag; the thin tip loses speed a bit faster)
            float g = -16f * h * h;
            for (int i = 1; i < N; i++)
            {
                float drag = Mathf.Lerp(0.992f, 0.975f, (float)i / (N - 1));
                Vector3 v = (p[i] - prev[i]) * Mathf.Pow(drag, h * 60f);
                prev[i] = p[i];
                p[i] += v + new Vector3(0f, g, 0f);
            }
            prev[0] = p[0]; p[0] = basePos;

            if (striking)
            {
                strikeT += h;
                float u = strikeT / strikeDur;
                if (u < 0.45f)
                {
                    // a loop rolls from the handle to the tip: each point turns from wind-up to the target direction,
                    // the base first, the tip last - the tip catches up and reaches the target at u = 0.4
                    float k = u / 0.4f;
                    aim[0] = p[0];
                    for (int i = 1; i < N; i++)
                    {
                        float t = (float)i / (N - 1);
                        float w = Mathf.Clamp01(k * 1.7f - t * 0.7f);
                        w = w * w * (3f - 2f * w);
                        Vector3 dir = Vector3.Slerp(startDir, endDir, w);
                        aim[i] = aim[i - 1] + dir * seg;
                        // pull hard near the handle, looser at the tip so it lags and overshoots
                        float pull = 1f - Mathf.Exp(-h * Mathf.Lerp(45f, 22f, t));
                        p[i] = Vector3.Lerp(p[i], aim[i], pull);
                    }
                    if (!cracked && u >= 0.4f)
                    {
                        cracked = true;
                        Fx.Electric(p[N - 1], glowColor, 0.45f, 1);
                    }
                }
                else if (u >= 1f) strikeT = -1f; // released: momentum carries the follow-through
            }

            // follow-the-leader length constraints (base is pinned, children follow): inextensible, stable
            for (int it = 0; it < 2; it++)
            {
                if (holding)
                {
                    // two-ended: pinned at the hand and on the target, sags a little in between
                    p[N - 1] = holdPos;
                    for (int i = N - 2; i >= 1; i--) Constrain(i + 1, i);
                }
                for (int i = 1; i < N; i++) Constrain(i - 1, i);
            }

            // floor: lie on it and slide with friction
            float fy = floorY + 0.012f;
            for (int i = 1; i < N; i++)
            {
                if (p[i].y < fy)
                {
                    p[i].y = fy;
                    prev[i].y = Mathf.Max(prev[i].y, fy);
                    prev[i].x = Mathf.Lerp(prev[i].x, p[i].x, 0.35f);
                    prev[i].z = Mathf.Lerp(prev[i].z, p[i].z, 0.35f);
                }
            }
        }

        void Constrain(int a, int b)
        {
            Vector3 d = p[b] - p[a];
            float len = d.magnitude;
            if (len < 1e-5f) return;
            Vector3 np = p[a] + d * (seg / len);
            // carry part of the correction into velocity (FTL damping), keeps the lash lively without jitter
            prev[b] += (np - p[b]) * 0.1f;
            p[b] = np;
        }

        void Render(Vector3 grip, Vector3 basePos, float dt)
        {
            handle.SetPosition(0, grip - (basePos - grip) * 0.08f);
            handle.SetPosition(1, Vector3.Lerp(grip, basePos, 0.12f));
            handle.SetPosition(2, Vector3.Lerp(grip, basePos, 0.2f));
            handle.SetPosition(3, basePos);
            lash.SetPositions(p);

            glowAmt = Mathf.MoveTowards(glowAmt, 0f, dt * 2.6f);
            bool on = glowAmt > 0.01f;
            if (glow.enabled != on) glow.enabled = on;
            if (on)
            {
                glow.SetPositions(p);
                Color c = glowColor * (0.9f * glowAmt); c.a = glowAmt;
                Color c0 = c * 0.35f; c0.a = glowAmt * 0.35f;
                glow.startColor = c0; glow.endColor = c;
            }
        }
    }
}
