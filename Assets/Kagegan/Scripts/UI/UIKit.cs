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
            b.onClick.AddListener(() => { Sfx.Play(SfxId.Click, 0.8f); Haptics.Tick(); onClick?.Invoke(); });
            rt.gameObject.AddComponent<ButtonPop>();
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

        static readonly System.Collections.Generic.Dictionary<string, Sprite> icons = new System.Collections.Generic.Dictionary<string, Sprite>();

        /// <summary>White silhouette icon from Resources/Kagegan/Icons (game-icons.net, CC BY 3.0). Null if missing.</summary>
        public static Sprite Icon(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            if (icons.TryGetValue(name, out var sp)) return sp;
            var tex = Resources.Load<Texture2D>("Kagegan/Icons/" + name);
            sp = tex != null ? Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f)) : null;
            icons[name] = sp;
            return sp;
        }

        public static Image IconImage(Transform parent, string icon, Vector2 pos, float size, Color c)
        {
            var rt = Box(parent, "Icon", new Vector2(0.5f, 0.5f), pos, new Vector2(size, size));
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = Icon(icon);
            img.preserveAspect = true;
            img.color = c;
            img.raycastTarget = false;
            img.enabled = img.sprite != null;
            return img;
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

    /// <summary>Small press/release scale animation for menu buttons.</summary>
    public class ButtonPop : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
    {
        float target = 1f, scale = 1f;
        public void OnPointerDown(PointerEventData e) { target = 0.93f; }
        public void OnPointerUp(PointerEventData e) { target = 1f; }
        public void OnPointerExit(PointerEventData e) { target = 1f; }
        void OnEnable() { scale = 0.9f; target = 1f; }
        void Update()
        {
            if (Mathf.Abs(scale - target) < 0.001f) return;
            scale = Mathf.Lerp(scale, target, 1f - Mathf.Exp(-18f * Time.unscaledDeltaTime));
            transform.localScale = Vector3.one * scale;
        }
    }

    /// <summary>Keeps a RectTransform inside Screen.safeArea (camera cut-outs, rounded corners, gesture bar) plus a small margin.</summary>
    public class SafeArea : MonoBehaviour
    {
        Rect last;
        Vector2Int lastRes;
        RectTransform rt;
        public float CornerMargin = 0.018f;

        void Awake() { rt = (RectTransform)transform; Apply(); }
        void Update() { if (Screen.safeArea != last || lastRes.x != Screen.width || lastRes.y != Screen.height) Apply(); }

        void Apply()
        {
            last = Screen.safeArea;
            lastRes = new Vector2Int(Screen.width, Screen.height);
            if (Screen.width <= 0 || Screen.height <= 0) return;
            Vector2 min = last.position, max = last.position + last.size;
            min.x /= Screen.width; min.y /= Screen.height; max.x /= Screen.width; max.y /= Screen.height;
            float m = Save.Data.safeAreaPadding ? CornerMargin : 0f;
            min.x += m; max.x -= m; min.y += m * 0.5f; max.y -= m * 0.5f;
            rt.anchorMin = min; rt.anchorMax = max;
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
        }
    }

    /// <summary>Full-screen fade used for scene/wave transitions.</summary>
    public class ScreenFader : MonoBehaviour
    {
        public static ScreenFader Inst;
        Image img;
        float alpha, target;
        public static void Create(Transform canvas)
        {
            var rt = UIKit.Rect(canvas, "Fader", Vector2.zero, Vector2.one);
            var f = rt.gameObject.AddComponent<ScreenFader>();
            f.img = rt.gameObject.AddComponent<Image>();
            f.img.color = new Color(0, 0, 0, 0);
            f.img.raycastTarget = false;
            Inst = f;
        }
        public static void FadeOut() { if (Inst != null) { Inst.target = 1f; Inst.transform.SetAsLastSibling(); } }
        public static void FadeIn() { if (Inst != null) Inst.target = 0f; }
        public static void Flash(float a) { if (Inst != null) { Inst.alpha = a; Inst.target = 0f; Inst.transform.SetAsLastSibling(); } }
        void Update()
        {
            alpha = Mathf.MoveTowards(alpha, target, Time.unscaledDeltaTime * 2.6f);
            img.color = new Color(0, 0, 0, alpha);
            img.raycastTarget = alpha > 0.5f;
        }
    }
}
