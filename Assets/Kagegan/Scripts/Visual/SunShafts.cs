using UnityEngine;

namespace Kagegan
{
    /// <summary>
    /// Cheap sun shafts for the forest: a few long soft additive strips that fall from the canopy along the sun's
    /// direction, behind the fighting lane. All strips live in one mesh (one draw call), turned around their own
    /// axis toward the camera every frame. They are brightest when the camera looks toward the sun, fade near the
    /// lens and vanish at night, in bad weather and on the Low preset (count from GraphicsQuality.SunShafts).
    /// </summary>
    public class SunShafts : MonoBehaviour
    {
        const int Max = 10;
        struct Shaft { public Vector3 end; public float top, width, phase, gain; }

        readonly Shaft[] shafts = new Shaft[Max];
        Mesh mesh;
        MeshRenderer mr;
        Material mat;
        static Texture2D tex;
        int built = -1;

        void Start()
        {
            var rnd = new System.Random(77);
            for (int i = 0; i < Max; i++)
            {
                // where the light lands: spread along the lane, in the trees just behind it
                float x = -20f + 40f * (i + (float)rnd.NextDouble() * 0.8f) / Max;
                float z = Arena.LaneDepth + 1f + (float)rnd.NextDouble() * 9f;
                shafts[i] = new Shaft
                {
                    end = new Vector3(x, 0.3f, z),
                    top = 9f + (float)rnd.NextDouble() * 5f,
                    width = 1.2f + (float)rnd.NextDouble() * 2.6f,
                    phase = (float)rnd.NextDouble() * 6.28f,
                    gain = 0.6f + (float)rnd.NextDouble() * 0.4f,
                };
            }
            mesh = new Mesh { name = "SunShafts" };
            mesh.MarkDynamic();
            var go = new GameObject("SunShaftsMesh");
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            mr = go.AddComponent<MeshRenderer>();
            mat = new Material(Mat.FxAdd) { name = "SunShafts", mainTexture = Tex() };
            mat.SetFloat("_Fog", 0f);
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            MakeMotes();
        }

        // golden dust drifting in the light, behind the lane where the shafts fall
        ParticleSystem motes;
        Material moteMat;

        void MakeMotes()
        {
            var go = new GameObject("SunMotes");
            go.transform.SetParent(transform, false);
            go.transform.position = new Vector3(0f, 2.6f, Arena.LaneDepth + 3.5f);
            motes = go.AddComponent<ParticleSystem>();
            motes.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = motes.main;
            main.loop = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(6f, 10f);
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.035f, 0.08f);
            main.maxParticles = 90;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            var sh = motes.shape;
            sh.shapeType = ParticleSystemShapeType.Box;
            sh.scale = new Vector3(36f, 4.5f, 8f);
            var vel = motes.velocityOverLifetime;
            vel.enabled = true;
            vel.space = ParticleSystemSimulationSpace.World;
            vel.x = new ParticleSystem.MinMaxCurve(-0.08f, 0.12f);
            vel.y = new ParticleSystem.MinMaxCurve(-0.05f, 0.06f);
            vel.z = new ParticleSystem.MinMaxCurve(-0.05f, 0.05f);
            var noise = motes.noise;
            noise.enabled = true;
            noise.strength = 0.12f;
            noise.frequency = 0.4f;
            noise.quality = ParticleSystemNoiseQuality.Low;
            // fade in and out so no speck pops
            var col = motes.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.25f), new GradientAlphaKey(1f, 0.7f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            var r = go.GetComponent<ParticleSystemRenderer>();
            moteMat = new Material(Mat.FxAdd) { name = "SunMotes", mainTexture = Mat.SoftDot };
            moteMat.SetFloat("_Fog", 0f);
            r.sharedMaterial = moteMat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            var em = motes.emission;
            em.rateOverTime = 0f;
            motes.Play();
        }

        void UpdateMotes(int n, float strength, Color sun)
        {
            if (motes == null) return;
            var em = motes.emission;
            // as many motes as the preset allows shafts (Low: none)
            em.rateOverTime = n * 1.1f;
            var main = motes.main;
            float k = Mathf.Clamp01(strength) * 0.7f;
            main.startColor = new Color(sun.r * k, sun.g * k * 0.92f, sun.b * k * 0.7f, 1f);
        }

        void OnDestroy()
        {
            if (mesh != null) Destroy(mesh);
            if (mat != null) Destroy(mat);
            if (moteMat != null) Destroy(moteMat);
        }

        void Build(int n)
        {
            built = n;
            mesh.Clear();
            if (n <= 0) return;
            var uv = new Vector2[n * 4];
            var tris = new int[n * 6];
            for (int i = 0; i < n; i++)
            {
                uv[i * 4] = new Vector2(0, 0); uv[i * 4 + 1] = new Vector2(1, 0);
                uv[i * 4 + 2] = new Vector2(0, 1); uv[i * 4 + 3] = new Vector2(1, 1);
                int b = i * 4, t = i * 6;
                tris[t] = b; tris[t + 1] = b + 2; tris[t + 2] = b + 1;
                tris[t + 3] = b + 1; tris[t + 4] = b + 2; tris[t + 5] = b + 3;
            }
            mesh.vertices = new Vector3[n * 4];
            mesh.uv = uv;
            mesh.colors = new Color[n * 4];
            mesh.triangles = tris;
            mesh.bounds = new Bounds(new Vector3(0, 8, 10), new Vector3(120, 40, 80));
        }

