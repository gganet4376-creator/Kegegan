using UnityEngine;

namespace Kagegan
{
    /// <summary>All character definitions: names, looks, stats, and factory methods for players, clones, minions and bosses.</summary>
    public static class Roster
    {
        public static Transform FighterRoot;

        public static string Name(CharacterId id)
        {
            switch (id)
            {
                case CharacterId.Kaito: return "Кайто";
                case CharacterId.Genryu: return "Гэнрю";
                case CharacterId.Reina: return "Рэйна";
                default: return "Мугэн";
            }
        }

        public static string Title(CharacterId id)
        {
            switch (id)
            {
                case CharacterId.Kaito: return "Призрачный ниндзя";
                case CharacterId.Genryu: return "Пепельный самурай";
                case CharacterId.Reina: return "Призрак сети";
                default: return "Кибернетический призрак";
            }
        }

        public static string Description(CharacterId id)
        {
            switch (id)
            {
                case CharacterId.Kaito:
                    return "Ниндзя теней: скорость, обман и удары из-за спины.\n• Теневой разрез — серия из двух ударов, исчезновение и неблокируемый удар сзади\n• Призрачный двойник — клон повторяет твои удары и принимает один удар на себя\n• Небесный шквал — подброс и серия ударов в воздухе\n• Смена тени — подмена: перенос за спину атакующего и контрудар\n• Лунный сюрикен — три сюрикена, возвращающиеся бумерангом\n• Пассивка: идеальное уклонение даёт ускорение и лишние удары\n• Ульта: Тысяча теней";
                case CharacterId.Genryu:
                    return "Самурай пепла: тяжёлый клинок и жар парирования.\n• Пылающий Иай — мгновенный рывок-разрез с огненным следом\n• Стойка бессмертного — парирование с контратакой, отражает снаряды\n• Разлом земли — трещина подбрасывает врагов и выпускает огонь\n• Алый вихрь — огненное вращение с бронёй\n• Последний самурай — броня, ожог вокруг, +20% урона\n• Пассивка: Жар — парирования копят жар, на 100% тяжёлый удар становится огненным\n• Ульта: Суд последнего солнца";
                case CharacterId.Reina:
                    return "Кибер-нетраннер: моноструна, взломы и акробатика.\n• Монострунный хлыст — удар светящейся нитью на 4–5 м\n• Захват струной — притягивает врага и бьёт ногой (к тяжёлым и боссам подтягивается сама)\n• Системный шок — блокирует техники и ульту врага\n• Нейроперегрузка — замедляет реакции и движение (на боссах слабее)\n• Кибер-удар, сальто при уклонении, пике с моноструной в воздухе\n• Ульта: Глубокое погружение — удары струн со всех сторон и добивание сверху";
                default:
                    return "Кибер-призрак: рывки, моноклинки и импланты.\n• Neural Dash — рывок с остаточными образами, точное уклонение замедляет время\n• Mono Edge — серия ударов клинками из предплечий\n• Kiroshi Lock — скан слабого места: следующий удар ×1.8\n• EMP Breaker — ЭМИ прерывает заряжаемые техники и оглушает\n• Overclock — быстрее атаки и движение\n• Пассивка: перегрев — частое использование имплантов отключает их на 4 с\n• Ульта: Нейронный коллапс";
            }
        }

