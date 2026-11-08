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
        static readonly string[] Roots = { "Kagegan/Nature/", "Kagegan/Props/", "Kagegan/City/", "Kagegan/Photo/" };
        static readonly Dictionary<string, GameObject> prefabs = new Dictionary<string, GameObject>();
        static readonly Dictionary<Material, Material> importedMats = new Dictionary<Material, Material>();
        static readonly Dictionary<string, Material> mats = new Dictionary<string, Material>();
        static readonly Dictionary<string, float> heights = new Dictionary<string, float>();

        // material name in the model -> texture, alpha-cut foliage?, wind amount
        // WindH = plant height (m) that gets the full bend; Flutter = leaf shiver
        struct MatInfo { public string Tex; public bool Cut, GrassContact; public float Wind; public float WindH; public float Flutter; public Color Tint; }
        static readonly KeyValuePair<string, MatInfo>[] Table =
        {
            new KeyValuePair<string, MatInfo>("Leaves_NormalTree", new MatInfo { Tex = "Nature/Textures/Leaves_NormalTree_C", Cut = true, Wind = 0.6f, WindH = 9f, Flutter = 1f }),
            new KeyValuePair<string, MatInfo>("Leaves_TwistedTree", new MatInfo { Tex = "Nature/Textures/Leaves_TwistedTree_C", Cut = true, Wind = 0.6f, WindH = 9f, Flutter = 1f }),
            new KeyValuePair<string, MatInfo>("Leaves_Pine", new MatInfo { Tex = "Nature/Textures/Leaf_Pine_C", Cut = true, Wind = 0.6f, WindH = 9f, Flutter = 0.5f }),
            new KeyValuePair<string, MatInfo>("Bark_NormalTree", new MatInfo { Tex = "Nature/Textures/Bark_NormalTree", Wind = 0.6f, WindH = 9f }),
            new KeyValuePair<string, MatInfo>("Bark_TwistedTree", new MatInfo { Tex = "Nature/Textures/Bark_TwistedTree", Wind = 0.6f, WindH = 9f }),
            new KeyValuePair<string, MatInfo>("Bark_DeadTree", new MatInfo { Tex = "Nature/Textures/Bark_DeadTree", Wind = 0.6f, WindH = 9f }),
            new KeyValuePair<string, MatInfo>("Rocks", new MatInfo { Tex = "Nature/Textures/Rocks_Diffuse" }),
            new KeyValuePair<string, MatInfo>("Mushrooms", new MatInfo { Tex = "Nature/Textures/Mushrooms" }),
            new KeyValuePair<string, MatInfo>("Flowers", new MatInfo { Tex = "Nature/Textures/Flowers", Cut = true, GrassContact = true, Wind = 1f, WindH = 0.6f, Flutter = 0.6f }),
            new KeyValuePair<string, MatInfo>("Grass", new MatInfo { Tex = "Nature/Textures/Grass", Cut = true, GrassContact = true, Wind = 1f, WindH = 0.6f, Flutter = 0.4f }),
            new KeyValuePair<string, MatInfo>("Leaves", new MatInfo { Tex = "Nature/Textures/Leaves", Cut = true, Wind = 0.8f, WindH = 2f, Flutter = 1f }),
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
            new KeyValuePair<string, MatInfo>("KC_colormap", new MatInfo { Tex = "City/Textures/KC_colormap" }),
            new KeyValuePair<string, MatInfo>("KR_colormap", new MatInfo { Tex = "City/Textures/KR_colormap" }),
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

        static Material ToonFor(Material src, string model)
        {
            string n = src != null ? src.name.Replace(" (Instance)", "") : "";
            // Kenney kits all call their single atlas "colormap"; tell the commercial and roads kits apart by prefix
            if (n == "colormap" && model.Length > 3 && model[0] == 'K' && model[2] == '_') n = model.Substring(0, 3) + "colormap";
            MatInfo info = default;
            bool found = false;
            foreach (var kv in Table)
                if (n.Contains(kv.Key)) { info = kv.Value; found = true; break; }
            // An unknown material name can appear in several kits with different textures.
            if (found && mats.TryGetValue(n, out var mapped)) return mapped;
            if (!found && src != null && importedMats.TryGetValue(src, out var imported)) return imported;
            if (src == null && mats.TryGetValue("", out var missing)) return missing;

            var m = new Material(Mat.Toon) { name = "KageKit_" + n, enableInstancing = true };
            m.SetFloat("_VColor", 0f);
            var tex = found && !string.IsNullOrEmpty(info.Tex)
                ? Resources.Load<Texture2D>("Kagegan/" + info.Tex) : null;
            if (tex != null) m.SetTexture("_MainTex", tex);
            else if (src != null)
            {
                Texture original = src.HasProperty("_BaseMap") ? src.GetTexture("_BaseMap") : null;
                if (original == null && src.HasProperty("_MainTex")) original = src.mainTexture;
                if (original != null) m.SetTexture("_MainTex", original);
                if (src.HasProperty("_BaseColor")) m.SetColor("_Color", src.GetColor("_BaseColor"));
                else if (src.HasProperty("_Color")) m.SetColor("_Color", src.color);
            }

            if (found)
            {
                if (info.Tint.a > 0f) m.SetColor("_Color", info.Tint);
                if (info.GrassContact) m.SetFloat("_GrassContact", 1f);
                if (info.Cut) { m.SetFloat("_Cutoff", 0.5f); m.SetFloat("_Cull", 0f); }
                if (info.Wind > 0f) { m.SetFloat("_Wind", info.Wind); m.SetFloat("_WindObj", 1f); m.SetFloat("_WindHeight", info.WindH); m.SetFloat("_Flutter", info.Flutter); }
            }

            if (found || src == null) mats[n] = m;
            else importedMats[src] = m;
            return m;
        }

        /// <summary>Photoscanned Poly Haven models (Resources/Kagegan/Photo, CC0) use the Surface shader with their
        /// own albedo + normal maps; null when the preset is below High or the textures are missing.</summary>
        static Material PhotoMat(string model)
        {
            string key = "photo:" + model;
            if (mats.TryGetValue(key, out var m)) return m;
            m = null;
            var diff = Resources.Load<Texture2D>("Kagegan/Photo/Textures/" + model + "_diff");
            if (diff != null)
                m = Mat.Surface("KagePhoto_" + model, diff, Resources.Load<Texture2D>("Kagegan/Photo/Textures/" + model + "_nor"), Color.white, 0f);
            mats[key] = m;
            return m;
        }

        /// <summary>Photoscanned rocks, stumps and the fire pit: only on Ultra (each is 4k-40k triangles with 1K maps).</summary>
        public static bool PhotoProps => GraphicsQuality.Current == GraphicsPreset.Ultra && Mat.PhotoSurfaces;

        /// <summary>Instantiates a model scaled to the given height (metres). Returns null when the kit is missing.</summary>
        public static GameObject Spawn(string model, Transform parent, Vector3 pos, float height, float yaw, bool cameraFade = true)
        {
            var prefab = Load(model);
            if (prefab == null) return null;
            var go = Object.Instantiate(prefab, parent, false);
            go.name = model;
            go.transform.position = pos;
            go.transform.rotation = Quaternion.Euler(0f, yaw, 0f) * go.transform.rotation;
            var rs = go.GetComponentsInChildren<Renderer>();
            var photo = PhotoMat(model);
            foreach (var r in rs)
            {
                var sm = r.sharedMaterials;
                for (int i = 0; i < sm.Length; i++) sm[i] = photo != null ? photo : ToonFor(sm[i], model);
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
                if (cameraFade && b.size.y >= 0.35f) // low bushes too: nothing may cover the fighters
                    go.AddComponent<CameraOccluder>().Init(rs);
            }
            return go;
        }

        public static readonly string[] Trees = { "CommonTree_1", "CommonTree_3", "Pine_1", "Pine_3" };
        public static readonly string[] DeadTrees = { "DeadTree_1", "DeadTree_3" };
        public static readonly string[] Rocks = { "Rock_Medium_1", "Rock_Medium_2", "Rock_Medium_3" };
        public static readonly string[] Plants = { "Bush_Common", "Bush_Common_Flowers", "Fern_1", "Plant_1_Big", "Flower_3_Group", "Mushroom_Common", "Grass_Wispy_Tall", "Grass_Common_Tall" };

        public static readonly string[] Buildings = { "Building_Large_2", "Building_Medium_2_001", "Building_Small_1" };

        public static readonly string[] Skyscrapers = { "KC_building-skyscraper-a", "KC_building-skyscraper-b", "KC_building-skyscraper-c", "KC_building-skyscraper-d", "KC_building-skyscraper-e", "KC_building-a", "KC_building-c", "KC_building-f", "KC_building-h" };

        public static readonly string[] PhotoRocks = { "rock_07", "rock_09" };
        public static readonly string[] PhotoMoonRocks = { "moon_rock_01", "moon_rock_03", "moon_rock_05" };

        public static string Pick(string[] set, System.Random rnd) => set[rnd.Next(set.Length)];

        /// <summary>
        /// Merge the models spawned under <paramref name="root"/> that sit entirely beyond <paramref name="minZ"/>
        /// (behind the fight, where they can never come between the camera and a fighter) into one mesh per material.
        /// A street of buildings with a dozen materials each, or a tree line, then costs a handful of draw calls
        /// instead of dozens, and drops out of the per-frame camera-fade checks. Breakable props are left alone.
        /// </summary>
        public static int MergeBehind(Transform root, float minZ)
        {
            if (root == null) return 0;
            var groups = new Dictionary<Material, List<CombineInstance>>();
            var merged = new List<GameObject>();
            Matrix4x4 toRoot = root.worldToLocalMatrix;
            for (int c = 0; c < root.childCount; c++)
            {
                var child = root.GetChild(c).gameObject;
                if (child.GetComponentInChildren<Destructible>() != null) continue;
                var rs = child.GetComponentsInChildren<Renderer>();
                if (rs.Length == 0) continue;
                bool ok = true;
                foreach (var r in rs)
                {
                    var mf = r.GetComponent<MeshFilter>();
                    if (!(r is MeshRenderer) || mf == null || mf.sharedMesh == null || !mf.sharedMesh.isReadable || r.bounds.min.z < minZ) { ok = false; break; }
                }
                if (!ok) continue;
                foreach (var r in rs)
                {
                    var mesh = r.GetComponent<MeshFilter>().sharedMesh;
                    var sm = r.sharedMaterials;
                    for (int s = 0; s < mesh.subMeshCount && s < sm.Length; s++)
                    {
                        if (sm[s] == null) continue;
                        if (!groups.TryGetValue(sm[s], out var list)) groups[sm[s]] = list = new List<CombineInstance>();
                        list.Add(new CombineInstance { mesh = mesh, subMeshIndex = s, transform = toRoot * r.transform.localToWorldMatrix });
                    }
                }
                merged.Add(child);
            }
            if (merged.Count < 2) return 0;
            foreach (var kv in groups)
            {
                var go = new GameObject("Merged_" + kv.Key.name);
                go.transform.SetParent(root, false);
                var mesh = new Mesh { name = go.name, indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
                mesh.CombineMeshes(kv.Value.ToArray(), true, true, false);
                mesh.RecalculateBounds();
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = kv.Key;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;
                go.isStatic = true;
            }
            // detach first so the merged copies replace them in this very frame (Destroy is deferred)
            foreach (var g in merged) { g.SetActive(false); Object.Destroy(g); }
            return merged.Count;
        }
    }
}
