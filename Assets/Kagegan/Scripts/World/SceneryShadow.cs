using System.Collections.Generic;
using UnityEngine;

namespace Kagegan
{
    /// <summary>
    /// Baked sun shadows of the static scenery (High / Ultra). Once per arena every static triangle is projected
    /// along the sun onto the ground plane and rasterised into a small texture: R = coverage (softened),
    /// G = height of the highest occluder. The toon and ground shaders read it back (KageSceneShadow in
    /// KageCasterShadow.cginc), so trees, gates and buildings throw shadows on the ground, the grass and the
    /// fighters without a shadow-map pass. Breakable props drop out of the bake when they break.
    /// </summary>
    public static class SceneryShadow
    {
        const int N = 512;
        const float X0 = -48f, Z0 = -30f, Size = 96f;   // world rectangle covered (the lane runs along x, camera at -z)
        const float MaxH = 32f;                          // heights are stored as h / MaxH in 8 bits
        const float MinH = 0.3f;                         // triangles lower than this are ground, not occluders
        const float Strength = 0.72f;

        class Source
        {
            public Object Owner;      // the Destructible it belongs to, or null for permanent scenery
            public float[] Tri;       // 9 floats per triangle: u, v (texels), height (m) for each corner
            public int Count;
            public int MinX, MinY, MaxX, MaxY;
        }

        static readonly List<Source> sources = new List<Source>();
        static float[] cov, hgt;
        static Color32[] px;
        static float[] bufA, bufB, bufT;
        static Texture2D tex;
        static Vector3 light;

        static readonly int IdTex = Shader.PropertyToID("_KageSceneShadow");
        static readonly int IdRect = Shader.PropertyToID("_KageSceneShadowRect");

        public static bool Active { get; private set; }

        public static void Clear()
        {
            sources.Clear();
            Active = false;
            Shader.SetGlobalVector(IdRect, new Vector4(X0, Z0, 1f / Size, 0f));
        }

        /// <summary>Bake the arena's static renderers for this sun direction (direction TO the sun).</summary>
        public static void Bake(Transform root, Vector3 dirToSun)
        {
            Clear();
            if (root == null || GraphicsQuality.Current < GraphicsPreset.High) return;
            light = dirToSun.normalized;
            if (light.y < 0.15f) light.y = 0.15f;  // same clamp as the shader

            float t0 = Time.realtimeSinceStartup;
            int tris = 0;
            var rends = root.GetComponentsInChildren<MeshRenderer>(false);
            foreach (var r in rends)
            {
                if (!Eligible(r)) continue;
                var mf = r.GetComponent<MeshFilter>();
                var mesh = mf != null ? mf.sharedMesh : null;
                if (mesh == null || !mesh.isReadable) continue;
                var s = Project(mesh, r.transform.localToWorldMatrix);
                if (s == null) continue;
                s.Owner = r.GetComponentInParent<Destructible>();
                sources.Add(s);
                tris += s.Count;
            }
            if (sources.Count == 0) return;

            if (cov == null) { cov = new float[N * N]; hgt = new float[N * N]; px = new Color32[N * N]; }
            System.Array.Clear(cov, 0, cov.Length);
            System.Array.Clear(hgt, 0, hgt.Length);
            foreach (var s in sources) Raster(s, 0, 0, N - 1, N - 1);
            Upload();
            Active = true;
            Debug.Log("[Kagegan] scenery shadow: " + sources.Count + " meshes, " + tris + " tris, " +
                      ((Time.realtimeSinceStartup - t0) * 1000f).ToString("0") + " ms");
        }

        /// <summary>A breakable prop broke or fell: take its shadow out (only that patch is re-rasterised).</summary>
        public static void Remove(Object owner)
        {
            if (!Active || owner == null) return;
            int x0 = N, y0 = N, x1 = -1, y1 = -1;
            for (int i = sources.Count - 1; i >= 0; i--)
            {
                var s = sources[i];
                if (s.Owner != owner) continue;
                x0 = Mathf.Min(x0, s.MinX); y0 = Mathf.Min(y0, s.MinY);
                x1 = Mathf.Max(x1, s.MaxX); y1 = Mathf.Max(y1, s.MaxY);
                sources.RemoveAt(i);
            }
            if (x1 < 0) return;
            x0 = Mathf.Max(0, x0 - 1); y0 = Mathf.Max(0, y0 - 1);
            x1 = Mathf.Min(N - 1, x1 + 1); y1 = Mathf.Min(N - 1, y1 + 1);
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++) { cov[y * N + x] = 0f; hgt[y * N + x] = 0f; }
            foreach (var s in sources)
                if (s.MaxX >= x0 && s.MinX <= x1 && s.MaxY >= y0 && s.MinY <= y1) Raster(s, x0, y0, x1, y1);
            Upload();
        }