        public static Look LookFor(CharacterId id)
        {
            switch (id)
            {
                case CharacterId.Kaito:
                    return new Look
                    {
                        // Kaito's own identity: wind-ninja in deep blue with a long red scarf and dark teal hair
                        skin = new Color(0.98f, 0.84f, 0.72f), top = new Color(0.14f, 0.2f, 0.42f), sleeve = new Color(0.14f, 0.2f, 0.42f), top2 = new Color(0.9f, 0.9f, 0.92f),
                        pants = new Color(0.16f, 0.17f, 0.22f), shoes = new Color(0.1f, 0.1f, 0.12f), hair = new Color(0.1f, 0.32f, 0.38f),
                        eye = new Color(0.35f, 0.95f, 1f), accent = new Color(0.78f, 0.12f, 0.12f), hairStyle = HairStyle.Spiky, headband = false, scarf = true,
                        bareForearms = true, eyeGlow = 0.45f, model = "kaito"
                    };
                case CharacterId.Genryu:
                    return new Look
                    {
                        skin = new Color(0.98f, 0.84f, 0.72f), top = new Color(0.16f, 0.15f, 0.25f), sleeve = new Color(0.16f, 0.15f, 0.25f), top2 = new Color(0.1f, 0.1f, 0.12f),
                        pants = new Color(0.14f, 0.13f, 0.2f), shoes = new Color(0.1f, 0.1f, 0.1f), hair = new Color(0.08f, 0.08f, 0.14f),
                        eye = new Color(0.95f, 0.15f, 0.1f), accent = new Color(0.62f, 0.13f, 0.12f), hairStyle = HairStyle.LongSpiky, armor = true,
                        eyeGlow = 0.8f, hairLength = 1.1f, bareForearms = false, model = "genryu"
                    };
                case CharacterId.Reina:
                    return new Look
                    {
                        // original design: crimson ponytail, graphite tech-suit with hot-orange circuit accents
                        skin = new Color(0.95f, 0.8f, 0.7f), top = new Color(0.12f, 0.13f, 0.15f), sleeve = new Color(0.12f, 0.13f, 0.15f), top2 = new Color(1f, 0.52f, 0.12f),
                        pants = new Color(0.1f, 0.1f, 0.12f), shoes = new Color(0.2f, 0.22f, 0.24f), hair = new Color(0.55f, 0.07f, 0.12f),
                        eye = new Color(0.4f, 1f, 0.6f), accent = new Color(1f, 0.52f, 0.12f), hairStyle = HairStyle.LongStraight, hairLength = 0.8f,
                        bareForearms = false, eyeGlow = 0.7f, height = 0.97f, bulk = 0.92f, model = "lucy"
                    };
                default:
                    return new Look
                    {
                        skin = new Color(0.96f, 0.92f, 0.96f), top = new Color(0.95f, 0.94f, 0.98f), sleeve = new Color(0.95f, 0.94f, 0.98f), top2 = new Color(0.4f, 0.25f, 0.6f),
                        pants = new Color(0.9f, 0.88f, 0.94f), shoes = new Color(0.3f, 0.25f, 0.4f), hair = new Color(0.97f, 0.96f, 1f),
                        eye = new Color(0.75f, 0.5f, 1f), accent = new Color(0.55f, 0.3f, 0.8f), hairStyle = HairStyle.LongWhite, horns = true, robe = true,
                        palmEyes = true, eyeGlow = 0.9f, hairLength = 0.55f, longSleeves = true, model = "mugen"
                    };
            }
        }

        public static StanceKind Stance(CharacterId id) => id == CharacterId.Kaito || id == CharacterId.Reina ? StanceKind.Swift : id == CharacterId.Genryu ? StanceKind.Heavy : StanceKind.Void;
        public static RunStyle Run(CharacterId id) => id == CharacterId.Kaito || id == CharacterId.Reina ? RunStyle.Ninja : id == CharacterId.Genryu ? RunStyle.Heavy : RunStyle.Glide;
        public static Pose Victory(CharacterId id) => id == CharacterId.Kaito || id == CharacterId.Reina ? PoseLib.WinSwift : id == CharacterId.Genryu ? PoseLib.WinHeavy : PoseLib.WinVoid;

        /// <summary>Each fighter's own captured celebration.</summary>
        public static string VictoryClip(CharacterId id)
        {
            switch (id)
            {
                case CharacterId.Kaito: return "Yes";
                case CharacterId.Reina: return "Dance_Loop";
                case CharacterId.Genryu: return "Idle_FoldArms_Loop";
                default: return "KK_Ranged_Magic_Raise";
            }
        }

        public static Kit KitFor(CharacterId id)
        {
            switch (id)
            {
                case CharacterId.Kaito: return new KaitoKit();
                case CharacterId.Genryu: return new GenryuKit();
                case CharacterId.Reina: return new ReinaKit();
                default: return new MugenKit();
            }
        }

        public static Fighter CreatePlayer(CharacterId id, Vector3 pos)
        {
            float hp = id == CharacterId.Genryu ? 640f : id == CharacterId.Kaito ? 560f : id == CharacterId.Reina ? 540f : 580f;
            float speed = id == CharacterId.Kaito ? 6.6f : id == CharacterId.Genryu ? 5.5f : id == CharacterId.Reina ? 6.9f : 6.1f;
            var f = FighterFactory.Make(Name(id), LookFor(id), Stance(id), Run(id), KitFor(id), Team.Player, pos, FighterRoot, hp, speed);
            f.IsPlayer = true;
            f.Anim.PlaySteps = true;
            f.Anim.VictoryPose = Victory(id);
            f.VictoryClip = VictoryClip(id);
            return f;
        }

