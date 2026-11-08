using System.Collections.Generic;
using UnityEngine;

namespace Kagegan
{
    /// <summary>
    /// Builds low-poly vertex-colored meshes from primitives (boxes, spheres, tapered limbs, cones).
    /// For characters every vertex is tagged with one bone so the result becomes a single SkinnedMeshRenderer
    /// (one draw call per character, 1-bone skinning).
    /// </summary>
    public class MeshBuilder
    {
        public readonly List<Vector3> V = new List<Vector3>(2048);
        public readonly List<Vector3> N = new List<Vector3>(2048);
        public readonly List<Color32> C = new List<Color32>(2048);
        public readonly List<Vector2> UV = new List<Vector2>(2048);
        public readonly List<int> T = new List<int>(6144);
        public readonly List<int> Bone = new List<int>(2048);

        public Matrix4x4 M = Matrix4x4.identity;
        public int CurrentBone;
        public Color32 Col = new Color32(255, 255, 255, 0);

        public MeshBuilder SetColor(Color32 c) { Col = c; return this; }
        public MeshBuilder SetBone(int b) { CurrentBone = b; return this; }

        int AddVert(Vector3 p, Vector3 n, Vector2 uv)
        {
            V.Add(M.MultiplyPoint3x4(p));
            N.Add(M.MultiplyVector(n).normalized);
            C.Add(Col);
            UV.Add(uv);
            Bone.Add(CurrentBone);
            return V.Count - 1;
        }

        public void Box(Vector3 center, Vector3 size, Quaternion rot)
        {
            Vector3 h = size * 0.5f;
            for (int f = 0; f < 6; f++)
            {
                Vector3 n = f == 0 ? Vector3.right : f == 1 ? Vector3.left : f == 2 ? Vector3.up : f == 3 ? Vector3.down : f == 4 ? Vector3.forward : Vector3.back;
                Vector3 u = Mathf.Abs(n.y) > 0.5f ? Vector3.right : Vector3.up;
                Vector3 w = Vector3.Cross(n, u);
                int start = V.Count;
                for (int i = 0; i < 4; i++)
                {
                    float su = (i == 0 || i == 3) ? -1f : 1f, sw = (i < 2) ? -1f : 1f;
                    Vector3 p = n + u * su + w * sw;
                    p = Vector3.Scale(p, h);
                    AddVert(center + rot * p, rot * n, new Vector2((su + 1) * 0.5f, (sw + 1) * 0.5f));
                }
                T.Add(start); T.Add(start + 1); T.Add(start + 2);
                T.Add(start); T.Add(start + 2); T.Add(start + 3);
            }
        }

        public void Box(Vector3 center, Vector3 size) => Box(center, size, Quaternion.identity);

        public void Sphere(Vector3 center, Vector3 radius, int lat = 6, int lon = 8, Quaternion? rotation = null)
        {
            Quaternion rot = rotation ?? Quaternion.identity;
            int start = V.Count;
            for (int y = 0; y <= lat; y++)
            {
                float v = (float)y / lat, phi = v * Mathf.PI;
                for (int x = 0; x <= lon; x++)
                {
                    float u = (float)x / lon, th = u * Mathf.PI * 2f;
                    Vector3 n = new Vector3(Mathf.Sin(phi) * Mathf.Cos(th), Mathf.Cos(phi), Mathf.Sin(phi) * Mathf.Sin(th));
                    Vector3 p = Vector3.Scale(n, radius);
                    Vector3 nn = new Vector3(n.x / Mathf.Max(radius.x, 1e-4f), n.y / Mathf.Max(radius.y, 1e-4f), n.z / Mathf.Max(radius.z, 1e-4f));
                    AddVert(center + rot * p, rot * nn.normalized, new Vector2(u, v));
                }
            }
            for (int y = 0; y < lat; y++)
                for (int x = 0; x < lon; x++)
                {
                    int a = start + y * (lon + 1) + x, b = a + lon + 1;
                    T.Add(a); T.Add(a + 1); T.Add(b);
                    T.Add(a + 1); T.Add(b + 1); T.Add(b);
                }
        }

        public void Sphere(Vector3 center, float r, int lat = 6, int lon = 8) => Sphere(center, new Vector3(r, r, r), lat, lon);

