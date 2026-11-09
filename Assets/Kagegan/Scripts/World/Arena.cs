using System.Collections.Generic;
using UnityEngine;

namespace Kagegan
{
    /// <summary>
    /// Builds the four locations procedurally. Static scenery is merged into a few vertex-colored meshes
    /// (one material => very few draw calls); breakable props are separate Destructible objects.
    /// </summary>
    public class Arena : MonoBehaviour
    {
        public static Arena Current;
        public static float Radius = 21f;
        // 2.5D fighting lane (Mortal-Kombat-like side view): fighters move freely along X and a little in depth (Z)
        public static float LaneHalf = 17f, LaneDepth = 3.6f;
        public ArenaId Id;
        public Vector3 PlayerSpawn = new Vector3(0, 0, -6f);
        public Vector3 EnemySpawn = new Vector3(0, 0, 6f);
        readonly List<GameObject> walls = new List<GameObject>();
        GameObject weatherGo;
        ParticleSystem weather;
        float weatherRate;
        MeshRenderer skyRenderer;
        Mesh skyMesh;
        Color skyTop, skyHorizon;
        Color sunCol, ambCol, fogCol;
        Vector3 sunDir;
        float fogStart, fogEnd, fogMax;
        float dimensionUntil;
        public System.Action<int> PhaseChanged;

        /// <summary>Lighting change a weather applies on top of the arena's own look.</summary>
        public struct WeatherLook
        {
            public float Sun, Amb, FogLerp, FogNear, FogMax;
            public Color SunTint, Fog, Sky;
        }
        WeatherLook? look;
        public WeatherType WeatherKind { get; private set; }
        /// <summary>The dojo built for snow: white ground, firs, frozen haze (training ground and snowy dojo fights).</summary>
        public bool Winter { get; private set; }

        /// <summary>Time of day for the daylight arenas (Dojo, Forest): picked per fight, the night ones stay as built.</summary>
        public enum TimeOfDay { Day, Evening, Night }
        public TimeOfDay Tod { get; private set; } = TimeOfDay.Day;
        Color todSky = Color.white, todFog = Color.white;

        static TimeOfDay PickTimeOfDay(ArenaId id)
        {
            if (id != ArenaId.Dojo && id != ArenaId.Forest) return TimeOfDay.Day;
            float r = Random.value;
            return r < 0.55f ? TimeOfDay.Day : r < 0.85f ? TimeOfDay.Evening : TimeOfDay.Night;
        }

        /// <summary>Re-colours the arena's base light for evening / night (runs before the scenery shadow is baked).</summary>
        void ApplyTimeOfDay(TimeOfDay t)
        {
            Tod = t;
            if (t == TimeOfDay.Day) return;
            if (t == TimeOfDay.Evening)
            {
                // low warm sun from the side: long shadows, orange rims
                sunDir = new Vector3(sunDir.x, 0.3f, sunDir.z).normalized;
                sunCol = new Color(1f, 0.62f, 0.38f) * 0.95f;
                ambCol = new Color(ambCol.r * 0.9f + 0.03f, ambCol.g * 0.75f, ambCol.b * 0.85f + 0.04f);
                fogCol = Color.Lerp(fogCol, new Color(0.85f, 0.55f, 0.42f), 0.6f);
                todSky = new Color(1f, 0.74f, 0.62f);
                todFog = new Color(1f, 0.78f, 0.66f);
            }
            else
            {
                // cool moonlight; kept bright enough to read the fighters on a phone screen
                sunDir = new Vector3(-sunDir.x, 0.55f, sunDir.z).normalized;
                sunCol = new Color(0.5f, 0.58f, 0.85f);
                ambCol = new Color(0.17f, 0.19f, 0.3f);
                fogCol = new Color(0.1f, 0.12f, 0.22f);
                todSky = new Color(0.2f, 0.24f, 0.42f);
                todFog = new Color(0.3f, 0.33f, 0.5f);
            }
            skyTop *= todSky; skyHorizon *= todSky;
        }
        public WeatherSystem WeatherFx { get; private set; }
        MoonTiles moonTiles;

        public static bool Inside(Vector3 p, float margin) =>
            Mathf.Abs(p.x) < LaneHalf + margin && p.z > -LaneDepth - margin - 2f && p.z < LaneDepth + margin + 6f;

        public static Vector3 ClampInside(Vector3 p, float margin)
        {
            p.x = Mathf.Clamp(p.x, -LaneHalf + margin, LaneHalf - margin);
            float dz = Mathf.Max(0.4f, LaneDepth - margin * 0.5f);
            p.z = Mathf.Clamp(p.z, -dz, dz);
            return p;
        }

        /// <summary>Moves breakable props out of the camera's way: tall ones line the back edge of the lane, low ones too.</summary>
        void LayoutPropsForLane()
        {
            var rnd = new System.Random(17 + (int)Id);
            int i = 0;
            foreach (var d in Destructible.All)
            {
                Vector3 p = d.transform.position;
                float x = Mathf.Clamp(p.x * 1.15f + ((float)rnd.NextDouble() - 0.5f) * 3f, -LaneHalf + 0.5f, LaneHalf - 0.5f);
                bool tall = d.Kind == DestructKind.Tree || d.Kind == DestructKind.Pillar || d.Kind == DestructKind.Wall;
                float z = tall ? LaneDepth + 1.2f + (float)rnd.NextDouble() * 3.5f : LaneDepth + 0.7f + (float)rnd.NextDouble() * 2f;
                if (i % 3 == 2 && !tall) z = LaneDepth + 0.6f; // some low props right at the edge so fights knock into them
                d.Relocate(new Vector3(x, 0f, z));
                i++;
            }
        }

        GameObject KitRoot(string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            return go;
        }

        /// <summary>Training-ground dressing from the Fantasy Props / Medieval Village kits: weapon stands, benches,
        /// banners, a market cart and fences behind the lane.</summary>
        void DojoProps()
        {
            var root = KitRoot("DojoProps").transform;
            var rnd = new System.Random(23);
            ModelKit.Spawn("WeaponStand", root, new Vector3(4f, 0, 16.5f), 1.3f, 180f);
            ModelKit.Spawn("WeaponStand", root, new Vector3(7.5f, 0, 16.2f), 1.3f, 170f);
            ModelKit.Spawn("Bench", root, new Vector3(-6f, 0, 17f), 0.55f, 0f);
            ModelKit.Spawn("Bench", root, new Vector3(11f, 0, 15.5f), 0.55f, 8f);
            ModelKit.Spawn("Banner_1", root, new Vector3(-9f, 0, 19.5f), 2.6f, 180f);
            ModelKit.Spawn("Banner_1", root, new Vector3(1f, 0, 19.5f), 2.6f, 180f);
            ModelKit.Spawn("Stall_Cart_Empty", root, new Vector3(18f, 0, 13f), 2.6f, 200f);
            ModelKit.Spawn("Prop_Wagon", root, new Vector3(-19f, 0, 12f), 1.6f, 75f);
            ModelKit.Spawn("Barrel_Apples", root, new Vector3(16f, 0, 11.5f), 0.9f, 30f);
            ModelKit.Spawn("Chest_Wood", root, new Vector3(-15f, 0, 14.5f), 0.7f, 190f);
            for (float x = -30f; x <= 30f; x += 2.05f)
            {
                if (x > -2f && x < 2f) continue; // gap for the gate
                ModelKit.Spawn("Prop_WoodenFence_Single", root, new Vector3(x, 0, 21.5f), 0.84f, 0f);
            }
            for (int i = 0; i < 5; i++)
                ModelKit.Spawn(i % 2 == 0 ? "Prop_Crate" : "Barrel", root, new Vector3(-26f + (float)rnd.NextDouble() * 52f, 0, 9.5f + (float)rnd.NextDouble() * 3f), i % 2 == 0 ? 1f : 0.9f, (float)rnd.NextDouble() * 360f);
            ModelKit.MergeBehind(root, MergeZ);
        }

        /// <summary>Scenery entirely beyond this depth is behind the fight from every camera (side view, victory
        /// orbit at 6.5 m from a fighter at the lane edge), so it can be merged and needs no camera fade.</summary>
        const float MergeZ = 10.5f;

        /// <summary>Downtown City MegaKit buildings as the street behind the lane, plus breakable street props.</summary>
        void CityModels()
        {
            var root = KitRoot("CityModels").transform;
            var rnd = new System.Random(41);
            bool low = GraphicsQuality.Current == GraphicsPreset.Low;
            float x = -44f;
            // The MegaKit buildings are 18-45k triangles with 12 materials each: fine on High/Ultra, but on Low/Medium
            // they alone cost ~60 draw calls. There the street uses the light Kenney blocks (1 material, ~1.5k tris).
            bool heavy = GraphicsQuality.Current >= GraphicsPreset.High;
            if (!heavy && ModelKit.Has("KC_building-a"))
            {
                string[] blocks = { "KC_building-a", "KC_building-c", "KC_building-f", "KC_building-h" };
                while (x < 44f)
                {
                    float w = 11f + (float)rnd.NextDouble() * 3f;
                    ModelKit.Spawn(blocks[rnd.Next(blocks.Length)], root, new Vector3(x + w * 0.5f, 0, 22f + (float)rnd.NextDouble() * 3f), 15f + (float)rnd.NextDouble() * 9f, 180f);
                    x += w + 5f + (float)rnd.NextDouble() * 4f;
                }
            }
            while (heavy && x < 44f)
            {
                string b = ModelKit.Buildings[rnd.Next(ModelKit.Buildings.Length)];
                float h = b == "Building_Large_2" ? 28f : b == "Building_Medium_2_001" ? 25f : 17f;
                float w = b == "Building_Large_2" ? 20.6f : b == "Building_Medium_2_001" ? 15f : 12.5f;
                // yaw 180 turns the windowed facade toward the side camera; gaps let the far skyline show through
                ModelKit.Spawn(b, root, new Vector3(x + w * 0.5f, 0, 22f + (float)rnd.NextDouble() * 3f), h, 180f);
                x += w + 5f + (float)rnd.NextDouble() * 4f;
            }
            for (float bx = -24f; bx <= 24f; bx += 4f)
                ModelKit.Spawn("Prop_Bollard", root, new Vector3(bx, 0, LaneDepth + 5f), 0.9f, 0f);
            // Kenney City Kit: a skyline of towers far behind the street, street lights and poles
            if (ModelKit.Has("KC_building-skyscraper-a"))
            {
                for (float sx = -60f; sx <= 60f; sx += low ? 16f : 9f)
                {
                    string t = ModelKit.Pick(ModelKit.Skyscrapers, rnd);
                    float th = t.Contains("skyscraper") ? 34f + (float)rnd.NextDouble() * 22f : 16f + (float)rnd.NextDouble() * 8f;
                    ModelKit.Spawn(t, root, new Vector3(sx + (float)rnd.NextDouble() * 4f, 0, 42f + (float)rnd.NextDouble() * 14f), th, rnd.Next(4) * 90f);
                }
                for (float lx = -22f; lx <= 22f; lx += 11f)
                    ModelKit.Spawn("KR_light-curved", root, new Vector3(lx, 0, LaneDepth + 6.5f), 5.5f, 180f);
                ModelKit.Spawn("KR_traffic-light", root, new Vector3(16f, 0, LaneDepth + 6f), 4.2f, 180f);
                ModelKit.Spawn("KR_road-sign-stop", root, new Vector3(-17f, 0, LaneDepth + 6f), 2.4f, 180f);
                if (!low)
                    for (float px = -40f; px <= 40f; px += 20f)
                        ModelKit.Spawn("KR_electricity-pole", root, new Vector3(px, 0, LaneDepth + 10.5f), 6.5f, 0f);
                // breakable street clutter
                string[] clutter = { "KR_dumpster", "KR_construction-barrier", "KR_construction-cone", "KR_dumpster", "KR_construction-barrier" };
                float[] ch = { 1.3f, 1.0f, 0.75f, 1.3f, 1.0f };
                for (int i = 0; i < clutter.Length; i++)
                {
                    float a = i * 1.25f + 1.0f;
                    Destructible.CreateFromModel(transform, DestructKind.Crate, new Vector3(Mathf.Cos(a) * 14f, 0, Mathf.Sin(a) * 14f), clutter[i], ch[i], clutter[i] == "KR_dumpster" ? 80f : 30f, new Color(0.45f, 0.5f, 0.45f), (float)rnd.NextDouble() * 360f);
                }
            }
            if (!low)
                for (int i = 0; i < 4; i++)
                    ModelKit.Spawn("Prop_ACUnit", root, new Vector3(-20f + i * 13f, 0, LaneDepth + 8.5f), 0.6f, 180f + (float)rnd.NextDouble() * 30f - 15f);
            // buildings and the far skyline become one mesh per material: the MegaKit street alone was ~60 draw calls
            ModelKit.MergeBehind(root, MergeZ);
            // breakable crates, barrels and planters along the fight
            string[] brk = { "Crate_Wooden", "Barrel", "Prop_Planter_Single", "Crate_Metal", "Barrel" };
            for (int i = 0; i < brk.Length; i++)
            {
                float a = i * 1.25f + 0.4f;
                Vector3 p = new Vector3(Mathf.Cos(a) * 12f, 0, Mathf.Sin(a) * 12f);
                float hh = brk[i] == "Prop_Planter_Single" ? 0.6f : brk[i] == "Barrel" ? 0.9f : 1f;
                Destructible.CreateFromModel(transform, DestructKind.Crate, p, brk[i], hh, brk[i] == "Crate_Metal" ? 70f : 40f, new Color(0.5f, 0.42f, 0.35f), (float)rnd.NextDouble() * 360f);
            }
        }