        /// <summary>Static scenery that throws sun shadows (also used by KageShadowMap).</summary>
        internal static bool Eligible(MeshRenderer r)
        {
            if (!r.enabled) return false;
            string n = r.gameObject.name;
            if (n == "GroundPhoto" || n == "Grass") return false;
            if (r.GetComponentInParent<FollowCameraXZ>() != null) return false; // sky dome
            var b = r.bounds;
            if (b.max.y < MinH || b.min.y > 18f) return false;                 // ground decals, clouds
            if (b.size.x > 160f || b.size.z > 160f) return false;              // sky / huge backdrop shells
            foreach (var m in r.sharedMaterials)
                if (m == null || m.renderQueue >= 2500) return false;          // glass, water, glows
            return true;
        }

        static Source Project(Mesh mesh, Matrix4x4 m)
        {
            var v = mesh.vertices;
            var t = mesh.triangles;
            if (v.Length == 0 || t.Length < 3) return null;
            float k = N / Size;
            var uvh = new Vector3[v.Length];
            for (int i = 0; i < v.Length; i++)
            {
                Vector3 w = m.MultiplyPoint3x4(v[i]);
                float s = w.y / light.y;
                uvh[i] = new Vector3((w.x - light.x * s - X0) * k, (w.z - light.z * s - Z0) * k, w.y);
            }
            var list = new List<float>(t.Length * 3);
            int minX = N, minY = N, maxX = -1, maxY = -1;
            for (int i = 0; i + 2 < t.Length; i += 3)
            {
                Vector3 a = uvh[t[i]], b = uvh[t[i + 1]], c = uvh[t[i + 2]];
                if (Mathf.Max(a.z, Mathf.Max(b.z, c.z)) < MinH) continue;
                float lx = Mathf.Min(a.x, Mathf.Min(b.x, c.x)), hx = Mathf.Max(a.x, Mathf.Max(b.x, c.x));
                float ly = Mathf.Min(a.y, Mathf.Min(b.y, c.y)), hy = Mathf.Max(a.y, Mathf.Max(b.y, c.y));
                if (hx < 0f || hy < 0f || lx >= N || ly >= N) continue;
                list.Add(a.x); list.Add(a.y); list.Add(a.z);
                list.Add(b.x); list.Add(b.y); list.Add(b.z);
                list.Add(c.x); list.Add(c.y); list.Add(c.z);
                minX = Mathf.Min(minX, Mathf.Max(0, (int)lx)); minY = Mathf.Min(minY, Mathf.Max(0, (int)ly));
                maxX = Mathf.Max(maxX, Mathf.Min(N - 1, (int)hx + 1)); maxY = Mathf.Max(maxY, Mathf.Min(N - 1, (int)hy + 1));
            }
            if (list.Count == 0) return null;
            return new Source { Tri = list.ToArray(), Count = list.Count / 9, MinX = minX, MinY = minY, MaxX = maxX, MaxY = maxY };
        }

