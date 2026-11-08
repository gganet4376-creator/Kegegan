using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Kagegan
{
    /// <summary>All menu screens: main, character select, difficulty, boss replay, settings, controls layout editor,
    /// pause, defeat, results and the ending text.</summary>
    public class Menus : MonoBehaviour
    {
        Transform canvas;
        readonly List<RectTransform> screens = new List<RectTransform>();
        RectTransform main, charSel, diffSel, bossSel, settings, layout, pause, defeat, results, ending, confirm;
        Text charName, charTitle, charDesc, endingLine, resultsTitle, resultsRank, resultsBody, settingsBody;
        readonly Button[] charButtons = new Button[4];
        readonly Button[] diffButtons = new Button[3];
        readonly Button[] bossButtons = new Button[3];
        Button bossDiffButton, sparringButton;
        CharacterId selected;
        Difficulty bossDiff;
        BattleMode pendingMode;
        Action settingsBack;
        public bool SkipRequested { get; private set; }
        BattleResult lastResult;
        readonly List<Action> settingsRefresh = new List<Action>();

        public void Build(Transform c)
        {
            canvas = c;
            BuildMain();
            BuildCharSelect();
            BuildDifficulty();
            BuildBossSelect();
            BuildSettings();
            BuildLayout();
            BuildPause();
            BuildDefeat();
            BuildResults();
            BuildEnding();
            BuildConfirm();
            HideAll();
        }

        RectTransform Screen(string name, Color bg)
        {
            var rt = UIKit.FullPanel(canvas, name, bg);
            rt.gameObject.AddComponent<ScreenAppear>();
            screens.Add(rt);
            return rt;
        }

        public void HideAll() { foreach (var s in screens) s.gameObject.SetActive(false); }
        void Show(RectTransform s) { HideAll(); s.gameObject.SetActive(true); s.SetAsLastSibling(); }

        // ===================== main =====================
        void BuildMain()
        {
            main = Screen("Main", new Color(0, 0, 0, 0));
            main.GetComponent<Image>().raycastTarget = false;
            var side = UIKit.Rect(main, "Side", new Vector2(0, 0), new Vector2(0, 1), Vector2.zero, new Vector2(760, 0));
            UIKit.Img(side, new Color(0.05f, 0.04f, 0.07f, 0.72f));
            var title = UIKit.Label(UIKit.Box(side, "Title", new Vector2(0.5f, 1), new Vector2(0, -150), new Vector2(720, 170)), "KAGEGAN", 128, UIKit.Paper, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIKit.Label(UIKit.Box(side, "Sub", new Vector2(0.5f, 1), new Vector2(0, -250), new Vector2(720, 60)), "M O B I L E", 40, UIKit.Accent, TextAnchor.MiddleCenter, FontStyle.Bold);
            float y = -380;
            UIKit.Button(side, "Босс-раш: 3 волны", new Vector2(0.5f, 1), new Vector2(0, y), new Vector2(600, 110), () => { pendingMode = BattleMode.BossRush; ShowCharSelect(); }, UIKit.Accent, 44);
            UIKit.Button(side, "Повтор боссов", new Vector2(0.5f, 1), new Vector2(0, y - 135), new Vector2(600, 100), () => { pendingMode = BattleMode.BossOnly; ShowCharSelect(); });
            UIKit.Button(side, "Полигон", new Vector2(0.5f, 1), new Vector2(0, y - 255), new Vector2(600, 100), () => { pendingMode = BattleMode.Training; ShowCharSelect(); });
            UIKit.Button(side, "Настройки", new Vector2(0.5f, 1), new Vector2(0, y - 375), new Vector2(600, 100), () => ShowSettings(ShowMain));
            UIKit.Button(side, "Выход", new Vector2(0.5f, 1), new Vector2(0, y - 495), new Vector2(600, 90), Application.Quit, new Color(0.15f, 0.12f, 0.14f, 0.9f), 34);
            UIKit.Label(UIKit.Box(side, "Ver", new Vector2(0.5f, 0), new Vector2(0, 74), new Vector2(700, 40)), "Офлайн • v" + Application.version, 24, new Color(1, 1, 1, 0.5f));
            UIKit.Label(UIKit.Box(side, "Credits", new Vector2(0.5f, 0), new Vector2(0, 30), new Vector2(720, 50)), "Анимации, модели: Quaternius (CC0). Звуки, музыка, эффекты: OpenGameArt, Kenney (CC0). Небо: Poly Haven (CC0). Шрифт: Russo One (OFL). Иконки: game-icons.net — Lorc, Delapouite и др. (CC BY 3.0)", 17, new Color(1, 1, 1, 0.45f));
        }

        public void ShowMain() { Show(main); }

        // ===================== character select =====================
        void BuildCharSelect()
        {
            charSel = Screen("CharSelect", new Color(0, 0, 0, 0));
            charSel.GetComponent<Image>().raycastTarget = false;
            var left = UIKit.Rect(charSel, "Info", new Vector2(0, 0.18f), new Vector2(0, 1), new Vector2(30, 0), new Vector2(820, -30));
            UIKit.Img(left, new Color(0.05f, 0.04f, 0.07f, 0.75f));
            charName = UIKit.Label(UIKit.Box(left, "Name", new Vector2(0.5f, 1), new Vector2(0, -70), new Vector2(740, 100)), "", 80, UIKit.Paper, TextAnchor.MiddleLeft, FontStyle.Bold);
            charTitle = UIKit.Label(UIKit.Box(left, "Title", new Vector2(0.5f, 1), new Vector2(0, -145), new Vector2(740, 50)), "", 36, UIKit.Gold, TextAnchor.MiddleLeft);
            charDesc = UIKit.Label(UIKit.Box(left, "Desc", new Vector2(0.5f, 1), new Vector2(0, -420), new Vector2(740, 480)), "", 30, Color.white, TextAnchor.UpperLeft);
            for (int i = 0; i < charButtons.Length; i++)
            {
                int idx = i;
                charButtons[i] = UIKit.Button(charSel, Roster.Name((CharacterId)i), new Vector2(0, 0), new Vector2(160 + i * 250, 90), new Vector2(235, 120), () => SelectChar((CharacterId)idx), UIKit.Ink, 40);
            }
            UIKit.Button(charSel, "Назад", new Vector2(1, 0), new Vector2(-560, 90), new Vector2(240, 110), ShowMain, new Color(0.15f, 0.12f, 0.14f, 0.9f));
            UIKit.Button(charSel, "Далее", new Vector2(1, 0), new Vector2(-220, 90), new Vector2(340, 120), CharNext, UIKit.Accent, 46);
        }

        public void ShowCharSelect()
        {
            Show(charSel);
            SelectChar((CharacterId)Save.Data.lastCharacter);
        }

        void SelectChar(CharacterId id)
        {
            selected = id;
            charName.text = Roster.Name(id);
            charTitle.text = Roster.Title(id);
            charDesc.text = Roster.Description(id);
            for (int i = 0; i < charButtons.Length; i++) charButtons[i].GetComponent<Image>().color = i == (int)id ? UIKit.Accent : UIKit.Ink;
            GameManager.Inst.SetShowcase(id);
        }

        void CharNext()
        {
            Save.Data.lastCharacter = (int)selected;
            switch (pendingMode)
            {
                case BattleMode.BossRush: ShowDifficulty(); break;
                case BattleMode.BossOnly: ShowBossSelect(); break;
                default: GameManager.Inst.StartBattle(BattleMode.Training, selected, Difficulty.Novice); break;
            }
        }

        // ===================== difficulty =====================
        void BuildDifficulty()
        {
            diffSel = Screen("Difficulty", new Color(0.03f, 0.02f, 0.05f, 0.7f));
            UIKit.Label(UIKit.Box(diffSel, "T", new Vector2(0.5f, 1), new Vector2(0, -110), new Vector2(1400, 110)), "СЛОЖНОСТЬ", 80, UIKit.Paper, TextAnchor.MiddleCenter, FontStyle.Bold);
            for (int i = 0; i < 3; i++)
            {
                int idx = i;
                diffButtons[i] = UIKit.Button(diffSel, "", new Vector2(0.5f, 0.5f), new Vector2(-560 + i * 560, 30), new Vector2(520, 380), () => GameManager.Inst.StartBattle(BattleMode.BossRush, selected, (Difficulty)idx), UIKit.Ink, 36);
            }
            UIKit.Button(diffSel, "Назад", new Vector2(0.5f, 0), new Vector2(0, 110), new Vector2(300, 110), ShowCharSelect, new Color(0.15f, 0.12f, 0.14f, 0.9f));
        }

        public void ShowDifficulty()
        {
            Show(diffSel);
            string[] desc = { "Враги слабее и медленнее\nреагируют.", "Сбалансированный бой.\nВраги блокируют и парируют.", "Быстрые, точные враги,\nдополнительные противники." };
            for (int i = 0; i < 3; i++)
            {
                bool unlocked = i <= Save.Data.unlockedDifficulty;
                var rec = Save.Record(selected, (Difficulty)i);
                string recText = rec.cleared ? "\n\nПройдено  •  " + FormatTime(rec.bestTime) + "  •  ранг " + BattleResult.RankName(rec.bestRank) : "";
                UIKit.SetLabel(diffButtons[i], Names.Difficulty((Difficulty)i).ToUpperInvariant() + "\n\n" + (unlocked ? desc[i] : "Закрыто:\nпройдите «" + Names.Difficulty((Difficulty)(i - 1)) + "»") + recText);
                diffButtons[i].interactable = unlocked;
                diffButtons[i].GetComponent<Image>().color = unlocked ? (i == 0 ? new Color(0.2f, 0.35f, 0.25f, 0.92f) : i == 1 ? new Color(0.35f, 0.28f, 0.15f, 0.92f) : new Color(0.45f, 0.12f, 0.12f, 0.92f)) : new Color(0.1f, 0.1f, 0.1f, 0.8f);
            }
        }

        // ===================== boss replay =====================
        void BuildBossSelect()
        {
            bossSel = Screen("BossSelect", new Color(0.03f, 0.02f, 0.05f, 0.7f));
            UIKit.Label(UIKit.Box(bossSel, "T", new Vector2(0.5f, 1), new Vector2(0, -110), new Vector2(1400, 110)), "ПОВТОР БОССОВ", 80, UIKit.Paper, TextAnchor.MiddleCenter, FontStyle.Bold);
            for (int i = 0; i < 3; i++)
            {
                int idx = i;
                bossButtons[i] = UIKit.Button(bossSel, "", new Vector2(0.5f, 0.5f), new Vector2(-560 + i * 560, 40), new Vector2(520, 320), () => GameManager.Inst.StartBattle(BattleMode.BossOnly, selected, bossDiff, idx), UIKit.Ink, 34);
            }
            bossDiffButton = UIKit.Button(bossSel, "", new Vector2(0.5f, 0), new Vector2(-220, 110), new Vector2(400, 110), () =>
            {
                bossDiff = (Difficulty)(((int)bossDiff + 1) % (Save.Data.unlockedDifficulty + 1));
                UIKit.SetLabel(bossDiffButton, "Сложность: " + Names.Difficulty(bossDiff));
            });
            UIKit.Button(bossSel, "Назад", new Vector2(0.5f, 0), new Vector2(220, 110), new Vector2(300, 110), ShowCharSelect, new Color(0.15f, 0.12f, 0.14f, 0.9f));
        }

        public void ShowBossSelect()
        {
            Show(bossSel);
            bossDiff = (Difficulty)Mathf.Min(Save.Data.lastDifficulty, Save.Data.unlockedDifficulty);
            UIKit.SetLabel(bossDiffButton, "Сложность: " + Names.Difficulty(bossDiff));
            for (int i = 0; i < 3; i++)
            {
                bool open = (Save.Data.bossesUnlocked & (1 << i)) != 0;
                UIKit.SetLabel(bossButtons[i], open ? Roster.BossName(i) + "\n\n" + Battle.WaveName(i) : "???\n\nВстретьте босса\nв режиме «Босс-раш»");
                bossButtons[i].interactable = open;
            }
        }

        // ===================== settings =====================
        void BuildSettings()
        {
            settings = Screen("Settings", new Color(0.03f, 0.02f, 0.05f, 0.88f));
            UIKit.Label(UIKit.Box(settings, "T", new Vector2(0.5f, 1), new Vector2(0, -80), new Vector2(1400, 100)), "НАСТРОЙКИ", 70, UIKit.Paper, TextAnchor.MiddleCenter, FontStyle.Bold);
            float y = -150;
            int row = 0;
            Func<float> nextY = () => y - (row++) * 56;

            SettingRow("Качество графики", () => Names.Preset((GraphicsPreset)Save.Data.graphics), d =>
            {
                Save.Data.graphics = (Save.Data.graphics + d + 4) % 4;
                GraphicsQuality.Apply((GraphicsPreset)Save.Data.graphics);
            }, nextY());
            SettingRow("Погода", () => Names.WeatherSetting(Save.Data.weather), d => Save.Data.weather = (Save.Data.weather + d + WeatherSystem.SettingCount) % WeatherSystem.SettingCount, nextY());
            SettingRow("Трава (густота и дальность)", () => GrassBend.LevelName, d => { Save.Data.grassLevel = (Save.Data.grassLevel + d + 4) % 4; GrassBend.ApplyDistance(); }, nextY());
            SettingRow("Динамическое разрешение", () => Save.Data.dynamicResolution ? "Вкл" : "Выкл", d => { Save.Data.dynamicResolution = !Save.Data.dynamicResolution; GraphicsQuality.SetRenderScale(GraphicsQuality.BaseRenderScale, false); }, nextY());
            SettingRow("Частота кадров", () => Save.Data.targetFps + " FPS", d =>
            {
                var opts = GraphicsQuality.FpsOptions();
                int i = System.Array.IndexOf(opts, Save.Data.targetFps);
                i = i < 0 ? 1 : (i + d + opts.Length) % opts.Length;
                Save.Data.targetFps = opts[Mathf.Clamp(i, 0, opts.Length - 1)];
                Application.targetFrameRate = Save.Data.targetFps;
            }, nextY());
            SettingRow("Виброотдача", () => Save.Data.vibration ? "Вкл" : "Выкл", d => { Save.Data.vibration = !Save.Data.vibration; if (Save.Data.vibration) Haptics.Medium(); }, nextY());
            SettingRow("Музыка", () => Mathf.RoundToInt(Save.Data.music * 100) + "%", d => { Save.Data.music = Mathf.Clamp01(Save.Data.music + d * 0.1f); Sfx.RefreshVolume(); }, nextY());
            SettingRow("Звуки", () => Mathf.RoundToInt(Save.Data.sfx * 100) + "%", d => { Save.Data.sfx = Mathf.Clamp01(Save.Data.sfx + d * 0.1f); Sfx.Play(SfxId.Hit); }, nextY());
            SettingRow("Показывать FPS", () => Save.Data.showFps ? "Вкл" : "Выкл", d => Save.Data.showFps = !Save.Data.showFps, nextY());
            SettingRow("Тряска камеры", () => Mathf.RoundToInt(Save.Data.shake * 100) + "%", d => { Save.Data.shake = Mathf.Clamp(Save.Data.shake + d * 0.25f, 0f, 1.5f); CameraRig.ShakeScale = Save.Data.shake; CameraRig.Shake(0.4f); }, nextY());
            SettingRow("Отступ от краёв экрана", () => Save.Data.safeAreaPadding ? "Вкл" : "Выкл", d => Save.Data.safeAreaPadding = !Save.Data.safeAreaPadding, nextY());
            SettingRow("Размер кнопок", () => Mathf.RoundToInt(Save.Data.buttonScale * 100) + "%", d => { Save.Data.buttonScale = Mathf.Clamp(Save.Data.buttonScale + d * 0.05f, 0.7f, 1.6f); GameManager.Inst.Touch.ApplyScaleAlpha(); }, nextY());
            SettingRow("Прозрачность кнопок", () => Mathf.RoundToInt(Save.Data.buttonAlpha * 100) + "%", d => { Save.Data.buttonAlpha = Mathf.Clamp(Save.Data.buttonAlpha + d * 0.05f, 0.3f, 1f); GameManager.Inst.Touch.ApplyScaleAlpha(); }, nextY());

            UIKit.Button(settings, "Расположение кнопок", new Vector2(0.5f, 0), new Vector2(-560, 90), new Vector2(520, 100), ShowLayout, new Color(0.2f, 0.3f, 0.5f, 0.95f), 34);
            UIKit.Button(settings, "Сбросить прогресс", new Vector2(0.5f, 0), new Vector2(0, 90), new Vector2(480, 100), () => Show(confirm), new Color(0.4f, 0.1f, 0.1f, 0.95f), 34);
            UIKit.Button(settings, "Готово", new Vector2(0.5f, 0), new Vector2(520, 90), new Vector2(400, 100), () => { Save.Write(); settingsBack?.Invoke(); }, UIKit.Accent, 40);
        }

        void SettingRow(string label, Func<string> value, Action<int> change, float y)
        {
            var rowRt = UIKit.Box(settings, "Row", new Vector2(0.5f, 1), new Vector2(0, y), new Vector2(1300, 52));
            UIKit.Img(rowRt, new Color(1, 1, 1, 0.05f));
            UIKit.Label(UIKit.Box(rowRt, "L", new Vector2(0, 0.5f), new Vector2(30, 0), new Vector2(700, 80), new Vector2(0, 0.5f)), label, 34, Color.white, TextAnchor.MiddleLeft);
            var valueText = UIKit.Label(UIKit.Box(rowRt, "V", new Vector2(1, 0.5f), new Vector2(-260, 0), new Vector2(300, 80)), "", 34, UIKit.Gold, TextAnchor.MiddleCenter, FontStyle.Bold);
            Action refresh = () => valueText.text = value();
            UIKit.Button(rowRt, "<", new Vector2(1, 0.5f), new Vector2(-460, 0), new Vector2(90, 56), () => { change(-1); refresh(); }, UIKit.Ink, 34);
            UIKit.Button(rowRt, ">", new Vector2(1, 0.5f), new Vector2(-60, 0), new Vector2(90, 56), () => { change(1); refresh(); }, UIKit.Ink, 34);
            settingsRefresh.Add(refresh);
            refresh();
        }

        public void ShowSettings(Action back)
        {
            settingsBack = back;
            foreach (var r in settingsRefresh) r();
            Show(settings);
        }

        // ===================== layout editor =====================
        void BuildLayout()
        {
            layout = Screen("Layout", new Color(0, 0, 0, 0.45f));
            var top = UIKit.Box(layout, "Top", new Vector2(0.5f, 1), new Vector2(0, -70), new Vector2(1500, 110));
            UIKit.Img(top, UIKit.Ink);
            UIKit.Label(UIKit.Box(top, "Hint", new Vector2(0, 0.5f), new Vector2(30, 0), new Vector2(700, 100), new Vector2(0, 0.5f)), "Перетащите кнопки и джойстик", 32, Color.white, TextAnchor.MiddleLeft);
            UIKit.Button(top, "Сброс", new Vector2(1, 0.5f), new Vector2(-480, 0), new Vector2(220, 80), () => GameManager.Inst.Touch.ResetLayout(), new Color(0.4f, 0.15f, 0.15f, 1f), 32);
            UIKit.Button(top, "Готово", new Vector2(1, 0.5f), new Vector2(-180, 0), new Vector2(260, 80), () =>
            {
                var t = GameManager.Inst.Touch;
                t.SaveLayout();
                t.SetEditMode(false);
                t.SetVisible(GameManager.Inst.Battle != null && !TimeFx.Paused);
                ShowSettings(settingsBack);
            }, UIKit.Accent, 34);
        }

        public void ShowLayout()
        {
            Show(layout);
            var t = GameManager.Inst.Touch;
            t.SetVisible(true);
            t.Root.SetAsLastSibling();
            t.SetEditMode(true);
            layout.SetSiblingIndex(t.Root.GetSiblingIndex() - 1);
        }

        // ===================== pause / defeat =====================
        void BuildPause()
        {
            pause = Screen("Pause", new Color(0.02f, 0.02f, 0.04f, 0.75f));
            UIKit.Label(UIKit.Box(pause, "T", new Vector2(0.5f, 1), new Vector2(0, -150), new Vector2(1000, 120)), "ПАУЗА", 90, UIKit.Paper, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIKit.Button(pause, "Продолжить", new Vector2(0.5f, 0.5f), new Vector2(0, 160), new Vector2(560, 110), () => GameManager.Inst.Pause(false), UIKit.Accent, 44);
            UIKit.Button(pause, "Начать волну заново", new Vector2(0.5f, 0.5f), new Vector2(0, 30), new Vector2(560, 100), () => { TimeFx.Paused = false; GameManager.Inst.Retry(); });
            sparringButton = UIKit.Button(pause, "Спарринг вкл/выкл", new Vector2(0.5f, 0.5f), new Vector2(0, 30), new Vector2(560, 100), () => { GameManager.Inst.Battle?.ToggleSparring(); GameManager.Inst.Pause(false); });
            UIKit.Button(pause, "Настройки", new Vector2(0.5f, 0.5f), new Vector2(0, -90), new Vector2(560, 100), () => ShowSettings(ShowPause));
            UIKit.Button(pause, "Выйти в меню", new Vector2(0.5f, 0.5f), new Vector2(0, -210), new Vector2(560, 100), () => GameManager.Inst.ShowMenu(), new Color(0.3f, 0.1f, 0.1f, 0.95f));
        }

        public void ShowPause()
        {
            Show(pause);
            bool training = GameManager.Inst.Battle != null && GameManager.Inst.Battle.Mode == BattleMode.Training;
            sparringButton.gameObject.SetActive(training);
            pause.Find("Btn_Начать волну заново").gameObject.SetActive(!training);
        }

        void BuildDefeat()
        {
            defeat = Screen("Defeat", new Color(0.15f, 0.02f, 0.02f, 0.75f));
            UIKit.Label(UIKit.Box(defeat, "T", new Vector2(0.5f, 0.5f), new Vector2(0, 200), new Vector2(1200, 160)), "ПОРАЖЕНИЕ", 120, new Color(1f, 0.4f, 0.35f), TextAnchor.MiddleCenter, FontStyle.Bold);
            defeatInfo = UIKit.Label(UIKit.Box(defeat, "Info", new Vector2(0.5f, 0.5f), new Vector2(0, 95), new Vector2(1500, 90)), "", 32, UIKit.Paper);
            UIKit.Label(UIKit.Box(defeat, "S", new Vector2(0.5f, 0.5f), new Vector2(0, 20), new Vector2(1500, 60)), "Совет: блок в момент удара — парирование, рывок в момент удара — идеальное уклонение, лежачего врага можно оседлать кнопкой «Захват».", 26, new Color(1f, 1f, 1f, 0.75f));
            UIKit.Button(defeat, "Реванш", new Vector2(0.5f, 0.5f), new Vector2(-280, -110), new Vector2(500, 130), () => GameManager.Inst.Retry(), UIKit.Accent, 48);
            UIKit.Button(defeat, "В меню", new Vector2(0.5f, 0.5f), new Vector2(290, -110), new Vector2(420, 130), () => GameManager.Inst.ShowMenu());
        }

        Text defeatInfo;

        public void ShowDefeat()
        {
            Show(defeat);
            var b = GameManager.Inst.Battle;
            if (defeatInfo != null && b != null)
            {
                string where = b.Mode == BattleMode.Training ? "Полигон" : "Волна " + (b.Wave + 1) + " — " + Battle.WaveName(b.Wave) + (b.Boss != null && b.Boss.Alive ? ", босс: " + b.Boss.DisplayName + " (" + Mathf.RoundToInt(b.Boss.HpFrac * 100) + "% здоровья)" : "");
                var p = b.Player;
                defeatInfo.text = where + (p != null ? "\nНанесено урона: " + Mathf.RoundToInt(p.Stats.damageDealt) + "   Макс. серия: " + p.Stats.maxCombo : "");
            }
            Haptics.Heavy();
        }

        // ===================== results =====================
        void BuildResults()
        {
            results = Screen("Results", new Color(0.03f, 0.02f, 0.05f, 0.85f));
            resultsTitle = UIKit.Label(UIKit.Box(results, "T", new Vector2(0.5f, 1), new Vector2(0, -110), new Vector2(1600, 130)), "", 96, UIKit.Gold, TextAnchor.MiddleCenter, FontStyle.Bold);
            resultsRank = UIKit.Label(UIKit.Box(results, "Rank", new Vector2(0.5f, 0.5f), new Vector2(-480, 20), new Vector2(400, 400)), "", 300, UIKit.Accent, TextAnchor.MiddleCenter, FontStyle.BoldAndItalic);
            resultsBody = UIKit.Label(UIKit.Box(results, "Body", new Vector2(0.5f, 0.5f), new Vector2(240, 10), new Vector2(900, 560)), "", 34, Color.white, TextAnchor.MiddleLeft);
            UIKit.Button(results, "В меню", new Vector2(0.5f, 0), new Vector2(-260, 100), new Vector2(420, 110), () => GameManager.Inst.ShowMenu(), UIKit.Ink, 40);
            UIKit.Button(results, "Ещё раз", new Vector2(0.5f, 0), new Vector2(260, 100), new Vector2(420, 110), () =>
            {
                var r = lastResult;
                GameManager.Inst.ShowMenu();
                GameManager.Inst.StartBattle(r.Mode, r.Character, r.Difficulty, r.WaveReached);
            }, UIKit.Accent, 40);
        }

        public void ShowResults(BattleResult r)
        {
            lastResult = r;
            Show(results);
            resultsTitle.text = r.Cleared ? (r.Mode == BattleMode.BossRush ? "БОСС-РАШ ПРОЙДЕН!" : "ПОБЕДА!") : "ИТОГИ";
            resultsRank.text = BattleResult.RankName(r.Rank);
            resultsBody.text =
                Roster.Name(r.Character) + "  •  " + Names.Difficulty(r.Difficulty) + "\n\n" +
                "Время: " + FormatTime(r.Time) + "\n" +
                "Макс. серия: " + r.MaxCombo + "\n" +
                "Нанесено урона: " + Mathf.RoundToInt(r.DamageDealt) + "\n" +
                "Получено урона: " + Mathf.RoundToInt(r.DamageTaken) + "\n" +
                "Повержено: " + r.Kills + "   Добиваний: " + r.Finishers + "\n" +
                "Парирований: " + r.Parries + "   Идеальных уклонений: " + r.PerfectDodges +
                (r.Absorbs > 0 ? "\nПоглощено техник: " + r.Absorbs : "") + "\n" +
                "Средний FPS: " + Mathf.RoundToInt(r.AvgFps) +
                (string.IsNullOrEmpty(r.Unlocked) ? "" : "\n\n<color=#FFD25A>" + r.Unlocked + "</color>");
            resultsBody.supportRichText = true;
            Sfx.Play(r.Cleared ? SfxId.Ko : SfxId.Click, 0.8f);
        }

        // ===================== ending =====================
        void BuildEnding()
        {
            ending = Screen("Ending", new Color(0, 0, 0, 0));
            ending.GetComponent<Image>().raycastTarget = false;
            var band = UIKit.Rect(ending, "Band", new Vector2(0, 0), new Vector2(1, 0.22f));
            UIKit.Img(band, new Color(0, 0, 0, 0.55f), UIKit.Soft);
            endingLine = UIKit.Label(UIKit.Box(ending, "Line", new Vector2(0.5f, 0), new Vector2(0, 130), new Vector2(1700, 120)), "", 52, UIKit.Paper, TextAnchor.MiddleCenter, FontStyle.Italic);
            UIKit.Button(ending, "Пропустить", new Vector2(1, 1), new Vector2(-170, -70), new Vector2(280, 80), () => SkipRequested = true, UIKit.Ink, 30);
        }

        public void ShowEndingText() { SkipRequested = false; Show(ending); }
        public void SetEndingLine(string s) { endingLine.text = s; }

        // ===================== confirm reset =====================
        void BuildConfirm()
        {
            confirm = Screen("Confirm", new Color(0.05f, 0.02f, 0.02f, 0.92f));
            UIKit.Label(UIKit.Box(confirm, "T", new Vector2(0.5f, 0.5f), new Vector2(0, 150), new Vector2(1400, 200)), "Сбросить весь прогресс?\nРекорды и открытые сложности будут удалены.", 46, Color.white);
            UIKit.Button(confirm, "Да, сбросить", new Vector2(0.5f, 0.5f), new Vector2(-260, -80), new Vector2(440, 110), () =>
            {
                var keepGraphics = Save.Data.graphics;
                Save.ResetAll();
                Save.Data.graphics = keepGraphics;
                Save.Write();
                ShowSettings(settingsBack);
            }, new Color(0.5f, 0.1f, 0.1f, 1f));
            UIKit.Button(confirm, "Отмена", new Vector2(0.5f, 0.5f), new Vector2(260, -80), new Vector2(440, 110), () => ShowSettings(settingsBack));
        }

        public static string FormatTime(float t)
        {
            int m = Mathf.FloorToInt(t / 60f), s = Mathf.FloorToInt(t % 60f);
            return m + ":" + s.ToString("00");
        }
    }

    /// <summary>Fade + slight zoom when a menu screen opens.</summary>
    public class ScreenAppear : MonoBehaviour
    {
        CanvasGroup g;
        float t;
        void Awake() { g = gameObject.AddComponent<CanvasGroup>(); }
        void OnEnable() { t = 0f; if (g != null) g.alpha = 0f; transform.localScale = Vector3.one * 0.97f; }
        void Update()
        {
            if (t >= 1f) return;
            t = Mathf.Min(1f, t + Time.unscaledDeltaTime / 0.25f);
            float e = 1f - (1f - t) * (1f - t);
            g.alpha = e;
            transform.localScale = Vector3.one * Mathf.Lerp(0.97f, 1f, e);
        }
    }
}