        /// <summary>Showcase model for menus (no controller, no collisions with gameplay).</summary>
        public static Fighter CreateShowcase(CharacterId id, Vector3 pos, Transform parent)
        {
            var f = FighterFactory.Make(Name(id), LookFor(id), Stance(id), Run(id), KitFor(id), Team.Neutral, pos, parent, 100f, 5f);
            f.Anim.VictoryPose = Victory(id);
            return f;
        }

        public static Fighter CreateClone(Fighter owner, Vector3 pos, Color? ghostTint = null)
        {
            pos = Arena.ClampInside(pos, 1f);
            // each double is slightly different: build, tempo, temperament
            var look = LookFor(CharacterId.Kaito);
            look.height *= Random.Range(0.97f, 1.03f);
            look.accent = Color.Lerp(look.accent, new Color(0.9f, 0.4f, 0.2f), Random.value * 0.5f);
            if (ghostTint.HasValue) { look.tint = ghostTint.Value; look.eyeGlow = 1f; }
            var f = FighterFactory.Make("Двойник", look, StanceKind.Swift, RunStyle.Ninja, new KaitoKit(true), owner.Team, pos, FighterRoot, 30f, Random.Range(6.3f, 7.1f));
            f.Anim.TimeScale = Random.Range(0.9f, 1.12f);
            f.IsClone = true;
            f.Owner = owner;
            f.DamageMul = 0.6f;
            f.InfiniteChakra = false;
            f.transform.rotation = owner.transform.rotation;
            var tune = DifficultyTuning.For(Difficulty.Warrior);
            tune.aggression = Random.Range(0.75f, 1f); tune.reaction = Random.Range(0.18f, 0.35f); tune.blockChance = Random.Range(0.05f, 0.3f);
            var brain = f.gameObject.AddComponent<AIBrain>();
            brain.Setup(f, Archetype.Clone, tune);
            f.Target = owner.Target;
            var life = f.gameObject.AddComponent<CloneLife>();
            life.Lifetime = 14f;
            Fx.Smoke(f.Center, 0.8f, new Color(1f, 1f, 1f, 0.7f), 10);
            f.OnDied += (dead, killer) => life.Poof();
            return f;
        }

