using UnityEngine;
using UnityEngine.UI;

namespace Kagegan
{
    /// <summary>In-battle HUD: player bars (HP/stamina/chakra/ultimate), target or boss bar with phases, wave info,
    /// combo counter, popups, banners, character lines, training stats and FPS.</summary>
    public class Hud : MonoBehaviour
    {
        static Hud inst;
        public RectTransform Root;
        Image hp, hpLag, stamina, chakra, rage, targetHp, targetLag, bossHp, bossLag;
        Text playerName, targetName, bossName, waveText, comboText, popupText, bannerText, lineText, fpsText, perfText, statsText, storedText, enemyCount;
        RectTransform targetPanel, bossPanel, comboRt, statsPanel, perfPanel;
        RawImage playerPortrait, bossPortrait;
        Image playerBadge;
        Text hpText, bossHpText;
        Image[] phasePips = new Image[3];
        RectTransform escapePanel;
        Image escapeFill;
        Text escapeText;
        float popupT, bannerT, lineT, comboShow;
        int lastCombo;
        public Fighter Player, Boss;
        public bool TrainingMode;
        float dpsWindowStart;
        float dpsDamage;
        float lastDealt;

        public static void Popup(string text, Color c) { if (inst != null) inst.ShowPopup(text, c); }
        float staminaFlash;
        /// <summary>Guard broken: the stamina bar flashes red.</summary>
        public static void FlashStamina() { if (inst != null) inst.staminaFlash = 1f; }
        public static void Banner(string text, Color c) { if (inst != null) inst.ShowBanner(text, c); }
        public static void Line(string text) { if (inst != null) inst.ShowLine(text); }
        public static Hud Inst => inst;