        Vector3[] vBuf; Color[] cBuf;

        void LateUpdate()
        {
            if (mesh == null) return;
            int n = Mathf.Clamp(GraphicsQuality.SunShafts, 0, Max);
            var cam = CameraRig.Inst != null ? CameraRig.Inst.Cam : Camera.main;
            Vector3 l = Shader.GetGlobalVector("_KageLightDir");
            Color sun = Shader.GetGlobalColor("_KageLightColor");
            float lum = sun.r * 0.3f + sun.g * 0.59f + sun.b * 0.11f;
            // no shafts from a sun that is down, too low to reach under the canopy, or hidden by storm clouds
            float day = Mathf.Clamp01((lum - 0.45f) / 0.35f) * Mathf.Clamp01((l.y - 0.2f) / 0.15f);
            if (cam == null || l.sqrMagnitude < 0.01f || day <= 0f) n = 0;
            if (n != built) { Build(n); vBuf = n > 0 ? new Vector3[n * 4] : null; cBuf = n > 0 ? new Color[n * 4] : null; }
            mr.enabled = n > 0;
            if (n == 0) { UpdateMotes(0, 0f, sun); return; }

            l.Normalize();
            Vector3 a = -l; // the way the light travels
            Vector3 cp = cam.transform.position;
            // shafts only show when the camera looks toward the light
            float face = Mathf.Clamp01(Vector3.Dot(cam.transform.forward, l) * 0.8f + 0.45f);
            float t = Time.time;
            UpdateMotes(n, day * face, sun);
            for (int i = 0; i < n; i++)
            {
                var s = shafts[i];
                float len = (s.top - s.end.y) / Mathf.Max(0.25f, -a.y);
                Vector3 p0 = s.end - a * len, p1 = s.end;
                Vector3 mid = (p0 + p1) * 0.5f;
                Vector3 side = Vector3.Cross(a, cp - mid);
                if (side.sqrMagnitude < 1e-4f) side = Vector3.right;
                side = side.normalized * (s.width * 0.5f);
                // the bottom spreads a little, like light opening out under the leaves
                vBuf[i * 4] = p0 - side * 0.7f; vBuf[i * 4 + 1] = p0 + side * 0.7f;
                vBuf[i * 4 + 2] = p1 - side * 1.3f; vBuf[i * 4 + 3] = p1 + side * 1.3f;
                // leaves moving in the wind open and close the gap the light comes through
                float flick = 0.75f + 0.25f * Mathf.Sin(t * 0.35f + s.phase) * Mathf.Sin(t * 0.21f + s.phase * 1.7f);
                // thin out a shaft the camera is about to pass through
                float near = Mathf.Clamp01((DistToSegment(cp, p0, p1) - 2.5f) / 4f);
                float k = 0.16f * s.gain * flick * near * face * day;
                // the Fx shader premultiplies by alpha, so alpha stays 1 and the strength goes into the colour
                Color col = new Color(sun.r * k, sun.g * k * 0.97f, sun.b * k * 0.85f, 1f);
                cBuf[i * 4] = cBuf[i * 4 + 1] = cBuf[i * 4 + 2] = cBuf[i * 4 + 3] = col;
            }
            mesh.vertices = vBuf;
            mesh.colors = cBuf;
        }

        static float DistToSegment(Vector3 p, Vector3 a, Vector3 b)
        {
            Vector3 ab = b - a;
            float u = Mathf.Clamp01(Vector3.Dot(p - a, ab) / Mathf.Max(ab.sqrMagnitude, 1e-4f));
            return Vector3.Distance(p, a + ab * u);
        }

        /// <summary>Soft strip: bright core across, fades in under the canopy and out toward the ground, faint streaks.</summary>
        static Texture2D Tex()
        {
            if (tex != null) return tex;
            const int w = 32, h = 128;
            tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { name = "SunShaft", wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[w * h];
            for (int y = 0; y < h; y++)
            {
                float vv = y / (h - 1f); // 0 at the canopy, 1 on the ground
                float along = Mathf.SmoothStep(0f, 1f, vv / 0.2f) * Mathf.Pow(1f - vv, 1.6f);
                for (int x = 0; x < w; x++)
                {
                    float u = x / (w - 1f) * 2f - 1f;
                    float across = Mathf.Pow(Mathf.Clamp01(1f - u * u), 2.2f);
                    float streak = 0.75f + 0.25f * Mathf.PerlinNoise(x * 0.45f, vv * 1.5f);
                    byte g = (byte)Mathf.RoundToInt(255f * Mathf.Clamp01(along * across * streak));
                    px[y * w + x] = new Color32(255, 255, 255, g);
                }
            }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            return tex;
        }
    }
}
