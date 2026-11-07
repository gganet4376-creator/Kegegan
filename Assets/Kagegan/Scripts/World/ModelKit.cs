using System.Collections.Generic;
using UnityEngine;

namespace Kagegan
{
    /// <summary>
    /// Hand-made models (Quaternius CC0 kits: Stylized Nature, Downtown City, Fantasy Props, Medieval Village)
    /// in Resources/Kagegan/{Nature,City,Props}. Their materials are swapped for the game's toon shader with the
    /// kits' painted textures, so they get the same lighting, fog, wind and camera fade as the rest of the world.
    /// </summary>
    public static class ModelKit
    {
        static readonly string[] Roots = { "Kagegan/Nature/", "Kagegan/Props/", "Kagegan/City/" };
        static readonly Dictionary<string, GameObject> prefabs = new Dictionary<string, GameObject>();
        static readonly Dictionary<string, Material> mats = new Dictionary<string, Material>();
        static readonly Dictionary<string, float> heights = new Dictionary<string, float>();

        // material name in the model -> texture, alpha-cut foliage?, wind amount
        struct MatInfo { public string Tex; public bool Cut; public float Wind; public Color Tint; }
        static readonly KeyValuePair<string, MatInfo>[] Table =
        {
            new KeyValuePair<string, MatInfo>("Leaves_NormalTree", new MatInfo { Tex = "Nature/Textures/Leaves_NormalTree_C", Cut = true, Wind = 0.6f }),
            new KeyValuePair<string, MatInfo>("Leaves_TwistedTree", new MatInfo { Tex = "Nature/Textures/Leaves_TwistedTree_C", Cut = true, Wind = 0.6f }),
            new KeyValuePair<string, MatInfo>("Leaves_Pine", new MatInfo { Tex = "Nature/Textures/Leaf_Pine_C", Cut = true, Wind = 0.4f }),
            new KeyValuePair<string, MatInfo>("Bark_NormalTree", new MatInfo { Tex = "Nature/Textures/Bark_NormalTree" }),
            new KeyValuePair<string, MatInfo>("Bark_TwistedTree", new MatInfo { Tex = "Nature/Textures/Bark_TwistedTree" }),
            new KeyValuePair<string, MatInfo>("Bark_DeadTree", new MatInfo { Tex = "Nature/Textures/Bark_DeadTree" }),
            new KeyValuePair<string, MatInfo>("Rocks", new MatInfo { Tex = "Nature/Textures/Rocks_Diffuse" }),
            new KeyValuePair<string, MatInfo>("Mushrooms", new MatInfo { Tex = "Nature/Textures/Mushrooms" }),
            new KeyValuePair<string, MatInfo>("Flowers", new MatInfo { Tex = "Nature/Textures/Flowers", Cut = true, Wind = 1f }),
            new KeyValuePair<string, MatInfo>("Grass", new MatInfo { Tex = "Nature/Textures/Grass", Cut = true, Wind = 1f }),
            new KeyValuePair<string, MatInfo>("Leaves", new MatInfo { Tex = "Nature/Textures/Leaves", Cut = true, Wind = 0.8f }),
            // city / props (first match wins, so specific names come before general ones)
            new KeyValuePair<string, MatInfo>("MI_Trim_MetalConcrete", new MatInfo { Tex = "City/Textures/T_MetalConcrete_BaseColor" }),
            new KeyValuePair<string, MatInfo>("MI_RedBrick", new MatInfo { Tex = "City/Textures/T_RedBrick_BaseColor" }),
            new KeyValuePair<string, MatInfo>("MI_InteriorWall", new MatInfo { Tex = "City/Textures/T_RedBrick_BaseColor" }),
            new KeyValuePair<string, MatInfo>("MI_InteriorFloor", new MatInfo { Tex = "City/Textures/T_MarbleFloor_BaseColor" }),
            new KeyValuePair<string, MatInfo>("MI_FakeInterior_1", new MatInfo { Tex = "City/Textures/T_lit_interior_1" }),
            new KeyValuePair<string, MatInfo>("MI_FakeInterior_2", new MatInfo { Tex = "City/Textures/T_lit_interior_2" }),
            new KeyValuePair<string, MatInfo>("MI_FakeInterior_3", new MatInfo { Tex = "City/Textures/T_lit_interior_1" }),
            new KeyValuePair<string, MatInfo>("MI_FakeInterior_4", new MatInfo { Tex = "City/Textures/T_dark_interior" }),
            new KeyValuePair<string, MatInfo>("MI_Concrete", new MatInfo { Tex = "City/Textures/T_Concrete_BaseColor" }),
            new KeyValuePair<string, MatInfo>("MI_Asphalt", new MatInfo { Tex = "City/Textures/T_Concrete_Asphalt_BaseColor" }),
            new KeyValuePair<string, MatInfo>("MI_Ornaments", new MatInfo { Tex = "City/Textures/T_Ornaments_BaseColor" }),
            new KeyValuePair<string, MatInfo>("MI_Dirt", new MatInfo { Tex = "City/Textures/T_Dirt_BaseColor" }),
            new KeyValuePair<string, MatInfo>("MI_Trim_Furniture", new MatInfo { Tex = "Props/Textures/T_Trim_Furniture_BaseColor" }),
            new KeyValuePair<string, MatInfo>("MI_Trim_Metal", new MatInfo { Tex = "Props/Textures/T_Trim_Metal_BaseColor" }),
            new KeyValuePair<string, MatInfo>("MI_Trim_Cloth", new MatInfo { Tex = "Props/Textures/T_Trim_Cloth_BaseColor" }),
            new KeyValuePair<string, MatInfo>("MI_Banner", new MatInfo { Tex = "Props/Textures/T_Trim_Cloth_BaseColor" }),
            new KeyValuePair<string, MatInfo>("MI_Trim_Props", new MatInfo { Tex = "Props/Textures/T_Trim_Props_BaseColor" }),
            new KeyValuePair<string, MatInfo>("MI_WoodTrim", new MatInfo { Tex = "Props/Textures/T_WoodTrim_BaseColor" }),
            new KeyValuePair<string, MatInfo>("MI_Trim", new MatInfo { Tex = "City/Textures/T_Trim_BaseColor" }),
            new KeyValuePair<string, MatInfo>("MI_Glass", new MatInfo { Tint = new Color(0.16f, 0.2f, 0.26f) }),
        };