        public void Build(Transform canvas, System.Action onPause)
        {
            inst = this;
            Root = UIKit.Rect(canvas, "HUD", Vector2.zero, Vector2.one);
            Root.gameObject.AddComponent<SafeArea>();

            // ---- player panel (top-left) ----
            var pause = UIKit.Button(Root, "", new Vector2(0, 1), new Vector2(54, -58), new Vector2(84, 84), onPause, UIKit.Ink, 34);
            UIKit.IconImage(pause.transform, "pause", Vector2.zero, 46, Color.white);
            var portrait = UIKit.Box(Root, "Portrait", new Vector2(0, 1), new Vector2(166, -64), new Vector2(104, 104));
            playerBadge = UIKit.Img(portrait, new Color(1f, 0.7f, 0.3f), UIKit.Circle);
            playerPortrait = MakePortrait(portrait, 96);
            var ring = UIKit.Box(portrait, "Ring", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(104, 104));
            UIKit.Img(ring, new Color(1f, 0.85f, 0.5f), UIKit.Ring);
            playerName = UIKit.Label(UIKit.Box(Root, "PName", new Vector2(0, 1), new Vector2(236, -26), new Vector2(400, 40), new Vector2(0, 0.5f)), "", 30, Color.white, TextAnchor.MiddleLeft, FontStyle.Bold);
            hpLag = UIKit.Bar(Root, new Vector2(0, 1), new Vector2(236, -58), new Vector2(520, 30), new Color(1f, 0.9f, 0.6f), null, new Vector2(0, 0.5f));
            hp = UIKit.Bar(Root, new Vector2(0, 1), new Vector2(236, -58), new Vector2(520, 30), new Color(0.88f, 0.18f, 0.18f), new Color(0, 0, 0, 0), new Vector2(0, 0.5f));
            hpText = UIKit.Label(UIKit.Box(Root, "HpText", new Vector2(0, 1), new Vector2(236, -58), new Vector2(510, 30), new Vector2(0, 0.5f)), "", 22, Color.white, TextAnchor.MiddleRight, FontStyle.Bold);
            stamina = UIKit.Bar(Root, new Vector2(0, 1), new Vector2(236, -86), new Vector2(250, 16), new Color(0.55f, 0.85f, 0.3f), null, new Vector2(0, 0.5f));
            chakra = UIKit.Bar(Root, new Vector2(0, 1), new Vector2(506, -86), new Vector2(250, 16), new Color(0.3f, 0.6f, 1f), null, new Vector2(0, 0.5f));
            rage = UIKit.Bar(Root, new Vector2(0, 1), new Vector2(236, -108), new Vector2(520, 12), new Color(1f, 0.6f, 0.1f), null, new Vector2(0, 0.5f));
            storedText = UIKit.Label(UIKit.Box(Root, "Stored", new Vector2(0, 1), new Vector2(236, -140), new Vector2(520, 36), new Vector2(0, 0.5f)), "", 26, new Color(0.85f, 0.7f, 1f), TextAnchor.MiddleLeft);
            waveText = UIKit.Label(UIKit.Box(Root, "Wave", new Vector2(0, 1), new Vector2(30, -180), new Vector2(760, 40), new Vector2(0, 0.5f)), "", 28, UIKit.Paper, TextAnchor.MiddleLeft);
            enemyCount = UIKit.Label(UIKit.Box(Root, "Enemies", new Vector2(0, 1), new Vector2(30, -216), new Vector2(600, 36), new Vector2(0, 0.5f)), "", 26, new Color(1f, 0.8f, 0.7f), TextAnchor.MiddleLeft);

            // ---- target panel (top-center-right, like the original) ----
            targetPanel = UIKit.Box(Root, "Target", new Vector2(0.5f, 1), new Vector2(280, -40), new Vector2(560, 70));
            targetName = UIKit.Label(UIKit.Box(targetPanel, "TName", new Vector2(0, 1), new Vector2(0, -14), new Vector2(560, 34), new Vector2(0, 0.5f)), "", 28, Color.white, TextAnchor.MiddleLeft, FontStyle.Bold);
            targetLag = UIKit.Bar(targetPanel, new Vector2(0, 0), new Vector2(0, 18), new Vector2(560, 22), new Color(1f, 0.9f, 0.6f), null, new Vector2(0, 0.5f));
            targetHp = UIKit.Bar(targetPanel, new Vector2(0, 0), new Vector2(0, 18), new Vector2(560, 22), new Color(0.85f, 0.15f, 0.15f), new Color(0, 0, 0, 0), new Vector2(0, 0.5f));

            // ---- boss bar (bottom-center, wide) ----
            bossPanel = UIKit.Box(Root, "Boss", new Vector2(0.5f, 1), new Vector2(0, -130), new Vector2(980, 80));
            bossName = UIKit.Label(UIKit.Box(bossPanel, "BName", new Vector2(0.5f, 1), new Vector2(0, -14), new Vector2(980, 38)), "", 32, UIKit.Gold, TextAnchor.MiddleCenter, FontStyle.Bold);
            bossLag = UIKit.Bar(bossPanel, new Vector2(0.5f, 0), new Vector2(0, 18), new Vector2(980, 26), new Color(1f, 0.9f, 0.6f));
            bossHp = UIKit.Bar(bossPanel, new Vector2(0.5f, 0), new Vector2(0, 18), new Vector2(980, 26), new Color(0.75f, 0.12f, 0.35f), new Color(0, 0, 0, 0));
            bossHpText = UIKit.Label(UIKit.Box(bossPanel, "BossHpText", new Vector2(0.5f, 0), new Vector2(0, 18), new Vector2(960, 26)), "", 20, Color.white, TextAnchor.MiddleRight, FontStyle.Bold);
            var bp = UIKit.Box(bossPanel, "BossPortrait", new Vector2(0, 0.5f), new Vector2(-62, 6), new Vector2(96, 96));
            UIKit.Img(bp, new Color(0.25f, 0.1f, 0.2f), UIKit.Circle);
            bossPortrait = MakePortrait(bp, 88);
            UIKit.Img(UIKit.Box(bp, "Ring", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(96, 96)), UIKit.Gold, UIKit.Ring);
            for (int i = 0; i < 3; i++)
            {
                var pip = UIKit.Box(bossPanel, "Pip", new Vector2(0.5f, 0), new Vector2(-30 + i * 30, -8), new Vector2(22, 22));
                phasePips[i] = UIKit.Img(pip, Color.white, UIKit.Circle);
            }
            bossPanel.gameObject.SetActive(false);

            // ---- centre texts ----
            comboRt = UIKit.Box(Root, "Combo", new Vector2(0.5f, 0.5f), new Vector2(-560, 120), new Vector2(360, 120));
            comboText = UIKit.Label(comboRt, "", 62, UIKit.Gold, TextAnchor.MiddleCenter, FontStyle.BoldAndItalic);
            popupText = UIKit.Label(UIKit.Box(Root, "Popup", new Vector2(0.5f, 0.5f), new Vector2(0, 230), new Vector2(1200, 80)), "", 50, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold);
            bannerText = UIKit.Label(UIKit.Box(Root, "Banner", new Vector2(0.5f, 0.5f), new Vector2(0, 60), new Vector2(1600, 140)), "", 86, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold);
            lineText = UIKit.Label(UIKit.Box(Root, "Line", new Vector2(0.5f, 1), new Vector2(0, -230), new Vector2(1300, 50)), "", 32, UIKit.Paper, TextAnchor.MiddleCenter, FontStyle.Bold);
            fpsText = UIKit.Label(UIKit.Box(Root, "Fps", new Vector2(1, 1), new Vector2(-120, -30), new Vector2(220, 40)), "", 26, new Color(0.7f, 1f, 0.7f), TextAnchor.MiddleRight);
            // detailed profiler block (settings: "Показывать FPS" = "Подробно"), top-right under the boss bar
            perfPanel = UIKit.Box(Root, "Perf", new Vector2(1, 1), new Vector2(-20, -180), new Vector2(760, 190), new Vector2(1, 1));
            UIKit.Img(perfPanel, new Color(0f, 0f, 0f, 0.55f));
            perfText = UIKit.Label(UIKit.Box(perfPanel, "T", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(740, 180)), "", 22, new Color(0.75f, 1f, 0.75f), TextAnchor.UpperLeft);
            perfText.raycastTarget = false;
            perfPanel.gameObject.SetActive(false);

            // ---- training stats (like the original "ПОЛИГОН" overlay) ----
            statsPanel = UIKit.Box(Root, "Stats", new Vector2(0, 1), new Vector2(30, -260), new Vector2(760, 120), new Vector2(0, 1));
            statsText = UIKit.Label(statsPanel, "", 26, UIKit.Paper, TextAnchor.UpperLeft);
            statsPanel.gameObject.SetActive(false);

            // ---- struggle meter (grab hold / pinned under a mount) ----
            escapePanel = UIKit.Box(Root, "Escape", new Vector2(0.5f, 0.5f), new Vector2(0, -20), new Vector2(620, 90));
            escapeText = UIKit.Label(UIKit.Box(escapePanel, "EscText", new Vector2(0.5f, 1), new Vector2(0, -18), new Vector2(620, 40)), "ВЫРВИСЬ! Жми быстрее", 32, new Color(1f, 0.85f, 0.7f), TextAnchor.MiddleCenter, FontStyle.Bold);
            escapeFill = UIKit.Bar(escapePanel, new Vector2(0.5f, 0), new Vector2(0, 20), new Vector2(600, 34), new Color(1f, 0.6f, 0.25f));
            escapePanel.gameObject.SetActive(false);

            var lockBtn = UIKit.Button(Root, "Цель", new Vector2(1, 1), new Vector2(-70, -110), new Vector2(110, 70), () => { var pc = FindAnyObjectByType<PlayerController>(); if (pc != null) pc.CycleTarget(); }, UIKit.Ink, 28);
        }

