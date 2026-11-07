using System;
using System.Collections.Generic;
using UnityEngine;

namespace Kagegan
{
    [Serializable] public class LayoutEntry { public string id; public float x, y; }

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
        public float music = 0.55f, sfx = 0.9f;
        public bool showFps;
        public float buttonScale = 1f;
        public float buttonAlpha = 0.85f;
        public List<LayoutEntry> layout = new List<LayoutEntry>();
        public List<RunRecord> records = new List<RunRecord>();
        public int lastCharacter;
        public int lastDifficulty;
    }

    /// <summary>Offline save stored as JSON in PlayerPrefs.</summary>
    public static class Save
    {
        const string Key = "kagegan_save_v1";
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
            if (data.records == null) data.records = new List<RunRecord>();
        }

        public static void Write()
        {
            PlayerPrefs.SetString(Key, JsonUtility.ToJson(Data));
            PlayerPrefs.Save();
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
