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
        }

        /// <summary>Downtown City MegaKit buildings as the street behind the lane, plus breakable street props.</summary>
        void CityModels()
        {
            var root = KitRoot("CityModels").transform;
            var rnd = new System.Random(41);
            bool low = GraphicsQuality.Current == GraphicsPreset.Low;
            float x = -44f;
            while (x < 44f)
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

        public static Arena Build(ArenaId id, Transform parent)
        {
            if (Current != null) Destroy(Current.gameObject);
            Destructible.All.Clear();
            var go = new GameObject("Arena_" + id);
            go.transform.SetParent(parent, false);
            var a = go.AddComponent<Arena>();
            a.Id = id;
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
            BackdropBand(stat, a.treeMb, id);
            if (id == ArenaId.Forest && ModelKit.Available) a.NatureBackdrop();
            a.AddStaticMesh(stat, "Static");
            if (a.grassMb != null) a.AddStaticMesh(a.grassMb, "Grass", Mat.ToonWind);
            a.AddStaticMesh(a.treeMb, "Trees", Mat.ToonTree);
            switch (id)
            {
                case ArenaId.Forest: Mat.SetWind(new Vector2(0.9f, 0.4f), 1.1f); break;
                case ArenaId.City: Mat.SetWind(new Vector2(1f, 0.2f), 1.8f, 1.3f); break; // storm
                case ArenaId.MoonTemple: Mat.SetWind(new Vector2(0.7f, 0.7f), 0.6f, 0.8f); break;
                default: Mat.SetWind(new Vector2(0.85f, 0.5f), 0.9f); break;
            }
            a.BuildClouds();
            LaneHalf = 17f;
            a.LayoutPropsForLane();
            a.PlayerSpawn = new Vector3(-5f, 0f, 0f);
            a.EnemySpawn = new Vector3(5f, 0f, 0f);
            a.BuildWalls(Radius);
            a.ApplyLighting();
            return a;
        }

        // ================= shared helpers =================
        MeshBuilder grassMb;
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
            Texture2D skyTex = string.IsNullOrEmpty(texName) ? null : Resources.Load<Texture2D>("Kagegan/Sky/" + texName);
            Color tintC = tint ?? Color.white;
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

        public void ApplyLighting()
        {
            WorldLighting.SetSun(sunDir, sunCol, ambCol);
            WorldLighting.SetFog(fogCol, fogStart, fogEnd, fogMax);
            if (CameraRig.Inst != null) CameraRig.Inst.Cam.backgroundColor = skyHorizon;
            PostFx.SetGrade(Id);
        }

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
                case ArenaId.City: c = new Color(0.35f, 0.33f, 0.4f, 0.85f); count = 22; break;
                case ArenaId.MoonTemple: c = new Color(0.45f, 0.38f, 0.7f, 0.35f); count = 14; break;
                case ArenaId.Forest: c = new Color(0.95f, 0.95f, 0.92f, 0.7f); count = 16; break;
                default: c = new Color(1f, 1f, 1f, 0.8f); count = 14; break;
            }
            count = Mathf.RoundToInt(count * Mathf.Max(0.5f, GraphicsQuality.WeatherMul));
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
            r.maxParticleSize = 3f;
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
            WorldLighting.SetSun(sunDir, sunCol * 2.4f, ambCol * 2f);
            Invoke(nameof(ApplyLighting), 0.12f);
            StartCoroutine(Thunder(Random.Range(0.4f, 1.4f)));
        }

        System.Collections.IEnumerator Thunder(float delay)
        {
            yield return new WaitForSeconds(delay);
            Sfx.Play(SfxId.Thunder, 0.55f, 0.7f);
            CameraRig.Shake(0.08f);
        }

        void Update()
        {
            if (Id == ArenaId.City && dimensionUntil <= 0f) Storm();
            if (dimensionUntil > 0f && Time.time > dimensionUntil)
            {
                dimensionUntil = 0f;
                ApplyLighting();
                if (skyRenderer != null) skyRenderer.SetPropertyBlock(new MaterialPropertyBlock());
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
            if (Current == this) Current = null;
        }

        // ---- ground ----
        void Ground(MeshBuilder mb, float r, Color a, Color b, Color edge, int seed, float yOffset = 0f)
        {
            var rnd = new System.Random(seed);
            int rings = 14, seg = 40;
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
                    if (rr > Radius + 2f) { c = Color.Lerp(c, edge, Mathf.Clamp01((rr - Radius - 2f) / 10f)); p.y = Mathf.Max(0, (rr - Radius - 6f) * 0.25f) * (0.6f + n); }
                    p.y += yOffset;
                    mb.V.Add(p); mb.N.Add(Vector3.up); mb.C.Add(Mat.C(c)); mb.UV.Add(Vector2.zero); mb.Bone.Add(0);
                }
            }
            for (int i = 0; i < rings; i++)
                for (int j = 0; j < seg; j++)
                {
                    int i0 = s + i * (seg + 1) + j, i1 = i0 + seg + 1;
                    mb.T.Add(i0); mb.T.Add(i1); mb.T.Add(i0 + 1);
                    mb.T.Add(i0 + 1); mb.T.Add(i1); mb.T.Add(i1 + 1);
                }
        }

        static void GrassTufts(MeshBuilder mb, int count, float rMin, float rMax, Color c, int seed)
        {
            if (!GraphicsQuality.GroundDetail) count /= 3;
            if (GraphicsQuality.Current == GraphicsPreset.Ultra) count = count * 3 / 2;
            int blades = GraphicsQuality.GroundDetail ? 7 : 4;
            var rnd = new System.Random(seed);
            for (int i = 0; i < count; i++)
            {
                float ang = (float)rnd.NextDouble() * Mathf.PI * 2f, rr = Mathf.Lerp(rMin, rMax, Mathf.Sqrt((float)rnd.NextDouble()));
                Vector3 p = new Vector3(Mathf.Cos(ang) * rr, 0, Mathf.Sin(ang) * rr);
                Color tint = c * (0.8f + 0.4f * (float)rnd.NextDouble());
                float tall = 0.35f + 0.3f * (float)rnd.NextDouble();
                for (int k = 0; k < blades; k++)
                {
                    // blades fan out from the clump centre and arc outwards
                    float yaw = (float)rnd.NextDouble() * Mathf.PI * 2f;
                    Vector3 off = new Vector3(Mathf.Sin(yaw), 0, Mathf.Cos(yaw)) * 0.07f * (float)rnd.NextDouble();
                    float h = tall * (0.6f + 0.5f * (float)rnd.NextDouble());
                    Color tip = Color.Lerp(tint * 1.25f, new Color(0.85f, 0.85f, 0.45f), 0.12f + 0.15f * (float)rnd.NextDouble());
                    mb.Blade(p + off, h, 0.045f + 0.025f * (float)rnd.NextDouble(), yaw, 0.15f + 0.35f * (float)rnd.NextDouble(), Mat.C(tint * 0.5f), Mat.C(tip));
                }
            }
        }

        static void Pine(MeshBuilder mb, Vector3 p, float h, Color leaf)
        {
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
            mb.SetColor(Mat.C(0.38f, 0.26f, 0.17f));
            mb.Limb(p, p + Vector3.up * h * 0.6f, 0.22f, 0.14f, 6, false);
            mb.SetColor(Mat.C(leaf));
            mb.Sphere(p + Vector3.up * h * 0.75f, new Vector3(h * 0.3f, h * 0.24f, h * 0.3f), 4, 7);
            mb.SetColor(Mat.C(leaf * 1.12f));
            mb.Sphere(p + new Vector3(h * 0.15f, h * 0.9f, 0.1f), new Vector3(h * 0.2f, h * 0.17f, h * 0.2f), 4, 6);
        }

        static void Rock(MeshBuilder mb, Vector3 p, float s, Color c)
        {
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
            build(mb);
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
            Lighting(new Vector3(-0.4f, 0.8f, -0.5f), new Color(1f, 0.96f, 0.86f), new Color(0.32f, 0.3f, 0.3f), new Color(0.93f, 0.9f, 0.82f), 25f, 110f, 0.55f);
            Sky(new Color(0.55f, 0.75f, 0.95f), new Color(0.97f, 0.92f, 0.8f), new Color(0.9f, 0.85f, 0.7f), "sky_dojo");
            Ground(mb, 70f, new Color(0.93f, 0.8f, 0.55f), new Color(0.86f, 0.7f, 0.45f), new Color(0.55f, 0.65f, 0.35f), 11);
            grassMb = new MeshBuilder();
            GrassTufts(grassMb, 260, 6f, 26f, new Color(0.45f, 0.62f, 0.25f), 3);
            // main hall behind the arena + side buildings
            House(mb, new Vector3(-6f, 0, 24f), 0, new Vector3(16f, 4.2f, 6f), new Color(0.96f, 0.94f, 0.88f), new Color(0.48f, 0.5f, 0.55f), new Color(0.35f, 0.24f, 0.17f));
            House(mb, new Vector3(-25f, 0, 8f), 70, new Vector3(9f, 3.4f, 5f), new Color(0.96f, 0.94f, 0.88f), new Color(0.48f, 0.5f, 0.55f), new Color(0.35f, 0.24f, 0.17f));
            mb.SetColor(Mat.C(0.8f, 0.12f, 0.12f));
            mb.Box(new Vector3(-3.5f, 3.2f, 20.9f), new Vector3(1.6f, 2.8f, 0.05f)); // red banner
            Torii(mb, new Vector3(16f, 0, 18f), -35, 1.2f);
            Fence(mb, new Vector3(-20f, 0, 15f), new Vector3(10f, 0, 17f), new Color(0.42f, 0.3f, 0.2f));
            Fence(mb, new Vector3(-22f, 0, -14f), new Vector3(-22f, 0, 12f), new Color(0.42f, 0.3f, 0.2f));
            for (int i = 0; i < 9; i++)
            {
                float a = 0.15f + i * 0.33f;
                Pine(treeMb, new Vector3(Mathf.Cos(a) * 30f, 0, Mathf.Sin(a) * 30f + 4f), 8f + (i % 3) * 2f, new Color(0.32f, 0.56f, 0.3f));
            }
            for (int i = 0; i < 6; i++) Pine(treeMb, new Vector3(-30f + i * 4f, 0, -30f - (i % 2) * 4f), 7f + i % 3, new Color(0.3f, 0.52f, 0.3f));
            Mountains(mb, 75f, new Color(0.62f, 0.7f, 0.8f), 12, 24f, 5);
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
            Lighting(new Vector3(0.5f, 0.6f, -0.4f), new Color(1f, 0.88f, 0.7f), new Color(0.25f, 0.32f, 0.28f), new Color(0.55f, 0.68f, 0.55f), 14f, 70f, 0.75f);
            Sky(new Color(0.45f, 0.62f, 0.75f), new Color(0.85f, 0.83f, 0.68f), new Color(0.4f, 0.5f, 0.35f), "sky_forest");
            Ground(mb, 70f, new Color(0.38f, 0.55f, 0.27f), new Color(0.48f, 0.42f, 0.27f), new Color(0.2f, 0.32f, 0.18f), 21);
            grassMb = new MeshBuilder();
            GrassTufts(grassMb, 360, 2f, 30f, new Color(0.35f, 0.58f, 0.22f), 7);
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
            Weather(new Color(0.7f, 0.75f, 0.9f, 0.6f), 160f, 0.03f, new Vector3(1f, -16f, 0.5f), 34f, true, 1.2f, Mat.FxAlpha);
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

        public void Build()
        {
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

        static void Tile(MeshBuilder mb, Vector3 p, bool even)
        {
            mb.SetColor(even ? Mat.C(0.86f, 0.86f, 0.93f) : Mat.C(0.78f, 0.78f, 0.88f));
            mb.Box(p + new Vector3(0, -0.25f, 0), new Vector3(3.1f, 0.5f, 3.1f));
            if (even)
            {
                mb.SetColor(Mat.C(0.55f, 0.45f, 0.95f, 0.8f));
                mb.Box(p + new Vector3(0, 0.005f, 0), new Vector3(3.12f, 0.02f, 0.08f));
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
                mr.sharedMaterial = Mat.Toon;
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