        public static Look MinionLook(MinionType t)
        {
            var L = new Look { eyeGlow = 0.2f };
            switch (t)
            {
                case MinionType.ForestBandit:
                    L.top = new Color(0.32f, 0.45f, 0.26f); L.sleeve = new Color(0.2f, 0.22f, 0.2f); L.top2 = new Color(0.22f, 0.25f, 0.2f); L.pants = new Color(0.2f, 0.24f, 0.2f);
                    L.hairStyle = HairStyle.Hood; L.hair = new Color(0.25f, 0.3f, 0.22f); L.mask = true; L.eye = new Color(0.4f, 0.3f, 0.2f); break;
                case MinionType.ForestAssassin:
                    L.top = new Color(0.22f, 0.22f, 0.25f); L.sleeve = L.top; L.top2 = new Color(0.12f, 0.12f, 0.14f); L.pants = new Color(0.18f, 0.18f, 0.2f);
                    L.hairStyle = HairStyle.Slick; L.hair = new Color(0.4f, 0.35f, 0.3f); L.mask = true; L.scarf = true; L.accent = new Color(0.5f, 0.15f, 0.15f); break;
                case MinionType.ForestThrower:
                    L.top = new Color(0.5f, 0.38f, 0.25f); L.sleeve = L.top; L.top2 = new Color(0.3f, 0.22f, 0.15f); L.pants = new Color(0.3f, 0.26f, 0.2f);
                    L.hairStyle = HairStyle.Hood; L.hair = new Color(0.45f, 0.35f, 0.22f); break;
                case MinionType.SnakeAdept:
                    L.top = new Color(0.45f, 0.35f, 0.55f); L.sleeve = L.top; L.top2 = new Color(0.25f, 0.18f, 0.3f); L.pants = new Color(0.3f, 0.25f, 0.35f);
                    L.hairStyle = HairStyle.Hood; L.hair = new Color(0.35f, 0.25f, 0.45f); L.mask = true; L.eye = new Color(1f, 0.85f, 0.2f); L.eyeGlow = 0.7f; break;
                case MinionType.CityBrute:
                    L.top = new Color(0.4f, 0.45f, 0.55f); L.sleeve = L.top; L.top2 = new Color(0.25f, 0.28f, 0.35f); L.pants = new Color(0.22f, 0.24f, 0.3f);
                    L.hairStyle = HairStyle.Short; L.hair = new Color(0.2f, 0.15f, 0.12f); L.armor = true; L.accent = new Color(0.5f, 0.5f, 0.55f); L.bulk = 1.35f; L.height = 1.08f; break;
                case MinionType.CityMerc:
                    L.top = new Color(0.12f, 0.12f, 0.14f); L.sleeve = L.top; L.top2 = new Color(0.7f, 0.12f, 0.12f); L.pants = new Color(0.14f, 0.14f, 0.16f);
                    L.hairStyle = HairStyle.Spiky; L.hair = new Color(0.85f, 0.85f, 0.9f); L.scarf = true; L.accent = new Color(0.75f, 0.1f, 0.1f); L.eye = new Color(0.6f, 0.8f, 1f); break;
                case MinionType.CityBomber:
                    L.top = new Color(0.6f, 0.42f, 0.22f); L.sleeve = L.top; L.top2 = new Color(0.3f, 0.2f, 0.12f); L.pants = new Color(0.32f, 0.28f, 0.22f);
                    L.hairStyle = HairStyle.Short; L.hair = new Color(0.6f, 0.2f, 0.1f); L.mask = true; break;
                case MinionType.MoonGuard:
                    L.top = new Color(0.92f, 0.92f, 0.97f); L.sleeve = L.top; L.top2 = new Color(0.45f, 0.35f, 0.7f); L.pants = new Color(0.85f, 0.85f, 0.92f);
                    L.hairStyle = HairStyle.Hood; L.hair = new Color(0.9f, 0.9f, 0.96f); L.mask = true; L.skin = new Color(0.95f, 0.95f, 1f); L.eye = new Color(0.8f, 0.7f, 1f); L.eyeGlow = 0.9f; break;
                case MinionType.MoonPriest:
                    L.top = new Color(0.95f, 0.95f, 1f); L.sleeve = L.top; L.top2 = new Color(0.5f, 0.4f, 0.8f); L.pants = L.top; L.robe = true; L.accent = new Color(0.5f, 0.4f, 0.8f);
                    L.hairStyle = HairStyle.LongStraight; L.hair = new Color(0.95f, 0.95f, 1f); L.skin = new Color(0.95f, 0.95f, 1f); L.eye = new Color(0.8f, 0.7f, 1f); L.eyeGlow = 1f; L.longSleeves = true; break;
                case MinionType.MoonBone:
                    L.top = new Color(0.9f, 0.88f, 0.82f); L.sleeve = L.top; L.top2 = new Color(0.6f, 0.55f, 0.5f); L.pants = new Color(0.7f, 0.68f, 0.62f);
                    L.hairStyle = HairStyle.Short; L.hair = new Color(0.95f, 0.95f, 0.95f); L.armor = true; L.accent = new Color(0.95f, 0.93f, 0.86f); L.bulk = 1.25f; L.height = 1.05f;
                    L.skin = new Color(0.92f, 0.9f, 0.92f); L.eye = new Color(1f, 0.3f, 0.3f); L.eyeGlow = 0.9f; break;
                case MinionType.CityCyber:
                    L.top = new Color(0.16f, 0.2f, 0.24f); L.sleeve = new Color(0.55f, 0.6f, 0.65f); L.top2 = new Color(0.1f, 0.5f, 0.45f); L.pants = new Color(0.14f, 0.16f, 0.2f);
                    L.hairStyle = HairStyle.Short; L.hair = new Color(0.15f, 0.15f, 0.18f); L.armor = true; L.accent = new Color(0.3f, 1f, 0.85f);
                    L.eye = new Color(0.3f, 1f, 0.85f); L.eyeGlow = 1f; break;
            }
            ApplyMinionModel(L, t);
            return L;
        }

