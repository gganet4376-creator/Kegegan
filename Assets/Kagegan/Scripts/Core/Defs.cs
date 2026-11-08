using UnityEngine;

namespace Kagegan
{
    public enum Team { Player, Enemy, Neutral }
    public enum Difficulty { Novice = 0, Warrior = 1, Legend = 2 }
    public enum CharacterId { Kaito = 0, Genryu = 1, Mugen = 2, Reina = 3 }
    public enum GraphicsPreset { Low = 0, Medium = 1, High = 2, Ultra = 3 }
    public enum ArenaId { Dojo, Forest, City, MoonTemple }

    public static class Names
    {
        public static string Difficulty(Difficulty d)
        {
            switch (d)
            {
                case Kagegan.Difficulty.Novice: return "Ученик";
                case Kagegan.Difficulty.Warrior: return "Воин";
                default: return "Легенда";
            }
        }

        public static string Preset(GraphicsPreset p)
        {
            switch (p)
            {
                case GraphicsPreset.Low: return "Низкая";
                case GraphicsPreset.Medium: return "Средняя";
                case GraphicsPreset.High: return "Высокая";
                default: return "Ультра (AAA)";
            }
        }

        public static string Weather(WeatherType w)
        {
            switch (w)
            {
                case WeatherType.Rain: return "Дождь";
                case WeatherType.Storm: return "Гроза";
                case WeatherType.Snow: return "Снег";
                case WeatherType.Fog: return "Туман";
                default: return "Ясно";
            }
        }

        /// <summary>Settings value: 0 = auto, 1 = random, 2.. = a fixed weather.</summary>
        public static string WeatherSetting(int s) => s <= 0 ? "Авто" : s == 1 ? "Случайная" : Weather((WeatherType)(s - 2));
    }

    /// <summary>Per-difficulty tuning for enemies.</summary>
    public struct DifficultyTuning
    {
        public float enemyDamage, enemyHp, reaction, blockChance, parryChance, breakChance, aggression;
        public int attackTokens;
        // bosses on top of the enemy values: hp/damage multipliers, pause between big patterns, max summons
        public float bossHp, bossDamage, bossPace;
        public int bossSummons;

        public static DifficultyTuning For(Difficulty d)
        {
            switch (d)
            {
                case Difficulty.Novice:
                    return new DifficultyTuning { enemyDamage = 0.7f, enemyHp = 0.8f, reaction = 0.45f, blockChance = 0.2f, parryChance = 0f, breakChance = 0f, aggression = 0.55f, attackTokens = 1, bossHp = 0.5f, bossDamage = 0.45f, bossPace = 2f, bossSummons = 1 };
                case Difficulty.Warrior:
                    return new DifficultyTuning { enemyDamage = 1f, enemyHp = 1f, reaction = 0.3f, blockChance = 0.4f, parryChance = 0.1f, breakChance = 0.15f, aggression = 0.75f, attackTokens = 2, bossHp = 0.6f, bossDamage = 0.6f, bossPace = 1.6f, bossSummons = 1 };
                default:
                    return new DifficultyTuning { enemyDamage = 1.35f, enemyHp = 1.3f, reaction = 0.18f, blockChance = 0.6f, parryChance = 0.25f, breakChance = 0.35f, aggression = 0.95f, attackTokens = 2, bossHp = 0.78f, bossDamage = 0.78f, bossPace = 1.25f, bossSummons = 1 };
            }
        }
    }

    public static class KMath
    {
        public static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }

        public static float Damp(float a, float b, float rate, float dt) => Mathf.Lerp(a, b, 1f - Mathf.Exp(-rate * dt));
        public static Vector3 Damp(Vector3 a, Vector3 b, float rate, float dt) => Vector3.Lerp(a, b, 1f - Mathf.Exp(-rate * dt));

        /// <summary>Closest distance between segments p1-q1 and p2-q2.</summary>
        public static float SegmentSegmentDistance(Vector3 p1, Vector3 q1, Vector3 p2, Vector3 q2)
        {
            Vector3 d1 = q1 - p1, d2 = q2 - p2, r = p1 - p2;
            float a = Vector3.Dot(d1, d1), e = Vector3.Dot(d2, d2), f = Vector3.Dot(d2, r);
            float s, t;
            if (a <= 1e-6f && e <= 1e-6f) return r.magnitude;
            if (a <= 1e-6f) { s = 0f; t = Mathf.Clamp01(f / e); }
            else
            {
                float c = Vector3.Dot(d1, r);
                if (e <= 1e-6f) { t = 0f; s = Mathf.Clamp01(-c / a); }
                else
                {
                    float b = Vector3.Dot(d1, d2), denom = a * e - b * b;
                    s = denom != 0f ? Mathf.Clamp01((b * f - c * e) / denom) : 0f;
                    t = (b * s + f) / e;
                    if (t < 0f) { t = 0f; s = Mathf.Clamp01(-c / a); }
                    else if (t > 1f) { t = 1f; s = Mathf.Clamp01((b - c) / a); }
                }
            }
            return (p1 + d1 * s - (p2 + d2 * t)).magnitude;
        }

        public static float PointSegmentDistance(Vector3 p, Vector3 a, Vector3 b)
        {
            Vector3 ab = b - a;
            float t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / Mathf.Max(1e-6f, ab.sqrMagnitude));
            return (a + ab * t - p).magnitude;
        }
    }
}
