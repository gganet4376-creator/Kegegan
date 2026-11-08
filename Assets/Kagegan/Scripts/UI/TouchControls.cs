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

        void OnDisable() { pointer = -99; Value = Vector2.zero; if (Knob != null) Knob.anchoredPosition = Vector2.zero; }
    }

    /// <summary>Round touch button with press feedback, cooldown radial and drag support for the layout editor.</summary>
    public class TouchButton : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IDragHandler
    {
        public string Id;
        public Action Down, Up;
        public Image Icon, CooldownFill, Glyph, Ring;
        public Text Label;
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
            if (LayoutEditor.Editing) return;
            pointer = e.pointerId;
            Held = true;
            transform.localScale = Vector3.one * 0.9f;
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
            transform.localScale = Vector3.one;
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
        public Action<string, bool> OnButton;
        CanvasGroup group;

        const float StickX = 300f, StickY = 290f, StickSize = 340f;

        struct Def { public string id, label, icon; public Vector2 pos; public float size; public Color color; }

        static readonly Def[] Defs =
        {
            // v2 layout: ~1.3x larger buttons, re-spaced so nothing overlaps on 18:9..21:9 phones
            new Def { id = "light", label = "Атака", icon = "light", pos = new Vector2(-200, 200), size = 270, color = new Color(0.72f, 0.14f, 0.13f, 1f) },
            new Def { id = "heavy", label = "Мощный", icon = "heavy", pos = new Vector2(-470, 140), size = 170, color = new Color(0.52f, 0.26f, 0.12f, 1f) },
            new Def { id = "block", label = "Блок", icon = "block", pos = new Vector2(-445, 385), size = 165, color = new Color(0.18f, 0.3f, 0.58f, 1f) },
            new Def { id = "dodge", label = "Рывок", icon = "dodge", pos = new Vector2(-175, 470), size = 150, color = new Color(0.16f, 0.42f, 0.42f, 1f) },
            new Def { id = "jump", label = "Прыжок", icon = "jump", pos = new Vector2(-685, 115), size = 130, color = new Color(0.24f, 0.24f, 0.52f, 1f) },
            new Def { id = "grab", label = "Захват", icon = "grab", pos = new Vector2(-695, 290), size = 125, color = new Color(0.42f, 0.22f, 0.42f, 1f) },
            new Def { id = "tech0", label = "Т1", icon = null, pos = new Vector2(-640, 510), size = 150, color = new Color(0.3f, 0.2f, 0.45f, 1f) },
            new Def { id = "tech1", label = "Т2", icon = null, pos = new Vector2(-405, 600), size = 150, color = new Color(0.3f, 0.2f, 0.45f, 1f) },
            new Def { id = "swap", label = "Смена", icon = "swap", pos = new Vector2(-835, 455), size = 100, color = new Color(0.2f, 0.2f, 0.25f, 1f) },
            new Def { id = "ult", label = "УЛЬТА", icon = "ult", pos = new Vector2(-175, 690), size = 160, color = new Color(0.82f, 0.52f, 0.1f, 1f) },
            new Def { id = "auto", label = "АВТО", icon = null, pos = new Vector2(-640, 720), size = 130, color = new Color(0.3f, 0.75f, 0.95f, 1f) },
            // context action (trip from the ground / break a hold): hidden until the situation allows it
            new Def { id = "ctx", label = "Подножка", icon = null, pos = new Vector2(-420, 850), size = 200, color = new Color(1f, 0.78f, 0.25f, 1f) },
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
            ApplyScaleAlpha();
            SetContext(null);
        }

        void MakeButton(Def d)
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
            if (glyph.sprite == null) { capRt.anchoredPosition = Vector2.zero; capRt.anchorMin = capRt.anchorMax = new Vector2(0.5f, 0.5f); label.fontSize += 4; }
            var b = rt.gameObject.AddComponent<TouchButton>();
            b.Setup(d.id, img, label, cd);
            b.Glyph = glyph;
            b.Ring = ringImg;
            string id = d.id;
            b.Down = () => OnButton?.Invoke(id, true);
            b.Up = () => OnButton?.Invoke(id, false);
            if (Save.TryGetLayout(d.id, out var p)) rt.anchoredPosition = p;
            Buttons[d.id] = b;
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