        /// <summary>Two baked bodies shared by all minions; a per-type tint keeps the factions readable.</summary>
        static void ApplyMinionModel(Look L, MinionType t)
        {
            switch (t)
            {
                case MinionType.ForestBandit: L.model = "merc"; L.tint = new Color(0.78f, 0.92f, 0.72f); break;
                case MinionType.ForestThrower: L.model = "merc"; L.tint = new Color(0.95f, 0.85f, 0.7f); break;
                case MinionType.CityBrute: L.model = "merc"; L.tint = new Color(0.8f, 0.86f, 1f); break;
                case MinionType.CityMerc: L.model = "merc"; L.tint = Color.white; break;
                case MinionType.MoonBone: L.model = "merc"; L.tint = new Color(1f, 0.97f, 0.9f); break;
                case MinionType.ForestAssassin: L.model = "cyber"; L.tint = new Color(0.8f, 0.8f, 0.85f); break;
                case MinionType.SnakeAdept: L.model = "cyber"; L.tint = new Color(0.85f, 0.75f, 1f); break;
                case MinionType.CityBomber: L.model = "cyber"; L.tint = new Color(1f, 0.85f, 0.7f); break;
                case MinionType.MoonGuard: L.model = "cyber"; L.tint = new Color(0.92f, 0.92f, 1.05f); break;
                case MinionType.MoonPriest: L.model = "cyber"; L.tint = new Color(1.05f, 1.02f, 1.1f); break;
                case MinionType.CityCyber: L.model = "cyber"; L.tint = new Color(0.85f, 1f, 0.98f); break;
            }
        }

        public static string MinionName(MinionType t)
        {
            switch (t)
            {
                case MinionType.ForestBandit: return "Лесной разбойник";
                case MinionType.ForestAssassin: return "Лесной убийца";
                case MinionType.ForestThrower: return "Метатель";
                case MinionType.SnakeAdept: return "Змеиный адепт";
                case MinionType.CityBrute: return "Громила";
                case MinionType.CityMerc: return "Наёмник";
                case MinionType.CityBomber: return "Подрывник";
                case MinionType.MoonGuard: return "Лунный страж";
                case MinionType.MoonPriest: return "Лунный жрец";
                case MinionType.CityCyber: return "Киберсолдат";
                default: return "Костяной воин";
            }
        }

        public static Fighter CreateMinion(MinionType t, Vector3 pos, DifficultyTuning tune, int wave)
        {
            Archetype arch;
            float hp, speed;
            switch (t)
            {
                case MinionType.ForestAssassin: case MinionType.CityMerc: case MinionType.MoonGuard: case MinionType.CityCyber: arch = Archetype.Striker; hp = 110f; speed = 6.4f; break;
                case MinionType.ForestThrower: case MinionType.CityBomber: case MinionType.MoonPriest: arch = Archetype.Ranged; hp = 90f; speed = 5.6f; break;
                case MinionType.CityBrute: case MinionType.MoonBone: arch = Archetype.Heavy; hp = 190f; speed = 4.8f; break;
                default: arch = Archetype.Brawler; hp = 120f; speed = 5.8f; break;
            }
            hp *= (1f + wave * 0.35f) * tune.enemyHp;
            var L = MinionLook(t);
            var stance = arch == Archetype.Heavy ? StanceKind.Heavy : arch == Archetype.Striker ? StanceKind.Swift : StanceKind.Brawler;
            var run = arch == Archetype.Striker ? RunStyle.Ninja : arch == Archetype.Heavy ? RunStyle.Heavy : RunStyle.Normal;
            var f = FighterFactory.Make(MinionName(t), L, stance, run, new MinionKit(t), Team.Enemy, pos, FighterRoot, hp, speed * (1f + wave * 0.06f));
            f.DamageMul = tune.enemyDamage * (1f + wave * 0.15f);
            f.Weight = arch == Archetype.Heavy ? 1.4f : 1f;
            f.MaxChakra = 60f; f.Chakra = 40f; f.ChakraRegen = 5f;
            var brain = f.gameObject.AddComponent<MinionBrain>();
            brain.Setup(f, arch, tune);
            brain.Init(t);
            return f;
        }

        public static string BossName(int i) => i == 0 ? "Дзякура, Змеиный мудрец" : i == 1 ? "Райдэн, Мститель грома" : "Сэйгэцу, Владычица луны";

