using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Kagegan
{
    /// <summary>
    /// A pre-rigged character body baked offline into Resources/Kagegan/Chars/&lt;name&gt;.bytes plus &lt;name&gt;_atlas.
    /// The file holds the rest positions of the 21 rig bones (rig space, feet at y = 0, facing +Z, identity rotations)
    /// and two LODs: full detail and a ~1/3 triangle version for the Low preset.
    /// Meshes and the material are shared by every fighter that uses the model.
    /// </summary>
    public class CharModel
    {
        public Vector3[] BonePos;
        public Mesh[] Lods;
        public Material Toon;

        static readonly Dictionary<string, CharModel> cache = new Dictionary<string, CharModel>();

        /// <summary>Loads (once) and returns the model, or null if the data is missing or broken.</summary>
        public static CharModel Get(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            if (cache.TryGetValue(name, out var m)) return m;
            m = null;
            try { m = Load(name); }
            catch (System.Exception e) { Debug.LogError("Kagegan: character model '" + name + "' failed to load: " + e.Message); }
            if (m == null) Debug.LogError("Kagegan: character model '" + name + "' not found, using the procedural body");
            cache[name] = m;
            return m;
        }

        public Mesh MeshFor(GraphicsPreset preset) => Lods[preset == GraphicsPreset.Low && Lods.Length > 1 ? 1 : 0];

        static CharModel Load(string name)
        {
            var data = Resources.Load<TextAsset>("Kagegan/Chars/" + name);
            if (data == null) return null;
            var atlas = Resources.Load<Texture2D>("Kagegan/Chars/" + name + "_atlas");
            using (var r = new BinaryReader(new MemoryStream(data.bytes)))
            {
                if (new string(r.ReadChars(4)) != "KGM1" || r.ReadInt32() != 1) throw new IOException("bad header");
                int nb = r.ReadInt32();
                if (nb != (int)B.Count) throw new IOException("bone count " + nb);
                var cm = new CharModel { BonePos = new Vector3[nb] };
                for (int i = 0; i < nb; i++) cm.BonePos[i] = ReadV3(r);
                var bind = new Matrix4x4[nb];
                for (int i = 0; i < nb; i++) bind[i] = Matrix4x4.Translate(-cm.BonePos[i]);
                int lods = r.ReadInt32();
                cm.Lods = new Mesh[lods];
                for (int l = 0; l < lods; l++) cm.Lods[l] = ReadMesh(r, name + "_lod" + l, bind);

                var mat = new Material(Mat.Toon) { name = "KageToon_" + name, enableInstancing = true };
                mat.SetFloat("_VColor", 0f);   // colour comes from the atlas only
                mat.SetFloat("_Cull", 0f);     // jacket, hair strands and straps are single-sided sheets
                if (atlas != null) mat.SetTexture("_MainTex", atlas);
                cm.Toon = mat;
                return cm;
            }
        }

        static Vector3 ReadV3(BinaryReader r) => new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());

        static Mesh ReadMesh(BinaryReader r, string name, Matrix4x4[] bind)
        {
            int nv = r.ReadInt32(), ni = r.ReadInt32();
            var pos = new Vector3[nv];
            var nrm = new Vector3[nv];
            var uv = new Vector2[nv];
            var bw = new BoneWeight[nv];
            for (int i = 0; i < nv; i++) pos[i] = ReadV3(r);
            for (int i = 0; i < nv; i++) nrm[i] = ReadV3(r);
            for (int i = 0; i < nv; i++) uv[i] = new Vector2(r.ReadSingle(), r.ReadSingle());
            var idx = r.ReadBytes(nv * 4);
            var wts = r.ReadBytes(nv * 4);
            for (int i = 0; i < nv; i++)
            {
                int o = i * 4;
                bw[i] = new BoneWeight
                {
                    boneIndex0 = idx[o], weight0 = wts[o] / 255f,
                    boneIndex1 = idx[o + 1], weight1 = wts[o + 1] / 255f,
                    boneIndex2 = idx[o + 2], weight2 = wts[o + 2] / 255f,
                    boneIndex3 = idx[o + 3], weight3 = wts[o + 3] / 255f,
                };
            }
            var tris = new int[ni];
            for (int i = 0; i < ni; i++) tris[i] = r.ReadUInt16();

            var mesh = new Mesh { name = name };
            mesh.vertices = pos;
            mesh.normals = nrm;
            mesh.uv = uv;
            mesh.triangles = tris;
            mesh.boneWeights = bw;
            mesh.bindposes = bind;
            mesh.RecalculateBounds();
            mesh.UploadMeshData(false); // keep CPU data: the SkinnedMeshRenderer may need it for CPU skinning on old GPUs
            return mesh;
        }
    }
}