        /// <summary>Moon temple offerings: breakable vases and candle stands from the Fantasy Props kit.</summary>
        void MoonProps()
        {
            var rnd = new System.Random(77);
            for (int i = 0; i < 6; i++)
            {
                float a = i * 1.05f + 0.5f;
                Vector3 p = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * 9f;
                if (i % 3 == 2)
                    Destructible.CreateFromModel(transform, DestructKind.Lantern, p, "CandleStick_Stand", 1.3f, 25f, new Color(0.7f, 0.6f, 0.35f), (float)rnd.NextDouble() * 360f);
                else
                    Destructible.CreateFromModel(transform, DestructKind.Crate, p, i % 2 == 0 ? "Vase_2" : "Vase_4", i % 2 == 0 ? 0.75f : 0.7f, 20f, new Color(0.75f, 0.6f, 0.45f), (float)rnd.NextDouble() * 360f);
            }
        }

        /// <summary>Forest backdrop from the hand-made nature models: a tree line behind the lane and undergrowth
        /// between it and the fight. Nothing is placed on the camera side, so the view stays clear.</summary>
        void NatureBackdrop()
        {
            var root = new GameObject("NatureBackdrop").transform;
            root.SetParent(transform, false);
            var rnd = new System.Random(57);
            bool low = GraphicsQuality.Current == GraphicsPreset.Low;
            float step = low ? 6.5f : 4f;
            // tree line, two staggered rows
            for (float x = -36f; x <= 36f; x += step + (float)rnd.NextDouble() * 2f)
            {
                float z = 11f + (float)rnd.NextDouble() * 4f;
                string m = rnd.NextDouble() < 0.12 ? ModelKit.Pick(ModelKit.DeadTrees, rnd) : ModelKit.Pick(ModelKit.Trees, rnd);
                ModelKit.Spawn(m, root, new Vector3(x, 0, z), 8f + (float)rnd.NextDouble() * 4f, (float)rnd.NextDouble() * 360f);
                if (!low)
                {
                    m = ModelKit.Pick(ModelKit.Trees, rnd);
                    ModelKit.Spawn(m, root, new Vector3(x + step * 0.5f, 0, z + 6f + (float)rnd.NextDouble() * 5f), 10f + (float)rnd.NextDouble() * 4f, (float)rnd.NextDouble() * 360f);
                }
            }
            // a big twisted landmark tree far back
            ModelKit.Spawn("TwistedTree_1", root, new Vector3(-6f, 0, 24f), 15f, 40f);
            // rocks and undergrowth between the props and the tree line
            int plants = low ? 14 : 34;
            for (int i = 0; i < plants; i++)
            {
                float x = -30f + (float)rnd.NextDouble() * 60f;
                float z = LaneDepth + 4.5f + (float)rnd.NextDouble() * 6f;
                string m = ModelKit.Pick(ModelKit.Plants, rnd);
                float h = m.StartsWith("Grass") || m.StartsWith("Flower") || m.StartsWith("Mushroom") ? 0.5f + (float)rnd.NextDouble() * 0.3f
                        : m == "Fern_1" ? 0.9f : 1.1f + (float)rnd.NextDouble() * 0.5f;
                ModelKit.Spawn(m, root, new Vector3(x, 0, z), h, (float)rnd.NextDouble() * 360f);
            }
            for (int i = 0; i < (low ? 4 : 9); i++)
            {
                float x = -32f + (float)rnd.NextDouble() * 64f;
                ModelKit.Spawn(ModelKit.Pick(ModelKit.Rocks, rnd), root, new Vector3(x, 0, 9f + (float)rnd.NextDouble() * 3f), 1.2f + (float)rnd.NextDouble() * 1.4f, (float)rnd.NextDouble() * 360f);
            }
            // the tree line merges into a few meshes (bark / leaves); the undergrowth near the lane keeps its camera fade
            ModelKit.MergeBehind(root, MergeZ);
        }

        /// <summary>Ultra only: photoscanned Poly Haven models (CC0) just behind the lane, where the camera sees them
        /// up close — mossy rocks and stumps in the forest, a stone fire pit at the dojo, moon rocks at the temple.
        /// Kept off the lane and the camera side, so they never block the fight.</summary>
        void PhotoScans()
        {
            var root = new GameObject("PhotoScans").transform;
            root.SetParent(transform, false);
            var rnd = new System.Random(91 + (int)Id);
            float near = LaneDepth + 3.2f;
            switch (Id)
            {
                case ArenaId.Forest:
                    for (int i = 0; i < 7; i++)
                    {
                        float x = -24f + i * 8f + ((float)rnd.NextDouble() - 0.5f) * 4f;
                        float z = near + (float)rnd.NextDouble() * 4f;
                        if (i % 3 == 1) ModelKit.Spawn("tree_stump_01", root, new Vector3(x, 0, z), 0.8f + (float)rnd.NextDouble() * 0.3f, (float)rnd.NextDouble() * 360f);
                        else ModelKit.Spawn(ModelKit.Pick(ModelKit.PhotoRocks, rnd), root, new Vector3(x, 0, z), 0.6f + (float)rnd.NextDouble() * 0.9f, (float)rnd.NextDouble() * 360f);
                    }
                    break;
                case ArenaId.Dojo:
                {
                    Vector3 pit = new Vector3(-3f, 0, near + 1.5f);
                    if (ModelKit.Spawn("stone_fire_pit", root, pit, 0.45f, 20f) != null)
                        root.gameObject.AddComponent<Campfire>().At = pit + Vector3.up * 0.3f;
                    for (int i = 0; i < 4; i++)
                    {
                        float x = (i < 2 ? -20f : 12f) + (float)rnd.NextDouble() * 8f;
                        ModelKit.Spawn(ModelKit.Pick(ModelKit.PhotoRocks, rnd), root, new Vector3(x, 0, near + (float)rnd.NextDouble() * 3f), 0.5f + (float)rnd.NextDouble() * 0.7f, (float)rnd.NextDouble() * 360f);
                    }
                    break;
                }
                case ArenaId.MoonTemple:
                    // inside the ring that survives the collapse (r < 15.5), outside the rising platforms (r < 10)
                    for (int i = 0; i < 6; i++)
                    {
                        float x = -9f + i * 3.6f + ((float)rnd.NextDouble() - 0.5f) * 1.5f;
                        float z = Mathf.Min(11f + (float)rnd.NextDouble() * 2.5f, Mathf.Sqrt(Mathf.Max(0f, 15f * 15f - x * x)));
                        ModelKit.Spawn(ModelKit.Pick(ModelKit.PhotoMoonRocks, rnd), root, new Vector3(x, 0, z), 0.5f + (float)rnd.NextDouble() * 1f, (float)rnd.NextDouble() * 360f);
                    }
                    break;
            }
        }

        /// <summary>Small fire in the dojo's stone pit (particles only, no light slot).</summary>
        class Campfire : MonoBehaviour
        {
            public Vector3 At;
            float next;
            void Update()
            {
                if (Time.time < next) return;
                next = Time.time + 0.14f;
                Fx.Fire(At, 0.28f, 3);
            }
        }

        /// <summary>
        /// Layered silhouette backdrop: three flat cut-out ridges far behind the lane (tree lines, hills or a skyline),
        /// each a step further into the haze, so the side view reads as near / mid / far like a painted stage set.
        /// A few hundred vertices in the static mesh, the same on every preset.
        /// </summary>
        static void SilhouetteLayers(MeshBuilder mb, ArenaId id, bool winter)
        {
            Color[] cols;
            switch (id)
            {
                case ArenaId.Forest: cols = new[] { new Color(0.2f, 0.34f, 0.24f), new Color(0.36f, 0.46f, 0.4f), new Color(0.56f, 0.62f, 0.58f) }; break;
                case ArenaId.City: cols = new[] { new Color(0.2f, 0.19f, 0.24f), new Color(0.3f, 0.29f, 0.35f), new Color(0.42f, 0.4f, 0.47f) }; break;
                case ArenaId.MoonTemple: cols = new[] { new Color(0.12f, 0.12f, 0.2f), new Color(0.18f, 0.17f, 0.3f), new Color(0.26f, 0.24f, 0.4f) }; break;
                default:
                    cols = winter ? new[] { new Color(0.24f, 0.33f, 0.36f), new Color(0.44f, 0.52f, 0.6f), new Color(0.68f, 0.74f, 0.84f) }
                                  : new[] { new Color(0.26f, 0.38f, 0.3f), new Color(0.44f, 0.52f, 0.5f), new Color(0.64f, 0.68f, 0.7f) };
                    break;
            }
            var rnd = new System.Random(57 + (int)id);
            float[] zs = { 36f, 50f, 66f };
            float[] baseH = { 3.5f, 7f, 11f };
            var pts = new List<Vector2>(256);
            for (int layer = 0; layer < 3; layer++)
            {
                pts.Clear();
                float z = zs[layer], seed = (float)rnd.NextDouble() * 100f;
                const float X0 = -120f, X1 = 120f;
                if (id == ArenaId.City)
                {
                    // stepped skyline: blocks of varied width and height
                    float x = X0;
                    while (x < X1)
                    {
                        float w = 3f + (float)rnd.NextDouble() * (6f + layer * 3f);
                        float h = baseH[layer] + (float)rnd.NextDouble() * (6f + layer * 5f);
                        pts.Add(new Vector2(x, h)); pts.Add(new Vector2(x + w, h));
                        x += w;
                    }
                }
                else
                {
                    // rolling ridge; the near two layers carry a tree line (pointed firs) on top
                    bool trees = id != ArenaId.MoonTemple && layer < 2;
                    float step = trees ? 1.1f + layer * 0.6f : 3f;
                    bool peak = false;
                    for (float x = X0; x <= X1; x += step * (0.7f + 0.6f * (float)rnd.NextDouble()))
                    {
                        float ridge = baseH[layer] + Mathf.PerlinNoise(x * 0.025f + seed, seed) * (5f + layer * 6f) - 2.5f;
                        if (id == ArenaId.MoonTemple) ridge += Mathf.Abs(Mathf.PerlinNoise(x * 0.09f, seed) - 0.5f) * 6f; // jagged rock
                        float h = ridge;
                        if (trees) { peak = !peak; if (peak) h += 1.6f + (float)rnd.NextDouble() * (2.2f + layer * 1.2f); }
                        pts.Add(new Vector2(x, Mathf.Max(0.5f, h)));
                    }
                }
                mb.SetColor(Mat.C(cols[layer]));
                mb.Silhouette(pts, z, -2f, new Vector3(0f, 0.45f, -0.9f));
            }
        }

        /// <summary>Mid-ground band behind the lane so the side view has depth (static, merged).</summary>
        static void BackdropBand(MeshBuilder mb, MeshBuilder trees, ArenaId id)
        {
            var rnd = new System.Random(31 + (int)id);
            for (float x = -34f; x <= 34f; x += 3.2f + (float)rnd.NextDouble() * 2.5f)
            {
                float z = 10f + (float)rnd.NextDouble() * 12f;
                Vector3 p = new Vector3(x, 0, z);
                switch (id)
                {
                    case ArenaId.Forest:
                        if (ModelKit.Available) break; // hand-made models are placed by NatureBackdrop instead
                        if (rnd.NextDouble() < 0.55) Pine(trees, p, 8f + (float)rnd.NextDouble() * 6f, new Color(0.2f, 0.42f, 0.24f) * (0.85f + (float)rnd.NextDouble() * 0.3f));
                        else LeafTree(trees, p, 7f + (float)rnd.NextDouble() * 5f, new Color(0.3f, 0.5f, 0.22f) * (0.85f + (float)rnd.NextDouble() * 0.3f));
                        if (rnd.NextDouble() < 0.4) Rock(mb, p + new Vector3(1.5f, 0, -2f), 0.6f + (float)rnd.NextDouble(), new Color(0.48f, 0.48f, 0.46f));
                        break;
                    case ArenaId.City:
                        if (rnd.NextDouble() < 0.5)
                        {
                            float h = 3f + (float)rnd.NextDouble() * 6f;
                            mb.SetColor(Mat.C(0.5f, 0.47f, 0.48f));
                            mb.Box(p + Vector3.up * h * 0.5f, new Vector3(3f + (float)rnd.NextDouble() * 2f, h, 2f), Quaternion.Euler(0, (float)rnd.NextDouble() * 20f - 10f, (float)rnd.NextDouble() * 8f - 4f));
                        }
                        else Rock(mb, p, 0.8f + (float)rnd.NextDouble(), new Color(0.42f, 0.4f, 0.41f));
                        break;
                    case ArenaId.Dojo:
                        if (rnd.NextDouble() < 0.35) Pine(trees, p + Vector3.forward * 6f, 7f + (float)rnd.NextDouble() * 4f, new Color(0.32f, 0.56f, 0.3f));
                        break;
                }
            }
        }