        /// <summary>True when the model exists in one of the kit folders.</summary>
        public static bool Has(string model) => Load(model) != null;

        static bool? available;
        /// <summary>Nature kit present (forest).</summary>
        public static bool Available
        {
            get
            {
                if (available == null) available = Load("CommonTree_1") != null && Load("Rock_Medium_1") != null;
                return available.Value;
            }
        }

        static GameObject Load(string model)
        {
            if (prefabs.TryGetValue(model, out var go)) return go;
            go = null;
            foreach (var root in Roots)
            {
                try { go = Resources.Load<GameObject>(root + model); }
                catch (System.Exception e) { Debug.LogWarning("Kagegan: model " + model + " failed: " + e.Message); go = null; }
                if (go != null) break;
            }
            prefabs[model] = go;
            return go;
        }

        static Material ToonFor(Material src)
        {
            string n = src != null ? src.name.Replace(" (Instance)", "") : "";
            if (mats.TryGetValue(n, out var m)) return m;
            MatInfo info = default;
            bool found = false;
            foreach (var kv in Table)
                if (n.Contains(kv.Key)) { info = kv.Value; found = true; break; }
            m = new Material(Mat.Toon) { name = "KageKit_" + n, enableInstancing = true };
            m.SetFloat("_VColor", 0f);
            if (found)
            {
                var tex = string.IsNullOrEmpty(info.Tex) ? null : Resources.Load<Texture2D>("Kagegan/" + info.Tex);
                if (tex != null) m.SetTexture("_MainTex", tex);
                if (info.Tint.a > 0f || info.Tint.r > 0f) m.SetColor("_Color", info.Tint);
                else if (src != null && src.HasProperty("_MainTex") && src.mainTexture != null) m.SetTexture("_MainTex", src.mainTexture);
                if (info.Cut) { m.SetFloat("_Cutoff", 0.5f); m.SetFloat("_Cull", 0f); }
                if (info.Wind > 0f) { m.SetFloat("_Wind", info.Wind); m.SetFloat("_WindObj", 1f); }
            }
            else if (src != null && src.HasProperty("_Color")) m.SetColor("_Color", src.color);
            mats[n] = m;
            return m;
        }

        /// <summary>Instantiates a model scaled to the given height (metres). Returns null when the kit is missing.</summary>
        public static GameObject Spawn(string model, Transform parent, Vector3 pos, float height, float yaw)
        {
            var prefab = Load(model);
            if (prefab == null) return null;
            var go = Object.Instantiate(prefab, parent, false);
            go.name = model;
            go.transform.position = pos;
            go.transform.rotation = Quaternion.Euler(0f, yaw, 0f) * go.transform.rotation;
            var rs = go.GetComponentsInChildren<Renderer>();
            foreach (var r in rs)
            {
                var sm = r.sharedMaterials;
                for (int i = 0; i < sm.Length; i++) sm[i] = ToonFor(sm[i]);
                r.sharedMaterials = sm;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
            }
            foreach (var c in go.GetComponentsInChildren<Collider>()) Object.Destroy(c);
            // file units differ between exporters: measure once and normalise to the wanted height
            if (!heights.TryGetValue(model, out float h0))
            {
                h0 = 0f;
                if (rs.Length > 0)
                {
                    var b = rs[0].bounds;
                    for (int i = 1; i < rs.Length; i++) b.Encapsulate(rs[i].bounds);
                    h0 = b.size.y / Mathf.Max(0.0001f, go.transform.lossyScale.y);
                }
                heights[model] = h0;
            }
            if (h0 > 0.0001f && height > 0f) go.transform.localScale *= height / h0;
            // sit on the ground
            if (rs.Length > 0)
            {
                var b = rs[0].bounds;
                for (int i = 1; i < rs.Length; i++) b.Encapsulate(rs[i].bounds);
                go.transform.position += Vector3.up * (pos.y - b.min.y);
            }
            return go;
        }

        public static readonly string[] Trees = { "CommonTree_1", "CommonTree_3", "Pine_1", "Pine_3" };
        public static readonly string[] DeadTrees = { "DeadTree_1", "DeadTree_3" };
        public static readonly string[] Rocks = { "Rock_Medium_1", "Rock_Medium_2", "Rock_Medium_3" };
        public static readonly string[] Plants = { "Bush_Common", "Bush_Common_Flowers", "Fern_1", "Plant_1_Big", "Flower_3_Group", "Mushroom_Common", "Grass_Wispy_Tall", "Grass_Common_Tall" };

        public static readonly string[] Buildings = { "Building_Large_2", "Building_Medium_2_001", "Building_Small_1" };

        public static string Pick(string[] set, System.Random rnd) => set[rnd.Next(set.Length)];
    }
}