        /// <summary>Tapered cylinder from a to b with rounded-ish caps (good for limbs and trunks).</summary>
        public void Limb(Vector3 a, Vector3 b, float ra, float rb, int seg = 8, bool caps = true, float flatten = 1f)
        {
            Vector3 axis = b - a;
            float len = axis.magnitude;
            if (len < 1e-5f) return;
            Vector3 dir = axis / len;
            Vector3 side = Vector3.Cross(dir, Mathf.Abs(dir.y) > 0.9f ? Vector3.forward : Vector3.up).normalized;
            Vector3 up = Vector3.Cross(side, dir);
            int start = V.Count;
            float slope = (ra - rb) / len;
            for (int ring = 0; ring < 2; ring++)
            {
                Vector3 c = ring == 0 ? a : b;
                float r = ring == 0 ? ra : rb;
                for (int i = 0; i <= seg; i++)
                {
                    float ang = (float)i / seg * Mathf.PI * 2f;
                    Vector3 radial = side * Mathf.Cos(ang) + up * Mathf.Sin(ang) * flatten;
                    Vector3 n = (radial.normalized + dir * slope).normalized;
                    AddVert(c + radial * r, n, new Vector2((float)i / seg, ring));
                }
            }
            for (int i = 0; i < seg; i++)
            {
                int i0 = start + i, i1 = start + seg + 1 + i;
                T.Add(i0); T.Add(i1); T.Add(i0 + 1);
                T.Add(i0 + 1); T.Add(i1); T.Add(i1 + 1);
            }
            if (caps)
            {
                Sphere(a, new Vector3(ra, ra, ra), 4, seg);
                Sphere(b, new Vector3(rb, rb, rb), 4, seg);
            }
        }

        /// <summary>One curved, tapering grass blade (uv.y = 0 at the root, 1 at the tip for the wind shader).</summary>
        public void Blade(Vector3 root, float height, float width, float yaw, float lean, Color32 rootCol, Color32 tipCol)
        {
            Vector3 fwd = new Vector3(Mathf.Sin(yaw), 0f, Mathf.Cos(yaw));
            Vector3 side = new Vector3(fwd.z, 0f, -fwd.x);
            Vector3 n = (Vector3.up + fwd * 0.45f).normalized; // soft, mostly-up normal so tufts shade like a lawn
            var keep = Col;
            const int S = 3;
            int start = V.Count;
            for (int i = 0; i < S; i++)
            {
                float t = (float)i / S;
                Vector3 c = root + Vector3.up * height * t + fwd * lean * height * t * t;
                float w = width * (1f - t * 0.75f) * 0.5f;
                Col = Color32.Lerp(rootCol, tipCol, t);
                AddVert(c - side * w, n, new Vector2(0f, t));
                AddVert(c + side * w, n, new Vector2(1f, t));
            }
            Col = tipCol;
            int tip = AddVert(root + Vector3.up * height + fwd * lean * height, n, new Vector2(0.5f, 1f));
            for (int i = 0; i < S - 1; i++)
            {
                int a = start + i * 2;
                T.Add(a); T.Add(a + 2); T.Add(a + 1);
                T.Add(a + 1); T.Add(a + 2); T.Add(a + 3);
            }
            int l = start + (S - 1) * 2;
            T.Add(l); T.Add(tip); T.Add(l + 1);
            Col = keep;
        }

        /// <summary>Small flower head: a fan of petals around a centre. Every vertex has uv.y = 1, so the wind and
        /// trampling move it exactly like the tip of the stem it sits on (grass shader bends by uv.y).</summary>
        public void Flower(Vector3 center, float radius, int petals, float yaw, Color32 petalCol, Color32 centreCol)
        {
            var keep = Col;
            Vector3 n = Vector3.up;
            Col = centreCol;
            int c = AddVert(center + Vector3.up * radius * 0.15f, n, new Vector2(0.5f, 1f));
            Col = petalCol;
            for (int i = 0; i < petals; i++)
            {
                float a0 = yaw + (i - 0.32f) / petals * Mathf.PI * 2f;
                float a1 = yaw + (i + 0.32f) / petals * Mathf.PI * 2f;
                float am = yaw + (float)i / petals * Mathf.PI * 2f;
                // petals cup slightly upwards, tips a bit lower than the middle
                int v0 = AddVert(center + new Vector3(Mathf.Cos(a0), 0.25f, Mathf.Sin(a0)) * radius * 0.55f, n, new Vector2(0f, 1f));
                int v1 = AddVert(center + new Vector3(Mathf.Cos(am), 0.1f, Mathf.Sin(am)) * radius, n, new Vector2(0.5f, 1f));
                int v2 = AddVert(center + new Vector3(Mathf.Cos(a1), 0.25f, Mathf.Sin(a1)) * radius * 0.55f, n, new Vector2(1f, 1f));
                T.Add(c); T.Add(v1); T.Add(v0);
                T.Add(c); T.Add(v2); T.Add(v1);
            }
            Col = keep;
        }

        /// <summary>Fallen leaf lying on the ground (uv.y = 0: the wind shader leaves it still).</summary>
        public void Leaf(Vector3 center, float length, float yaw, Color32 col)
        {
            var keep = Col;
            Col = col;
            Vector3 f = new Vector3(Mathf.Sin(yaw), 0f, Mathf.Cos(yaw)) * length * 0.5f;
            Vector3 s = new Vector3(f.z, 0f, -f.x) * 0.45f;
            Vector3 n = Vector3.up;
            int a = AddVert(center - f, n, Vector2.zero);
            int b = AddVert(center + s + Vector3.up * 0.012f, n, Vector2.zero);
            int c = AddVert(center + f, n, Vector2.zero);
            int d = AddVert(center - s + Vector3.up * 0.012f, n, Vector2.zero);
            T.Add(a); T.Add(c); T.Add(b);
            T.Add(a); T.Add(d); T.Add(c);
            Col = keep;
        }