        public static Arena Build(ArenaId id, Transform parent, WeatherType weather = WeatherType.Clear)
        {
            if (Current != null) Destroy(Current.gameObject);
            Destructible.All.Clear();
            SceneryShadow.Clear();
            blockers.Clear();
            WeatherSystem.ResetGlobals();
            var go = new GameObject("Arena_" + id);
            go.transform.SetParent(parent, false);
            var a = go.AddComponent<Arena>();
            a.Id = id;
            a.WeatherKind = weather;
            a.look = WeatherSystem.Look(weather, id);
            a.Winter = id == ArenaId.Dojo && weather == WeatherType.Snow;
            Current = a;
            Radius = 21f;
            var stat = new MeshBuilder();
            switch (id)
            {
                case ArenaId.Dojo: a.BuildDojo(stat); break;
                case ArenaId.Forest: a.BuildForest(stat); break;
                case ArenaId.City: a.BuildCity(stat); break;
                default: a.BuildMoon(stat); break;
            }
            a.ApplyTimeOfDay(PickTimeOfDay(id));
            BackdropBand(stat, a.treeMb, id);
            SilhouetteLayers(stat, id, a.Winter);
            // muted scenery, full-colour fighters: the eye goes to the fight (night arenas are muted already)
            Shader.SetGlobalFloat("_KageEnvMute", id == ArenaId.Dojo || id == ArenaId.Forest ? 0.38f : 0.2f);
            if (id == ArenaId.Forest && ModelKit.Available) a.NatureBackdrop();
            if (ModelKit.PhotoProps) a.PhotoScans();
            a.AddStaticMesh(stat, "Static");
            if (a.groundMb != null) a.AddStaticMesh(a.groundMb, "GroundPhoto", a.groundMat);
            a.AddStaticMesh(a.treeMb, "Trees", Mat.ToonTree);
            switch (id)
            {
                case ArenaId.Forest: Mat.SetWind(new Vector2(0.9f, 0.4f), 1.1f); break;
                case ArenaId.City: Mat.SetWind(new Vector2(1f, 0.2f), 1.8f, 1.3f); break; // storm
                case ArenaId.MoonTemple: Mat.SetWind(new Vector2(0.7f, 0.7f), 0.6f, 0.8f); break;
                default: Mat.SetWind(new Vector2(0.85f, 0.5f), 0.9f); break;
            }
            // weather: stronger wind in rain and storms, a calm hush in snow and fog
            ApplyWind(weather);
            // the arena's own drifting dust / sparkles don't belong in a downpour (forest leaves still blow about)
            if (a.weatherGo != null && HidesAmbientParticles(id, weather)) a.weatherGo.SetActive(false);
            a.BuildClouds();
            if (weather != WeatherType.Clear)
            {
                var wgo = new GameObject("Weather_" + weather);
                wgo.transform.SetParent(go.transform, false);
                a.WeatherFx = wgo.AddComponent<WeatherSystem>();
                a.WeatherFx.Begin(a, weather);
            }
            LaneHalf = 17f;
            a.LayoutPropsForLane();
            // grass goes in last, around everything that now stands on the ground
            a.PlantGrass();
            a.PlayerSpawn = new Vector3(-5f, 0f, 0f);
            a.EnemySpawn = new Vector3(5f, 0f, 0f);
            a.BuildWalls(Radius);
            a.ApplyLighting();
            // trees, gates and buildings throw baked sun shadows (High / Ultra)
            SceneryShadow.Bake(go.transform, a.sunDir);
            GraphicsQuality.Changed += a.RefreshSceneryShadow;
            return a;
        }

        // ================= shared helpers =================
        struct GrassSpec { public int count; public float rMin, rMax; public Color c; public int seed; }
        readonly List<GrassSpec> grassSpecs = new List<GrassSpec>();
        // ground footprints that grass must not grow through: x, z centre and half sizes
        static readonly List<Vector4> blockers = new List<Vector4>();
        static bool noBlock; // building a breakable prop's own mesh at the origin: not a footprint

        static void Block(Vector3 c, float hx, float hz)
        {
            if (!noBlock) blockers.Add(new Vector4(c.x, c.z, hx, hz));
        }

        static bool Blocked(Vector3 p, float margin)
        {
            for (int i = 0; i < blockers.Count; i++)
            {
                var b = blockers[i];
                if (Mathf.Abs(p.x - b.x) < b.z + margin && Mathf.Abs(p.z - b.y) < b.w + margin) return true;
            }
            return false;
        }

        void Grass(int count, float rMin, float rMax, Color c, int seed) =>
            grassSpecs.Add(new GrassSpec { count = count, rMin = rMin, rMax = rMax, c = c, seed = seed });

        /// <summary>Builds the swaying grass mesh after every prop is in place: tufts that would grow out of rocks,
        /// trunks, houses, crates or fences are skipped.</summary>
        void PlantGrass()
        {
            if (grassSpecs.Count == 0) return;
            foreach (var r in GetComponentsInChildren<Renderer>())
            {
                if (!(r is MeshRenderer) && !(r is SkinnedMeshRenderer)) continue;
                string n = r.gameObject.name;
                if (n == "Static" || n == "GroundPhoto" || n == "Trees" || n == "Grass") continue;
                var b = r.bounds;
                if (b.min.y > 0.5f || b.size.y < 0.12f) continue;   // floating things and flat decals
                if (b.size.x > 30f || b.size.z > 30f) continue;     // ground plates, sky, water
                float hx = b.extents.x, hz = b.extents.z;
                // tall and narrow (trees, banners, lamp posts): only the trunk is on the ground
                if (b.size.y > 3f && Mathf.Min(hx, hz) < 4f) hx = hz = Mathf.Min(hx, hz) * 0.25f + 0.2f;
                blockers.Add(new Vector4(b.center.x, b.center.z, hx, hz));
            }
            var mb = new MeshBuilder();
            foreach (var g in grassSpecs) GrassTufts(mb, g.count, g.rMin, g.rMax, g.c, g.seed, Id == ArenaId.Forest);
            grassSpecs.Clear();
            AddStaticMesh(mb, "Grass", Mat.ToonWind);
        }
        MeshBuilder treeMb = new MeshBuilder(); // background trees, swayed by the wind shader

        void AddStaticMesh(MeshBuilder mb, string name) => AddStaticMesh(mb, name, Mat.Toon);

        void AddStaticMesh(MeshBuilder mb, string name, Material mat)
        {
            if (mb.V.Count == 0) return;
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mb.ToMesh(name);
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            go.isStatic = true;
        }

        void BuildWalls(float r)
        {
            foreach (var w in walls) Destroy(w);
            walls.Clear();
            // rectangular lane boundary
            AddWall(new Vector3(-LaneHalf - 0.5f, 5f, 0f), new Vector3(1f, 12f, LaneDepth * 2f + 4f));
            AddWall(new Vector3(LaneHalf + 0.5f, 5f, 0f), new Vector3(1f, 12f, LaneDepth * 2f + 4f));
            AddWall(new Vector3(0f, 5f, -LaneDepth - 0.5f), new Vector3(LaneHalf * 2f + 2f, 12f, 1f));
            AddWall(new Vector3(0f, 5f, LaneDepth + 0.5f), new Vector3(LaneHalf * 2f + 2f, 12f, 1f));
            // ground collider
            var g = new GameObject("GroundCollider");
            g.transform.SetParent(transform, false);
            var gc = g.AddComponent<BoxCollider>();
            gc.center = new Vector3(0, -0.5f, 0);
            gc.size = new Vector3(120f, 1f, 120f);
            walls.Add(g);
        }

        void AddWall(Vector3 pos, Vector3 size)
        {
            var go = new GameObject("Wall");
            go.transform.SetParent(transform, false);
            go.transform.position = pos;
            go.AddComponent<BoxCollider>().size = size;
            walls.Add(go);
        }

        public void SetRadius(float r)
        {
            Radius = r;
            LaneHalf = Mathf.Min(17f, r - 2.5f);
            BuildWalls(r);
            // anyone left outside the new boundary is pulled back in
            for (int i = 0; i < Fighter.All.Count; i++)
            {
                var f = Fighter.All[i];
                if (f == null) continue;
                Vector3 cl = ClampInside(f.transform.position, 1f);
                if (KMath.Flat(cl - f.transform.position).sqrMagnitude < 0.01f) continue;
                Fx.Smoke(f.Center, 0.6f, new Color(0.8f, 0.8f, 1f, 0.5f), 6);
                f.Teleport(ClampInside(f.transform.position, 2f));
            }
        }

        /// <summary>Sky dome. With a texture name it shows a photographed sky (Poly Haven, CC0) from Resources/Kagegan/Sky,
        /// tinted towards the arena palette; otherwise a vertex-colour gradient.</summary>
        void Sky(Color top, Color horizon, Color bottom, string texName = null, Color? tint = null)
        {
            skyTop = top; skyHorizon = horizon;
            Texture2D skyTex = null;
            if (!string.IsNullOrEmpty(texName))
            {
                // strong phones get the 4K panorama (4096 px around), the rest the 2K one
                if (GraphicsQuality.Current >= GraphicsPreset.High) skyTex = Resources.Load<Texture2D>("Kagegan/Sky/" + texName + "_4k");
                if (skyTex == null) skyTex = Resources.Load<Texture2D>("Kagegan/Sky/" + texName);
            }
            Color tintC = tint ?? Color.white;
            // wet streets and puddles mirror this sky (Kagegan/Surface)
            Shader.SetGlobalTexture("_KageSkyTex", skyTex != null ? (Texture)skyTex : Texture2D.grayTexture);
            Shader.SetGlobalColor("_KageSkyTint", skyTex != null ? Color.Lerp(Color.white, horizon, 0.18f) * tintC : horizon);
            var mb = new MeshBuilder();
            int lat = skyTex != null ? 18 : 10, lon = skyTex != null ? 32 : 16;
            int start = 0;
            for (int y = 0; y <= lat; y++)
            {
                float v = (float)y / lat, phi = v * Mathf.PI;
                float h = Mathf.Cos(phi);
                Color c = h > 0f ? Color.Lerp(horizon, top, Mathf.Pow(h, 0.6f)) : Color.Lerp(horizon, bottom, Mathf.Clamp01(-h * 4f));
                for (int x = 0; x <= lon; x++)
                {
                    float th = (float)x / lon * Mathf.PI * 2f;
                    Vector3 n = new Vector3(Mathf.Sin(phi) * Mathf.Cos(th), h, Mathf.Sin(phi) * Mathf.Sin(th));
                    if (skyTex != null)
                    {
                        // the image holds the top 55% of an equirectangular panorama (zenith .. just below the horizon)
                        float tv = Mathf.Clamp01(1f - v / 0.55f);
                        Color tc = Color.Lerp(Color.white, c, 0.18f) * tintC; tc.a = 1f;
                        mb.V.Add(n * 140f); mb.N.Add(-n); mb.C.Add(tc); mb.UV.Add(new Vector2((float)x / lon, tv)); mb.Bone.Add(0);
                    }
                    else { mb.V.Add(n * 140f); mb.N.Add(-n); mb.C.Add(c); mb.UV.Add(Vector2.zero); mb.Bone.Add(0); }
                }
            }
            for (int y = 0; y < lat; y++)
                for (int x = 0; x < lon; x++)
                {
                    int a = start + y * (lon + 1) + x, b = a + lon + 1;
                    mb.T.Add(a); mb.T.Add(a + 1); mb.T.Add(b);
                    mb.T.Add(a + 1); mb.T.Add(b + 1); mb.T.Add(b);
                }
            var go = new GameObject("Sky");
            go.transform.SetParent(transform, false);
            skyMesh = mb.ToMesh("sky");
            go.AddComponent<MeshFilter>().sharedMesh = skyMesh;
            skyRenderer = go.AddComponent<MeshRenderer>();
            if (skyTex != null)
            {
                skyTex.wrapModeU = TextureWrapMode.Repeat;
                skyTex.wrapModeV = TextureWrapMode.Clamp;
                var sm = new Material(Mat.Sky) { name = "Sky_" + texName };
                sm.SetTexture("_MainTex", skyTex);
                skyRenderer.sharedMaterial = sm;
            }
            else skyRenderer.sharedMaterial = Mat.Sky;
            skyRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            go.AddComponent<FollowCameraXZ>();
        }

        void Lighting(Vector3 dirToSun, Color sun, Color amb, Color fog, float fStart, float fEnd, float fMax)
        {
            sunDir = dirToSun; sunCol = sun; ambCol = amb; fogCol = fog; fogStart = fStart; fogEnd = fEnd; fogMax = fMax;
        }

        // weather look blending during a mid-fight weather change (lookFrom -> look)
        WeatherLook? lookFrom;
        float lookBlend = 1f, lookBlendSpeed;
        MaterialPropertyBlock skyMpb;

        /// <summary>"No weather" expressed as a look, so two looks can be blended.</summary>
        WeatherLook Neutral => new WeatherLook { Sun = 1f, Amb = 1f, FogLerp = 0f, FogNear = 1f, FogMax = 0f, SunTint = Color.white, Fog = fogCol, Sky = Color.white };

