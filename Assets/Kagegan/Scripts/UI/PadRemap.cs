using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Kagegan
{
    /// <summary>
    /// "Геймпад" screen: shows the controller, its rumble support and every action's binding; "Изменить" listens for
    /// the next button / trigger / D-pad / right-stick press; "Настроить всё" walks through the main actions in turn.
    /// Android controllers report triggers and the D-pad on different raw axes per model, so this is how they get bound.
    /// </summary>
    public class PadRemap : MonoBehaviour
    {
        public static bool Listening { get; private set; }

        static readonly PadAction[] WizardOrder =
        {
            PadAction.Light, PadAction.Heavy, PadAction.Jump, PadAction.Dodge, PadAction.Block, PadAction.Grab,
            PadAction.Ult, PadAction.SlotA, PadAction.SlotB, PadAction.Tech1, PadAction.Tech2, PadAction.Tech3, PadAction.Tech4,
            PadAction.TechPage, PadAction.Target, PadAction.Capri, PadAction.Pause
        };

        Text status, prompt;
        RectTransform promptBox;
        readonly List<Action> refresh = new List<Action>();
        int listenIndex = -1;          // index into the queue
        readonly List<PadAction> queue = new List<PadAction>();
        bool waitRelease;
        float listenUntil;
        public Action OnDone;

        public void Build(RectTransform screen)
        {
            UIKit.Label(UIKit.Box(screen, "T", new Vector2(0.5f, 1), new Vector2(0, -75), new Vector2(1400, 100)), "ГЕЙМПАД", 66, UIKit.Paper, TextAnchor.MiddleCenter, FontStyle.Bold);
            status = UIKit.Label(UIKit.Box(screen, "S", new Vector2(0.5f, 1), new Vector2(0, -140), new Vector2(1700, 50)), "", 28, new Color(1, 1, 1, 0.75f));
            int n = (int)PadAction.Count, half = (n + 1) / 2;
            for (int i = 0; i < n; i++)
            {
                var a = (PadAction)i;
                int col = i < half ? 0 : 1, row = i < half ? i : i - half;
                var r = UIKit.Box(screen, "Row", new Vector2(0.5f, 1), new Vector2(col == 0 ? -450 : 450, -215 - row * 72), new Vector2(860, 62));
                UIKit.Img(r, new Color(1, 1, 1, 0.05f));
                UIKit.Label(UIKit.Box(r, "L", new Vector2(0, 0.5f), new Vector2(20, 0), new Vector2(380, 60), new Vector2(0, 0.5f)), PadInput.ActionNames[i], 30, Color.white, TextAnchor.MiddleLeft);
                var v = UIKit.Label(UIKit.Box(r, "V", new Vector2(1, 0.5f), new Vector2(-370, 0), new Vector2(240, 60)), "", 30, UIKit.Gold, TextAnchor.MiddleCenter, FontStyle.Bold);
                refresh.Add(() => v.text = PadInput.BindText(a));
                UIKit.Button(r, "Изменить", new Vector2(1, 0.5f), new Vector2(-120, 0), new Vector2(210, 52), () => Listen(new[] { a }), UIKit.Ink, 26);
            }
            UIKit.Button(screen, "Настроить всё", new Vector2(0.5f, 0), new Vector2(-690, 80), new Vector2(380, 96), () => Listen(WizardOrder), new Color(0.2f, 0.3f, 0.5f, 0.95f), 32);
            UIKit.Button(screen, "Тест вибрации", new Vector2(0.5f, 0), new Vector2(-270, 80), new Vector2(380, 96), () => Haptics.Ko(), UIKit.Ink, 32);
            UIKit.Button(screen, "Сбросить", new Vector2(0.5f, 0), new Vector2(150, 80), new Vector2(380, 96), () => { PadInput.ResetDefaults(); Refresh(); }, new Color(0.4f, 0.1f, 0.1f, 0.95f), 32);
            UIKit.Button(screen, "Готово", new Vector2(0.5f, 0), new Vector2(560, 80), new Vector2(360, 96), () => { StopListen(); Save.Write(); OnDone?.Invoke(); }, UIKit.Accent, 36);

            promptBox = UIKit.Box(screen, "Prompt", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1200, 300));
            UIKit.Img(promptBox, new Color(0.04f, 0.03f, 0.06f, 0.96f), null, true);
            prompt = UIKit.Label(UIKit.Box(promptBox, "P", new Vector2(0.5f, 0.5f), new Vector2(0, 40), new Vector2(1100, 180)), "", 40, UIKit.Paper, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIKit.Button(promptBox, "Пропустить", new Vector2(0.5f, 0), new Vector2(-170, 60), new Vector2(300, 80), Skip, UIKit.Ink, 30);
            UIKit.Button(promptBox, "Очистить", new Vector2(0.5f, 0), new Vector2(170, 60), new Vector2(300, 80), () => { if (Current(out var a)) PadInput.Bind(a, -1); Next(); }, new Color(0.4f, 0.1f, 0.1f, 0.95f), 30);
            promptBox.gameObject.SetActive(false);
        }

        public void Refresh()
        {
            foreach (var r in refresh) r();
            int pads = PadInput.ConnectedCount;
            string name = "";
            try { foreach (var s in Input.GetJoystickNames()) if (!string.IsNullOrEmpty(s)) { name = s; break; } } catch { }
            status.text = pads == 0
                ? "Геймпад не найден — подключите по Bluetooth или USB. Курки и крестовину назначьте кнопкой «Настроить всё»."
                : name + (Haptics.PadHasRumble ? "  •  вибрация геймпада: есть" : "  •  вибрация геймпада: система не дала доступ (будет вибрировать телефон)");
        }

        void OnEnable() { Haptics.Light(); if (status != null) Refresh(); }
        void OnDisable() { StopListen(); }

        bool Current(out PadAction a)
        {
            a = default;
            if (listenIndex < 0 || listenIndex >= queue.Count) return false;
            a = queue[listenIndex];
            return true;
        }

        void Listen(PadAction[] actions)
        {
            queue.Clear(); queue.AddRange(actions);
            listenIndex = -1;
            Listening = true;
            if (EventSystem.current != null) { EventSystem.current.sendNavigationEvents = false; EventSystem.current.SetSelectedGameObject(null); }
            promptBox.gameObject.SetActive(true);
            promptBox.SetAsLastSibling();
            Next();
        }

        void Next()
        {
            listenIndex++;
            if (!Current(out var a)) { StopListen(); Refresh(); return; }
            prompt.text = "Нажмите кнопку, курок или крестовину для:\n<color=#FFD15A>" + PadInput.ActionNames[(int)a] + "</color>\n<size=26>сейчас: " + PadInput.BindText(a) + "</size>";
            waitRelease = true;
            listenUntil = Time.unscaledTime + 10f;
            Refresh();
        }

        void Skip() { Next(); }

        void StopListen()
        {
            if (!Listening) return;
            Listening = false;
            queue.Clear(); listenIndex = -1;
            if (promptBox != null) promptBox.gameObject.SetActive(false);
            if (EventSystem.current != null) EventSystem.current.sendNavigationEvents = true;
        }

        void Update()
        {
            if (!Listening) { if (Time.frameCount % 60 == 0) Refresh(); return; }
            if (Input.GetKeyDown(KeyCode.Escape)) { StopListen(); Refresh(); return; }
            if (Time.unscaledTime > listenUntil) { Next(); return; }
            if (waitRelease) { if (!PadInput.AnyHeld()) { waitRelease = false; PadInput.ProbeNewPress(); } return; }
            int code = PadInput.ProbeNewPress();
            if (code < 0 || !Current(out var a)) return;
            PadInput.Bind(a, code);
            Sfx.Play(SfxId.Click, 0.8f, 1.3f);
            Haptics.Light();
            Next();
        }
    }
}
