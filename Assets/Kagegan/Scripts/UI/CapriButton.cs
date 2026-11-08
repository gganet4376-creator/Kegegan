using UnityEngine;
using UnityEngine.UI;

namespace Kagegan
{
    /// <summary>
    /// Lucy-only summon button: Capri's portrait in a rotating red/green data ring. Pulses when she can be called,
    /// glitches (jitter + colour split) when pressed, shows her remaining seconds while she is out and the cooldown after.
    /// Everything animates a child "Art" rect so TouchButton's own press scaling on the root stays intact.
    /// </summary>
    public class CapriButton : MonoBehaviour
    {
        TouchButton btn;
        RectTransform art, ring, ghostR, ghostG;
        Image face, ringImg, ghostRImg, ghostGImg;
        Sprite faceIdle, faceLaugh;
        float glitchUntil, lastFill = -1f;
        bool wasReady;

        public static CapriButton Attach(TouchButton b)
        {
            var c = b.gameObject.AddComponent<CapriButton>();
            c.Build(b);
            return c;
        }

        void Build(TouchButton b)
        {
            btn = b;
            var rt = (RectTransform)b.transform;
            float size = rt.sizeDelta.x;
            faceIdle = UIKit.Icon("capri_face");
            faceLaugh = UIKit.Icon("capri_face_laugh");
            if (b.Glyph != null) b.Glyph.enabled = false; // the portrait replaces the generic glyph

            art = UIKit.Box(rt, "Art", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(size, size));
            art.SetSiblingIndex(2); // above the disc and ring, below the cooldown shade and caption
            ghostR = UIKit.Box(art, "GhostR", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(size * 0.8f, size * 0.8f));
            ghostRImg = UIKit.Img(ghostR, new Color(1f, 0.16f, 0.33f, 0f), faceIdle);
            ghostG = UIKit.Box(art, "GhostG", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(size * 0.8f, size * 0.8f));
            ghostGImg = UIKit.Img(ghostG, new Color(0.25f, 1f, 0.42f, 0f), faceIdle);
            var frt = UIKit.Box(art, "Face", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(size * 0.8f, size * 0.8f));
            face = UIKit.Img(frt, Color.white, faceIdle);
            ring = UIKit.Box(art, "DataRing", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(size * 1.08f, size * 1.08f));
            ringImg = UIKit.Img(ring, Color.white, UIKit.Icon("capri_ring"));
            foreach (var i in new[] { ghostRImg, ghostGImg, face, ringImg }) { i.raycastTarget = false; i.preserveAspect = true; }
            if (b.Label != null) b.Label.transform.parent.SetAsLastSibling();
            if (b.CooldownFill != null) { b.CooldownFill.transform.SetSiblingIndex(art.GetSiblingIndex() + 1); b.CooldownFill.color = new Color(0f, 0f, 0f, 0.55f); }
            b.Down += () => glitchUntil = Time.unscaledTime + 0.35f;
        }

        /// <summary>Called every frame by PlayerController with ReinaKit.CapriState.</summary>
        public void Refresh(float fill, bool ready, float timeLeft, bool present)
        {
            float t = Time.unscaledTime;
            bool active = present || timeLeft > 0f;
            if (btn.CooldownFill != null)
            {
                // while she is out the shade shows how much of her stay is used up; otherwise the cooldown
                float f = active ? 1f - Mathf.Clamp01(fill) : 1f - Mathf.Clamp01(fill);
                if (!active && ready) f = 0f;
                if (!Mathf.Approximately(f, lastFill)) { btn.CooldownFill.fillAmount = f; lastFill = f; }
            }
            if (btn.Label != null)
            {
                string s = active ? Mathf.CeilToInt(timeLeft) + "с"
                    : ready ? "КАПРИ"
                    : fill < 1f ? Mathf.CeilToInt((1f - fill) * ReinaKit.CapriCooldown) + "с" : "КАПРИ";
                if (btn.Label.text != s) btn.Label.text = s;
            }
            face.sprite = active && Mathf.Repeat(t, 2.4f) < 1.2f ? faceLaugh : faceIdle;
            ghostRImg.sprite = ghostGImg.sprite = face.sprite;
            face.color = ready || active ? Color.white : new Color(0.55f, 0.55f, 0.6f, 1f);

            // the data ring spins faster while she is out, slow and dim while waiting
            ring.localRotation = Quaternion.Euler(0, 0, -t * (active ? 120f : ready ? 45f : 12f));
            ringImg.color = new Color(1f, 1f, 1f, active ? 1f : ready ? 0.75f + 0.25f * Mathf.Sin(t * 6f) : 0.35f);

            // ready: a breathing pulse; on the frame she becomes ready, a short glitch announces it
            if (ready && !wasReady) glitchUntil = t + 0.3f;
            wasReady = ready;
            float pulse = ready ? 1f + 0.06f * Mathf.Sin(t * 5f) : 1f;

            bool glitching = t < glitchUntil || (active && Random.value < 0.015f);
            if (glitching)
            {
                float j = 7f;
                art.anchoredPosition = new Vector2(Random.Range(-j, j), Random.Range(-j * 0.4f, j * 0.4f));
                ghostR.anchoredPosition = new Vector2(Random.Range(4f, 10f), 0f);
                ghostG.anchoredPosition = new Vector2(-Random.Range(4f, 10f), 0f);
                ghostRImg.color = new Color(1f, 0.16f, 0.33f, 0.75f);
                ghostGImg.color = new Color(0.25f, 1f, 0.42f, 0.75f);
                art.localScale = Vector3.one * (pulse * Random.Range(1.02f, 1.12f));
                if (btn.Ring != null) btn.Ring.color = Random.value < 0.5f ? new Color(1f, 0.16f, 0.33f) : new Color(0.25f, 1f, 0.42f);
            }
            else
            {
                art.anchoredPosition = Vector2.zero;
                ghostRImg.color = new Color(1f, 0.16f, 0.33f, 0f);
                ghostGImg.color = new Color(0.25f, 1f, 0.42f, 0f);
                art.localScale = Vector3.one * pulse;
                if (btn.Ring != null) btn.Ring.color = Color.Lerp(new Color(1f, 0.16f, 0.33f), new Color(0.25f, 1f, 0.42f), active ? 0.5f + 0.5f * Mathf.Sin(t * 4f) : 0f);
            }
        }
    }
}
