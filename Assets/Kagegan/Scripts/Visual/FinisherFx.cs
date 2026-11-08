using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace Kagegan
{
    /// <summary>
    /// Killing-blow cinematic, played when the player's side lands a lethal hit on a boss, the last enemy of a wave,
    /// or (with a cooldown) a heavy blow: close-up camera and slow motion, a two-frame ink impact frame
    /// (paper-white world, black fighter silhouettes), a big slash with splatter, a red brush kanji for the
    /// attacker's element, then a light burst and smoke. Everything is UI or shader globals, so it costs the same on Low.
    /// </summary>
    public class FinisherFx : MonoBehaviour
    {
        // kanji_atlas.png: 4x4 cells of 256 px, row-major from the top left (rendered from Yuji Boku, OFL)
        public enum Glyph { Zan, Rai, Kaze, En, Kage, Metsu, Geki, Owari, Tsuki, Kori, Ha, Oni }

        public static bool Busy { get; private set; }
        static FinisherFx inst;
        static float lastAt = -99f;
        static readonly int InkId = Shader.PropertyToID("_KageInk");

        Canvas canvas;
        RectTransform kanjiRt, barTop, barBottom;
        Image kanji, kanjiShadow, flash;
        Sprite[] glyphs;
        Coroutine running;

        static readonly Color Paper = new Color(0.97f, 0.95f, 0.9f);
        static readonly Color InkRed = new Color(0.82f, 0.06f, 0.07f);
        static readonly Color Splatter = new Color(0.35f, 0.02f, 0.04f);

        /// <summary>Called from Fighter.Die before OnDied. Plays the cinematic when the kill deserves it.</summary>
        public static void OnKill(Fighter victim, Fighter killer, HitInfo h)
        {
            if (victim == null || killer == null || victim.IsPlayer || victim.IsClone || Busy) return;
            Fighter side = killer.IsClone && killer.Owner != null ? killer.Owner : killer;
            if (!side.IsPlayer || !side.Alive) return;
            bool lastOne = Battle.Inst != null && Battle.Inst.EnemiesLeft == 0;
            bool heavy = h.Kind == HitKind.Heavy || h.Kind == HitKind.Technique || h.Kind == HitKind.Ultimate;
            bool cooled = Time.unscaledTime - lastAt > 14f;
            if (!(victim.IsBoss || lastOne || (heavy && cooled))) return;
            if (inst == null) Build();
            if (inst == null) return;
            lastAt = Time.unscaledTime;
            inst.running = inst.StartCoroutine(inst.Play(victim, side, killer, h));
        }

        /// <summary>Stops everything at once (leaving a battle, pausing to the menu).</summary>
        public static void Cancel()
        {
            if (inst == null) return;
            if (inst.running != null) inst.StopCoroutine(inst.running);
            inst.running = null;
            inst.ResetVisuals();
        }

        static void Build()
        {
            var tex = Resources.Load<Texture2D>("Kagegan/Vfx/kanji_atlas");
            if (tex == null) { Debug.LogWarning("Kagegan: kanji_atlas missing, finisher cinematic off"); return; }
            var c = UIKit.MakeCanvas("FinisherFx", 30);
            DontDestroyOnLoad(c.gameObject);
            Destroy(c.GetComponent<GraphicRaycaster>()); // never blocks touches
            var f = c.gameObject.AddComponent<FinisherFx>();
            f.canvas = c;
            f.glyphs = new Sprite[16];
            int cell = tex.width / 4;
            for (int i = 0; i < 16; i++)
            {
                int cx = i % 4, cy = i / 4;
                f.glyphs[i] = Sprite.Create(tex, new Rect(cx * cell, tex.height - (cy + 1) * cell, cell, cell), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
            }
            Transform root = c.transform;
            // cinematic letterbox bars
            f.barTop = UIKit.Rect(root, "BarTop", new Vector2(0, 1), new Vector2(1, 1));
            f.barTop.pivot = new Vector2(0.5f, 0f);
            f.barTop.sizeDelta = new Vector2(0, 130);
            UIKit.Img(f.barTop, new Color(0.02f, 0.02f, 0.03f, 1f));
            f.barBottom = UIKit.Rect(root, "BarBottom", new Vector2(0, 0), new Vector2(1, 0));
            f.barBottom.pivot = new Vector2(0.5f, 1f);
            f.barBottom.sizeDelta = new Vector2(0, 130);
            UIKit.Img(f.barBottom, new Color(0.02f, 0.02f, 0.03f, 1f));
            // light burst
            f.flash = UIKit.Img(UIKit.Rect(root, "Flash", Vector2.zero, Vector2.one), new Color(1f, 1f, 1f, 0f));
            // kanji: a dark offset copy gives the brush stroke weight against bright skies and snow
            f.kanjiRt = UIKit.Box(root, "Kanji", new Vector2(0.5f, 0.5f), new Vector2(0, 40), new Vector2(620, 620));
            f.kanjiShadow = UIKit.Img(UIKit.Box(f.kanjiRt, "Shadow", new Vector2(0.5f, 0.5f), new Vector2(14, -14), new Vector2(620, 620)), new Color(0.05f, 0.02f, 0.03f, 0f));
            f.kanji = UIKit.Img(UIKit.Box(f.kanjiRt, "Glyph", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(620, 620)), new Color(InkRed.r, InkRed.g, InkRed.b, 0f));
            f.kanji.preserveAspect = f.kanjiShadow.preserveAspect = true;
            inst = f;
            f.ResetVisuals();
        }

        void ResetVisuals()
        {
            Shader.SetGlobalFloat(InkId, 0f);
            if (savedBg.HasValue && CameraRig.Inst != null && CameraRig.Inst.Cam != null) CameraRig.Inst.Cam.backgroundColor = savedBg.Value;
            savedBg = null;
            SetAlpha(kanji, 0f); SetAlpha(kanjiShadow, 0f); SetAlpha(flash, 0f);
            SetBars(0f);
            Busy = false;
        }

        Color? savedBg;

        void Ink(bool on)
        {
            Shader.SetGlobalFloat(InkId, on ? 1f : 0f);
            var cam = CameraRig.Inst != null ? CameraRig.Inst.Cam : null;
            if (cam == null) return;
            if (on) { if (!savedBg.HasValue) savedBg = cam.backgroundColor; cam.backgroundColor = Paper; }
            else if (savedBg.HasValue) { cam.backgroundColor = savedBg.Value; savedBg = null; }
        }

        static void SetAlpha(Graphic g, float a) { if (g == null) return; var c = g.color; c.a = a; g.color = c; }

        void SetBars(float k)
        {
            if (barTop == null) return;
            // slides in from the screen edges; 130 px of 1080 at full
            barTop.anchoredPosition = new Vector2(0, -130f * k);
            barBottom.anchoredPosition = new Vector2(0, 130f * k);
        }

        static Glyph GlyphFor(Fighter side, HitInfo h)
        {
            if (h.Kind == HitKind.Ultimate) return Glyph.Metsu;
            var k = side.Kit;
            if (k is KaitoKit) return Glyph.Kaze;
            if (k is GenryuKit) return Glyph.En;
            if (k is MugenKit) return Glyph.Kage;
            if (k is ReinaKit) return Glyph.Zan;
            return Glyph.Geki;
        }

        IEnumerator Play(Fighter victim, Fighter side, Fighter killer, HitInfo h)
        {
            Busy = true;
            Glyph g = GlyphFor(side, h);
            kanji.sprite = kanjiShadow.sprite = glyphs[(int)g];
            canvas.transform.SetAsLastSibling();

            Vector3 c = victim.Center;
            Vector3 along = KMath.Flat(victim.transform.position - killer.transform.position);
            along = along.sqrMagnitude > 0.01f ? along.normalized : (victim.transform.forward.x >= 0f ? Vector3.left : Vector3.right);
            // close-up from the camera's side of the lane, a little in front of the victim so the hit reads
            Vector3 view = Vector3.Cross(Vector3.up, along);
            if (view.z > 0f) view = -view;
            view = (view + along * 0.35f + Vector3.up * 0.05f).normalized;
            Vector3 focus = Vector3.Lerp(c, side.Center, 0.25f);
            CameraRig.Cinematic(focus, view, 3.1f, 0.2f, 1.35f);
            TimeFx.SlowMo(1.3f, 0.12f);

            // 1) impact frame: the world turns to paper, the fighters to ink, for two or three frames
            Ink(true);
            Sfx.Play(SfxId.HeavyHit, 1f, 0.7f);
            Haptics.Heavy();
            yield return new WaitForSecondsRealtime(0.05f);
            Ink(false);

            // 2) the cut: slash across the body, splatter, shockwave
            Color slashCol = Color.Lerp(side.HitColor, Color.white, 0.25f);
            float tilt = along.x >= 0f ? -32f : 32f;
            Fx.Slash(victim.transform, new Vector3(0f, victim.Height * 0.55f, 0f), new Vector3(0f, 0f, tilt), slashCol, 3.4f, 0.32f);
            Fx.Hit(c, slashCol, true);
            Fx.Debris(c, Splatter, 16, 5.5f);
            Fx.Ring(c, slashCol, 0.4f, 3.2f, 0.3f, Quaternion.LookRotation(view));
            CameraRig.Shake(0.55f);
            Sfx.Play(SfxId.Whoosh, 1f, 1.3f);

            // 3) the kanji stamps onto the screen
            float t = 0f;
            const float In = 0.11f;
            kanjiRt.localRotation = Quaternion.Euler(0, 0, Random.Range(-8f, 8f));
            while (t < 0.85f)
            {
                if (TimeFx.Paused) { yield return null; continue; }
                t += Time.unscaledDeltaTime;
                float u = Mathf.Clamp01(t / In);
                float s = Mathf.Lerp(1.9f, 1f, 1f - (1f - u) * (1f - u)) + (u >= 1f ? Mathf.Sin(Mathf.Min(1f, (t - In) / 0.12f) * Mathf.PI) * 0.05f : 0f);
                kanjiRt.localScale = Vector3.one * s;
                SetAlpha(kanji, u);
                SetAlpha(kanjiShadow, u * 0.85f);
                SetBars(Mathf.Clamp01(t / 0.18f));
                yield return null;
            }

            // 4) light burst and smoke where the body drops
            if (victim != null)
            {
                Vector3 p = victim.Center;
                Fx.Burst(p, new Color(1f, 0.95f, 0.85f), 3.2f, 0.4f);
                Fx.Smoke(p, 1.5f, new Color(0.88f, 0.88f, 0.9f, 0.6f), 14);
                WorldLighting.Flash(p, new Color(1f, 0.9f, 0.75f), 9f, 0.35f);
            }
            Sfx.Play(SfxId.Ko, 1f);
            SetAlpha(flash, 0.75f);
            t = 0f;
            while (t < 0.45f)
            {
                if (TimeFx.Paused) { yield return null; continue; }
                t += Time.unscaledDeltaTime;
                float u = Mathf.Clamp01(t / 0.45f);
                SetAlpha(flash, 0.75f * (1f - u) * (1f - u));
                SetAlpha(kanji, 1f - u);
                SetAlpha(kanjiShadow, (1f - u) * 0.85f);
                kanjiRt.localScale = Vector3.one * (1f + u * 0.12f);
                SetBars(1f - u);
                yield return null;
            }
            ResetVisuals();
            running = null;
        }

        void OnDisable() { if (Busy) ResetVisuals(); }
    }
}
