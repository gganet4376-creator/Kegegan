using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Kagegan
{
    /// <summary>Floating virtual joystick in the left part of the screen.</summary>
    public class VirtualJoystick : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
    {
        public RectTransform Base, Knob;
        public float Radius = 145f;
        public Vector2 Value { get; private set; }
        int pointer = -99;
        Vector2 restPos;
        RectTransform zone;
        Canvas canvas;

        public void Setup(RectTransform zone, RectTransform baseRt, RectTransform knob)
        {
            this.zone = zone; Base = baseRt; Knob = knob;
            restPos = Base.anchoredPosition;
            canvas = GetComponentInParent<Canvas>();
        }

        public void ResetRest() { restPos = Base.anchoredPosition; }

        Vector2 Local(PointerEventData e)
        {
            RectTransformUtility.ScreenPointToLocalPointInRectangle((RectTransform)Base.parent, e.position, e.pressEventCamera, out var p);
            return p;
        }

        public void OnPointerDown(PointerEventData e)
        {
            if (LayoutEditor.Editing) return;
            if (pointer != -99) return;
            pointer = e.pointerId;
            Vector2 p = Local(e);
            Vector2 baseCenter = Base.localPosition;
            if ((p - baseCenter).magnitude > Radius * 1.3f)
            {
                // float the joystick to where the thumb landed
                Base.localPosition = p;
            }
            OnDrag(e);
        }

        public void OnDrag(PointerEventData e)
        {
            if (e.pointerId != pointer) return;
            Vector2 d = Local(e) - (Vector2)Base.localPosition;
            Vector2 c = Vector2.ClampMagnitude(d, Radius);
            Knob.anchoredPosition = c;
            Vector2 v = c / Radius;
            Value = v.magnitude < 0.12f ? Vector2.zero : v;
        }

        public void OnPointerUp(PointerEventData e)
        {
            if (e.pointerId != pointer) return;
            pointer = -99;
            Value = Vector2.zero;
            Knob.anchoredPosition = Vector2.zero;
            Base.anchoredPosition = restPos;
        }

        void OnDisable()
        {
            pointer = -99;
            Value = Vector2.zero;
            if (Knob != null) Knob.anchoredPosition = Vector2.zero;
            if (Base != null) Base.anchoredPosition = restPos;
        }
    }

    /// <summary>Round touch button with press feedback, cooldown radial and drag support for the layout editor.</summary>
    /// <summary>Breathing ring used by the tutorial to point at a control.</summary>
    public class HighlightPulse : MonoBehaviour
    {
        Image img;
        void Awake() { img = GetComponent<Image>(); }
        void Update()
        {
            float s = Mathf.Repeat(Time.unscaledTime * 1.3f, 1f);
            transform.localScale = Vector3.one * (1f + s * 0.35f);
            if (img != null) img.color = new Color(1f, 0.82f, 0.3f, 1f - s);
        }
    }

    public class TouchButton : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IDragHandler
    {
        public string Id;
        public Action Down, Up;
        public Image Icon, CooldownFill, Glyph, Ring;
        public Text Label;
        /// <summary>Cooldown seconds, drawn over the icon so the caption always stays readable.</summary>
        public Text Timer;
        public bool Held { get; private set; }
        public Vector2 DefaultPos;
        int pointer = -99;
        RectTransform rt;
        Color baseColor;

        public void Setup(string id, Image icon, Text label, Image cd)
        {
            Id = id; Icon = icon; Label = label; CooldownFill = cd;
            rt = (RectTransform)transform;
            baseColor = icon.color;
            DefaultPos = rt.anchoredPosition;
        }

        public void SetColor(Color c) { baseColor = c; if (!Held) Icon.color = c; }

        public void OnPointerDown(PointerEventData e)
        {
            if (LayoutEditor.Editing || Held) return;
            pointer = e.pointerId;
            Held = true;
            transform.localScale = Vector3.one * (Save.Data.buttonScale * 0.9f);
            Icon.color = baseColor * 1.35f;
            Down?.Invoke();
        }

        public void OnPointerUp(PointerEventData e)
        {
            if (LayoutEditor.Editing) { Save.SetLayout(Id, rt.anchoredPosition); return; }
            if (e.pointerId != pointer) return;
            Release();
        }

        void Release()
        {
            if (!Held) return;
            Held = false;
            pointer = -99;
            transform.localScale = Vector3.one * Save.Data.buttonScale;
            Icon.color = baseColor;
            Up?.Invoke();
        }

        public void OnDrag(PointerEventData e)
        {
            if (!LayoutEditor.Editing) return;
            var parent = (RectTransform)rt.parent;
            float scale = parent.lossyScale.x > 0f ? parent.lossyScale.x : 1f;
            rt.anchoredPosition += e.delta / scale;
        }

        void OnDisable() { Release(); }
    }

    public static class LayoutEditor
    {
        public static bool Editing;
    }

    /// <summary>Builds the on-screen controls: joystick + attack cluster, technique slots, ultimate, lock-on, pause.</summary>
    public class TouchControls : MonoBehaviour
    {
        public VirtualJoystick Stick;
        public readonly Dictionary<string, TouchButton> Buttons = new Dictionary<string, TouchButton>();
        public RectTransform Root;
        public CapriButton Capri;
        public Action<string, bool> OnButton;
        CanvasGroup group;

        const float StickX = 300f, StickY = 290f, StickSize = 340f;

        struct Def { public string id, label, icon; public Vector2 pos; public float size; public Color color; }

        static readonly Def[] Defs =
        {
            // Keep the action cluster on the right so the centre of the fight stays visible.
            new Def { id = "light", label = "Атака", icon = "light", pos = new Vector2(-180, 180), size = 215, color = new Color(0.72f, 0.14f, 0.13f, 1f) },
            new Def { id = "heavy", label = "Мощный", icon = "heavy", pos = new Vector2(-410, 155), size = 145, color = new Color(0.52f, 0.26f, 0.12f, 1f) },
            new Def { id = "block", label = "Блок", icon = "block", pos = new Vector2(-410, 375), size = 145, color = new Color(0.18f, 0.3f, 0.58f, 1f) },
            new Def { id = "dodge", label = "Рывок", icon = "dodge", pos = new Vector2(-180, 400), size = 133, color = new Color(0.16f, 0.42f, 0.42f, 1f) },
            new Def { id = "jump", label = "Прыжок", icon = "jump", pos = new Vector2(-595, 135), size = 112, color = new Color(0.24f, 0.24f, 0.52f, 1f) },
            new Def { id = "grab", label = "Захват", icon = "grab", pos = new Vector2(-595, 330), size = 112, color = new Color(0.42f, 0.22f, 0.42f, 1f) },
            new Def { id = "techs", label = "Техники", icon = null, pos = new Vector2(-595, 540), size = 140, color = new Color(0.45f, 0.3f, 0.75f, 1f) },
            new Def { id = "ult", label = "УЛЬТА", icon = "ult", pos = new Vector2(-180, 595), size = 145, color = new Color(0.82f, 0.52f, 0.1f, 1f) },
            new Def { id = "auto", label = "АВТО", icon = null, pos = new Vector2(-180, 765), size = 100, color = new Color(0.3f, 0.75f, 0.95f, 1f) },
            // context action (trip from the ground / break a hold): hidden until the situation allows it
            // Lucy only: summon Capri (hidden for everyone else)
            new Def { id = "capri", label = "КАПРИ", icon = "capri_face", pos = new Vector2(-790, 230), size = 135, color = new Color(1f, 0.16f, 0.33f, 1f) },
            new Def { id = "ctx", label = "Подножка", icon = null, pos = new Vector2(-410, 765), size = 160, color = new Color(1f, 0.78f, 0.25f, 1f) },
        };

        public void Build(Transform canvas)
        {
            Root = UIKit.Rect(canvas, "TouchControls", Vector2.zero, Vector2.one);
            Root.gameObject.AddComponent<SafeArea>();
            group = Root.gameObject.AddComponent<CanvasGroup>();

            // joystick zone: left 42% of the screen
            var zone = UIKit.Rect(Root, "StickZone", new Vector2(0, 0), new Vector2(0.42f, 0.78f));
            var zimg = zone.gameObject.AddComponent<Image>();
            zimg.color = new Color(0, 0, 0, 0);
            var baseRt = UIKit.Box(zone, "StickBase", new Vector2(0, 0), new Vector2(StickX, StickY), new Vector2(StickSize, StickSize));
            UIKit.Img(baseRt, new Color(1f, 1f, 1f, 0.16f), UIKit.Circle);
            var ring = UIKit.Box(baseRt, "Ring", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(StickSize, StickSize));
            UIKit.Img(ring, new Color(1f, 1f, 1f, 0.35f), UIKit.Ring);
            var knob = UIKit.Box(baseRt, "Knob", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(StickSize * 0.46f, StickSize * 0.46f));
            UIKit.Img(knob, new Color(0.25f, 0.22f, 0.22f, 0.85f), UIKit.Circle);
            Stick = zone.gameObject.AddComponent<VirtualJoystick>();
            Stick.Setup(zone, baseRt, knob);
            if (Save.TryGetLayout("stick", out var sp)) { baseRt.anchoredPosition = sp; Stick.ResetRest(); }

            foreach (var d in Defs) MakeButton(d);
            BuildFan();
            if (Buttons.TryGetValue("capri", out var cb)) { Capri = CapriButton.Attach(cb); cb.gameObject.SetActive(false); }
            ApplyScaleAlpha();
            SetContext(null);
        }

        TouchButton MakeButton(Def d, bool register = true)
        {
            var rt = UIKit.Box(Root, "Btn_" + d.id, new Vector2(1, 0), d.pos, new Vector2(d.size, d.size));
            // unified style: dark translucent disc, white glyph, colour only in the thin ring
            var img = UIKit.Img(rt, d.id == "light" ? new Color(0.3f, 0.06f, 0.06f, 0.7f) : new Color(0.05f, 0.06f, 0.09f, 0.58f), UIKit.Circle, true);
            var ring = UIKit.Box(rt, "Ring", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(d.size, d.size));
            var ringImg = UIKit.Img(ring, Color.Lerp(d.color, Color.white, 0.25f), UIKit.Ring);
            var cdRt = UIKit.Box(rt, "Cooldown", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(d.size, d.size));
            var cd = cdRt.gameObject.AddComponent<Image>();
            cd.sprite = UIKit.Circle;
            cd.type = Image.Type.Filled;
            cd.fillMethod = Image.FillMethod.Radial360;
            cd.fillOrigin = 2;
            cd.fillClockwise = false;
            cd.color = new Color(0, 0, 0, 0.6f);
            cd.fillAmount = 0f;
            cd.raycastTarget = false;
            // icon in the middle, caption under it (readable at arm's length on a 6.3" screen)
            var glyph = UIKit.IconImage(rt, d.icon, new Vector2(0, d.size * 0.08f), d.size * 0.5f, new Color(1f, 1f, 1f, 0.95f));
            var capRt = UIKit.Box(rt, "Caption", new Vector2(0.5f, 0f), new Vector2(0, d.size * 0.2f), new Vector2(d.size * 1.1f, d.size * 0.26f));
            var label = UIKit.Label(capRt, d.label, d.size > 200 ? 40 : d.size > 140 ? 31 : 27, Color.white);
            label.fontStyle = FontStyle.Bold;
            // never break a word across lines ("Перегру з"): shrink to fit the caption box instead
            label.resizeTextForBestFit = true;
            label.resizeTextMinSize = 15;
            label.resizeTextMaxSize = label.fontSize;
            var timerRt = UIKit.Box(rt, "Timer", new Vector2(0.5f, 0.5f), new Vector2(0, d.size * 0.08f), new Vector2(d.size * 0.8f, d.size * 0.42f));
            var timer = UIKit.Label(timerRt, "", Mathf.RoundToInt(d.size * 0.3f), new Color(1f, 0.92f, 0.7f, 1f));
            timer.fontStyle = FontStyle.Bold;
            timer.enabled = false;
            if (glyph.sprite == null) { capRt.anchoredPosition = Vector2.zero; capRt.anchorMin = capRt.anchorMax = new Vector2(0.5f, 0.5f); label.fontSize += 4; }
            var b = rt.gameObject.AddComponent<TouchButton>();
            b.Setup(d.id, img, label, cd);
            b.Glyph = glyph;
            b.Ring = ringImg;
            b.Timer = timer;
            string id = d.id;
            b.Down = () => OnButton?.Invoke(id, true);
            b.Up = () => OnButton?.Invoke(id, false);
            if (!register) return b;
            if (Save.TryGetLayout(d.id, out var p)) rt.anchoredPosition = p;
            Buttons[d.id] = b;
            return b;
        }

        // ---- technique fan: one "Техники" button opens up to 6 technique buttons in an arc around it ----
        public const int FanMax = 6;
        public readonly TouchButton[] Fan = new TouchButton[FanMax];
        public bool FanOpen { get; private set; }
        public int FanCount = 5;
        public Action<int, bool> OnFan;
        float fanT, fanCloseAt;
        const float FanRadius = 235f, FanSize = 122f;

        void BuildFan()
        {
            for (int i = 0; i < FanMax; i++)
            {
                var d = new Def { id = "tf" + i, label = "", icon = null, pos = Vector2.zero, size = FanSize, color = new Color(0.45f, 0.3f, 0.75f, 1f) };
                var b = MakeButton(d, false);
                int slot = i;
                b.Down = () => { KeepFanOpen(); OnFan?.Invoke(slot, true); };
                b.Up = () => OnFan?.Invoke(slot, false);
                b.gameObject.SetActive(false);
                Fan[i] = b;
            }
        }

        public void ToggleFan() { SetFan(!FanOpen); }

        public void SetFan(bool open)
        {
            if (LayoutEditor.Editing) open = false;
            FanOpen = open;
            if (open) { KeepFanOpen(); fanDirty = true; }
        }

        /// <summary>Each technique press keeps the fan open a while, so techniques can be chained.</summary>
        public void KeepFanOpen() { fanCloseAt = Time.unscaledTime + 4f; }

        readonly Vector2[] fanSlots = new Vector2[FanMax];
        Vector4 fanKey = new Vector4(-1, 0, 0, 0);
        Vector2 fanKeySize;
        bool fanDirty = true;

        /// <summary>
        /// Picks a free on-screen spot for each of the n technique buttons: arcs around the "Техники" button
        /// (up first, then sweeping left toward the free middle of the screen), skipping spots that would
        /// overlap another visible control, each other, the HP bars at the top or the screen edge.
        /// Works for any technique count and any custom button layout, so newly unlocked skills never stack.
        /// </summary>
        void LayoutFan(int n)
        {
            var c = ((RectTransform)Buttons["techs"].transform).anchoredPosition;
            float s = Save.Data.buttonScale;
            var size = Root.rect.size;
            float d = FanSize * s;
            float need = d * 1.12f + 10f;            // caption is 1.1x the disc wide
            float m = d * 0.58f;
            float top = size.y - Mathf.Max(m, 150f);  // keep clear of the HP/name bars
            var taken = new List<Vector3>();          // x, y, radius of things to avoid
            foreach (var kv in Buttons)
            {
                if (kv.Key == "techs" || !kv.Value.gameObject.activeSelf) continue;
                var r = (RectTransform)kv.Value.transform;
                taken.Add(new Vector3(r.anchoredPosition.x, r.anchoredPosition.y, r.sizeDelta.x * 0.55f * s));
            }
            var tr = (RectTransform)Buttons["techs"].transform;
            taken.Add(new Vector3(c.x, c.y, tr.sizeDelta.x * 0.55f * s));
            int got = 0;
            float r0 = Mathf.Max(FanRadius * s, tr.sizeDelta.x * 0.55f * s + d * 0.62f + 6f);
            for (int ring = 0; ring < 4 && got < n; ring++)
            {
                float r = r0 + ring * need;
                float step = 2f * Mathf.Asin(Mathf.Min(1f, need / (2f * r))) * Mathf.Rad2Deg;
                float start = 88f + (ring % 2) * step * 0.5f;
                for (float a = start; a <= 268f && got < n; a += step)
                {
                    float ar = a * Mathf.Deg2Rad;
                    var p = c + new Vector2(Mathf.Cos(ar), Mathf.Sin(ar)) * r;
                    if (p.x < -size.x + m || p.x > -m || p.y < m || p.y > top) continue;
                    bool ok = true;
                    foreach (var o in taken)
                        if ((new Vector2(o.x, o.y) - p).sqrMagnitude < Sq(o.z + d * 0.56f + 6f)) { ok = false; break; }
                    for (int k = 0; k < got && ok; k++)
                        if ((fanSlots[k] - p).sqrMagnitude < need * need) ok = false;
                    if (!ok) continue;
                    fanSlots[got++] = p;
                }
            }
            // crowded custom layout: fall back to a clamped column left of the button rather than stacking
            for (; got < n; got++)
                fanSlots[got] = new Vector2(Mathf.Clamp(c.x - need * (1 + got / 3), -size.x + m, -m),
                                            Mathf.Clamp(c.y + need * (got % 3 - 1), m, top));
        }

        static float Sq(float v) { return v * v; }

        public void InvalidateFan() { fanDirty = true; }

        void Update()
        {
            if (Root == null || !Buttons.ContainsKey("techs")) return;
            if (FanOpen && Time.unscaledTime > fanCloseAt)
            {
                bool anyHeld = false;
                for (int i = 0; i < FanMax; i++) if (Fan[i] != null && Fan[i].Held) anyHeld = true;
                if (anyHeld) KeepFanOpen(); else FanOpen = false;
            }
            float target = FanOpen ? 1f : 0f;
            if (fanT == 0f && !FanOpen) return;
            fanT = Mathf.MoveTowards(fanT, target, Time.unscaledDeltaTime / 0.14f);
            float e = 1f - (1f - fanT) * (1f - fanT);
            var c = ((RectTransform)Buttons["techs"].transform).anchoredPosition;
            int n = Mathf.Clamp(FanCount, 0, FanMax);
            var key = new Vector4(n, c.x, c.y, Save.Data.buttonScale);
            if (fanDirty || key != fanKey || fanKeySize != Root.rect.size)
            {
                fanKey = key; fanKeySize = Root.rect.size; fanDirty = false;
                LayoutFan(n);
            }
            for (int i = 0; i < FanMax; i++)
            {
                var b = Fan[i];
                bool on = i < n && fanT > 0.01f;
                if (b.gameObject.activeSelf != on) b.gameObject.SetActive(on);
                if (!on) continue;
                var rt = (RectTransform)b.transform;
                rt.anchoredPosition = Vector2.LerpUnclamped(c, fanSlots[i], e);
                if (!b.Held) rt.localScale = Vector3.one * (Save.Data.buttonScale * Mathf.Lerp(0.3f, 1f, e));
            }
            var tb = Buttons["techs"];
            if (tb.Ring != null && FanOpen) tb.Ring.color = Color.Lerp(new Color(0.7f, 0.55f, 1f), Color.white, 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 6f));
        }

        public void ApplyScaleAlpha()
        {
            float s = Save.Data.buttonScale;
            foreach (var b in Buttons.Values) ((RectTransform)b.transform).localScale = Vector3.one * s;
            Stick.Base.localScale = Vector3.one * s;
            group.alpha = Save.Data.buttonAlpha;
        }

        public void SaveLayout()
        {
            foreach (var kv in Buttons) Save.SetLayout(kv.Key, ((RectTransform)kv.Value.transform).anchoredPosition);
            Save.SetLayout("stick", Stick.Base.anchoredPosition);
            Stick.ResetRest();
            Save.Write();
        }

        public void ResetLayout()
        {
            Save.Data.layout.Clear();
            foreach (var kv in Buttons) ((RectTransform)kv.Value.transform).anchoredPosition = kv.Value.DefaultPos;
            Stick.Base.anchoredPosition = new Vector2(StickX, StickY);
            Stick.ResetRest();
            Save.Write();
        }

        public void SetEditMode(bool on)
        {
            LayoutEditor.Editing = on;
            if (on) { FanOpen = false; fanT = 0f; foreach (var fb in Fan) if (fb != null) fb.gameObject.SetActive(false); }
            SetContext(null); // the context button is shown (and draggable) only while editing the layout
            // the joystick base becomes draggable in edit mode through a helper button
            if (on && Stick.Base.GetComponent<TouchButton>() == null)
            {
                var img = Stick.Base.GetComponent<Image>();
                img.raycastTarget = true;
                var tb = Stick.Base.gameObject.AddComponent<TouchButton>();
                tb.Setup("stick", img, null, null);
            }
            var sb = Stick.Base.GetComponent<TouchButton>();
            if (sb != null) sb.enabled = on;
            Stick.Base.GetComponent<Image>().raycastTarget = on;
        }

        public void SetVisible(bool v) { Root.gameObject.SetActive(v); }

        RectTransform highlight;

        /// <summary>Tutorial: a pulsing gold ring around a button ("stick" = the joystick); null hides it.</summary>
        public void Highlight(string id)
        {
            RectTransform target = null;
            if (id == "stick") target = Stick != null ? Stick.Base : null;
            else if (id != null && Buttons.TryGetValue(id, out var b)) target = (RectTransform)b.transform;
            if (target == null) { if (highlight != null) highlight.gameObject.SetActive(false); return; }
            if (highlight == null)
            {
                highlight = UIKit.Box(Root, "Highlight", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(100, 100));
                var img = UIKit.Img(highlight, new Color(1f, 0.82f, 0.3f, 1f), UIKit.Ring);
                img.raycastTarget = false;
                highlight.gameObject.AddComponent<HighlightPulse>();
            }
            highlight.SetParent(target, false);
            highlight.anchorMin = highlight.anchorMax = new Vector2(0.5f, 0.5f);
            highlight.anchoredPosition = Vector2.zero;
            highlight.sizeDelta = target.sizeDelta * 1.25f;
            highlight.SetAsLastSibling();
            highlight.gameObject.SetActive(true);
        }

        /// <summary>Shows the context button with the given caption (pulsing), or hides it when caption is null.</summary>
        public void SetContext(string caption)
        {
            if (!Buttons.TryGetValue("ctx", out var b)) return;
            bool on = caption != null || LayoutEditor.Editing;
            if (b.gameObject.activeSelf != on) b.gameObject.SetActive(on);
            if (!on) return;
            if (caption != null && b.Label.text != caption) b.Label.text = caption;
            float s = Save.Data.buttonScale * (b.Held ? 0.9f : 1f + 0.07f * Mathf.Sin(Time.unscaledTime * 10f));
            ((RectTransform)b.transform).localScale = Vector3.one * s;
            if (b.Ring != null) b.Ring.color = Color.Lerp(new Color(1f, 0.75f, 0.2f), Color.white, 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 10f));
        }
    }
}
