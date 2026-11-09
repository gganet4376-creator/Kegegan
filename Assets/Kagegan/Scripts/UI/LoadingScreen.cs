using UnityEngine;
using UnityEngine.UI;

namespace Kagegan
{
    /// <summary>A separate overlay so a frame can be presented before synchronous world construction.</summary>
    public class LoadingScreen : MonoBehaviour
    {
        Canvas canvas;
        Text message;
        RectTransform spinner;

        public static LoadingScreen Create()
        {
            var canvas = UIKit.MakeCanvas("Loading", 100);
            DontDestroyOnLoad(canvas.gameObject);
            var screen = canvas.gameObject.AddComponent<LoadingScreen>();
            screen.canvas = canvas;
            var background = UIKit.FullPanel(canvas.transform, "Background", new Color(0.035f, 0.03f, 0.05f, 1f));
            background.GetComponent<Image>().raycastTarget = true;
            var title = UIKit.Box(background, "Title", new Vector2(0.5f, 0.5f), new Vector2(0f, 145f), new Vector2(800f, 120f));
            UIKit.Label(title, "KAGEGAN", 90, UIKit.Paper, TextAnchor.MiddleCenter, FontStyle.Bold);
            var ring = UIKit.Box(background, "Spinner", new Vector2(0.5f, 0.5f), new Vector2(0f, -5f), new Vector2(70f, 70f));
            var image = UIKit.Img(ring, UIKit.Accent, UIKit.Ring);
            image.type = Image.Type.Filled;
            image.fillMethod = Image.FillMethod.Radial360;
            image.fillAmount = 0.72f;
            screen.spinner = ring;
            var label = UIKit.Box(background, "Status", new Vector2(0.5f, 0.5f), new Vector2(0f, -105f), new Vector2(1000f, 75f));
            screen.message = UIKit.Label(label, "", 32, UIKit.Paper);
            canvas.gameObject.SetActive(false);
            return screen;
        }

        public void Show(string text)
        {
            message.text = text;
            canvas.gameObject.SetActive(true);
        }

        public void Hide() { canvas.gameObject.SetActive(false); }

        void Update()
        {
            spinner.Rotate(0f, 0f, -140f * Time.unscaledDeltaTime);
        }
    }
}