        WeatherLook? CurLook
        {
            get
            {
                if (lookBlend >= 1f || (!lookFrom.HasValue && !look.HasValue)) return look;
                WeatherLook a = lookFrom ?? Neutral, b = look ?? Neutral;
                float t = Mathf.SmoothStep(0f, 1f, lookBlend);
                return new WeatherLook
                {
                    Sun = Mathf.Lerp(a.Sun, b.Sun, t), Amb = Mathf.Lerp(a.Amb, b.Amb, t),
                    FogLerp = Mathf.Lerp(a.FogLerp, b.FogLerp, t), FogNear = Mathf.Lerp(a.FogNear, b.FogNear, t), FogMax = Mathf.Lerp(a.FogMax, b.FogMax, t),
                    SunTint = Color.Lerp(a.SunTint, b.SunTint, t), Fog = Color.Lerp(a.Fog, b.Fog, t), Sky = Color.Lerp(a.Sky, b.Sky, t)
                };
            }
        }

        Color SunNow { get { var l = CurLook; return l.HasValue ? sunCol * l.Value.Sun * l.Value.SunTint : sunCol; } }
        Color AmbNow { get { var l = CurLook; return l.HasValue ? ambCol * l.Value.Amb : ambCol; } }

        public void ApplyLighting()
        {
            var cl = CurLook;
            WorldLighting.SetSun(sunDir, SunNow, AmbNow);
            if (cl.HasValue)
            {
                var l = cl.Value;
                WorldLighting.SetFog(Color.Lerp(fogCol, l.Fog * todFog, l.FogLerp), fogStart * l.FogNear, fogEnd * Mathf.Lerp(1f, l.FogNear, 0.6f), Mathf.Max(fogMax, l.FogMax));
            }
            else WorldLighting.SetFog(fogCol, fogStart, fogEnd, fogMax);
            if (skyRenderer != null && dimensionUntil <= 0f)
            {
                if (skyMpb == null) skyMpb = new MaterialPropertyBlock();
                skyMpb.Clear();
                if (cl.HasValue || todSky != Color.white) skyMpb.SetColor("_Color", (cl.HasValue ? cl.Value.Sky : Color.white) * todSky);
                skyRenderer.SetPropertyBlock(skyMpb);
            }
            if (CameraRig.Inst != null) CameraRig.Inst.Cam.backgroundColor = cl.HasValue ? skyHorizon * cl.Value.Sky : skyHorizon;
            PostFx.SetGrade(Id);
        }

        /// <summary>
        /// Weather changes during the fight: the old precipitation thins out, the new one builds up, light, fog,
        /// sky, wind and the ambience loop blend over `seconds`.
        /// </summary>
        public void ChangeWeather(WeatherType to, float seconds = 14f)
        {
            if (to == WeatherKind) return;
            lookFrom = CurLook;
            look = WeatherSystem.Look(to, Id);
            lookBlend = 0f;
            lookBlendSpeed = 1f / Mathf.Max(1f, seconds);
            WeatherKind = to;
            ApplyWind(to);
            if (WeatherFx != null) WeatherFx.FadeOut(seconds * 0.7f);
            var wgo = new GameObject("Weather_" + to);
            wgo.transform.SetParent(transform, false);
            WeatherFx = wgo.AddComponent<WeatherSystem>();
            WeatherFx.Begin(this, to, seconds);
            // the arena's own drifting dust only shows in dry weather
            if (weatherGo != null) weatherGo.SetActive(!HidesAmbientParticles(Id, to));
            var (n, v) = WeatherFx.Ambience();
            Sfx.FadeAmbience(n, v, Mathf.Min(6f, seconds * 0.5f));
            if (to == WeatherType.Storm) stormTimer = Mathf.Max(stormTimer, seconds * 0.6f); // first strike once the clouds have rolled in
        }

        static bool HidesAmbientParticles(ArenaId id, WeatherType w) =>
            WeatherSystem.Precipitation(w) && !(id == ArenaId.Forest && w != WeatherType.Snow) && !(id == ArenaId.MoonTemple && w == WeatherType.Snow);

        static void ApplyWind(WeatherType weather)
        {
            Vector4 wind = Shader.GetGlobalVector("_KageWind");
            switch (weather)
            {
                case WeatherType.Rain: Mat.SetWind(new Vector2(wind.x, wind.y), Mathf.Max(wind.z, 1.5f), Mathf.Max(wind.w, 1.15f)); break;
                case WeatherType.Storm: Mat.SetWind(new Vector2(wind.x, wind.y), Mathf.Max(wind.z, 1.9f), Mathf.Max(wind.w, 1.3f)); break;
                case WeatherType.Downpour: Mat.SetWind(new Vector2(wind.x, wind.y), Mathf.Max(wind.z, 2.1f), Mathf.Max(wind.w, 1.35f)); break;
                case WeatherType.Snow: Mat.SetWind(new Vector2(wind.x, wind.y), Mathf.Min(wind.z, 0.7f), 0.8f); break;
                case WeatherType.Fog: Mat.SetWind(new Vector2(wind.x, wind.y), Mathf.Min(wind.z, 0.45f), 0.7f); break;
            }
        }

        // scheduled mid-fight change (set by WeatherSystem.PlanChange)
        float weatherChangeAt = -1f;
        WeatherType weatherChangeTo;
        public void ScheduleWeatherChange(WeatherType to, float delay) { weatherChangeTo = to; weatherChangeAt = Time.time + delay; }

        /// <summary>Temporarily shift into another "dimension" (Moon boss): recolors sky, sun and fog.</summary>
        public void DimensionShift(Color sun, Color amb, Color fog, Color skyTint, float seconds)
        {
            dimensionUntil = Time.time + seconds;
            WorldLighting.SetSun(sunDir, sun, amb);
            WorldLighting.SetFog(fog, fogStart * 0.6f, fogEnd * 0.8f, Mathf.Max(fogMax, 0.6f));
            if (skyRenderer != null)
            {
                var mpb = new MaterialPropertyBlock();
                mpb.SetColor("_Color", skyTint);
                skyRenderer.SetPropertyBlock(mpb);
            }
        }

        float stormTimer = 5f;

