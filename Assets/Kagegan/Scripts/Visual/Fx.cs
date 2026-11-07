using System.Collections.Generic;
using UnityEngine;

namespace Kagegan
{
    /// <summary>
    /// Visual effects. Particle bursts are emitted from a few shared world-space ParticleSystems (no per-effect objects),
    /// meshes like shockwave rings, slash arcs, ground cracks and lightning bolts are pooled.
    /// Particle counts scale with the graphics preset to keep overdraw low on weak GPUs.
    /// </summary>
    public class Fx : MonoBehaviour
    {
        static Fx inst;
        ParticleSystem sparks, smoke, fire, debris, glow, leaves;
        readonly List<Anim> anims = new List<Anim>(64);
        Pool<MeshRenderer> ringPool, slashPool, crackPool, spherePool;
        Pool<LineRenderer> boltPool;
        Mesh ringMesh, slashMesh, crackMesh, sphereMesh;
        MaterialPropertyBlock mpb;
        static readonly int IdColor = Shader.PropertyToID("_Color");

        enum Kind { Ring, Slash, Crack, Bolt, Sphere }

        class Anim
        {
            public Kind kind;
            public Component c;
            public float t, life, size0, size1;
            public Color color;
            public Vector3 from, to;
            public Transform follow;
        }

        public static void Init(GameObject host)
        {
            inst = new GameObject("Fx").AddComponent<Fx>();
            inst.transform.SetParent(host.transform, false);
            inst.Build();
        }

        void Build()
        {
            mpb = new MaterialPropertyBlock();
            sparks = MakePS("Sparks", Mat.FxAdd, 500, 0f, true);
            smoke = MakePS("Smoke", Mat.FxAlpha, 300, -0.15f, false);
            fire = MakePS("Fire", Mat.FxAdd, 300, -0.6f, false);
            glow = MakePS("Glow", Mat.FxAdd, 120, 0f, false);
            debris = MakePS("Debris", Mat.FxAlphaSolid, 200, 1.6f, false);
            var dr = debris.GetComponent<ParticleSystemRenderer>();
            dr.renderMode = ParticleSystemRenderMode.Mesh;
            dr.mesh = CubeMesh();
            var col = debris.collision;
            col.enabled = true;
            col.type = ParticleSystemCollisionType.Planes;
            var ground = new GameObject("DebrisGround").transform;
            ground.SetParent(transform, false);
            col.SetPlane(0, ground);
            col.bounce = 0.35f;
            col.dampen = 0.4f;
            col.lifetimeLoss = 0f;
            var rot = debris.rotationOverLifetime;
            rot.enabled = true;
            rot.separateAxes = true;
            rot.x = new ParticleSystem.MinMaxCurve(-6f, 6f);
            rot.y = new ParticleSystem.MinMaxCurve(-6f, 6f);
            rot.z = new ParticleSystem.MinMaxCurve(-6f, 6f);

            var sr = sparks.GetComponent<ParticleSystemRenderer>();
            sr.renderMode = ParticleSystemRenderMode.Stretch;
            sr.velocityScale = 0.06f;
            sr.lengthScale = 1.2f;

            ringMesh = BuildRingMesh();
            slashMesh = BuildSlashMesh();
            crackMesh = BuildQuadXZ();
            sphereMesh = BuildSphereMesh();
            ringPool = new Pool<MeshRenderer>(() => MakeMeshObj("Ring", ringMesh, Mat.FxAddSolid), 6);
            slashPool = new Pool<MeshRenderer>(() => MakeMeshObj("Slash", slashMesh, Mat.FxAddSolid), 6);
            crackPool = new Pool<MeshRenderer>(() => MakeMeshObj("Crack", crackMesh, Mat.Decal), 6);
            spherePool = new Pool<MeshRenderer>(() => MakeMeshObj("Burst", sphereMesh, Mat.FxAddSolid), 4);
            boltPool = new Pool<LineRenderer>(MakeBolt, 4);
        }

