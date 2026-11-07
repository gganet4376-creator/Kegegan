using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace Kagegan
{
    /// <summary>
    /// Motion-captured humanoid clips from the Quaternius Universal Animation Library (CC0),
    /// imported as Humanoid so Unity retargets them onto every procedural fighter's runtime avatar.
    /// </summary>
    public static class MocapLibrary
    {
        static Dictionary<string, AnimationClip> clips;
        static bool loaded;

        // contact times (seconds into the clip) measured from the source data: when the striking limb is fully extended
        public const float JabContact = 0.18f, CrossContact = 0.27f, SwordContact = 0.42f;
        // root speeds of the locomotion loops (m/s), measured from the root-motion version of the library
        public const float WalkSpeed = 0.97f, JogSpeed = 5.36f, SprintSpeed = 8.25f;

        public static bool Available
        {
            get { Load(); return clips != null && clips.Count > 0 && Get("Idle_Loop") != null; }
        }

        static void Load()
        {
            if (loaded) return;
            loaded = true;
            clips = new Dictionary<string, AnimationClip>();
            try
            {
                foreach (var c in Resources.LoadAll<AnimationClip>("Kagegan/Anim/UAL1_Standard"))
                {
                    if (c == null || c.name.StartsWith("__preview__")) continue;
                    if (!c.humanMotion) continue; // only humanoid clips can be retargeted
                    clips[c.name] = c;
                }
            }
            catch (System.Exception e) { Debug.LogWarning("Kagegan: mocap library unavailable: " + e.Message); }
            Debug.Log("Kagegan: mocap clips loaded: " + clips.Count);
        }

        public static AnimationClip Get(string name)
        {
            Load();
            if (clips == null || string.IsNullOrEmpty(name)) return null;
            clips.TryGetValue(name, out var c);
            return c;
        }
    }

    /// <summary>
    /// Per-fighter PlayableGraph: a speed-driven locomotion blend (idle / walk / jog / sprint / jump loop) and
    /// two action slots that cross-fade into each other (punches, hit reactions, casting, jumps, death).
    /// Playback rates follow the fighter's real speed so feet do not slide.
    /// </summary>
    public class MocapPlayer : MonoBehaviour
    {
        Animator animator;
        PlayableGraph graph;
        AnimationMixerPlayable root, loco;
        readonly AnimationClipPlayable[] locoClips = new AnimationClipPlayable[5];
        readonly float[] locoW = new float[5];
        readonly float[] locoRef = { 1f, MocapLibrary.WalkSpeed, MocapLibrary.JogSpeed, MocapLibrary.SprintSpeed, 1f };
        AnimationClipPlayable actA, actB;
        bool aIsCurrent = true, hasA, hasB;
        float actWeight, actTarget, actFadeRate = 12f;
        float actEnd, actTime, actSpeed = 1f;
        bool actHold;
        float crossW;
        public bool Ready { get; private set; }
        public float Speed;
        public bool Grounded = true;
        public float TimeScale = 1f;

        public bool ActionPlaying => actTarget > 0.5f;
        public float ActionWeight => actWeight;

        static readonly string[] LocoNames = { "Idle_Loop", "Walk_Loop", "Jog_Fwd_Loop", "Sprint_Loop", "Jump_Loop" };

        public bool Init(Avatar avatar)
        {
            if (avatar == null || !avatar.isValid || !avatar.isHuman) return false;
            animator = gameObject.GetComponent<Animator>();
            if (animator == null) animator = gameObject.AddComponent<Animator>();
            animator.avatar = avatar;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate; // hitboxes read bone positions every frame
            animator.updateMode = AnimatorUpdateMode.Normal;

            graph = PlayableGraph.Create("KageganMocap_" + name);
            graph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);
            var output = AnimationPlayableOutput.Create(graph, "Anim", animator);
            root = AnimationMixerPlayable.Create(graph, 3);
            loco = AnimationMixerPlayable.Create(graph, LocoNames.Length);
            for (int i = 0; i < LocoNames.Length; i++)
            {
                var clip = MocapLibrary.Get(LocoNames[i]) ?? MocapLibrary.Get("Idle_Loop");
                if (clip == null) { graph.Destroy(); return false; }
                locoClips[i] = AnimationClipPlayable.Create(graph, clip);
                locoClips[i].SetApplyFootIK(true);
                locoClips[i].SetTime(Random.value * clip.length); // de-sync idles between fighters
                graph.Connect(locoClips[i], 0, loco, i);
            }
            loco.SetInputWeight(0, 1f);
            graph.Connect(loco, 0, root, 0);
            root.SetInputWeight(0, 1f);
            output.SetSourcePlayable(root);
            graph.Play();
            Ready = true;
            return true;
        }

        /// <summary>Plays a one-shot clip. speed scales playback; start is the clip time to begin at; hold keeps the last frame.</summary>
        public bool PlayAction(string clipName, float speed, float start = 0f, float fade = 0.08f, bool hold = false, float maxDuration = -1f)
        {
            if (!Ready) return false;
            var clip = MocapLibrary.Get(clipName);
            if (clip == null) return false;
            // swap slots so the old action can fade out under the new one
            aIsCurrent = !aIsCurrent;
            int slot = aIsCurrent ? 1 : 2;
            var old = aIsCurrent ? actA : actB;
            if (aIsCurrent ? hasA : hasB) { graph.Disconnect(root, slot); old.Destroy(); }
            var p = AnimationClipPlayable.Create(graph, clip);
            p.SetApplyFootIK(true);
            p.SetTime(start);
            p.SetSpeed(speed * TimeScale);
            graph.Connect(p, 0, root, slot);
            if (aIsCurrent) { actA = p; hasA = true; } else { actB = p; hasB = true; }
            actSpeed = speed;
            actTime = start;
            float len = clip.length;
            actEnd = maxDuration > 0f ? start + maxDuration * speed : len;
            actHold = hold;
            actTarget = 1f;
            actFadeRate = 1f / Mathf.Max(0.02f, fade);
            crossW = actWeight > 0.05f ? 0f : 1f; // cross-fade from the previous action if one was showing
            return true;
        }

        public void StopAction(float fade = 0.12f)
        {
            actTarget = 0f;
            actFadeRate = 1f / Mathf.Max(0.02f, fade);
        }

        void Update()
        {
            if (!Ready) return;
            float dt = Time.deltaTime;

            // ---- locomotion weights from speed (piecewise linear 1D blend) ----
            float s = Speed;
            float[] w = locoW;
            for (int i = 0; i < w.Length; i++) w[i] = 0f;
            if (!Grounded) w[4] = 1f;
            else if (s < 1.2f) { float u = s / 1.2f; w[0] = 1f - u; w[1] = u; }
            else if (s < 4.5f) { float u = (s - 1.2f) / 3.3f; w[1] = 1f - u; w[2] = u; }
            else if (s < 7.5f) { float u = (s - 4.5f) / 3f; w[2] = 1f - u; w[3] = u; }
            else w[3] = 1f;
            for (int i = 0; i < w.Length; i++)
            {
                float cur = loco.GetInputWeight(i);
                loco.SetInputWeight(i, Mathf.MoveTowards(cur, w[i], dt * 8f));
                float rate = (i >= 1 && i <= 3) ? Mathf.Clamp(Mathf.Max(s, 0.3f) / locoRef[i], 0.6f, 1.45f) : 1f;
                locoClips[i].SetSpeed(rate * TimeScale);
                // manual wrap so loops keep cycling even if the import loop flag is missing
                var c = locoClips[i].GetAnimationClip();
                double t = locoClips[i].GetTime();
                if (c != null && c.length > 0f && t > c.length) locoClips[i].SetTime(t % c.length);
            }

            // ---- action slot ----
            if (actTarget > 0f)
            {
                actTime += dt * actSpeed * TimeScale;
                if (actTime >= actEnd)
                {
                    if (actHold) { var cur = aIsCurrent ? actA : actB; cur.SetSpeed(0f); }
                    else actTarget = 0f;
                }
            }
            actWeight = Mathf.MoveTowards(actWeight, actTarget, dt * actFadeRate);
            crossW = Mathf.MoveTowards(crossW, 1f, dt * actFadeRate);
            float newW = actWeight * crossW, oldW = actWeight * (1f - crossW);
            root.SetInputWeight(0, 1f - actWeight);
            root.SetInputWeight(aIsCurrent ? 1 : 2, newW);
            root.SetInputWeight(aIsCurrent ? 2 : 1, oldW);
        }

        void OnDestroy()
        {
            if (graph.IsValid()) graph.Destroy();
        }
    }
}