        static RawImage MakePortrait(RectTransform parent, float size)
        {
            var maskRt = UIKit.Box(parent, "Mask", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(size, size));
            var mimg = maskRt.gameObject.AddComponent<Image>();
            mimg.sprite = UIKit.Circle;
            mimg.raycastTarget = false;
            var mask = maskRt.gameObject.AddComponent<Mask>();
            mask.showMaskGraphic = false;
            var pr = UIKit.Box(maskRt, "Portrait", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(size, size));
            var raw = pr.gameObject.AddComponent<RawImage>();
            raw.raycastTarget = false;
            raw.enabled = false;
            return raw;
        }

        static void SetPortrait(RawImage img, Fighter f)
        {
            if (img == null || f == null || f.Rig == null) return;
            var tex = Portraits.Get(f.DisplayName, f.Rig.Look);
            img.texture = tex;
            img.enabled = tex != null;
        }

        public void Bind(Fighter player)
        {
            Player = player;
            playerName.text = player.DisplayName;
            SetPortrait(playerPortrait, player);
            if (player.Kit != null) playerBadge.color = player.Kit.AuraColor;
            player.OnShout = Line;
        }

        public void SetBoss(Fighter boss)
        {
            Boss = boss;
            bossPanel.gameObject.SetActive(boss != null);
            if (boss != null) { bossName.text = boss.DisplayName; boss.OnShout = Line; bossHp.fillAmount = bossLag.fillAmount = 1f; SetPortrait(bossPortrait, boss); }
        }