        /// <summary>Fill the projected triangles (pixel centres) inside the clip box: coverage 1, keep the highest occluder.</summary>
        static void Raster(Source s, int cx0, int cy0, int cx1, int cy1)
        {
            var d = s.Tri;
            for (int i = 0; i < d.Length; i += 9)
            {
                float ax = d[i], ay = d[i + 1], ah = d[i + 2];
                float bx = d[i + 3], by = d[i + 4], bh = d[i + 5];
                float qx = d[i + 6], qy = d[i + 7], qh = d[i + 8];
                float area = (bx - ax) * (qy - ay) - (by - ay) * (qx - ax);
                if (Mathf.Abs(area) < 1e-5f)
                {
                    // edge-on sliver (e.g. a wall seen exactly along the sun): mark the corner pixels
                    Plot(ax, ay, ah, cx0, cy0, cx1, cy1); Plot(bx, by, bh, cx0, cy0, cx1, cy1); Plot(qx, qy, qh, cx0, cy0, cx1, cy1);
                    continue;
                }
                float inv = 1f / area;
                int x0 = Mathf.Max(cx0, Mathf.FloorToInt(Mathf.Min(ax, Mathf.Min(bx, qx))));
                int x1 = Mathf.Min(cx1, Mathf.CeilToInt(Mathf.Max(ax, Mathf.Max(bx, qx))));
                int y0 = Mathf.Max(cy0, Mathf.FloorToInt(Mathf.Min(ay, Mathf.Min(by, qy))));
                int y1 = Mathf.Min(cy1, Mathf.CeilToInt(Mathf.Max(ay, Mathf.Max(by, qy))));
                if (x0 > x1 || y0 > y1) continue;
                if (x0 == x1 && y0 == y1) { Plot(ax, ay, Mathf.Max(ah, Mathf.Max(bh, qh)), cx0, cy0, cx1, cy1); continue; }
                const float eps = -0.02f;
                for (int y = y0; y <= y1; y++)
                {
                    float py = y + 0.5f;
                    for (int x = x0; x <= x1; x++)
                    {
                        float pxf = x + 0.5f;
                        float w0 = ((bx - pxf) * (qy - py) - (by - py) * (qx - pxf)) * inv;
                        float w1 = ((qx - pxf) * (ay - py) - (qy - py) * (ax - pxf)) * inv;
                        float w2 = 1f - w0 - w1;
                        if (w0 < eps || w1 < eps || w2 < eps) continue;
                        float h = w0 * ah + w1 * bh + w2 * qh;
                        int idx = y * N + x;
                        cov[idx] = 1f;
                        if (h > hgt[idx]) hgt[idx] = h;
                    }
                }
            }
        }

        static void Plot(float x, float y, float h, int cx0, int cy0, int cx1, int cy1)
        {
            int ix = (int)x, iy = (int)y;
            if (ix < cx0 || iy < cy0 || ix > cx1 || iy > cy1) return;
            int idx = iy * N + ix;
            cov[idx] = 1f;
            if (h > hgt[idx]) hgt[idx] = h;
        }

        /// <summary>Soften the coverage (two box passes), widen the heights by a texel so the soft edge keeps them, upload.</summary>
        static void Upload()
        {
            if (bufA == null) { bufA = new float[N * N]; bufB = new float[N * N]; bufT = new float[N * N]; }
            var b = bufB;
            BoxBlur(cov, bufA, bufT, 2);
            BoxBlur(bufA, bufB, bufT, 1);  // result lands in bufB
            for (int y = 0; y < N; y++)
            {
                int y0 = Mathf.Max(0, y - 2), y1 = Mathf.Min(N - 1, y + 2);
                for (int x = 0; x < N; x++)
                {
                    int x0 = Mathf.Max(0, x - 2), x1 = Mathf.Min(N - 1, x + 2);
                    float h = 0f;
                    for (int yy = y0; yy <= y1; yy++)
                    {
                        int row = yy * N;
                        for (int xx = x0; xx <= x1; xx++) { float v = hgt[row + xx]; if (v > h) h = v; }
                    }
                    int i = y * N + x;
                    float c = b[i];
                    px[i] = new Color32((byte)(Mathf.Clamp01(c) * 255f + 0.5f), (byte)(Mathf.Clamp01(h / MaxH) * 255f + 0.5f), 0, 255);
                }
            }
            if (tex == null)
            {
                tex = new Texture2D(N, N, TextureFormat.RGBA32, false, true)
                {
                    name = "KageSceneShadow", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear
                };
            }
            tex.SetPixels32(px);
            tex.Apply(false, false);
            Shader.SetGlobalTexture(IdTex, tex);
            Shader.SetGlobalVector(IdRect, new Vector4(X0, Z0, 1f / Size, Strength));
        }

        /// <summary>Separable box blur of `src` with radius r: horizontal into `tmp`, vertical into `dst`.</summary>
        static void BoxBlur(float[] src, float[] dst, float[] tmp, int r)
        {
            float inv = 1f / (2 * r + 1);
            for (int y = 0; y < N; y++)
            {
                int row = y * N;
                for (int x = 0; x < N; x++)
                {
                    float sum = 0f;
                    for (int k = -r; k <= r; k++) sum += src[row + Mathf.Clamp(x + k, 0, N - 1)];
                    tmp[row + x] = sum * inv;
                }
            }
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float sum = 0f;
                    for (int k = -r; k <= r; k++) sum += tmp[Mathf.Clamp(y + k, 0, N - 1) * N + x];
                    dst[y * N + x] = sum * inv;
                }
        }
    }
}