        public void Cone(Vector3 baseCenter, Vector3 tip, float r, int seg = 6, bool capBase = true)
        {
            Vector3 dir = (tip - baseCenter).normalized;
            Vector3 side = Vector3.Cross(dir, Mathf.Abs(dir.y) > 0.9f ? Vector3.forward : Vector3.up).normalized;
            Vector3 up = Vector3.Cross(side, dir);
            float h = (tip - baseCenter).magnitude;
            int start = V.Count;
            for (int i = 0; i <= seg; i++)
            {
                float ang = (float)i / seg * Mathf.PI * 2f;
                Vector3 radial = side * Mathf.Cos(ang) + up * Mathf.Sin(ang);
                Vector3 n = (radial * h + dir * r).normalized;
                AddVert(baseCenter + radial * r, n, new Vector2((float)i / seg, 0));
                AddVert(tip, n, new Vector2((float)i / seg, 1));
            }
            for (int i = 0; i < seg; i++)
            {
                int b0 = start + i * 2;
                T.Add(b0); T.Add(b0 + 1); T.Add(b0 + 2);
            }
            if (capBase)
            {
                int c = AddVert(baseCenter, -dir, new Vector2(0.5f, 0.5f));
                int rs = V.Count;
                for (int i = 0; i <= seg; i++)
                {
                    float ang = (float)i / seg * Mathf.PI * 2f;
                    AddVert(baseCenter + (side * Mathf.Cos(ang) + up * Mathf.Sin(ang)) * r, -dir, Vector2.zero);
                }
                for (int i = 0; i < seg; i++) { T.Add(c); T.Add(rs + i); T.Add(rs + i + 1); }
            }
        }

        /// <summary>Flat quad facing +normal (used for decals, ground patches, banners).</summary>
        public void Quad(Vector3 center, Vector3 right, Vector3 upDir, bool doubleSided = false)
        {
            Vector3 n = Vector3.Cross(right, upDir).normalized;
            int s = V.Count;
            AddVert(center - right - upDir, n, new Vector2(0, 0));
            AddVert(center + right - upDir, n, new Vector2(1, 0));
            AddVert(center + right + upDir, n, new Vector2(1, 1));
            AddVert(center - right + upDir, n, new Vector2(0, 1));
            T.Add(s); T.Add(s + 1); T.Add(s + 2);
            T.Add(s); T.Add(s + 2); T.Add(s + 3);
            if (doubleSided) { T.Add(s); T.Add(s + 2); T.Add(s + 1); T.Add(s); T.Add(s + 3); T.Add(s + 2); }
        }

        /// <summary>Upward-facing ring strip on the XZ plane (shockwaves, magic circles).</summary>
        public void RingXZ(Vector3 center, float inner, float outer, int seg, Color32 innerCol, Color32 outerCol)
        {
            int s = V.Count;
            for (int i = 0; i <= seg; i++)
            {
                float a = (float)i / seg * Mathf.PI * 2f;
                Vector3 d = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                Col = innerCol; AddVert(center + d * inner, Vector3.up, new Vector2((float)i / seg, 0));
                Col = outerCol; AddVert(center + d * outer, Vector3.up, new Vector2((float)i / seg, 1));
            }
            for (int i = 0; i < seg; i++)
            {
                int a = s + i * 2;
                T.Add(a); T.Add(a + 2); T.Add(a + 1);
                T.Add(a + 1); T.Add(a + 2); T.Add(a + 3);
            }
        }

        public Mesh ToMesh(string name)
        {
            var m = new Mesh { name = name };
            if (V.Count > 65000) m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            m.SetVertices(V);
            m.SetNormals(N);
            m.SetColors(C);
            m.SetUVs(0, UV);
            m.SetTriangles(T, 0);
            m.RecalculateBounds();
            return m;
        }

        /// <summary>Build a skinned mesh; vertices were authored in each bone's local space.</summary>
        public Mesh ToSkinnedMesh(string name, Matrix4x4[] boneToRoot, Matrix4x4[] bindposes, System.Func<int, Vector3, BoneWeight> getWeight)
        {
            var weights = new BoneWeight[V.Count];
            for (int i = 0; i < V.Count; i++)
            {
                weights[i] = getWeight(Bone[i], V[i]);
                var bm = boneToRoot[Bone[i]];
                V[i] = bm.MultiplyPoint3x4(V[i]);
                N[i] = bm.MultiplyVector(N[i]).normalized;
            }
            var m = ToMesh(name);
            m.boneWeights = weights;
            m.bindposes = bindposes;
            return m;
        }

        public void Clear()
        {
            V.Clear(); N.Clear(); C.Clear(); UV.Clear(); T.Clear(); Bone.Clear();
            M = Matrix4x4.identity; CurrentBone = 0;
        }
    }
}