        ParticleSystem MakePS(string name, Material m, int max, float gravity, bool stretch)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = true; // stays 'playing' so particles from Emit() always simulate
            main.playOnAwake = false;
            main.maxParticles = max;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.gravityModifier = gravity;
            main.startLifetime = 0.5f;
            main.startSpeed = 0f;
            main.duration = 1f;
            var em = ps.emission;
            em.enabled = false;
            var shape = ps.shape;
            shape.enabled = false;
            var sol = ps.sizeOverLifetime;
            sol.enabled = true;
            sol.size = new ParticleSystem.MinMaxCurve(1f, stretch ? AnimationCurve.Linear(0, 1, 1, 0.1f) : new AnimationCurve(new Keyframe(0, 0.5f), new Keyframe(0.25f, 1f), new Keyframe(1, 1.3f)));
            var colm = ps.colorOverLifetime;
            colm.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                      new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(0.8f, 0.5f), new GradientAlphaKey(0, 1) });
            colm.color = g;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = m;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.maxParticleSize = 0.6f;
            ps.Play();
            return ps;
        }

        MeshRenderer MakeMeshObj(string name, Mesh mesh, Material m)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = m;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            go.SetActive(false);
            return r;
        }

        LineRenderer MakeBolt()
        {
            var go = new GameObject("Bolt");
            go.transform.SetParent(transform, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.sharedMaterial = Mat.FxAddSolid;
            lr.positionCount = 10;
            lr.widthMultiplier = 0.12f;
            lr.useWorldSpace = true;
            lr.numCapVertices = 0;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            go.SetActive(false);
            return lr;
        }

        static int Scaled(int n) => Mathf.Max(1, Mathf.RoundToInt(n * GraphicsQuality.ParticleMul));

        static void Emit(ParticleSystem ps, Vector3 pos, Vector3 vel, Color c, float size, float life)
        {
            var ep = new ParticleSystem.EmitParams
            {
                position = pos, velocity = vel, startColor = c, startSize = size, startLifetime = life,
                rotation3D = new Vector3(Random.value * 360f, Random.value * 360f, Random.value * 360f)
            };
            ps.Emit(ep, 1);
        }

        // ================= public API =================

        public static void Hit(Vector3 pos, Color color, bool heavy)
        {
            if (inst == null) return;
            int n = Scaled(heavy ? 22 : 11);
            for (int i = 0; i < n; i++)
                Emit(inst.sparks, pos, Random.onUnitSphere * Random.Range(4f, heavy ? 14f : 9f), color, Random.Range(0.08f, 0.16f), Random.Range(0.15f, 0.35f));
            Emit(inst.glow, pos, Vector3.zero, color, heavy ? 1.6f : 0.9f, 0.12f);
            if (heavy) Ring(pos, color, 0.3f, 2.4f, 0.25f, Quaternion.LookRotation(Camera.main != null ? Camera.main.transform.forward : Vector3.forward) * Quaternion.Euler(90, 0, 0));
            WorldLighting.Flash(pos, color * 1.2f, heavy ? 5f : 3f, 0.15f);
        }

        public static void Block(Vector3 pos)
        {
            if (inst == null) return;
            int n = Scaled(8);
            for (int i = 0; i < n; i++)
                Emit(inst.sparks, pos, Random.onUnitSphere * Random.Range(3f, 7f), new Color(0.6f, 0.8f, 1f), 0.08f, 0.2f);
            Emit(inst.glow, pos, Vector3.zero, new Color(0.5f, 0.7f, 1f), 0.8f, 0.1f);
        }

        public static void Parry(Vector3 pos)
        {
            if (inst == null) return;
            int n = Scaled(26);
            for (int i = 0; i < n; i++)
                Emit(inst.sparks, pos, Random.onUnitSphere * Random.Range(8f, 16f), new Color(1f, 0.95f, 0.5f), 0.12f, 0.35f);
            Ring(pos, new Color(1f, 0.9f, 0.4f), 0.2f, 3f, 0.3f, Quaternion.LookRotation(Camera.main != null ? Camera.main.transform.forward : Vector3.forward) * Quaternion.Euler(90, 0, 0));
            WorldLighting.Flash(pos, new Color(1f, 0.9f, 0.5f) * 2f, 6f, 0.25f);
        }

        public static void Dust(Vector3 pos, float size, int count = 8)
        {
            if (inst == null) return;
            int n = Scaled(count);
            for (int i = 0; i < n; i++)
            {
                Vector3 d = Random.insideUnitSphere; d.y = Mathf.Abs(d.y) * 0.4f;
                Emit(inst.smoke, pos + d * size * 0.3f, d * Random.Range(1f, 3f) * size, new Color(0.78f, 0.7f, 0.58f, 0.6f), Random.Range(0.5f, 1f) * size, Random.Range(0.6f, 1.2f));
            }
        }

        public static void Smoke(Vector3 pos, float size, Color c, int count = 12)
        {
            if (inst == null) return;
            int n = Scaled(count);
            for (int i = 0; i < n; i++)
                Emit(inst.smoke, pos + Random.insideUnitSphere * size * 0.4f, Random.onUnitSphere * Random.Range(0.6f, 2f) * size, c, Random.Range(0.6f, 1.1f) * size, Random.Range(0.5f, 0.9f));
        }

        public static void Fire(Vector3 pos, float size, int count = 10)
        {
            if (inst == null) return;
            int n = Scaled(count);
            for (int i = 0; i < n; i++)
            {
                Color c = Color.Lerp(new Color(1f, 0.85f, 0.3f), new Color(1f, 0.3f, 0.05f), Random.value);
                Emit(inst.fire, pos + Random.insideUnitSphere * size * 0.5f, Random.onUnitSphere * size * 1.5f + Vector3.up * size, c, Random.Range(0.5f, 1f) * size, Random.Range(0.25f, 0.6f));
            }
        }

        public static void Glow(Vector3 pos, Color c, float size, float life)
        {
            if (inst == null) return;
            Emit(inst.glow, pos, Vector3.zero, c, size, life);
        }

        public static void Trail(Vector3 pos, Color c, float size)
        {
            if (inst == null || GraphicsQuality.ParticleMul < 0.5f && Random.value < 0.5f) return;
            Emit(inst.fire, pos + Random.insideUnitSphere * size * 0.2f, Random.insideUnitSphere * 0.4f, c, size, 0.25f);
        }

        public static void Debris(Vector3 pos, Color c, int count, float force)
        {
            if (inst == null) return;
            int n = Mathf.Max(1, Mathf.RoundToInt(count * Mathf.Max(0.4f, GraphicsQuality.ParticleMul)));
            for (int i = 0; i < n; i++)
            {
                Vector3 v = Random.onUnitSphere * force; v.y = Mathf.Abs(v.y) * 1.4f + force * 0.4f;
                Color cc = c * Random.Range(0.75f, 1.1f); cc.a = 1f;
                Emit(inst.debris, pos + Random.insideUnitSphere * 0.4f, v, cc, Random.Range(0.12f, 0.35f), Random.Range(1.6f, 2.6f));
            }
        }

        public static void Leaves(Vector3 pos, int count)
        {
            if (inst == null) return;
            int n = Scaled(count);
            for (int i = 0; i < n; i++)
            {
                Color c = Color.Lerp(new Color(0.35f, 0.6f, 0.2f), new Color(0.7f, 0.65f, 0.2f), Random.value);
                Emit(inst.debris, pos + Random.insideUnitSphere * 1.5f, new Vector3(Random.Range(-2f, 2f), Random.Range(0f, 3f), Random.Range(-2f, 2f)), c, Random.Range(0.08f, 0.15f), Random.Range(1.5f, 2.5f));
            }
        }

        public static void Explosion(Vector3 pos, float radius, Color color)
        {
            if (inst == null) return;
            Fire(pos, radius * 0.6f, 24);
            Smoke(pos + Vector3.up * 0.5f, radius * 0.6f, new Color(0.25f, 0.22f, 0.22f, 0.7f), 14);
            Hit(pos, color, true);
            Burst(pos, color, radius * 0.9f, 0.35f);
            Ring(new Vector3(pos.x, 0.06f, pos.z), color, 0.5f, radius * 2.2f, 0.45f, Quaternion.identity);
            Crack(pos, radius * 0.9f);
            Debris(pos, new Color(0.45f, 0.37f, 0.3f), 10, radius * 2f);
            WorldLighting.Flash(pos + Vector3.up, color * 2.5f, radius * 3f, 0.5f);
        }

        public static void Ring(Vector3 pos, Color color, float size0, float size1, float life, Quaternion rot)
        {
            if (inst == null) return;
            var r = inst.ringPool.Get();
            r.transform.SetPositionAndRotation(pos, rot);
            inst.anims.Add(new Anim { kind = Kind.Ring, c = r, life = life, size0 = size0, size1 = size1, color = color });
        }

        public static void Burst(Vector3 pos, Color color, float size, float life)
        {
            if (inst == null) return;
            var r = inst.spherePool.Get();
            r.transform.position = pos;
            inst.anims.Add(new Anim { kind = Kind.Sphere, c = r, life = life, size0 = size * 0.3f, size1 = size, color = color });
        }

        /// <summary>Crescent swing trail in front of the attacker.</summary>
        public static void Slash(Transform owner, Vector3 localOffset, Vector3 euler, Color color, float size, float life = 0.18f)
        {
            if (inst == null) return;
            var r = inst.slashPool.Get();
            r.transform.SetPositionAndRotation(owner.TransformPoint(localOffset), owner.rotation * Quaternion.Euler(euler));
            inst.anims.Add(new Anim { kind = Kind.Slash, c = r, life = life, size0 = size * 0.8f, size1 = size * 1.15f, color = color });
        }

        public static void Crack(Vector3 pos, float size)
        {
            if (inst == null || !GraphicsQuality.GroundDetail && size < 2f) return;
            var r = inst.crackPool.Get();
            r.transform.SetPositionAndRotation(new Vector3(pos.x, 0.03f + Random.value * 0.01f, pos.z), Quaternion.Euler(0, Random.value * 360f, 0));
            r.transform.localScale = Vector3.one * size;
            inst.anims.Add(new Anim { kind = Kind.Crack, c = r, life = 7f, size0 = size, size1 = size, color = Color.white });
        }

        public static void Lightning(Vector3 from, Vector3 to, Color color, float width = 0.12f, float life = 0.25f)
        {
            if (inst == null) return;
            var lr = inst.boltPool.Get();
            lr.widthMultiplier = width;
            inst.anims.Add(new Anim { kind = Kind.Bolt, c = lr, life = life, from = from, to = to, color = color });
            JaggedLine(lr, from, to);
            WorldLighting.Flash((from + to) * 0.5f, color * 2f, 6f, life);
        }

        static void JaggedLine(LineRenderer lr, Vector3 a, Vector3 b)
        {
            int n = lr.positionCount;
            Vector3 dir = b - a;
            Vector3 side = Vector3.Cross(dir.normalized, Vector3.up);
            if (side.sqrMagnitude < 0.01f) side = Vector3.right;
            for (int i = 0; i < n; i++)
            {
                float u = (float)i / (n - 1);
                float j = (i == 0 || i == n - 1) ? 0f : 1f;
                lr.SetPosition(i, a + dir * u + (side * Random.Range(-0.35f, 0.35f) + Vector3.up * Random.Range(-0.3f, 0.3f)) * j * Mathf.Min(1.5f, dir.magnitude * 0.12f));
            }
        }

        void Update()
        {
            float dt = Time.deltaTime;
            for (int i = anims.Count - 1; i >= 0; i--)
            {
                var a = anims[i];
                a.t += dt;
                float u = Mathf.Clamp01(a.t / a.life);
                Color c = a.color;
                switch (a.kind)
                {
                    case Kind.Ring:
                    case Kind.Sphere:
                    case Kind.Slash:
                    {
                        var r = (MeshRenderer)a.c;
                        float e = 1f - (1f - u) * (1f - u);
                        r.transform.localScale = Vector3.one * Mathf.Lerp(a.size0, a.size1, e);
                        c *= (1f - u);
                        mpb.SetColor(IdColor, c);
                        r.SetPropertyBlock(mpb);
                        break;
                    }
                    case Kind.Crack:
                    {
                        var r = (MeshRenderer)a.c;
                        c.a = u < 0.75f ? 1f : (1f - u) * 4f;
                        mpb.SetColor(IdColor, c);
                        r.SetPropertyBlock(mpb);
                        break;
                    }
                    case Kind.Bolt:
                    {
                        var lr = (LineRenderer)a.c;
                        if (Random.value < 0.5f) JaggedLine(lr, a.from, a.to);
                        Color cc = c * (1f - u * 0.7f);
                        lr.startColor = cc; lr.endColor = cc;
                        break;
                    }
                }
                if (u >= 1f)
                {
                    switch (a.kind)
                    {
                        case Kind.Ring: ringPool.Release((MeshRenderer)a.c); break;
                        case Kind.Sphere: spherePool.Release((MeshRenderer)a.c); break;
                        case Kind.Slash: slashPool.Release((MeshRenderer)a.c); break;
                        case Kind.Crack: crackPool.Release((MeshRenderer)a.c); break;
                        case Kind.Bolt: boltPool.Release((LineRenderer)a.c); break;
                    }
                    anims[i] = anims[anims.Count - 1];
                    anims.RemoveAt(anims.Count - 1);
                }
            }
        }

        public static void ClearAll()
        {
            if (inst == null) return;
            foreach (var a in inst.anims)
                if (a.c != null) a.c.gameObject.SetActive(false);
            inst.anims.Clear();
            inst.ringPool.ReleaseAll(); inst.slashPool.ReleaseAll(); inst.crackPool.ReleaseAll(); inst.spherePool.ReleaseAll(); inst.boltPool.ReleaseAll();
            inst.sparks.Clear(); inst.smoke.Clear(); inst.fire.Clear(); inst.debris.Clear(); inst.glow.Clear();
        }

        // ================= meshes =================
        static Mesh BuildRingMesh()
        {
            var mb = new MeshBuilder();
            mb.RingXZ(Vector3.zero, 0.75f, 1f, 32, new Color32(255, 255, 255, 0), new Color32(255, 255, 255, 255));
            return mb.ToMesh("ring");
        }

        static Mesh BuildSlashMesh()
        {
            // crescent in the XZ plane, sweeping 200 degrees, bright on the outer edge
            var mb = new MeshBuilder();
            int seg = 18;
            int s = 0;
            for (int i = 0; i <= seg; i++)
            {
                float u = (float)i / seg;
                float a = Mathf.Lerp(-100f, 100f, u) * Mathf.Deg2Rad;
                float w = Mathf.Sin(u * Mathf.PI);
                Vector3 d = new Vector3(Mathf.Sin(a), 0, Mathf.Cos(a));
                mb.Col = new Color32(255, 255, 255, 0);
                mb.V.Add(d * (1f - 0.35f * w)); mb.N.Add(Vector3.up); mb.C.Add(mb.Col); mb.UV.Add(new Vector2(u, 0)); mb.Bone.Add(0);
                mb.Col = new Color32(255, 255, 255, (byte)(255 * w));
                mb.V.Add(d); mb.N.Add(Vector3.up); mb.C.Add(mb.Col); mb.UV.Add(new Vector2(u, 1)); mb.Bone.Add(0);
                if (i < seg)
                {
                    int k = s + i * 2;
                    mb.T.Add(k); mb.T.Add(k + 1); mb.T.Add(k + 2);
                    mb.T.Add(k + 1); mb.T.Add(k + 3); mb.T.Add(k + 2);
                }
            }
            return mb.ToMesh("slash");
        }

        static Mesh BuildQuadXZ()
        {
            var mb = new MeshBuilder();
            mb.Quad(Vector3.zero, Vector3.right * 0.5f, Vector3.back * 0.5f);
            return mb.ToMesh("decal");
        }

        static Mesh BuildSphereMesh()
        {
            var mb = new MeshBuilder();
            mb.SetColor(new Color32(255, 255, 255, 255));
            mb.Sphere(Vector3.zero, 1f, 8, 12);
            return mb.ToMesh("burst");
        }

        static Mesh CubeMesh()
        {
            var mb = new MeshBuilder();
            mb.SetColor(new Color32(255, 255, 255, 255));
            mb.Box(Vector3.zero, Vector3.one);
            return mb.ToMesh("cube");
        }
    }
}