        public void SetWave(string text) { waveText.text = text; }
        public void SetEnemyCount(string text) { enemyCount.text = text; }

        public void SetTraining(bool on)
        {
            TrainingMode = on;
            statsPanel.gameObject.SetActive(on);
            dpsWindowStart = Time.time; dpsDamage = 0f;
        }

        void ShowPopup(string t, Color c) { popupText.text = t; popupText.color = c; popupT = 1.2f; }
        void ShowBanner(string t, Color c) { bannerText.text = t; bannerText.color = c; bannerT = 2.2f; }
        void ShowLine(string t) { lineText.text = t; lineT = 3.2f; }

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            var p = Player;
            if (p != null)
            {
                hp.fillAmount = p.HpFrac;
                hpText.text = Mathf.CeilToInt(p.Hp) + " / " + Mathf.CeilToInt(p.MaxHp);
                hp.color = p.HpFrac < 0.25f ? Color.Lerp(new Color(0.88f, 0.18f, 0.18f), new Color(1f, 0.5f, 0.4f), Mathf.PingPong(Time.unscaledTime * 3f, 1f)) : new Color(0.88f, 0.18f, 0.18f);
                hpLag.fillAmount = Mathf.MoveTowards(hpLag.fillAmount, p.HpFrac, dt * 0.35f);
                if (hpLag.fillAmount < hp.fillAmount) hpLag.fillAmount = hp.fillAmount;
                stamina.fillAmount = p.Stamina / p.MaxStamina;
                staminaFlash = Mathf.Max(0f, staminaFlash - dt * 0.8f);
                stamina.color = staminaFlash > 0f ? Color.Lerp(new Color(0.55f, 0.85f, 0.3f), new Color(1f, 0.25f, 0.2f), staminaFlash * Mathf.PingPong(Time.unscaledTime * 8f, 1f) * 0.6f + staminaFlash * 0.4f) : new Color(0.55f, 0.85f, 0.3f);
                chakra.fillAmount = p.Chakra / p.MaxChakra;
                rage.fillAmount = p.Rage / 100f;
                rage.color = p.Rage >= 100f ? Color.Lerp(new Color(1f, 0.6f, 0.1f), Color.white, Mathf.PingPong(Time.unscaledTime * 3f, 1f)) : new Color(1f, 0.6f, 0.1f);
                var mk = p.Kit as MugenKit;
                storedText.text = mk != null && mk.Stored > 0f ? "Накоплено: " + Mathf.RoundToInt(mk.Stored) : "";

                // combo
                int combo = p.ComboDealt;
                if (combo >= 2)
                {
                    comboText.text = combo + " УДАРОВ";
                    if (combo != lastCombo) { comboShow = 1f; comboRt.localScale = Vector3.one * 1.25f; }
                }
                lastCombo = combo;
                comboRt.localScale = Vector3.Lerp(comboRt.localScale, Vector3.one, dt * 10f);
                comboShow -= dt * 0.6f;
                comboText.color = new Color(UIKit.Gold.r, UIKit.Gold.g, UIKit.Gold.b, Mathf.Clamp01(comboShow * 2f));

                // struggle meter
                bool struggling = p.CanBreakFree;
                if (escapePanel.gameObject.activeSelf != struggling) escapePanel.gameObject.SetActive(struggling);
                if (struggling)
                {
                    escapeFill.fillAmount = Mathf.MoveTowards(escapeFill.fillAmount, p.EscapeMeter, dt * 6f);
                    escapeFill.color = Color.Lerp(new Color(1f, 0.45f, 0.2f), new Color(0.5f, 1f, 0.55f), p.EscapeMeter);
                    escapePanel.localScale = Vector3.one * (1f + 0.03f * Mathf.Sin(Time.unscaledTime * 14f));
                }
                else escapeFill.fillAmount = 0f;

                // target
                var t = p.Target;
                bool showTarget = t != null && t.Alive && t != Boss;
                targetPanel.gameObject.SetActive(showTarget);
                if (showTarget)
                {
                    targetName.text = t.DisplayName + "   " + Mathf.CeilToInt(t.Hp) + " / " + Mathf.CeilToInt(t.MaxHp);
                    targetHp.fillAmount = t.HpFrac;
                    targetLag.fillAmount = Mathf.Max(t.HpFrac, Mathf.MoveTowards(targetLag.fillAmount, t.HpFrac, dt * 0.5f));
                }

                if (TrainingMode)
                {
                    float dealt = p.Stats.damageDealt;
                    if (dealt > lastDealt) { dpsDamage += dealt - lastDealt; lastDealt = dealt; }
                    float window = Mathf.Max(1f, Time.time - dpsWindowStart);
                    if (window > 5f) { dpsWindowStart = Time.time - 2.5f; dpsDamage *= 0.5f; }
                    statsText.text = "ПОЛИГОН — чакра бесконечна, перезарядок нет\nУдар: " + p.LastHitDamage.ToString("0.0") + "   Серия: " + p.Stats.maxCombo +
                                     "   Урон/с: " + (dpsDamage / window).ToString("0.0") + (t != null ? "\n" + t.DisplayName + ": " + Mathf.CeilToInt(t.Hp) + " / " + Mathf.CeilToInt(t.MaxHp) : "");
                }
            }
            if (Boss != null)
            {
                bossHp.fillAmount = Boss.HpFrac;
                bossHpText.text = Mathf.CeilToInt(Boss.Hp) + " / " + Mathf.CeilToInt(Boss.MaxHp);
                bossLag.fillAmount = Mathf.Max(Boss.HpFrac, Mathf.MoveTowards(bossLag.fillAmount, Boss.HpFrac, dt * 0.25f));
                var bb = Boss.Brain as BossBrain;
                for (int i = 0; i < 3; i++) phasePips[i].color = bb != null && bb.Phase >= i ? Boss.Kit.AuraColor : new Color(0.2f, 0.2f, 0.2f, 0.8f);
                if (!Boss.Alive) bossPanel.gameObject.SetActive(false);
            }

            popupT -= dt; bannerT -= dt; lineT -= dt;
            popupText.canvasRenderer.SetAlpha(Mathf.Clamp01(popupT * 3f));
            bannerText.canvasRenderer.SetAlpha(Mathf.Clamp01(bannerT * 2f));
            lineText.canvasRenderer.SetAlpha(Mathf.Clamp01(lineT * 2f));
            // cached strings from PerfProbe: no new string per frame
            fpsText.text = PerfProbe.ShortText;
            string detail = PerfProbe.DetailText;
            if (perfPanel.gameObject.activeSelf != (detail.Length > 0)) perfPanel.gameObject.SetActive(detail.Length > 0);
            perfText.text = detail;
        }

        void OnDestroy() { if (inst == this) inst = null; }
    }
}