        public static Fighter CreateBoss(int i, Vector3 pos, DifficultyTuning tune)
        {
            Look L;
            Kit kit;
            StanceKind stance;
            RunStyle run;
            float hp, speed, scale = 1.08f;
            switch (i)
            {
                case 0:
                    L = new Look
                    {
                        skin = new Color(0.9f, 0.92f, 0.88f), top = new Color(0.75f, 0.72f, 0.62f), sleeve = new Color(0.75f, 0.72f, 0.62f), top2 = new Color(0.45f, 0.25f, 0.6f),
                        pants = new Color(0.5f, 0.48f, 0.42f), hair = new Color(0.06f, 0.06f, 0.08f), eye = new Color(1f, 0.85f, 0.15f), eyeGlow = 0.9f,
                        accent = new Color(0.5f, 0.25f, 0.65f), hairStyle = HairStyle.LongStraight, robe = true, longSleeves = true, model = "dzyakura"
                    };
                    kit = new SerpentKit(); stance = StanceKind.Serpent; run = RunStyle.Glide; hp = 1500f; speed = 6f;
                    break;
                case 1:
                    L = new Look
                    {
                        skin = new Color(0.98f, 0.86f, 0.75f), top = new Color(0.85f, 0.85f, 0.88f), sleeve = new Color(0.85f, 0.85f, 0.88f), top2 = new Color(0.35f, 0.25f, 0.55f),
                        pants = new Color(0.15f, 0.15f, 0.22f), hair = new Color(0.1f, 0.1f, 0.2f), eye = new Color(1f, 0.15f, 0.15f), eyeGlow = 1f,
                        accent = new Color(0.35f, 0.25f, 0.55f), hairStyle = HairStyle.Spiky, cape = true, bareForearms = false, model = "tsukuyomi"
                    };
                    kit = new ThunderKit(); stance = StanceKind.Thunder; run = RunStyle.Ninja; hp = 1700f; speed = 6.8f;
                    break;
                default:
                    L = new Look
                    {
                        skin = new Color(0.97f, 0.96f, 1f), top = new Color(0.97f, 0.96f, 1f), sleeve = new Color(0.97f, 0.96f, 1f), top2 = new Color(0.4f, 0.3f, 0.6f),
                        pants = new Color(0.95f, 0.94f, 1f), hair = new Color(0.98f, 0.98f, 1f), eye = new Color(0.85f, 0.75f, 1f), eyeGlow = 1f,
                        accent = new Color(0.45f, 0.3f, 0.7f), hairStyle = HairStyle.LongWhite, robe = true, horns = true, longSleeves = true, hairLength = 1.3f, model = "seigetsu"
                    };
                    kit = new MoonKit(); stance = StanceKind.Moon; run = RunStyle.Glide; hp = 2000f; speed = 6.2f; scale = 1.12f;
                    break;
            }
            var f = FighterFactory.Make(BossName(i), L, stance, run, kit, Team.Enemy, pos, FighterRoot, hp * tune.enemyHp * tune.bossHp, speed, scale);
            f.IsBoss = true;
            f.DamageMul = tune.enemyDamage * tune.bossDamage * (1f + i * 0.06f);
            f.Weight = 1.25f;
            f.MaxChakra = 150f; f.Chakra = 150f; f.ChakraRegen = 7f;
            f.MaxStamina = 160f; f.Stamina = 160f;
            BossBrain brain;
            if (i == 0) brain = f.gameObject.AddComponent<SerpentBrain>();
            else if (i == 1) brain = f.gameObject.AddComponent<ThunderBrain>();
            else brain = f.gameObject.AddComponent<MoonBrain>();
            brain.Setup(f, Archetype.Boss, tune);
            return f;
        }
    }

    /// <summary>Shadow doubles vanish in a puff of smoke when their time is up or they are hit hard.</summary>
    public class CloneLife : MonoBehaviour
    {
        public float Lifetime = 14f;
        bool gone;

        void Update()
        {
            Lifetime -= Time.deltaTime;
            if (Lifetime <= 0f) Poof();
        }

        public void Poof()
        {
            if (gone) return;
            gone = true;
            var f = GetComponent<Fighter>();
            if (f != null) Fx.Smoke(f.Center, 0.8f, new Color(1f, 1f, 1f, 0.7f), 10);
            Sfx.Play(SfxId.Poof, 0.5f, 1.2f);
            Destroy(gameObject);
        }
    }
}
