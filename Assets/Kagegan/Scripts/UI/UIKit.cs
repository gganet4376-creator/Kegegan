using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Kagegan
{
    /// <summary>Code-built uGUI helpers: canvas, rounded panels, labels, buttons, bars. Reference resolution 1920x1080 (landscape).</summary>
    public static class UIKit
    {
        public static Font Font;
        public static Sprite Rounded, Circle, Ring, Soft;
        public static readonly Color Ink = new Color(0.06f, 0.05f, 0.08f, 0.82f);
        public static readonly Color Accent = new Color(0.86f, 0.2f, 0.18f);
        public static readonly Color Gold = new Color(1f, 0.82f, 0.35f);
        public static readonly Color Paper = new Color(0.97f, 0.94f, 0.88f);

        public static void Init()
        {
            if (Font != null) return;
            try { Font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); } catch { Font = null; }
            if (Font == null) { try { Font = Resources.GetBuiltinResource<Font>("Arial.ttf"); } catch { Font = null; } }
            if (Font == null) Font = Font.CreateDynamicFontFromOSFont(new[] { "Roboto", "Noto Sans", "Arial", "DejaVu Sans" }, 32);
            Rounded = MakeRounded(64, 18);
            Circle = MakeCircle(128, false);
            Ring = MakeCircle(128, true);
            Soft = Sprite.Create(Mat.SoftDot, new Rect(0, 0, Mat.SoftDot.width, Mat.SoftDot.height), new Vector2(0.5f, 0.5f));
        }

        static Sprite MakeRounded(int size, int radius)
        {
            var t = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float dx = Mathf.Max(0, Mathf.Max(radius - x - 0.5f, x + 0.5f - (size - radius)));
                    float dy = Mathf.Max(0, Mathf.Max(radius - y - 0.5f, y + 0.5f - (size - radius)));
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    byte a = (byte)(Mathf.Clamp01(radius - d + 0.5f) * 255);
                    px[y * size + x] = new Color32(255, 255, 255, a);
                }
            t.SetPixels32(px);
            t.Apply(false, true);
            return Sprite.Create(t, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(radius, radius, radius, radius));
        }

        static Sprite MakeCircle(int size, bool ring)
        {
            var t = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[size * size];
            float r = size * 0.5f;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float d = Mathf.Sqrt((x + 0.5f - r) * (x + 0.5f - r) + (y + 0.5f - r) * (y + 0.5f - r));
                    float a = Mathf.Clamp01(r - d);
                    if (ring) a *= Mathf.Clamp01(d - (r - 7f));
                    px[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255));
                }
            t.SetPixels32(px);
            t.Apply(false, true);
            return Sprite.Create(t, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
        }

        public static Canvas MakeCanvas(string name, int order)
        {
            var go = new GameObject(name);
            var c = go.AddComponent<Canvas>();
            c.renderMode = RenderMode.ScreenSpaceOverlay;
            c.sortingOrder = order;
            var s = go.AddComponent<CanvasScaler>();
            s.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            s.referenceResolution = new Vector2(1920, 1080);
            s.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            s.matchWidthOrHeight = 1f;
            go.AddComponent<GraphicRaycaster>();
            return c;
        }

        public static void EnsureEventSystem()
        {
            if (UnityEngine.Object.FindAnyObjectByType<EventSystem>() != null) return;
            var es = new GameObject("EventSystem");
            es.AddComponent<EventSystem>();
            es.AddComponent<StandaloneInputModule>();
            UnityEngine.Object.DontDestroyOnLoad(es);
        }

        public static RectTransform Rect(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin = default, Vector2 offsetMax = default)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = anchorMin; rt.anchorMax = anchorMax;
            rt.offsetMin = offsetMin; rt.offsetMax = offsetMax;
            return rt;
        }

        /// <summary>Fixed-size rect anchored at a point.</summary>
        public static RectTransform Box(Transform parent, string name, Vector2 anchor, Vector2 pos, Vector2 size, Vector2? pivot = null)
        {
            var rt = Rect(parent, name, anchor, anchor);
            rt.pivot = pivot ?? new Vector2(0.5f, 0.5f);
            rt.sizeDelta = size;
            rt.anchoredPosition = pos;
            return rt;
        }

        public static Image Img(RectTransform rt, Color c, Sprite s = null, bool raycast = false)
        {
            var i = rt.gameObject.AddComponent<Image>();
            i.sprite = s ?? Rounded;
            i.type = s == null || s == Rounded ? Image.Type.Sliced : Image.Type.Simple;
            i.color = c;
            i.raycastTarget = raycast;
            return i;
        }

        public static Text Label(Transform parent, string text, int size, Color color, TextAnchor align = TextAnchor.MiddleCenter, FontStyle style = FontStyle.Normal)
        {
            var rt = Rect(parent, "Text", Vector2.zero, Vector2.one);
            var t = rt.gameObject.AddComponent<Text>();
            t.font = Font;
            t.text = text;
            t.fontSize = size;
            t.color = color;
            t.alignment = align;
            t.fontStyle = style;
            t.raycastTarget = false;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            var sh = rt.gameObject.AddComponent<Shadow>();
            sh.effectColor = new Color(0, 0, 0, 0.6f);
            sh.effectDistance = new Vector2(2, -2);
            return t;
        }

        public static Button Button(Transform parent, string label, Vector2 anchor, Vector2 pos, Vector2 size, Action onClick, Color? bg = null, int fontSize = 40)
        {
            var rt = Box(parent, "Btn_" + label, anchor, pos, size);
            var img = Img(rt, bg ?? Ink, null, true);
            var b = rt.gameObject.AddComponent<Button>();
            b.targetGraphic = img;
            var cb = b.colors;
            cb.highlightedColor = new Color(1.1f, 1.1f, 1.1f);
            cb.pressedColor = new Color(0.75f, 0.75f, 0.75f);
            cb.disabledColor = new Color(0.5f, 0.5f, 0.5f, 0.6f);
            b.colors = cb;
            var t = Label(rt, label, fontSize, Color.white);
            t.fontStyle = FontStyle.Bold;
            b.onClick.AddListener(() => { Sfx.Play(SfxId.Click, 0.8f); onClick?.Invoke(); });
            return b;
        }

        public static void SetLabel(Button b, string text)
        {
            var t = b.GetComponentInChildren<Text>();
            if (t != null) t.text = text;
        }

        /// <summary>Horizontal bar: returns the fill image (fillAmount 0..1).</summary>
        public static Image Bar(Transform parent, Vector2 anchor, Vector2 pos, Vector2 size, Color fill, Color? back = null, Vector2? pivot = null)
        {
            var rt = Box(parent, "Bar", anchor, pos, size, pivot);
            Img(rt, back ?? new Color(0, 0, 0, 0.55f));
            var f = Rect(rt, "Fill", Vector2.zero, Vector2.one, new Vector2(3, 3), new Vector2(-3, -3));
            var img = f.gameObject.AddComponent<Image>();
            img.sprite = Texture2DSprite();
            img.type = Image.Type.Filled;
            img.fillMethod = Image.FillMethod.Horizontal;
            img.fillOrigin = 0;
            img.color = fill;
            img.raycastTarget = false;
            return img;
        }

        static Sprite whiteSprite;
        static Sprite Texture2DSprite()
        {
            if (whiteSprite == null) whiteSprite = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f));
            return whiteSprite;
        }

        public static RectTransform FullPanel(Transform parent, string name, Color c)
        {
            var rt = Rect(parent, name, Vector2.zero, Vector2.one);
            var img = rt.gameObject.AddComponent<Image>();
            img.color = c;
            img.raycastTarget = true;
            return rt;
        }
    }
}
