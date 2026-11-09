using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Kagegan
{
    /// <summary>
    /// Gamepad / keyboard menu navigation: when a stick, D-pad or arrow key is used and nothing is selected, the top
    /// button of the front-most menu screen gets focus; a gold frame marks the focused control; B (Cancel) presses
    /// that screen's back button. Scroll views follow the focus. Touch input hides the frame again.
    /// </summary>
    public class PadFocus : MonoBehaviour
    {
        static readonly string[] BackNames = { "Btn_Готово", "Btn_Назад", "Btn_Продолжить", "Btn_В меню" };
        RectTransform frame;
        GameObject lastSel;
        bool padMode;
        float navCooldown;

        public static void Attach(GameObject eventSystem)
        {
            if (eventSystem.GetComponent<PadFocus>() == null) eventSystem.AddComponent<PadFocus>();
        }

        void Update()
        {
            var es = EventSystem.current;
            if (es == null) return;
            bool navInput = false, cancel = false;
            try
            {
                navInput = Mathf.Abs(Input.GetAxisRaw("Horizontal")) > 0.5f || Mathf.Abs(Input.GetAxisRaw("Vertical")) > 0.5f;
                cancel = Input.GetKeyDown(KeyCode.JoystickButton1);
                for (int b = 0; b < 20 && !navInput; b++) if (Input.GetKeyDown(KeyCode.JoystickButton0 + b)) navInput = true;
            }
            catch { return; }
            if (Input.touchCount > 0 || Input.GetMouseButtonDown(0)) { padMode = false; if (frame != null) frame.gameObject.SetActive(false); }
            if (navInput) padMode = true;
            if (!padMode) { Hide(); return; }
            if (PadRemap.Listening) { Hide(); return; }

            var sel = es.currentSelectedGameObject;
            var front = FrontScreen();
            if (front == null) { Hide(); return; }
            bool bad = sel == null || !sel.activeInHierarchy || (sel.GetComponent<Selectable>() is Selectable s && !s.IsInteractable())
                       || front != null && !sel.transform.IsChildOf(front);
            if (bad && front != null)
            {
                var pick = FirstIn(front);
                if (pick != null) { es.SetSelectedGameObject(pick.gameObject); sel = pick.gameObject; }
            }
            if (cancel && front != null)
            {
                foreach (var n in BackNames)
                {
                    var t = FindDeep(front, n);
                    if (t != null && t.gameObject.activeInHierarchy && t.GetComponent<Button>() is Button b && b.IsInteractable()) { b.onClick.Invoke(); break; }
                }
            }
            if (sel != null) Follow(sel.transform as RectTransform);
        }

        void Hide() { if (frame != null && frame.gameObject.activeSelf) frame.gameObject.SetActive(false); }

        static Transform FindDeep(Transform t, string name)
        {
            if (t.name == name) return t;
            for (int i = 0; i < t.childCount; i++) { var r = FindDeep(t.GetChild(i), name); if (r != null) return r; }
            return null;
        }

        /// <summary>The visible menu screen drawn last (screens are siblings under the menu canvas).</summary>
        static Transform FrontScreen()
        {
            Transform best = null;
            int bestOrder = int.MinValue, bestIdx = -1;
            foreach (var sa in FindObjectsByType<ScreenAppear>(FindObjectsSortMode.None))
            {
                if (!sa.isActiveAndEnabled) continue;
                var c = sa.GetComponentInParent<Canvas>();
                int order = c != null ? c.sortingOrder : 0, idx = sa.transform.GetSiblingIndex();
                if (order > bestOrder || order == bestOrder && idx > bestIdx) { best = sa.transform; bestOrder = order; bestIdx = idx; }
            }
            return best;
        }

        static Selectable FirstIn(Transform screen)
        {
            Selectable best = null;
            float bestScore = float.MinValue;
            foreach (var s in screen.GetComponentsInChildren<Selectable>(false))
            {
                if (!s.IsInteractable()) continue;
                Vector3 p = s.transform.position;
                float score = p.y * 4f - p.x;           // top-most, then left-most
                if (s.GetComponentInParent<ScrollRect>() != null) score -= 0.001f;
                if (score > bestScore) { bestScore = score; best = s; }
            }
            return best;
        }

        void Follow(RectTransform target)
        {
            if (target == null) return;
            if (frame == null) BuildFrame();
            if (frame.parent != target) { frame.SetParent(target, false); frame.offsetMin = new Vector2(-7, -7); frame.offsetMax = new Vector2(7, 7); }
            frame.SetAsLastSibling();
            if (!frame.gameObject.activeSelf) frame.gameObject.SetActive(true);
            float a = 0.65f + 0.35f * Mathf.Sin(Time.unscaledTime * 6f);
            foreach (var img in frame.GetComponentsInChildren<Image>()) img.color = new Color(UIKit.Gold.r, UIKit.Gold.g, UIKit.Gold.b, a);
            if (target.gameObject != lastSel) { lastSel = target.gameObject; Sfx.Play(SfxId.Click, 0.35f, 1.5f); ScrollTo(target); }
        }

        static void ScrollTo(RectTransform target)
        {
            var sr = target.GetComponentInParent<ScrollRect>();
            if (sr == null || sr.content == null || sr.viewport == null) return;
            Canvas.ForceUpdateCanvases();
            var content = sr.content; var view = sr.viewport;
            Vector3[] tc = new Vector3[4], vc = new Vector3[4];
            target.GetWorldCorners(tc); view.GetWorldCorners(vc);
            float scale = content.lossyScale.y > 0 ? content.lossyScale.y : 1f;
            float shift = 0f;
            if (tc[1].y > vc[1].y) shift = (tc[1].y - vc[1].y) / scale + 20f;   // above: move content down
            else if (tc[0].y < vc[0].y) shift = (tc[0].y - vc[0].y) / scale - 20f; // below: move content up
            if (shift != 0f) content.anchoredPosition -= new Vector2(0, shift);
        }

        void BuildFrame()
        {
            frame = UIKit.Rect(transform, "PadFocus", Vector2.zero, Vector2.one);
            const float w = 5f;
            Bar(new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, -w), Vector2.zero);
            Bar(new Vector2(0, 0), new Vector2(1, 0), Vector2.zero, new Vector2(0, w));
            Bar(new Vector2(0, 0), new Vector2(0, 1), Vector2.zero, new Vector2(w, 0));
            Bar(new Vector2(1, 0), new Vector2(1, 1), new Vector2(-w, 0), Vector2.zero);
        }

        void Bar(Vector2 aMin, Vector2 aMax, Vector2 oMin, Vector2 oMax)
        {
            var rt = UIKit.Rect(frame, "B", aMin, aMax, oMin, oMax);
            var i = rt.gameObject.AddComponent<Image>();
            i.raycastTarget = false;
            i.color = UIKit.Gold;
        }
    }
}
