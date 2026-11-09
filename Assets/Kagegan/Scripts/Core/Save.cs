using System;
using System.Collections.Generic;
using UnityEngine;

namespace Kagegan
{
    [Serializable] public class LayoutEntry { public string id; public float x, y; }
    [Serializable]
    public class FighterSkills
    {
        public int character, points, health, stamina, chakra;
        public int bossWins; // bit (boss * 3 + difficulty): first win already paid out a skill point
    }

    [Serializable] public class PadBind { public int action; public int code; } // code: 0..19 joystick button, 100+axis*2(+1 negative)

    [Serializable]
    public class RunRecord
    {
        public int character, difficulty;
        public bool cleared;
        public float bestTime;
        public int bestRank = -1; // 0=S,1=A,2=B,3=C
        public int maxCombo;
    }

    [Serializable]
    public class SaveData
    {
        public int version = 1;
        public int unlockedDifficulty;      // highest difficulty index available
        public int bossesUnlocked;          // bitmask of bosses reached (boss rush replay)
        public int graphics = -1;           // -1 = not chosen yet (auto-detect)
        public bool dynamicResolution = true;
        public int targetFps = 30;
        public bool vibration = true;
        public float vibeStrength = 1f;     // phone/controller haptics strength 0.5..1.5
        public bool padRumble = true;       // rumble in the gamepad itself
        public List<PadBind> padBinds = new List<PadBind>(); // custom gamepad mapping (empty = defaults)
        public bool padHintShown;
        public float music = 0.55f, sfx = 0.9f;
        public bool showFps;
        public bool perfDetail;             // with showFps: frame ms, 1% low, CPU/GPU, draw calls, heat
        public float shake = 1f;
        public int weather;                 // 0 = auto (per arena), 1 = random, 2.. = fixed WeatherType - 2
        public bool safeAreaPadding = true;
        public float buttonScale = 1f;
        public float buttonAlpha = 0.85f;
        public int grassLevel = 1;          // 0 sparse, 1 normal, 2 dense, 3 max (density + draw distance)
        public List<LayoutEntry> layout = new List<LayoutEntry>();
        public int layoutVersion;           // bumped when the default button layout changes; older custom layouts are reset
        public List<RunRecord> records = new List<RunRecord>();
        public List<FighterSkills> skills = new List<FighterSkills>();
        public bool skillHistoryMigrated;
        public bool bossWinsMigrated;
        public int lastCharacter;
        public int lastDifficulty;
        public bool tutorialDone;           // first-fight hints were shown (settings can turn them back on)
    }

    /// <summary>Offline save stored as JSON in PlayerPrefs.</summary>
    public static class Save
    {
        const string Key = "kagegan_save_v1";
        public const int LayoutVersion = 2;
        static SaveData data;

        public static SaveData Data
        {
            get { if (data == null) Load(); return data; }
        }

        public static void Load()
        {
            try
            {
                string json = PlayerPrefs.GetString(Key, "");
                data = string.IsNullOrEmpty(json) ? new SaveData() : JsonUtility.FromJson<SaveData>(json);
            }
            catch (Exception e)
            {
                Debug.LogWarning("Kagegan: save corrupted, resetting. " + e.Message);
                data = null;
            }
            if (data == null) data = new SaveData();
            if (data.layout == null) data.layout = new List<LayoutEntry>();
            if (data.layoutVersion < LayoutVersion) { data.layout.Clear(); data.layoutVersion = LayoutVersion; }
            if (data.padBinds == null) data.padBinds = new List<PadBind>();
            if (data.records == null) data.records = new List<RunRecord>();
            if (data.skills == null) data.skills = new List<FighterSkills>();
            foreach (var skill in data.skills)
            {
                if (skill == null) continue;
                skill.points = Mathf.Max(0, skill.points);
                skill.health = Mathf.Clamp(skill.health, 0, 3);
                skill.stamina = Mathf.Clamp(skill.stamina, 0, 3);
                skill.chakra = Mathf.Clamp(skill.chakra, 0, 3);
            }
            if (!data.skillHistoryMigrated)
            {
                // Existing completed Boss Rush records earned one point per boss.
                foreach (var record in data.records)
                {
                    if (record == null || !record.cleared || record.character < 0 || record.character > (int)CharacterId.Reina) continue;
                    var skills = Skills((CharacterId)record.character);
                    skills.points = Mathf.Min(9, skills.points + 3);
                }
                data.skillHistoryMigrated = true;
                Write();
            }
            if (!data.bossWinsMigrated)
            {
                // cleared Boss Rush runs already paid for their three bosses at that difficulty
                foreach (var record in data.records)
                {
                    if (record == null || !record.cleared || record.character < 0 || record.character > (int)CharacterId.Reina) continue;
                    if (record.difficulty < 0 || record.difficulty > 2) continue;
                    var skills = Skills((CharacterId)record.character);
                    for (int b = 0; b < 3; b++) skills.bossWins |= 1 << (b * 3 + record.difficulty);
                }
                // anyone who already beat the Moon Sovereign gets the secret boss
                foreach (var record in data.records)
                    if (record != null && record.cleared) { data.bossesUnlocked |= 1 << 3; break; }
                data.bossWinsMigrated = true;
                Write();
            }
        }

        public static void Write()
        {
            PlayerPrefs.SetString(Key, JsonUtility.ToJson(Data));
            PlayerPrefs.Save();
        }

        public static FighterSkills Skills(CharacterId character)
        {
            foreach (var skill in Data.skills)
                if (skill != null && skill.character == (int)character) return skill;
            var created = new FighterSkills { character = (int)character };
            Data.skills.Add(created);
            return created;
        }

        /// <summary>Marks the boss as beaten by this fighter on this difficulty. True only the first time.</summary>
        public static bool FirstBossWin(CharacterId character, int boss, Difficulty d)
        {
            var skills = Skills(character);
            int bit = 1 << (Mathf.Clamp(boss, 0, 9) * 3 + Mathf.Clamp((int)d, 0, 2));
            if ((skills.bossWins & bit) != 0) return false;
            skills.bossWins |= bit;
            return true;
        }

        public static RunRecord Record(CharacterId c, Difficulty d)
        {
            foreach (var r in Data.records)
                if (r.character == (int)c && r.difficulty == (int)d) return r;
            var nr = new RunRecord { character = (int)c, difficulty = (int)d };
            Data.records.Add(nr);
            return nr;
        }

        public static bool TryGetLayout(string id, out Vector2 pos)
        {
            foreach (var e in Data.layout)
                if (e.id == id) { pos = new Vector2(e.x, e.y); return true; }
            pos = default;
            return false;
        }

        public static void SetLayout(string id, Vector2 pos)
        {
            foreach (var e in Data.layout)
                if (e.id == id) { e.x = pos.x; e.y = pos.y; return; }
            Data.layout.Add(new LayoutEntry { id = id, x = pos.x, y = pos.y });
        }

        public static void ResetAll()
        {
            data = new SaveData();
            Write();
        }
    }
}