        /// <summary>Drifting cloud layer made from the CC0 smoke-puff atlas.</summary>
        void BuildClouds()
        {
            var tex = Resources.Load<Texture2D>("Kagegan/Vfx/puff_white");
            if (tex == null) return;
            Color c; int count;
            switch (Id)
            {
                case ArenaId.City: c = new Color(0.35f, 0.33f, 0.4f, 0.85f); count = 13; break;
                case ArenaId.MoonTemple: c = new Color(0.45f, 0.38f, 0.7f, 0.35f); count = 9; break;
                case ArenaId.Forest: c = new Color(0.95f, 0.95f, 0.92f, 0.7f); count = 16; break;
                default: c = new Color(1f, 1f, 1f, 0.8f); count = 14; break;
            }
            // overcast: heavier, darker cloud cover
            switch (WeatherKind)
            {
                case WeatherType.Rain: if (Id != ArenaId.City) { c = new Color(c.r * 0.6f, c.g * 0.62f, c.b * 0.7f, 0.9f); count += 8; } break;
                case WeatherType.Downpour:
                case WeatherType.Storm: if (Id != ArenaId.City) { c = new Color(c.r * 0.42f, c.g * 0.43f, c.b * 0.52f, 0.92f); count += 10; } break;
                case WeatherType.Snow: c = new Color(Mathf.Lerp(c.r, 0.85f, 0.5f), Mathf.Lerp(c.g, 0.87f, 0.5f), Mathf.Lerp(c.b, 0.92f, 0.5f), 0.88f); count += 6; break;
                case WeatherType.Fog: c.a = Mathf.Min(1f, c.a + 0.1f); count += 4; break;
            }
            // evening / night: clouds take the sky colour instead of glowing white
            if (todSky != Color.white) { float al = c.a; c *= Color.Lerp(todSky, Color.white, 0.25f); c.a = al; }
            // huge alpha quads: each one is a full-screen-sized layer of overdraw, so Ultra gets no extra clouds
            count = Mathf.RoundToInt(count * Mathf.Clamp(GraphicsQuality.WeatherMul, 0.5f, 1f));
            var go = new GameObject("Clouds");
            go.transform.SetParent(transform, false);
            go.transform.position = new Vector3(0f, 55f, 0f);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = true; main.prewarm = true;
            main.startLifetime = 120f; main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(28f, 55f);
            main.startColor = c;
            main.maxParticles = count;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            var em = ps.emission; em.rateOverTime = count / 120f;
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Box; sh.scale = new Vector3(200f, 18f, 200f);
            var vel = ps.velocityOverLifetime; vel.enabled = true; vel.space = ParticleSystemSimulationSpace.World;
            vel.x = new ParticleSystem.MinMaxCurve(1.2f, 2.2f); vel.y = new ParticleSystem.MinMaxCurve(0f, 0f); vel.z = new ParticleSystem.MinMaxCurve(0.3f, 0.8f);
            var tsa = ps.textureSheetAnimation; tsa.enabled = true; tsa.numTilesX = 2; tsa.numTilesY = 2;
            tsa.frameOverTime = new ParticleSystem.MinMaxCurve(0f); tsa.startFrame = new ParticleSystem.MinMaxCurve(0f, 3.999f);
            var col = ps.colorOverLifetime; col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) }, new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(1, 0.1f), new GradientAlphaKey(1, 0.9f), new GradientAlphaKey(0, 1) });
            col.color = g;
            var r = go.GetComponent<ParticleSystemRenderer>();
            tex.wrapMode = TextureWrapMode.Clamp;
            r.sharedMaterial = Mat.FxTex(tex, false, "Clouds");
            r.sharedMaterial.renderQueue = 1500; // behind the arena, in front of the sky dome
            r.maxParticleSize = 1.1f; // a cloud may not grow past the screen: big sky overdraw was the City/Moon fps drop
            ps.Play();
        }

        void Storm()
        {
            // thunderstorm over the ruined city: lightning flashes the whole scene, thunder follows
            stormTimer -= Time.deltaTime;
            if (stormTimer > 0f) return;
            stormTimer = Random.Range(6f, 13f);
            Vector3 p = new Vector3(Random.Range(-45f, 45f), 0f, Random.Range(25f, 60f)) * (Random.value < 0.5f ? 1f : -1f);
            Fx.Lightning(p + Vector3.up * 70f, p, new Color(0.8f, 0.85f, 1f), 0.6f, 0.35f);
            // brighter flash on a dark night, so it always reads
            Color sun = SunNow;
            WorldLighting.SetSun(sunDir, sun * 2.4f + new Color(0.25f, 0.27f, 0.32f), AmbNow * 2f + new Color(0.1f, 0.1f, 0.13f));
            Invoke(nameof(ApplyLighting), 0.12f);
            if (Random.value < 0.35f) Invoke(nameof(Reflash), 0.2f); // lightning often strikes twice
            // sound travels ~340 m/s: near strikes crack almost at once and loudly, far ones rumble later
            float dist = new Vector2(p.x, p.z).magnitude;
            StartCoroutine(Thunder(Mathf.Lerp(0.25f, 1.6f, Mathf.InverseLerp(30f, 75f, dist)), Mathf.Lerp(0.9f, 0.55f, Mathf.InverseLerp(30f, 75f, dist))));
        }

        void Reflash()
        {
            WorldLighting.SetSun(sunDir, SunNow * 1.8f, AmbNow * 1.6f);
            Invoke(nameof(ApplyLighting), 0.08f);
        }

        System.Collections.IEnumerator Thunder(float delay, float volume)
        {
            yield return new WaitForSeconds(delay);
            // recorded thunder (CC-BY, see credits); falls back to the synthesised one
            if (!Sfx.WeatherSound("thunder", 6, volume)) Sfx.Play(SfxId.Thunder, 0.55f, 0.7f);
            CameraRig.Shake(0.06f + volume * 0.06f);
        }

        void Update()
        {
            if (weatherChangeAt > 0f && Time.time >= weatherChangeAt && dimensionUntil <= 0f)
            {
                weatherChangeAt = -1f;
                ChangeWeather(weatherChangeTo);
            }
            if (lookBlend < 1f && dimensionUntil <= 0f)
            {
                lookBlend = Mathf.Min(1f, lookBlend + Time.deltaTime * lookBlendSpeed);
                // relight a few times a second (cheap) and on the final step
                if (lookBlend >= 1f || Time.frameCount % 4 == 0) ApplyLighting();
            }
            if (WeatherKind == WeatherType.Storm && dimensionUntil <= 0f) Storm();
            if (dimensionUntil > 0f && Time.time > dimensionUntil)
            {
                dimensionUntil = 0f;
                ApplyLighting(); // also restores the weather's sky tint
                Fx.Ring(Vector3.up * 0.2f, Color.white, 1f, 30f, 0.6f, Quaternion.identity);
            }
        }

        void Weather(Color c, float rate, float size, Vector3 velocity, float spread, bool stretch, float life, Material m)
        {
            weatherGo = new GameObject("Weather");
            weatherGo.transform.SetParent(transform, false);
            var ps = weatherGo.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = true;
            main.startLifetime = life;
            main.startSpeed = 0f;
            main.startSize = size;
            main.startColor = c;
            main.maxParticles = Mathf.CeilToInt(rate * life * 1.2f);
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            var em = ps.emission;
            weatherRate = rate;
            em.rateOverTime = rate * GraphicsQuality.WeatherMul;
            var sh = ps.shape;
            sh.shapeType = ParticleSystemShapeType.Box;
            sh.scale = new Vector3(spread, 1f, spread);
            var vel = ps.velocityOverLifetime;
            vel.enabled = true;
            vel.space = ParticleSystemSimulationSpace.World;
            vel.x = new ParticleSystem.MinMaxCurve(velocity.x - 0.5f, velocity.x + 0.5f);
            vel.y = new ParticleSystem.MinMaxCurve(velocity.y * 0.8f, velocity.y * 1.2f);
            vel.z = new ParticleSystem.MinMaxCurve(velocity.z - 0.5f, velocity.z + 0.5f);
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) }, new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(1, 0.15f), new GradientAlphaKey(1, 0.8f), new GradientAlphaKey(0, 1) });
            col.color = g;
            var r = weatherGo.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = m;
            if (stretch) { r.renderMode = ParticleSystemRenderMode.Stretch; r.velocityScale = 0.03f; r.lengthScale = 2f; }
            weatherGo.transform.position = new Vector3(0, 12f, 0);
            ps.Play();
            weather = ps;
            GraphicsQuality.Changed += RefreshWeather;
        }

        void RefreshWeather()
        {
            if (weather == null) return;
            var em = weather.emission;
            em.rateOverTime = weatherRate * GraphicsQuality.WeatherMul;
        }

        void OnDestroy()
        {
            GraphicsQuality.Changed -= RefreshWeather;
            GraphicsQuality.Changed -= RefreshSceneryShadow;
            if (Current == this) { Current = null; SceneryShadow.Clear(); }
        }

        // quality changed from the pause menu: drop or (re)bake the scenery shadows to match
        void RefreshSceneryShadow()
        {
            if (GraphicsQuality.Current < GraphicsPreset.High) SceneryShadow.Clear();
            else if (!SceneryShadow.Active) SceneryShadow.Bake(transform, sunDir);
        }

        // ---- ground ----
        // photo-textured ground (Poly Haven, CC0) in its own mesh with the Surface shader (lite variant on Low/Medium)
        MeshBuilder groundMb;
        Material groundMat;

        /// <summary>Photo ground (full on High/Ultra, lite on Low/Medium). photo = texture set in Resources/Kagegan/Ground (ground_&lt;photo&gt;_diff/_nor),
        /// mean = that photo's average colour (so it can be tinted towards the arena palette).</summary>
        void PhotoGround(string photo, Color palette, Color mean, float tile, float wet = 0f, float tintAmount = 0.7f)
        {
            if (!Mat.GroundTextures) return;
            var diff = Resources.Load<Texture2D>("Kagegan/Ground/ground_" + photo + "_diff");
            var nor = Mat.PhotoSurfaces ? Resources.Load<Texture2D>("Kagegan/Ground/ground_" + photo + "_nor") : null; // lite ground skips the normal map
            groundMat = Mat.GroundSurface("Ground_" + photo, diff, nor, Mat.TintTowards(palette, mean, tintAmount), tile, 1f, wet);
            if (groundMat != null) groundMb = new MeshBuilder();
        }

        void Ground(MeshBuilder mb, float r, Color a, Color b, Color edge, int seed, float yOffset = 0f)
        {
            var rnd = new System.Random(seed);
            int rings = 14, seg = 40;
            // photo ground: the vertex colour keeps the patchy colour variation and darker rim, around a neutral 0.5
            bool photo = groundMb != null && yOffset == 0f;
            if (photo) mb = groundMb;
            Color mid = (a + b) * 0.5f;
            int s = mb.V.Count;
            for (int i = 0; i <= rings; i++)
            {
                float rr = r * Mathf.Pow((float)i / rings, 1.15f);
                for (int j = 0; j <= seg; j++)
                {
                    float ang = (float)j / seg * Mathf.PI * 2f;
                    Vector3 p = new Vector3(Mathf.Cos(ang) * rr, 0, Mathf.Sin(ang) * rr);
                    float n = Mathf.PerlinNoise(p.x * 0.09f + seed, p.z * 0.09f);
                    Color c = Color.Lerp(a, b, n);
                    if (rr > Radius + 2f)
                    {
                        c = Color.Lerp(c, edge, Mathf.Clamp01((rr - Radius - 2f) / 10f));
                        // the rim rises into hills; a sunken floor (moon temple abyss) stays flat so it never pokes above the arena
                        if (yOffset == 0f) p.y = Mathf.Max(0, (rr - Radius - 6f) * 0.25f) * (0.6f + n);
                    }
                    p.y += yOffset;
                    if (photo) c = new Color(0.5f * c.r / Mathf.Max(0.05f, mid.r), 0.5f * c.g / Mathf.Max(0.05f, mid.g), 0.5f * c.b / Mathf.Max(0.05f, mid.b));
                    mb.V.Add(p); mb.N.Add(Vector3.up); mb.C.Add(Mat.C(c)); mb.UV.Add(Vector2.zero); mb.Bone.Add(0);
                }
            }
            for (int i = 0; i < rings; i++)
                for (int j = 0; j < seg; j++)
                {
                    int i0 = s + i * (seg + 1) + j, i1 = i0 + seg + 1;
                    // clockwise seen from above (Unity front face): the old order was back-facing, so the disc was culled
                    // and the sky showed through as a flat blue-grey floor
                    mb.T.Add(i0); mb.T.Add(i0 + 1); mb.T.Add(i1);
                    mb.T.Add(i0 + 1); mb.T.Add(i1 + 1); mb.T.Add(i1);
                }
        }

        static void GrassTufts(MeshBuilder mb, int count, float rMin, float rMax, Color c, int seed, bool forest)
        {
            // Low stays light for weak GPUs; High/Ultra get a dense, visibly swaying lawn
            int blades;
            switch (GraphicsQuality.Current)
            {
                case GraphicsPreset.Low: count /= 3; blades = 4; break;
                case GraphicsPreset.Medium: blades = 7; break;
                case GraphicsPreset.High: count *= 2; blades = 9; break;
                default: count = count * 5 / 2; blades = 9; break; // was x3 / 10 blades: ~25% fewer triangles
            }
            count = Mathf.Max(20, Mathf.RoundToInt(count * GrassBend.DensityMul));
            bool low = GraphicsQuality.Current == GraphicsPreset.Low;
            Color straw = new Color(0.72f, 0.64f, 0.36f);
            Color[] petals =
            {
                new Color(0.95f, 0.85f, 0.3f), new Color(0.78f, 0.55f, 0.9f), new Color(0.95f, 0.6f, 0.7f),
                new Color(0.55f, 0.65f, 0.95f), new Color(0.86f, 0.84f, 0.76f), new Color(0.95f, 0.5f, 0.3f)
            };
            var rnd = new System.Random(seed);
            int laneCount = forest ? Mathf.CeilToInt(count * 0.6f) : 0;
            int columns = Mathf.Max(1, Mathf.CeilToInt(Mathf.Sqrt(laneCount * LaneHalf / LaneDepth)));
            int rows = Mathf.Max(1, Mathf.CeilToInt((float)laneCount / columns));
            for (int i = 0; i < count; i++)
            {
                float u = (float)rnd.NextDouble(), v = (float)rnd.NextDouble();
                float ang = u * Mathf.PI * 2f;
                float rr = Mathf.Sqrt(Mathf.Lerp(rMin * rMin, rMax * rMax, v));
                Vector3 p = new Vector3(Mathf.Cos(ang) * rr, 0f, Mathf.Sin(ang) * rr);
                if (i < laneCount)
                {
                    // Put most forest grass where fights happen, with a jittered grid to avoid bare gaps.
                    p.x = Mathf.Lerp(-LaneHalf, LaneHalf, (i % columns + u) / columns);
                    p.z = Mathf.Lerp(-LaneDepth - 1f, LaneDepth + 1f, (i / columns + v) / rows);
                }
                float kind = (float)rnd.NextDouble();
                float tall = (0.35f + 0.3f * (float)rnd.NextDouble()) * (GraphicsQuality.Current >= GraphicsPreset.High ? 1.3f : 1f);
                if (Blocked(p, 0.12f)) continue;
                // between the camera and the lane: only short grass, never tall or bushy clumps that cover the fighters
                bool camSide = p.z < -LaneDepth - 0.5f;
                if (camSide) { tall = Mathf.Min(tall, 0.42f); if (kind >= 0.08f && kind < 0.22f) kind = 0.5f; }
                // patches: large slow noise mixes lush and drier grass so the field is not one flat colour
                float patch = Mathf.PerlinNoise(p.x * 0.07f + seed, p.z * 0.07f);
                Color tint = Color.Lerp(c, Color.Lerp(c, straw, 0.45f), Mathf.Clamp01((patch - 0.45f) * 2.2f)) * (0.8f + 0.4f * (float)rnd.NextDouble());
                int n = blades;
                float width = 0.045f, lean = 0.15f;
                if (kind < 0.08f && !low)
                {
                    // flower clump: thin stems, one to three heads at the stem tips
                    Color pc = petals[rnd.Next(petals.Length)] * (0.85f + 0.15f * (float)rnd.NextDouble());
                    int heads = 1 + rnd.Next(3);
                    for (int k = 0; k < heads; k++)
                    {
                        float yaw = (float)rnd.NextDouble() * Mathf.PI * 2f;
                        Vector3 off = new Vector3(Mathf.Sin(yaw), 0, Mathf.Cos(yaw)) * 0.06f * (float)rnd.NextDouble();
                        float h = tall * (0.9f + 0.4f * (float)rnd.NextDouble());
                        float ln = 0.08f + 0.12f * (float)rnd.NextDouble();
                        mb.Blade(p + off, h, 0.018f, yaw, ln, Mat.C(tint * 0.5f), Mat.C(tint * 0.95f));
                        Vector3 tip = p + off + Vector3.up * h + new Vector3(Mathf.Sin(yaw), 0, Mathf.Cos(yaw)) * ln * h;
                        mb.Flower(tip, 0.05f + 0.03f * (float)rnd.NextDouble(), 5 + rnd.Next(2), (float)rnd.NextDouble() * 6.28f, Mat.C(pc), Mat.C(new Color(0.95f, 0.75f, 0.25f)));
                    }
                    n = Mathf.Max(3, blades / 2); // a few leaves around the stems
                }
                else if (kind < 0.16f)
                {
                    // tall dry clump
                    tint = Color.Lerp(tint, straw, 0.6f);
                    tall *= 1.5f; n = blades + 3; lean = 0.25f;
                }
                else if (kind < 0.22f)
                {
                    // broad leafy clump, darker, low and wide like a small bush
                    tint *= 0.78f; tall *= 0.85f; n = blades + 4; width = 0.09f; lean = 0.4f;
                }
                for (int k = 0; k < n; k++)
                {
                    // blades fan out from the clump centre and arc outwards
                    float yaw = (float)rnd.NextDouble() * Mathf.PI * 2f;
                    Vector3 off = new Vector3(Mathf.Sin(yaw), 0, Mathf.Cos(yaw)) * 0.07f * (float)rnd.NextDouble();
                    float h = tall * (0.6f + 0.5f * (float)rnd.NextDouble());
                    Color tip = Color.Lerp(tint * 1.25f, new Color(0.85f, 0.85f, 0.45f), 0.12f + 0.15f * (float)rnd.NextDouble());
                    // roots are darker: the base of a clump sits in its own shade
                    mb.Blade(p + off, h, width + 0.025f * (float)rnd.NextDouble(), yaw, lean + 0.35f * (float)rnd.NextDouble(), Mat.C(tint * 0.45f), Mat.C(tip));
                }
            }
            if (!forest || low) return;
            // fallen leaves in the forest: still on the ground, in warm autumn colours
            Color[] leafCols = { new Color(0.62f, 0.38f, 0.16f), new Color(0.75f, 0.55f, 0.2f), new Color(0.5f, 0.3f, 0.15f), new Color(0.55f, 0.5f, 0.2f) };
            int leaves = Mathf.RoundToInt(count * 0.9f);
            for (int i = 0; i < leaves; i++)
            {
                float ang = (float)rnd.NextDouble() * Mathf.PI * 2f, rr = Mathf.Lerp(rMin, rMax, Mathf.Sqrt((float)rnd.NextDouble()));
                Vector3 p = new Vector3(Mathf.Cos(ang) * rr, 0.015f + 0.004f * (float)rnd.NextDouble(), Mathf.Sin(ang) * rr);
                if (Blocked(p, 0.05f)) continue;
                mb.Leaf(p, 0.09f + 0.06f * (float)rnd.NextDouble(), (float)rnd.NextDouble() * 6.28f, Mat.C(leafCols[rnd.Next(leafCols.Length)] * (0.85f + 0.3f * (float)rnd.NextDouble())));
            }
        }

        static void Pine(MeshBuilder mb, Vector3 p, float h, Color leaf)
        {
            Block(p, 0.4f, 0.4f);
            mb.SetColor(Mat.C(0.4f, 0.27f, 0.18f));
            mb.Limb(p, p + Vector3.up * h * 0.35f, 0.18f * h / 6f + 0.1f, 0.12f, 6, false);
            for (int i = 0; i < 3; i++)
            {
                float y = h * (0.25f + i * 0.22f);
                mb.SetColor(Mat.C(leaf * (0.85f + i * 0.1f)));
                mb.Cone(p + Vector3.up * y, p + Vector3.up * (y + h * 0.4f), h * (0.32f - i * 0.07f), 7, true);
            }
        }

        static void LeafTree(MeshBuilder mb, Vector3 p, float h, Color leaf)
        {
            Block(p, 0.4f, 0.4f);
            mb.SetColor(Mat.C(0.38f, 0.26f, 0.17f));
            mb.Limb(p, p + Vector3.up * h * 0.6f, 0.22f, 0.14f, 6, false);
            mb.SetColor(Mat.C(leaf));
            mb.Sphere(p + Vector3.up * h * 0.75f, new Vector3(h * 0.3f, h * 0.24f, h * 0.3f), 4, 7);
            mb.SetColor(Mat.C(leaf * 1.12f));
            mb.Sphere(p + new Vector3(h * 0.15f, h * 0.9f, 0.1f), new Vector3(h * 0.2f, h * 0.17f, h * 0.2f), 4, 6);
        }

        static void Rock(MeshBuilder mb, Vector3 p, float s, Color c)
        {
            Block(p, s * 1.05f, s * 0.9f);
            mb.SetColor(Mat.C(c));
            mb.Sphere(p + Vector3.up * s * 0.3f, new Vector3(s, s * 0.6f, s * 0.85f), 3, 6);
        }

        static void Torii(MeshBuilder mb, Vector3 p, float yaw, float s)
        {
            var q = Quaternion.Euler(0, yaw, 0);
            Color32 red = Mat.C(0.78f, 0.15f, 0.12f), black = Mat.C(0.12f, 0.1f, 0.1f);
            mb.SetColor(red);
            mb.Limb(p + q * new Vector3(-1.6f * s, 0, 0), p + q * new Vector3(-1.5f * s, 4.2f * s, 0), 0.2f * s, 0.17f * s, 8, false);
            mb.Limb(p + q * new Vector3(1.6f * s, 0, 0), p + q * new Vector3(1.5f * s, 4.2f * s, 0), 0.2f * s, 0.17f * s, 8, false);
            mb.Box(p + q * new Vector3(0, 3.5f * s, 0), new Vector3(3.6f * s, 0.25f * s, 0.25f * s), q);
            mb.SetColor(black);
            mb.Box(p + q * new Vector3(0, 4.35f * s, 0), new Vector3(4.6f * s, 0.32f * s, 0.45f * s), q);
            mb.SetColor(red);
            mb.Box(p + q * new Vector3(0, 4.1f * s, 0), new Vector3(4.2f * s, 0.22f * s, 0.35f * s), q);
        }

        static void House(MeshBuilder mb, Vector3 p, float yaw, Vector3 size, Color wall, Color roof, Color wood)
        {
            var q = Quaternion.Euler(0, yaw, 0);
            float cs = Mathf.Abs(Mathf.Cos(yaw * Mathf.Deg2Rad)), sn = Mathf.Abs(Mathf.Sin(yaw * Mathf.Deg2Rad));
            Block(p, (cs * size.x + sn * size.z) * 0.5f + 0.3f, (sn * size.x + cs * size.z) * 0.5f + 0.3f);
            mb.SetColor(Mat.C(wall));
            mb.Box(p + q * new Vector3(0, size.y * 0.5f, 0), size, q);
            mb.SetColor(Mat.C(wood));
            // timber frame
            for (int i = -2; i <= 2; i++)
                mb.Box(p + q * new Vector3(i * size.x / 4.5f, size.y * 0.5f, size.z * 0.5f + 0.03f), new Vector3(0.18f, size.y, 0.08f), q);
            mb.Box(p + q * new Vector3(0, size.y * 0.55f, size.z * 0.5f + 0.04f), new Vector3(size.x, 0.15f, 0.08f), q);
            // sliding doors (shoji lattice)
            mb.SetColor(Mat.C(wall * 1.05f));
            mb.Box(p + q * new Vector3(0, size.y * 0.3f, size.z * 0.5f + 0.05f), new Vector3(size.x * 0.4f, size.y * 0.55f, 0.05f), q);
            // roof (two slanted slabs + ridge)
            mb.SetColor(Mat.C(roof));
            float rw = size.z * 0.62f;
            mb.Box(p + q * new Vector3(0, size.y + 0.55f, rw * 0.45f), new Vector3(size.x + 1.4f, 0.25f, rw * 1.15f), q * Quaternion.Euler(24, 0, 0));
            mb.Box(p + q * new Vector3(0, size.y + 0.55f, -rw * 0.45f), new Vector3(size.x + 1.4f, 0.25f, rw * 1.15f), q * Quaternion.Euler(-24, 0, 0));
            mb.SetColor(Mat.C(roof * 0.7f));
            mb.Box(p + q * new Vector3(0, size.y + 1.1f, 0), new Vector3(size.x + 1.6f, 0.25f, 0.4f), q);
        }

        static void Fence(MeshBuilder mb, Vector3 a, Vector3 b, Color wood)
        {
            mb.SetColor(Mat.C(wood));
            Vector3 d = b - a;
            int posts = Mathf.Max(2, Mathf.CeilToInt(d.magnitude / 1.6f));
            for (int i = 0; i <= posts; i++) mb.Box(a + d * ((float)i / posts) + Vector3.up * 0.55f, new Vector3(0.14f, 1.1f, 0.14f));
            var q = Quaternion.LookRotation(d.normalized);
            mb.Box((a + b) * 0.5f + Vector3.up * 0.85f, new Vector3(0.07f, 0.1f, d.magnitude), q);
            mb.Box((a + b) * 0.5f + Vector3.up * 0.45f, new Vector3(0.07f, 0.1f, d.magnitude), q);
        }

        static void Mountains(MeshBuilder mb, float r, Color c, int count, float h, int seed)
        {
            var rnd = new System.Random(seed);
            for (int i = 0; i < count; i++)
            {
                float a = (float)i / count * Mathf.PI * 2f + (float)rnd.NextDouble() * 0.2f;
                Vector3 p = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * (r + (float)rnd.NextDouble() * 15f);
                float hh = h * (0.6f + (float)rnd.NextDouble() * 0.8f);
                mb.SetColor(Mat.C(c * (0.9f + 0.2f * (float)rnd.NextDouble())));
                mb.Cone(p - Vector3.up * 2f, p + Vector3.up * hh, hh * 0.9f, 6, false);
                mb.SetColor(Mat.C(Color.Lerp(c, Color.white, 0.75f)));
                mb.Cone(p + Vector3.up * hh * 0.72f, p + Vector3.up * (hh + 0.3f), hh * 0.27f, 6, false);
            }
        }

        Mesh SingleMesh(System.Action<MeshBuilder> build)
        {
            var mb = new MeshBuilder();
            noBlock = true;
            try { build(mb); } finally { noBlock = false; }
            return mb.ToMesh("prop");
        }

        Destructible TreeProp(Vector3 p, float h, Color leaf, bool pine)
        {
            var m = SingleMesh(mb => { if (pine) Pine(mb, Vector3.zero, h, leaf); else LeafTree(mb, Vector3.zero, h, leaf); });
            return Destructible.Create(transform, DestructKind.Tree, p, m, 110f, new Color(0.45f, 0.3f, 0.2f), h / 6f);
        }

        // ================= 0. Dojo / training ground (matches the original training scene) =================
        void BuildDojo(MeshBuilder mb)
        {
            bool w = Winter;
            if (w)
            {
                // winter training ground: snow everywhere, pale low sun, a cold white haze with a warm cast
                Lighting(new Vector3(-0.72f, 0.5f, 0.22f), new Color(1f, 0.95f, 0.88f), new Color(0.4f, 0.42f, 0.5f), new Color(0.9f, 0.88f, 0.86f), 12f, 85f, 0.75f);
                Sky(new Color(0.62f, 0.72f, 0.86f), new Color(0.95f, 0.93f, 0.9f), new Color(0.9f, 0.9f, 0.92f), "sky_dojo");
                PhotoGround("snow", new Color(0.93f, 0.94f, 0.98f), new Color(0.648f, 0.647f, 0.655f), 3.4f, 0f, 0.5f);
                Ground(mb, 70f, new Color(0.93f, 0.94f, 0.97f), new Color(0.86f, 0.88f, 0.93f), new Color(0.8f, 0.83f, 0.9f), 11);
                Grass(110, 6f, 26f, new Color(0.6f, 0.56f, 0.4f), 3); // dry stalks poking through the snow
            }
            else
            {
                // warm distance haze: the far trees and mountains fade into a golden mist, the lane stays crisp
                Lighting(new Vector3(-0.72f, 0.62f, 0.22f), new Color(1f, 0.96f, 0.86f), new Color(0.32f, 0.3f, 0.3f), new Color(0.96f, 0.88f, 0.74f), 14f, 92f, 0.72f);
                Sky(new Color(0.55f, 0.75f, 0.95f), new Color(0.97f, 0.92f, 0.8f), new Color(0.9f, 0.85f, 0.7f), "sky_dojo");
                PhotoGround("dojo", new Color(0.895f, 0.75f, 0.5f), new Color(0.603f, 0.451f, 0.321f), 3.2f);
                Ground(mb, 70f, new Color(0.93f, 0.8f, 0.55f), new Color(0.86f, 0.7f, 0.45f), new Color(0.55f, 0.65f, 0.35f), 11);
                Grass(260, 6f, 26f, new Color(0.45f, 0.62f, 0.25f), 3);
            }
            Color fir = w ? new Color(0.16f, 0.32f, 0.27f) : new Color(0.32f, 0.56f, 0.3f);
            // main hall behind the arena + side buildings
            House(mb, new Vector3(-6f, 0, 24f), 0, new Vector3(16f, 4.2f, 6f), new Color(0.96f, 0.94f, 0.88f), new Color(0.48f, 0.5f, 0.55f), new Color(0.35f, 0.24f, 0.17f));
            House(mb, new Vector3(-25f, 0, 8f), 70, new Vector3(9f, 3.4f, 5f), new Color(0.96f, 0.94f, 0.88f), new Color(0.48f, 0.5f, 0.55f), new Color(0.35f, 0.24f, 0.17f));
            mb.SetColor(Mat.C(0.8f, 0.12f, 0.12f));
            mb.Box(new Vector3(-3.5f, 3.2f, 20.9f), new Vector3(1.6f, 2.8f, 0.05f)); // red banner
            Torii(mb, new Vector3(16f, 0, 18f), -35, 1.2f);
            if (w) Torii(mb, new Vector3(-15f, 0, 19.5f), 20, 0.95f); // a second gate on the snowy path
            Fence(mb, new Vector3(-20f, 0, 15f), new Vector3(10f, 0, 17f), new Color(0.42f, 0.3f, 0.2f));
            Fence(mb, new Vector3(-22f, 0, -14f), new Vector3(-22f, 0, 12f), new Color(0.42f, 0.3f, 0.2f));
            for (int i = 0; i < 9; i++)
            {
                float a = 0.15f + i * 0.33f;
                Pine(treeMb, new Vector3(Mathf.Cos(a) * 30f, 0, Mathf.Sin(a) * 30f + 4f), 8f + (i % 3) * 2f, fir);
            }
            if (w) for (int i = 0; i < 7; i++) Pine(treeMb, new Vector3(-27f + i * 9f, 0, 21f + (i % 3) * 3f), 9f + (i % 2) * 3f, fir * 0.92f);
            for (int i = 0; i < 6; i++) Pine(treeMb, new Vector3(-30f + i * 4f, 0, -30f - (i % 2) * 4f), 7f + i % 3, fir * 0.95f);
            Mountains(mb, 75f, w ? new Color(0.76f, 0.8f, 0.88f) : new Color(0.62f, 0.7f, 0.8f), 12, 24f, 5);
            bool kit = ModelKit.Has("Dummy");
            if (kit) DojoProps();
            else
            {
                // weapon rack
                mb.SetColor(Mat.C(0.4f, 0.28f, 0.18f));
                mb.Box(new Vector3(4f, 1f, 16.5f), new Vector3(3f, 0.12f, 0.12f));
                mb.Box(new Vector3(4f, 0.4f, 16.5f), new Vector3(3f, 0.12f, 0.12f));
                mb.SetColor(Mat.C(0.55f, 0.55f, 0.6f));
                for (int i = 0; i < 5; i++) mb.Box(new Vector3(2.8f + i * 0.6f, 1.2f, 16.6f), new Vector3(0.05f, 2.2f, 0.05f), Quaternion.Euler(0, 0, 8));
            }
            // straw dummies and lanterns are breakable
            for (int i = 0; i < 3; i++)
            {
                if (kit && Destructible.CreateFromModel(transform, DestructKind.Dummy, new Vector3(-12f + i * 3f, 0, 13f), "Dummy", 1.86f, 60f, new Color(0.86f, 0.7f, 0.38f), 180f + (i - 1) * 15f) != null) continue;
                var m = SingleMesh(b =>
                {
                    b.SetColor(Mat.C(0.4f, 0.28f, 0.18f)); b.Limb(Vector3.zero, Vector3.up * 1.7f, 0.07f, 0.06f, 6, false);
                    b.Box(Vector3.up * 1.3f, new Vector3(1f, 0.08f, 0.08f));
                    b.SetColor(Mat.C(0.86f, 0.7f, 0.38f)); b.Limb(Vector3.up * 0.8f, Vector3.up * 1.5f, 0.24f, 0.2f, 7, true);
                    b.Sphere(Vector3.up * 1.82f, 0.18f, 4, 7);
                    b.SetColor(Mat.C(0.85f, 0.2f, 0.15f, 0.2f)); b.Box(new Vector3(0, 1.25f, 0.2f), new Vector3(0.18f, 0.18f, 0.04f));
                });
                Destructible.Create(transform, DestructKind.Dummy, new Vector3(-12f + i * 3f, 0, 13f), m, 60f, new Color(0.86f, 0.7f, 0.38f), 1f);
            }
            for (int i = 0; i < 4; i++) Lantern(new Vector3(-16f + i * 9f, 0, 15.5f));
            for (int i = 0; i < 3; i++)
            {
                if (kit && Destructible.CreateFromModel(transform, DestructKind.Crate, new Vector3(14f, 0, -8f + i * 1.1f), i == 1 ? "Barrel" : "Crate_Wooden", i == 1 ? 0.95f : 1.0f, 40f, new Color(0.52f, 0.36f, 0.22f), i * 37f) != null) continue;
                var m = SingleMesh(b => { b.SetColor(Mat.C(0.52f, 0.36f, 0.22f)); b.Box(Vector3.up * 0.45f, new Vector3(0.9f, 0.9f, 0.9f)); b.SetColor(Mat.C(0.4f, 0.27f, 0.16f)); b.Box(Vector3.up * 0.45f, new Vector3(0.95f, 0.12f, 0.95f)); });
                Destructible.Create(transform, DestructKind.Crate, new Vector3(14f, 0, -8f + i * 1.1f), m, 40f, new Color(0.52f, 0.36f, 0.22f), 1f);
            }
            Weather(new Color(1f, 1f, 0.9f, 0.5f), 12f, 0.06f, new Vector3(0.3f, -0.2f, 0.1f), 40f, false, 8f, Mat.FxAdd);
            PlayerSpawn = new Vector3(-3f, 0, 0); EnemySpawn = new Vector3(3f, 0, 0);
        }

        void Lantern(Vector3 p)
        {
            var m = SingleMesh(b =>
            {
                b.SetColor(Mat.C(0.25f, 0.2f, 0.18f)); b.Limb(Vector3.zero, Vector3.up * 1.6f, 0.06f, 0.05f, 5, false);
                b.SetColor(Mat.C(0.4f, 0.4f, 0.42f)); b.Box(Vector3.up * 1.62f, new Vector3(0.45f, 0.08f, 0.45f));
                b.SetColor(Mat.C(1f, 0.82f, 0.45f, 0.9f)); b.Box(Vector3.up * 1.85f, new Vector3(0.32f, 0.38f, 0.32f));
                b.SetColor(Mat.C(0.4f, 0.4f, 0.42f)); b.Cone(Vector3.up * 2.04f, Vector3.up * 2.35f, 0.32f, 4, true);
            });
            Destructible.Create(transform, DestructKind.Lantern, p, m, 25f, new Color(0.4f, 0.4f, 0.42f), 1f);
        }

        // ================= 1. Forest of Destruction =================
        void BuildForest(MeshBuilder mb)
        {
            Lighting(new Vector3(0.74f, 0.58f, 0.2f), new Color(1f, 0.88f, 0.7f), new Color(0.25f, 0.32f, 0.28f), new Color(0.8f, 0.78f, 0.6f), 11f, 72f, 0.78f); // warm green-gold haze
            Sky(new Color(0.45f, 0.62f, 0.75f), new Color(0.85f, 0.83f, 0.68f), new Color(0.4f, 0.5f, 0.35f), "sky_forest");
            PhotoGround("forest", new Color(0.43f, 0.485f, 0.27f), new Color(0.568f, 0.529f, 0.366f), 3.6f, 0f, 0.6f);
            Ground(mb, 70f, new Color(0.38f, 0.55f, 0.27f), new Color(0.48f, 0.42f, 0.27f), new Color(0.2f, 0.32f, 0.18f), 21);
            Grass(360, 2f, 30f, new Color(0.35f, 0.58f, 0.22f), 7);
            var rnd = new System.Random(4);
            // dense ring of background trees
            for (int i = 0; i < 46; i++)
            {
                float a = (float)i / 46f * Mathf.PI * 2f;
                float rr = 26f + (float)rnd.NextDouble() * 14f;
                Vector3 p = new Vector3(Mathf.Cos(a) * rr, 0, Mathf.Sin(a) * rr);
                if (i % 2 == 0) Pine(treeMb, p, 9f + (float)rnd.NextDouble() * 6f, new Color(0.2f, 0.42f, 0.24f));
                else LeafTree(treeMb, p, 8f + (float)rnd.NextDouble() * 5f, new Color(0.3f, 0.5f, 0.22f));
            }
            for (int i = 0; i < 10; i++) Rock(mb, new Vector3((float)rnd.NextDouble() * 50f - 25f, 0, (float)rnd.NextDouble() * 50f - 25f) * (i < 5 ? 1.2f : 1f), 0.6f + (float)rnd.NextDouble(), new Color(0.5f, 0.5f, 0.48f));
            Mountains(mb, 70f, new Color(0.35f, 0.5f, 0.42f), 14, 22f, 9);
            // breakable trees inside the arena
            bool kit = ModelKit.Available;
            for (int i = 0; i < 11; i++)
            {
                float a = (float)i / 11f * Mathf.PI * 2f + 0.3f;
                float rr = 9f + (i % 3) * 3.5f;
                Vector3 tp = new Vector3(Mathf.Cos(a) * rr, 0, Mathf.Sin(a) * rr);
                if (kit)
                {
                    string model = i % 5 == 4 ? ModelKit.Pick(ModelKit.DeadTrees, rnd) : ModelKit.Trees[i % ModelKit.Trees.Length];
                    if (Destructible.CreateFromModel(transform, DestructKind.Tree, tp, model, 6.5f + (i % 3) * 0.8f, 110f, new Color(0.45f, 0.3f, 0.2f), (float)rnd.NextDouble() * 360f) != null) continue;
                }
                TreeProp(new Vector3(Mathf.Cos(a) * rr, 0, Mathf.Sin(a) * rr), 6f + (i % 3), i % 2 == 0 ? new Color(0.24f, 0.46f, 0.26f) : new Color(0.32f, 0.52f, 0.22f), i % 2 == 0);
            }
            for (int i = 0; i < 5; i++)
            {
                float a = i * 1.3f + 0.6f;
                Vector3 rp = new Vector3(Mathf.Cos(a) * 16f, 0, Mathf.Sin(a) * 16f);
                if (kit && Destructible.CreateFromModel(transform, DestructKind.Rock, rp, ModelKit.Rocks[i % ModelKit.Rocks.Length], 1.5f + (i % 2) * 0.4f, 90f, new Color(0.55f, 0.55f, 0.52f), (float)rnd.NextDouble() * 360f) != null) continue;
                var m = SingleMesh(b => Rock(b, Vector3.zero, 1.1f, new Color(0.55f, 0.55f, 0.52f)));
                Destructible.Create(transform, DestructKind.Rock, rp, m, 90f, new Color(0.55f, 0.55f, 0.52f), 1.2f);
            }
            Weather(new Color(0.55f, 0.7f, 0.3f, 0.9f), 10f, 0.12f, new Vector3(0.8f, -1.2f, 0.3f), 40f, false, 9f, Mat.FxAlpha);
            PlayerSpawn = new Vector3(0, 0, -7f); EnemySpawn = new Vector3(0, 0, 7f);
        }

        // ================= 2. Ruined City =================
        void BuildCity(MeshBuilder mb)
        {
            Lighting(new Vector3(-0.7f, 0.35f, 0.4f), new Color(1f, 0.7f, 0.5f), new Color(0.3f, 0.26f, 0.36f), new Color(0.55f, 0.45f, 0.5f), 12f, 65f, 0.75f);
            Sky(new Color(0.3f, 0.28f, 0.5f), new Color(0.95f, 0.6f, 0.42f), new Color(0.35f, 0.3f, 0.35f), "sky_city");
            PhotoGround("city", new Color(0.46f, 0.44f, 0.435f), new Color(0.355f, 0.351f, 0.332f), 4f, 1f, 0.6f); // rain-soaked asphalt
            Ground(mb, 70f, new Color(0.5f, 0.48f, 0.47f), new Color(0.42f, 0.4f, 0.4f), new Color(0.3f, 0.28f, 0.3f), 31);
            // road markings
            mb.SetColor(Mat.C(0.85f, 0.8f, 0.55f));
            for (int i = -6; i <= 6; i++) mb.Box(new Vector3(i * 3.2f, 0.01f, 0), new Vector3(1.6f, 0.02f, 0.18f));
            var rnd = new System.Random(8);
            // skyline of ruined buildings around the arena
            for (int i = 0; i < 26; i++)
            {
                float a = (float)i / 26f * Mathf.PI * 2f;
                float rr = 28f + (float)rnd.NextDouble() * 10f;
                Vector3 p = new Vector3(Mathf.Cos(a) * rr, 0, Mathf.Sin(a) * rr);
                float h = 6f + (float)rnd.NextDouble() * 14f, w = 4f + (float)rnd.NextDouble() * 4f;
                Color c = Color.Lerp(new Color(0.55f, 0.5f, 0.5f), new Color(0.45f, 0.42f, 0.5f), (float)rnd.NextDouble());
                var q = Quaternion.Euler(0, -a * Mathf.Rad2Deg + 90f, (float)rnd.NextDouble() * 6f - 3f);
                mb.SetColor(Mat.C(c));
                mb.Box(p + Vector3.up * h * 0.5f, new Vector3(w, h, w * 0.8f), q);
                // broken top and windows
                mb.SetColor(Mat.C(c * 0.8f));
                mb.Box(p + Vector3.up * (h + 0.6f) + q * Vector3.right * w * 0.25f, new Vector3(w * 0.4f, 1.4f, w * 0.7f), q * Quaternion.Euler(0, 0, 18));
                mb.SetColor(Mat.C(1f, 0.75f, 0.4f, (i % 3 == 0) ? 0.85f : 0.1f));
                for (int k = 0; k < 3; k++) mb.Box(p + q * new Vector3(-w * 0.25f + k * w * 0.25f, h * 0.55f, -w * 0.41f), new Vector3(0.5f, 0.7f, 0.05f), q);
            }
            for (int i = 0; i < 18; i++) Rock(mb, new Vector3((float)rnd.NextDouble() * 50f - 25f, 0, (float)rnd.NextDouble() * 50f - 25f), 0.4f + (float)rnd.NextDouble() * 0.6f, new Color(0.45f, 0.43f, 0.43f));
            Mountains(mb, 80f, new Color(0.35f, 0.3f, 0.42f), 10, 18f, 13);
            // breakable wall segments, pillars and cars-like crates inside the arena
            for (int i = 0; i < 9; i++)
            {
                float a = (float)i / 9f * Mathf.PI * 2f;
                float rr = 13f + (i % 2) * 4f;
                Vector3 p = new Vector3(Mathf.Cos(a) * rr, 0, Mathf.Sin(a) * rr);
                float yaw = -a * Mathf.Rad2Deg + 90f;
                var m = SingleMesh(b =>
                {
                    var q = Quaternion.Euler(0, yaw, 0);
                    b.SetColor(Mat.C(0.62f, 0.58f, 0.55f)); b.Box(Vector3.up * 1.6f, new Vector3(3.6f, 3.2f, 0.5f), q);
                    b.SetColor(Mat.C(0.5f, 0.47f, 0.45f)); b.Box(Vector3.up * 3.3f + q * Vector3.right * 0.8f, new Vector3(1.6f, 0.4f, 0.55f), q);
                    b.SetColor(Mat.C(0.3f, 0.3f, 0.35f)); b.Box(Vector3.up * 1.8f, new Vector3(0.8f, 1f, 0.55f), q);
                });
                Destructible.Create(transform, DestructKind.Wall, p, m, 120f, new Color(0.6f, 0.56f, 0.53f), 1.4f);
            }
            if (ModelKit.Has("Building_Small_1")) CityModels();
            for (int i = 0; i < 4; i++)
            {
                float a = i * 1.57f + 0.78f;
                var m = SingleMesh(b => { b.SetColor(Mat.C(0.3f, 0.3f, 0.32f)); b.Limb(Vector3.zero, Vector3.up * 4f, 0.08f, 0.07f, 6, false); b.SetColor(Mat.C(1f, 0.85f, 0.55f, 1f)); b.Sphere(Vector3.up * 4.1f, 0.2f, 4, 6); });
                Destructible.Create(transform, DestructKind.Lantern, new Vector3(Mathf.Cos(a) * 9f, 0, Mathf.Sin(a) * 9f), m, 25f, new Color(0.3f, 0.3f, 0.32f), 1f);
            }
            PlayerSpawn = new Vector3(0, 0, -7f); EnemySpawn = new Vector3(0, 0, 7f);
        }

        // ================= 3. Moon Temple =================
        void BuildMoon(MeshBuilder mb)
        {
            Lighting(new Vector3(0.3f, 0.7f, 0.6f), new Color(0.75f, 0.8f, 1f), new Color(0.22f, 0.22f, 0.36f), new Color(0.12f, 0.12f, 0.25f), 18f, 80f, 0.8f);
            Sky(new Color(0.03f, 0.03f, 0.12f), new Color(0.2f, 0.18f, 0.38f), new Color(0.05f, 0.05f, 0.1f), "sky_moon", new Color(0.32f, 0.32f, 0.5f));
            // void floor far below + outer rubble
            Ground(mb, 70f, new Color(0.25f, 0.24f, 0.35f), new Color(0.2f, 0.2f, 0.3f), new Color(0.1f, 0.1f, 0.18f), 41, -14f);
            // giant moon
            mb.SetColor(Mat.C(0.95f, 0.95f, 1f, 1f));
            mb.Sphere(new Vector3(-40f, 55f, 95f), 22f, 10, 16);
            mb.SetColor(Mat.C(0.8f, 0.82f, 0.9f, 1f));
            mb.Sphere(new Vector3(-48f, 60f, 82f), 4f, 4, 8);
            // stars
            var rnd = new System.Random(77);
            mb.SetColor(Mat.C(1f, 1f, 1f, 1f));
            for (int i = 0; i < 120; i++)
            {
                Vector3 d = new Vector3((float)rnd.NextDouble() * 2f - 1f, (float)rnd.NextDouble() * 0.9f + 0.1f, (float)rnd.NextDouble() * 2f - 1f).normalized;
                mb.Box(d * 120f, Vector3.one * (0.25f + (float)rnd.NextDouble() * 0.35f));
            }
            // temple structures at the edge
            for (int i = 0; i < 12; i++)
            {
                float a = (float)i / 12f * Mathf.PI * 2f;
                Vector3 p = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * 30f;
                mb.SetColor(Mat.C(0.88f, 0.88f, 0.94f));
                mb.Limb(p, p + Vector3.up * 14f, 1f, 0.85f, 8, false);
                mb.SetColor(Mat.C(0.8f, 0.68f, 0.35f, 0.3f));
                mb.Box(p + Vector3.up * 14.2f, new Vector3(2.4f, 0.5f, 2.4f));
                if (i % 2 == 0)
                {
                    float a2 = (i + 2f) / 12f * Mathf.PI * 2f;
                    Vector3 p2 = new Vector3(Mathf.Cos(a2), 0, Mathf.Sin(a2)) * 30f;
                    mb.SetColor(Mat.C(0.86f, 0.86f, 0.92f));
                    mb.Box((p + p2) * 0.5f + Vector3.up * 14.6f, new Vector3(1.2f, 0.8f, (p2 - p).magnitude), Quaternion.LookRotation(p2 - p));
                }
            }
            // floating rock islands
            for (int i = 0; i < 8; i++)
            {
                float a = i * 0.8f;
                Vector3 p = new Vector3(Mathf.Cos(a) * 45f, 8f + i % 3 * 6f, Mathf.Sin(a) * 45f);
                mb.SetColor(Mat.C(0.4f, 0.38f, 0.5f));
                mb.Cone(p, p + Vector3.down * 5f, 3f, 6, true);
                mb.SetColor(Mat.C(0.75f, 0.75f, 0.85f));
                mb.Sphere(p + Vector3.up * 0.2f, new Vector3(3f, 0.4f, 3f), 3, 8);
            }
            Mountains(mb, 85f, new Color(0.18f, 0.16f, 0.3f), 12, 28f, 17);
            // the temple floor is made of moving tiles (arena changes between boss phases)
            var tilesGo = new GameObject("MoonTiles");
            tilesGo.transform.SetParent(transform, false);
            moonTiles = tilesGo.AddComponent<MoonTiles>();
            moonTiles.Build();
            // breakable pillars
            for (int i = 0; i < 8; i++)
            {
                float a = (float)i / 8f * Mathf.PI * 2f + 0.2f;
                Vector3 p = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * 11.5f;
                var m = SingleMesh(b =>
                {
                    b.SetColor(Mat.C(0.92f, 0.92f, 0.97f)); b.Limb(Vector3.zero, Vector3.up * 5f, 0.5f, 0.42f, 8, false);
                    b.SetColor(Mat.C(0.8f, 0.68f, 0.35f, 0.4f)); b.Box(Vector3.up * 5.1f, new Vector3(1.2f, 0.3f, 1.2f)); b.Box(Vector3.up * 0.15f, new Vector3(1.2f, 0.3f, 1.2f));
                    b.SetColor(Mat.C(0.6f, 0.5f, 1f, 1f)); b.Box(Vector3.up * 2.5f + Vector3.forward * 0.42f, new Vector3(0.2f, 1.2f, 0.05f));
                });
                Destructible.Create(transform, DestructKind.Pillar, p, m, 140f, new Color(0.9f, 0.9f, 0.95f), 0.9f);
            }
            if (ModelKit.Has("Vase_2")) MoonProps();
            Weather(new Color(0.8f, 0.8f, 1f, 0.8f), 18f, 0.08f, new Vector3(0.1f, 0.4f, 0.1f), 40f, false, 9f, Mat.FxAdd);
            weatherGo.transform.position = new Vector3(0, 0.5f, 0);
            PlayerSpawn = new Vector3(0, 0, -7f); EnemySpawn = new Vector3(0, 0, 7f);
        }

        /// <summary>Boss-driven arena change (Moon Temple): 0 = intact, 1 = outer ring collapses, 2 = central platforms rise.</summary>
        public void SetPhase(int phase)
        {
            if (moonTiles != null) moonTiles.SetPhase(phase);
            if (Id == ArenaId.MoonTemple)
            {
                if (phase == 1) SetRadius(15.5f);
                if (phase == 2)
                {
                    Lighting(new Vector3(0.3f, 0.7f, 0.6f), new Color(1f, 0.65f, 0.75f), new Color(0.32f, 0.16f, 0.3f), new Color(0.25f, 0.06f, 0.15f), 14f, 60f, 0.8f);
                    ApplyLighting();
                }
            }
            CameraRig.Shake(0.6f);
            PhaseChanged?.Invoke(phase);
        }

        public void ResetProps()
        {
            foreach (var d in Destructible.All) d.Restore();
            if (moonTiles != null) moonTiles.SetPhase(0);
            if (Id == ArenaId.MoonTemple) { SetRadius(21f); BuildMoonLightingAgain(); }
        }

        void BuildMoonLightingAgain()
        {
            Lighting(new Vector3(0.3f, 0.7f, 0.6f), new Color(0.75f, 0.8f, 1f), new Color(0.22f, 0.22f, 0.36f), new Color(0.12f, 0.12f, 0.25f), 18f, 80f, 0.8f);
            ApplyLighting();
        }
    }

    /// <summary>Keeps the sky dome centred on the camera.</summary>
    public class FollowCameraXZ : MonoBehaviour
    {
        void LateUpdate()
        {
            var c = CameraRig.Inst;
            if (c != null) transform.position = new Vector3(c.transform.position.x, 0, c.transform.position.z);
        }
    }

    /// <summary>
    /// Moon temple floor. Tiles are merged into a few meshes to keep draw calls low on weak GPUs:
    /// one static centre, 8 outer sectors that collapse in phase 2, and a handful of single tiles that rise in phase 3.
    /// </summary>
    public class MoonTiles : MonoBehaviour
    {
        class Group { public Transform t; public float target; public float speed; public BoxCollider col; }
        readonly List<Group> sectors = new List<Group>();
        readonly List<Group> risers = new List<Group>();

        Material tileMat; // photo stone (lite on Low/Medium) or null for the toon tiles

        public void Build()
        {
            if (Mat.GroundTextures)
                tileMat = Mat.GroundSurface("MoonTiles", Resources.Load<Texture2D>("Kagegan/Ground/ground_moon_diff"), Mat.PhotoSurfaces ? Resources.Load<Texture2D>("Kagegan/Ground/ground_moon_nor") : null,
                    Mat.TintTowards(new Color(0.82f, 0.82f, 0.905f), new Color(0.399f, 0.335f, 0.28f), 0.6f), 3.2f, 0.6f);
            var inner = new MeshBuilder();
            var sectorMb = new MeshBuilder[8];
            for (int i = 0; i < 8; i++) sectorMb[i] = new MeshBuilder();
            var riserTiles = new List<Vector3>();
            var rnd = new System.Random(3);
            for (int x = -7; x <= 7; x++)
                for (int z = -7; z <= 7; z++)
                {
                    Vector3 p = new Vector3(x * 3.2f, 0, z * 3.2f);
                    float r = p.magnitude;
                    if (r > 23f) continue;
                    bool even = (x + z) % 2 == 0;
                    if (r > 16f)
                    {
                        int sector = Mathf.Clamp(Mathf.FloorToInt((Mathf.Atan2(p.z, p.x) + Mathf.PI) / (Mathf.PI * 2f) * 8f), 0, 7);
                        Tile(sectorMb[sector], p, even);
                    }
                    else if (r > 3f && r < 10f && rnd.NextDouble() < 0.2) riserTiles.Add(p);
                    else Tile(inner, p, even);
                }
            MakeGroup(inner, "TilesInner", Vector3.zero);
            for (int i = 0; i < 8; i++)
            {
                var g = MakeGroup(sectorMb[i], "TilesOuter" + i, Vector3.zero);
                sectors.Add(new Group { t = g, speed = 5f + i * 0.7f });
            }
            foreach (var p in riserTiles)
            {
                var mb = new MeshBuilder();
                Tile(mb, Vector3.zero, true);
                var g = MakeGroup(mb, "Riser", p);
                risers.Add(new Group { t = g, speed = 2.5f });
            }
        }

        void Tile(MeshBuilder mb, Vector3 p, bool even)
        {
            if (tileMat != null) mb.SetColor(even ? Mat.C(0.52f, 0.52f, 0.52f) : Mat.C(0.46f, 0.46f, 0.48f));
            else mb.SetColor(even ? Mat.C(0.86f, 0.86f, 0.93f) : Mat.C(0.78f, 0.78f, 0.88f));
            mb.Box(p + new Vector3(0, -0.25f, 0), new Vector3(3.1f, 0.5f, 3.1f));
            if (even)
            {
                // faint inlaid rune line: low glow so bloom does not turn it into a white dashed stripe
                mb.SetColor(Mat.C(0.42f, 0.36f, 0.7f, 0.22f));
                mb.Box(p + new Vector3(0, 0.003f, 0), new Vector3(2.6f, 0.012f, 0.06f));
            }
        }

        Transform MakeGroup(MeshBuilder mb, string name, Vector3 pos)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.transform.position = pos;
            if (mb.V.Count > 0)
            {
                go.AddComponent<MeshFilter>().sharedMesh = mb.ToMesh(name);
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = tileMat != null ? tileMat : Mat.Toon;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;
            }
            return go.transform;
        }

        public void SetPhase(int p)
        {
            for (int i = 0; i < sectors.Count; i++) sectors[i].target = p >= 1 ? -12f - i * 0.9f : 0f;
            foreach (var r in risers)
            {
                r.target = p >= 2 ? 1.4f : 0f;
                if (p >= 2 && r.col == null)
                {
                    r.col = r.t.gameObject.AddComponent<BoxCollider>();
                    r.col.center = new Vector3(0, -0.6f, 0);
                    r.col.size = new Vector3(3.1f, 1.2f, 3.1f);
                }
                if (p < 2 && r.col != null) { Destroy(r.col); r.col = null; }
            }
            enabled = true;
        }

        void Update()
        {
            bool moving = false;
            float dt = Time.deltaTime;
            moving |= Move(sectors, dt, true);
            moving |= Move(risers, dt, false);
            if (!moving) enabled = false;
        }

        static bool Move(List<Group> list, float dt, bool debris)
        {
            bool moving = false;
            foreach (var g in list)
            {
                Vector3 pos = g.t.position;
                if (Mathf.Abs(pos.y - g.target) < 0.01f) continue;
                moving = true;
                float ny = Mathf.MoveTowards(pos.y, g.target, g.speed * dt);
                g.t.position = new Vector3(pos.x, ny, pos.z);
                if (debris && g.target < pos.y && Random.value < 0.05f)
                {
                    Vector3 rim = Quaternion.Euler(0, Random.value * 360f, 0) * Vector3.forward * 17f;
                    Fx.Debris(new Vector3(rim.x, 0.2f, rim.z), new Color(0.85f, 0.85f, 0.92f), 2, 3f);
                }
            }
            return moving;
        }
    }
}
