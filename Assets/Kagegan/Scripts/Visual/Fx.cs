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
        Material wireMat; // textured monowire (razor core, discharge filaments, energy packets); scrolls hand -> tip
        // flipbook / textured systems built from imported CC0 VFX textures (null when a texture is missing)
        ParticleSystem slashFlip, hitStar, hitBurst, electric, flames, dirtPs, glyphs;
        ParticleSystem ringGold, ringEnergy, ringVoid, ringLightning, ringFire, fireBurst, explosionFlip, groundRing, puffWhite, puffBlack, puffFire, flashPs;
        readonly List<Anim> anims = new List<Anim>(64);
        Pool<MeshRenderer> ringPool, slashPool, crackPool, spherePool;
        Pool<LineRenderer> boltPool, wirePool;
        Mesh ringMesh, slashMesh, crackMesh, sphereMesh;
        MaterialPropertyBlock mpb;
        static readonly int IdColor = Shader.PropertyToID("_Color");

        enum Kind { Ring, Slash, Crack, Bolt, Sphere, Wire, Scan }

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
            smoke = MakePS("Smoke", Own(Mat.FxAlpha, "FxSmoke", 1f, 1f), 300, -0.15f, false);
            // real smoke puffs (Kenney CC0 atlas, 2x2 cells) instead of a soft dot: random cell, angle and a slow turn
            var puffTex = Resources.Load<Texture2D>("Kagegan/Vfx/puff_white");
            if (puffTex != null)
            {
                var sm = Mat.FxTex(puffTex, false, "FxSmokePuff");
                sm.SetFloat("_Lit", 1f);
                sm.SetFloat("_Soft", 1f);
                smoke.GetComponent<ParticleSystemRenderer>().sharedMaterial = sm;
                var stsa = smoke.textureSheetAnimation;
                stsa.enabled = true; stsa.numTilesX = 2; stsa.numTilesY = 2;
                stsa.frameOverTime = new ParticleSystem.MinMaxCurve(0f);
                stsa.startFrame = new ParticleSystem.MinMaxCurve(0f, 3.999f);
                var smain = smoke.main;
                smain.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
                var srot = smoke.rotationOverLifetime;
                srot.enabled = true; srot.z = new ParticleSystem.MinMaxCurve(-0.6f, 0.6f);
            }
            fire = MakePS("Fire", Mat.FxAdd, 300, -0.6f, false);
            glow = MakePS("Glow", Mat.FxAdd, 120, 0f, false);
            debris = MakePS("Debris", Own(Mat.FxAlphaSolid, "FxDebris", 1f, 0f), 200, 1.6f, false);
            // chunks keep their size, then shrink away in the last quarter of their life (no pop)
            var dsol = debris.sizeOverLifetime;
            dsol.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0, 1f), new Keyframe(0.72f, 1f), new Keyframe(1f, 0f, -2.5f, 0f)));
            var dcol = debris.colorOverLifetime;
            var dg = new Gradient();
            dg.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                       new[] { new GradientAlphaKey(1f, 0), new GradientAlphaKey(1f, 0.8f), new GradientAlphaKey(0f, 1f) });
            dcol.color = dg;
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

            ringGold = MakeFlip("RingGold", "ring_gold", 8, 8, 64, true, 24, false);
            ringEnergy = MakeFlip("RingEnergy", "ring_energy", 8, 8, 64, true, 24, false);
            ringVoid = MakeFlip("RingVoid", "ring_void", 8, 8, 64, true, 16, false);
            ringLightning = MakeFlip("RingLightning", "ring_lightning", 8, 8, 64, true, 16, false);
            ringFire = MakeFlip("RingFire", "ring_fire", 8, 8, 64, true, 16, false);
            fireBurst = MakeFlip("FireBurst", "fire_burst", 8, 8, 64, true, 24, false);
            explosionFlip = MakeFlip("Explosion", "explosion_sheet", 10, 10, 91, false, 12, false);
            groundRing = MakeFlip("GroundRing", "smoke_ring", 8, 8, 64, false, 12, true);
            puffWhite = MakeFlip("PuffWhite", "puff_white", 2, 2, 0, false, 220, false);
            puffBlack = MakeFlip("PuffBlack", "puff_black", 2, 2, 0, false, 120, false);
            puffFire = MakeFlip("PuffFire", "puff_fire", 2, 2, 0, true, 200, false);
            flashPs = MakeFlip("Flash", "flash", 2, 2, 0, true, 60, false);
            // hand-painted CC0 sheets: slash arcs (6 frames x 5 styles) and hit stars that burst and fade on their own
            slashFlip = MakeFlip("Slash", "slash_sheet", 6, 3, 6, true, 24, false, true); // rows 0-2: crescent arcs only (the spiky rows read as white paper)
            hitStar = MakeFlip("HitStar", "hit_star", 4, 4, 16, true, 40, false);
            hitBurst = MakeFlip("HitBurst", "hit_burst", 4, 4, 16, true, 24, false);
            // Kenney particle pack (CC0): electric arcs, wispy flames, flying dirt, star sparks, radial glow
            electric = MakeFlip("Electric", "electric", 2, 2, 0, true, 60, false);
            flames = MakeFlip("Flames", "flames", 2, 2, 0, true, 160, false);
            dirtPs = MakeFlip("Dirt", "dirt", 2, 2, 0, false, 60, false);
            if (dirtPs != null) { var dm = dirtPs.main; dm.gravityModifier = 1.4f; }
            var sparkTex = Resources.Load<Texture2D>("Kagegan/Vfx/spark_star");
            if (sparkTex != null) sparks.GetComponent<ParticleSystemRenderer>().sharedMaterial = Mat.FxTex(sparkTex, true, "Fx_spark_star");
            var glowTex = Resources.Load<Texture2D>("Kagegan/Vfx/glow_light");
            if (glowTex != null) glow.GetComponent<ParticleSystemRenderer>().sharedMaterial = Mat.FxTex(glowTex, true, "Fx_glow_light");
            ringMesh = BuildRingMesh();
            slashMesh = BuildSlashMesh();
            crackMesh = BuildQuadXZ();
            sphereMesh = BuildSphereMesh();
            ringPool = new Pool<MeshRenderer>(() => MakeMeshObj("Ring", ringMesh, Mat.FxAddSolid), 6);
            slashPool = new Pool<MeshRenderer>(() => MakeMeshObj("Slash", slashMesh, Mat.FxAddSolid), 6);
            crackPool = new Pool<MeshRenderer>(() => MakeMeshObj("Crack", crackMesh, Mat.Decal), 6);
            spherePool = new Pool<MeshRenderer>(() => MakeMeshObj("Burst", sphereMesh, Mat.FxAddSolid), 4);
            boltPool = new Pool<LineRenderer>(MakeBolt, 4);
            var wireTex = Resources.Load<Texture2D>("Kagegan/Vfx/monowire");
            if (wireTex != null)
            {
                wireMat = Mat.FxTex(wireTex, true, "Fx_monowire");
                wireMat.mainTextureScale = new Vector2(0.55f, 1f); // one texture repeat every ~1.8 m of wire
            }
            wirePool = new Pool<LineRenderer>(MakeWire, 6);
            // netrunner data glyphs (hex digits / code symbols, 4x4 sheet): upright, constant size, flicker out
            glyphs = MakeFlip("Glyphs", "hack_glyphs", 4, 4, 0, true, 160, false);
            if (glyphs != null)
            {
                var gm = glyphs.main; gm.startRotation = new ParticleSystem.MinMaxCurve(0f);
                var glim = glyphs.limitVelocityOverLifetime; glim.enabled = false;
                var grol = glyphs.rotationOverLifetime; grol.enabled = false;
                var gsol = glyphs.sizeOverLifetime; gsol.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0, 1.25f), new Keyframe(0.12f, 1f), new Keyframe(1, 0.9f)));
                var gcol = glyphs.colorOverLifetime;
                var gg = new Gradient();
                gg.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                           new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.06f), new GradientAlphaKey(0.85f, 0.6f), new GradientAlphaKey(0.3f, 0.8f), new GradientAlphaKey(0.6f, 0.86f), new GradientAlphaKey(0f, 1f) });
                gcol.color = gg;
            }
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
            colm.color = EaseOut(0.9f, 0.08f);
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = m;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.maxParticleSize = 0.6f;
            ps.Play();
            return ps;
        }

        /// <summary>
        /// Flipbook particle system for an imported texture sheet. frames &gt; 0 animates through the sheet over the particle's
        /// life; frames == 0 picks a random cell (puff atlases). Returns null if the texture is not present.
        /// </summary>
        ParticleSystem MakeFlip(string name, string texName, int tilesX, int tilesY, int frames, bool additive, int max, bool horizontal, bool randomRow = false)
        {
            var tex = Resources.Load<Texture2D>("Kagegan/Vfx/" + texName);
            if (tex == null) return null;
            tex.wrapMode = TextureWrapMode.Clamp;
            var ps = MakePS(name, Mat.FxTex(tex, additive, "Fx_" + texName), max, 0f, false);
            var main = ps.main;
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            var sol = ps.sizeOverLifetime;
            // animated sheets keep their own fade but also swell slightly; puffs grow as they thin out
            sol.size = new ParticleSystem.MinMaxCurve(1f, frames > 0
                ? new AnimationCurve(new Keyframe(0, 0.92f), new Keyframe(0.5f, 1f), new Keyframe(1, 1.12f))
                : new AnimationCurve(new Keyframe(0, 0.55f), new Keyframe(0.25f, 0.95f), new Keyframe(1, 1.6f)));
            var col = ps.colorOverLifetime;
            // sheets start visible (they animate their own burst); puffs fade in so smoke never pops into view
            col.color = frames > 0 ? EaseOut(0.85f, 0.1f) : EaseOut(0f, 0.14f);
            // smoke and dust are lit by the scene; everything not lying flat fades softly into the ground on High/Ultra
            var fm = ps.GetComponent<ParticleSystemRenderer>().sharedMaterial;
            if (!additive) fm.SetFloat("_Lit", 1f);
            if (!horizontal) fm.SetFloat("_Soft", 1f);
            if (frames == 0)
            {
                // smoke and dust decelerate, turn slowly and dissipate instead of popping out
                var lim = ps.limitVelocityOverLifetime;
                lim.enabled = true;
                lim.limit = new ParticleSystem.MinMaxCurve(0.4f);
                lim.dampen = 0.08f;
                var rol = ps.rotationOverLifetime;
                rol.enabled = true;
                rol.z = new ParticleSystem.MinMaxCurve(-0.6f, 0.6f);
            }
            var tsa = ps.textureSheetAnimation;
            tsa.enabled = true;
            tsa.numTilesX = tilesX;
            tsa.numTilesY = tilesY;
            int total = tilesX * tilesY;
            if (frames > 0 && randomRow)
            {
                tsa.animation = ParticleSystemAnimationType.SingleRow;
                tsa.rowMode = ParticleSystemAnimationRowMode.Random;
                tsa.frameOverTime = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0f, 1f, (frames - 0.01f) / tilesX));
                tsa.cycleCount = 1;
            }
            else if (frames > 0)
            {
                tsa.animation = ParticleSystemAnimationType.WholeSheet;
                tsa.frameOverTime = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0f, 1f, Mathf.Min(1f, (frames - 0.01f) / total)));
                tsa.cycleCount = 1;
            }
            else
            {
                tsa.frameOverTime = new ParticleSystem.MinMaxCurve(0f);
                tsa.startFrame = new ParticleSystem.MinMaxCurve(0f, total - 0.001f);
            }
            var r = ps.GetComponent<ParticleSystemRenderer>();
            r.maxParticleSize = 1.5f;
            if (horizontal) r.renderMode = ParticleSystemRenderMode.HorizontalBillboard;
            return ps;
        }

        /// <summary>Alpha over life: a quick rise to full, then a long eased tail down to exactly zero (no hard cut).</summary>
        static Gradient EaseOut(float startAlpha, float peakAt)
        {
            var g = new Gradient();
            float r = 1f - peakAt;
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                      new[]
                      {
                          new GradientAlphaKey(startAlpha, 0f), new GradientAlphaKey(1f, peakAt),
                          // (1 - x)^2 sampled along the tail
                          new GradientAlphaKey(0.86f, peakAt + r * 0.07f), new GradientAlphaKey(0.56f, peakAt + r * 0.25f),
                          new GradientAlphaKey(0.3f, peakAt + r * 0.45f), new GradientAlphaKey(0.12f, peakAt + r * 0.65f),
                          new GradientAlphaKey(0.03f, peakAt + r * 0.83f), new GradientAlphaKey(0f, 1f)
                      });
            return g;
        }

        /// <summary>A private copy of a shared effect material with its own lighting / soft-edge settings.</summary>
        static Material Own(Material shared, string name, float lit, float soft)
        {
            var m = new Material(shared) { name = name };
            m.SetFloat("_Lit", lit);
            m.SetFloat("_Soft", soft);
            return m;
        }

        static void EmitF(ParticleSystem ps, Vector3 pos, Vector3 vel, Color c, float size, float life) => EmitF(ps, pos, vel, c, size, life, Random.value * 360f);

        static void EmitF(ParticleSystem ps, Vector3 pos, Vector3 vel, Color c, float size, float life, float rotationDeg)
        {
            if (ps == null) return;
            var ep = new ParticleSystem.EmitParams { position = pos, velocity = vel, startColor = c, startSize = size, startLifetime = life, rotation = rotationDeg };
            ps.Emit(ep, 1);
        }

        static ParticleSystem RingFor(Color c)
        {
            Color.RGBToHSV(c, out float h, out float s, out float v);
            if (s < 0.25f) return inst.ringEnergy;
            if (h > 0.68f && h < 0.92f) return inst.ringVoid ?? inst.ringEnergy;
            if (h > 0.11f && h < 0.2f) return inst.ringLightning ?? inst.ringGold;
            if (h <= 0.11f || h >= 0.92f) return inst.ringFire ?? inst.ringGold;
            if (h >= 0.45f && h <= 0.68f) return inst.ringEnergy;
            return inst.ringGold;
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
            int n = Scaled(heavy ? 14 : 7);
            Color spark = Color.Lerp(color, Color.white, 0.35f);
            for (int i = 0; i < n; i++)
                Emit(inst.sparks, pos, Random.onUnitSphere * Random.Range(4f, heavy ? 13f : 8f), spark, Random.Range(0.05f, 0.11f), Random.Range(0.12f, 0.28f));
            Color tint = Color.Lerp(Color.white, color, 0.55f);
            if (inst.hitStar != null)
            {
                // painted cross-star that bursts and fades through its own frames
                EmitF(inst.hitStar, pos, Vector3.zero, tint, heavy ? 2.6f : 1.7f, heavy ? 0.42f : 0.32f);
                if (heavy && inst.hitBurst != null) EmitF(inst.hitBurst, pos, Vector3.zero, tint, 2.2f, 0.5f);
            }
            else if (inst.flashPs != null)
            {
                EmitF(inst.flashPs, pos, Vector3.zero, tint, heavy ? 1.9f : 1.05f, heavy ? 0.13f : 0.09f);
            }
            else Emit(inst.glow, pos, Vector3.zero, color, heavy ? 1.6f : 0.9f, 0.12f);
            if (heavy)
            {
                var ring = RingFor(color) ?? inst.ringGold;
                if (ring != null) EmitF(ring, pos, Vector3.zero, Color.white, 2.6f, 0.32f);
                if (inst.puffWhite != null) for (int i = 0; i < Scaled(3); i++) EmitF(inst.puffWhite, pos + Random.insideUnitSphere * 0.2f, Random.onUnitSphere * 1.2f, new Color(1f, 1f, 1f, 0.35f), Random.Range(0.6f, 1f), 0.45f);
            }
            WorldLighting.Flash(pos, color * 1.2f, heavy ? 5f : 3f, 0.15f);
        }

        public static void Block(Vector3 pos)
        {
            if (inst == null) return;
            int n = Scaled(8);
            for (int i = 0; i < n; i++)
                Emit(inst.sparks, pos, Random.onUnitSphere * Random.Range(3f, 7f), new Color(0.65f, 0.85f, 1f), 0.07f, 0.2f);
            if (inst.flashPs != null) EmitF(inst.flashPs, pos, Vector3.zero, new Color(0.5f, 0.7f, 1f), 0.8f, 0.1f);
            if (inst.ringEnergy != null) EmitF(inst.ringEnergy, pos, Vector3.zero, new Color(0.7f, 0.85f, 1f, 0.7f), 1.3f, 0.22f);
        }

        public static void Parry(Vector3 pos)
        {
            if (inst == null) return;
            int n = Scaled(20);
            for (int i = 0; i < n; i++)
                Emit(inst.sparks, pos, Random.onUnitSphere * Random.Range(7f, 15f), new Color(1f, 0.95f, 0.55f), 0.1f, 0.32f);
            if (inst.ringGold != null) EmitF(inst.ringGold, pos, Vector3.zero, Color.white, 3.2f, 0.36f);
            if (inst.flashPs != null) EmitF(inst.flashPs, pos, Vector3.zero, new Color(1f, 0.9f, 0.5f), 1.8f, 0.14f);
            WorldLighting.Flash(pos, new Color(1f, 0.9f, 0.5f) * 2f, 6f, 0.25f);
        }

        public static void Dust(Vector3 pos, float size, int count = 8)
        {
            GrassBend.Impact(pos, 0.4f * size, 1.1f * size, 1.1f + 0.3f * size, Mathf.Clamp01(0.5f + 0.3f * size));
            if (inst == null) return;
            if (inst.dirtPs != null && size >= 0.8f)
                for (int i = 0; i < Scaled(Mathf.Max(1, count / 4)); i++)
                {
                    Vector3 v = Random.onUnitSphere * 2.5f * size; v.y = Mathf.Abs(v.y) + 2.5f * size;
                    EmitF(inst.dirtPs, pos + Vector3.up * 0.2f, v, new Color(0.75f, 0.65f, 0.5f, 0.9f), Random.Range(0.35f, 0.6f) * size, Random.Range(0.5f, 0.8f));
                }
            int n = Scaled(Mathf.Max(2, count / 2));
            var ps = inst.puffWhite ?? inst.smoke;
            for (int i = 0; i < n; i++)
            {
                Vector3 d = Random.insideUnitSphere; d.y = Mathf.Abs(d.y) * 0.35f;
                var c = new Color(0.82f, 0.74f, 0.6f, 0.55f);
                if (ps == inst.smoke) Emit(ps, pos + d * size * 0.3f, d * Random.Range(1f, 3f) * size, c, Random.Range(0.5f, 1f) * size, Random.Range(0.6f, 1.2f));
                else EmitF(ps, pos + d * size * 0.3f + Vector3.up * 0.2f, d * Random.Range(1f, 2.6f) * size, c, Random.Range(0.8f, 1.4f) * size, Random.Range(0.7f, 1.3f));
            }
        }

        public static void Smoke(Vector3 pos, float size, Color c, int count = 12)
        {
            if (inst == null) return;
            int n = Scaled(Mathf.Max(3, count * 2 / 3));
            bool dark = c.r + c.g + c.b < 1.2f;
            var ps = dark ? (inst.puffBlack ?? inst.puffWhite) : inst.puffWhite;
            for (int i = 0; i < n; i++)
            {
                if (ps == null) { Emit(inst.smoke, pos + Random.insideUnitSphere * size * 0.4f, Random.onUnitSphere * Random.Range(0.6f, 2f) * size, c, Random.Range(0.6f, 1.1f) * size, Random.Range(0.5f, 0.9f)); continue; }
                Color cc = dark ? new Color(1f, 1f, 1f, c.a) : c;
                EmitF(ps, pos + Random.insideUnitSphere * size * 0.4f, Random.onUnitSphere * Random.Range(0.5f, 1.5f) * size, cc, Random.Range(0.6f, 1.05f) * size, Random.Range(0.45f, 0.85f));
            }
        }

        public static void Fire(Vector3 pos, float size, int count = 10)
        {
            if (inst == null) return;
            int n = Scaled(Mathf.Max(2, count * 2 / 3));
            for (int i = 0; i < n; i++)
            {
                Vector3 v = Random.onUnitSphere * size * 1.2f + Vector3.up * size * 1.4f;
                if (inst.flames != null && (i & 1) == 0) EmitF(inst.flames, pos + Random.insideUnitSphere * size * 0.4f, v * 0.8f, new Color(1f, 0.75f, 0.45f), Random.Range(0.8f, 1.3f) * size, Random.Range(0.3f, 0.6f), Random.Range(-25f, 25f));
                else if (inst.puffFire != null) EmitF(inst.puffFire, pos + Random.insideUnitSphere * size * 0.5f, v, Color.white, Random.Range(0.7f, 1.2f) * size, Random.Range(0.25f, 0.55f));
                else Emit(inst.fire, pos + Random.insideUnitSphere * size * 0.5f, v, new Color(1f, 0.6f, 0.2f), Random.Range(0.5f, 1f) * size, Random.Range(0.25f, 0.6f));
            }
            if (count >= 8 && inst.fireBurst != null) EmitF(inst.fireBurst, pos, Vector3.zero, Color.white, size * 2.2f, 0.45f);
        }

        /// <summary>Crackling electric arcs (Kenney sprites) around a point — lightning techniques and charges.</summary>
        public static void Electric(Vector3 pos, Color c, float size, int count = 2)
        {
            if (inst == null || inst.electric == null) return;
            int n = Scaled(count);
            for (int i = 0; i < n; i++)
                EmitF(inst.electric, pos + Random.insideUnitSphere * size * 0.3f, Vector3.zero, Color.Lerp(Color.white, c, 0.5f), size * Random.Range(0.7f, 1.2f), Random.Range(0.08f, 0.16f));
        }

        /// <summary>One floating data glyph (hex digit / code symbol), always upright.</summary>
        public static void Glyph(Vector3 pos, Vector3 vel, Color c, float size, float life)
        {
            if (inst == null || inst.glyphs == null) return;
            EmitF(inst.glyphs, pos, vel, Color.Lerp(Color.white, c, 0.7f), size, life, 0f);
        }

        /// <summary>Quickhack upload: a stream of data glyphs flies from the netrunner's hand into the target.</summary>
        public static void HackStream(Vector3 from, Vector3 to, Color c, int count, float travel = 0.28f)
        {
            if (inst == null) return;
            int n = Scaled(count);
            Vector3 d = to - from;
            for (int i = 0; i < n; i++)
            {
                // staggered along the path so the stream reads as packets in flight, not one clump
                float k = Random.value * 0.6f;
                Vector3 p = from + d * k + Random.insideUnitSphere * 0.12f;
                float life = travel * (1f - k) + Random.Range(0.02f, 0.08f);
                Glyph(p, (to - p) / Mathf.Max(0.05f, life), c, Random.Range(0.11f, 0.17f), life);
            }
            if (inst.glyphs == null) Electric(to, c, 0.6f, 1);
        }

        /// <summary>Hacked status: glyphs rise around the fighter's body.</summary>
        public static void HackAura(Vector3 feet, float height, Color c, int count = 1)
        {
            if (inst == null) return;
            for (int i = 0; i < count; i++)
            {
                Vector2 ring = Random.insideUnitCircle.normalized * Random.Range(0.3f, 0.5f);
                Vector3 p = feet + new Vector3(ring.x, Random.Range(0.2f, height * 0.9f), ring.y);
                Glyph(p, new Vector3(0f, Random.Range(0.35f, 0.7f), 0f), c, Random.Range(0.09f, 0.14f), Random.Range(0.45f, 0.8f));
            }
        }

        /// <summary>Scan ring sweeping up a fighter's body (follows them while they move).</summary>
        public static void Scan(Transform target, float height, float radius, Color color, float life = 0.45f)
        {
            if (inst == null || target == null) return;
            var r = inst.ringPool.Get();
            r.transform.SetPositionAndRotation(target.position, Quaternion.identity);
            inst.anims.Add(new Anim { kind = Kind.Scan, c = r, life = life, size0 = radius, size1 = height, color = color, follow = target, from = target.position });
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
            if (inst.explosionFlip != null)
            {
                EmitF(inst.explosionFlip, pos + Vector3.up * radius * 0.3f, Vector3.zero, Color.white, Mathf.Min(radius * 2.2f, 7f), 0.9f);
                if (GraphicsQuality.ParticleMul > 0.6f) EmitF(inst.explosionFlip, pos + Random.insideUnitSphere * radius * 0.4f + Vector3.up * radius * 0.4f, Vector3.zero, Color.white, radius * 1.8f, 0.8f);
            }
            Fire(pos, radius * 0.55f, 12);
            Smoke(pos + Vector3.up * 0.4f, radius * 0.45f, new Color(0.25f, 0.22f, 0.22f, 0.5f), 6);
            var ring = RingFor(color);
            if (ring != null) EmitF(ring, pos + Vector3.up * 0.8f, Vector3.zero, Color.white, radius * 2.2f, 0.4f);
            Ring(new Vector3(pos.x, 0.08f, pos.z), color, 0.5f, radius * 2.2f, 0.5f, Quaternion.identity);
            Crack(pos, radius * 0.9f);
            Debris(pos, new Color(0.45f, 0.37f, 0.3f), 10, radius * 2f);
            WorldLighting.Flash(pos + Vector3.up, color * 2.5f, radius * 3f, 0.5f);
        }

        public static void Ring(Vector3 pos, Color color, float size0, float size1, float life, Quaternion rot)
        {
            // a ring lying on the floor is a shockwave: it flattens the grass as it rolls outward
            if (rot == Quaternion.identity) GrassBend.Impact(pos, size0 * 0.5f, Mathf.Min(size1 * 0.6f, 7f), Mathf.Max(0.8f, life * 2.2f), 1f);
            if (inst == null) return;
            if (rot == Quaternion.identity && inst.groundRing != null)
            {
                // ground shockwave: dusty ring lying on the floor, tinted by the technique colour
                EmitF(inst.groundRing, new Vector3(pos.x, Mathf.Max(0.1f, pos.y), pos.z), Vector3.zero, Color.Lerp(new Color(0.85f, 0.8f, 0.72f, 0.9f), color, 0.35f), size1 * 1.3f, Mathf.Max(0.35f, life));
                return;
            }
            if (rot != Quaternion.identity)
            {
                var flip = RingFor(color);
                if (flip != null) { EmitF(flip, pos, Vector3.zero, Color.white, size1 * 1.1f, Mathf.Max(0.3f, life)); return; }
            }
            var r = inst.ringPool.Get();
            r.transform.SetPositionAndRotation(pos, rot);
            inst.anims.Add(new Anim { kind = Kind.Ring, c = r, life = life, size0 = size0, size1 = size1, color = color });
        }

        public static void Burst(Vector3 pos, Color color, float size, float life)
        {
            if (inst == null) return;
            var flip = RingFor(color);
            if (flip != null)
            {
                EmitF(flip, pos, Vector3.zero, Color.white, size * 2f, Mathf.Max(0.3f, life));
                if (inst.flashPs != null) EmitF(inst.flashPs, pos, Vector3.zero, color, size * 0.9f, 0.15f);
                return;
            }
            var r = inst.spherePool.Get();
            r.transform.position = pos;
            inst.anims.Add(new Anim { kind = Kind.Sphere, c = r, life = life, size0 = size * 0.3f, size1 = size, color = color });
        }

        /// <summary>Saturated, slightly dimmed tint so additive arcs keep the move colour instead of washing out to white.</summary>
        static Color SlashTint(Color c)
        {
            Color t = Color.Lerp(Color.white, c, 0.82f);
            return new Color(t.r, t.g, t.b, 0.75f);
        }

        /// <summary>Crescent swing trail in front of the attacker.</summary>
        public static void Slash(Transform owner, Vector3 localOffset, Vector3 euler, Color color, float size, float life = 0.18f)
        {
            if (inst == null) return;
            if (inst.slashFlip != null)
            {
                // painted swing arc; mirrored when the fighter faces left on the side-on camera
                Vector3 p = owner.TransformPoint(localOffset);
                float rot = euler.z + (owner.forward.x < 0f ? 180f : 0f) + Random.Range(-15f, 15f);
                EmitF(inst.slashFlip, p, owner.forward * 0.8f, SlashTint(color), size * 1.7f, Mathf.Max(0.22f, life * 1.6f), rot);
                return;
            }
            var r = inst.slashPool.Get();
            r.transform.SetPositionAndRotation(owner.TransformPoint(localOffset), owner.rotation * Quaternion.Euler(euler));
            inst.anims.Add(new Anim { kind = Kind.Slash, c = r, life = life, size0 = size * 0.8f, size1 = size * 1.15f, color = color * 0.55f });
        }

        public static void Crack(Vector3 pos, float size)
        {
            GrassBend.Impact(pos, size * 0.5f, size * 1.2f, 2f, 1f);
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
            Electric(to, color, width * 8f + 0.6f, 2);
            WorldLighting.Flash((from + to) * 0.5f, color * 2f, 6f, life);
        }

        LineRenderer MakeWire()
        {
            var go = new GameObject("Monowire");
            go.transform.SetParent(transform, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.positionCount = WireSegs;
            lr.useWorldSpace = true;
            lr.alignment = LineAlignment.View;
            if (wireMat != null)
            {
                // the texture carries its own glow halo, so the strip is wider than the visible filament
                lr.sharedMaterial = wireMat;
                lr.textureMode = LineTextureMode.Tile;
                lr.numCapVertices = 0;
                lr.widthCurve = new AnimationCurve(new Keyframe(0, 0.3f), new Keyframe(0.85f, 0.2f), new Keyframe(1, 0.1f));
            }
            else
            {
                lr.sharedMaterial = Mat.FxAddSolid;
                lr.numCapVertices = 2;
                lr.widthCurve = new AnimationCurve(new Keyframe(0, 0.045f), new Keyframe(0.85f, 0.03f), new Keyframe(1, 0.012f));
            }
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
            go.SetActive(false);
            return lr;
        }

        const int WireSegs = 18;

        /// <summary>
        /// Monowire whip: a thin glowing filament shoots out of <paramref name="hand"/>, follows a curved whip arc
        /// (<paramref name="sweep"/> bends it sideways, + = right), hits <paramref name="to"/> at 35% of its life and retracts.
        /// </summary>
        public static void Wire(Transform hand, Vector3 to, Color color, float life = 0.3f, float sweep = 1f)
        {
            if (inst == null || hand == null) return;
            var lr = inst.wirePool.Get();
            inst.anims.Add(new Anim { kind = Kind.Wire, c = lr, life = life, from = hand.position, to = to, color = color, follow = hand, size0 = sweep });
            WireShape(lr, hand.position, to, 0f, sweep);
        }

        static void WireShape(LineRenderer lr, Vector3 a, Vector3 b, float u, float sweep)
        {
            // extend (0..0.35), taut on contact, then whip back while the tail swings
            float ext = u < 0.35f ? 1f - Mathf.Pow(1f - u / 0.35f, 3f) : 1f - Mathf.Pow((u - 0.35f) / 0.65f, 2f);
            Vector3 dir = b - a;
            float len = dir.magnitude;
            Vector3 side = Vector3.Cross(Vector3.up, dir / Mathf.Max(0.01f, len));
            if (side.sqrMagnitude < 0.01f) side = Vector3.right;
            float bend = sweep * len * 0.22f * (u < 0.35f ? 1f - u / 0.35f : (u - 0.35f) * 1.4f);
            // a whip wave runs from the hand to the tip, perpendicular to the swing, and dies down after the snap
            Vector3 lift = Vector3.Cross(dir / Mathf.Max(0.01f, len), side);
            float waveAmp = len * 0.045f * (u < 0.35f ? 1f : Mathf.Max(0f, 1f - (u - 0.35f) * 2.2f));
            for (int i = 0; i < WireSegs; i++)
            {
                float t = (float)i / (WireSegs - 1);
                float k = t * ext;
                float arc = Mathf.Sin(t * Mathf.PI);
                float wave = Mathf.Sin((t * 1.6f - u * 4.5f) * Mathf.PI * 2f) * arc * waveAmp;
                Vector3 p = a + dir * k + side * bend * arc + lift * wave + Vector3.up * (-0.08f * len * arc * (1f - ext));
                lr.SetPosition(i, p);
            }
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
            if (wireMat != null) wireMat.mainTextureOffset = new Vector2(-Time.time * 2.4f % 1f, 0f); // energy packets run from the hand to the tip
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
                        // hold briefly, then an eased fade-out instead of a linear cut
                        float fade = u < 0.15f ? 1f : Mathf.Pow(1f - (u - 0.15f) / 0.85f, 1.7f);
                        c *= fade;
                        mpb.SetColor(IdColor, c);
                        r.SetPropertyBlock(mpb);
                        break;
                    }
                    case Kind.Scan:
                    {
                        // body scan: a thin ring sweeps from the feet to the head of the hacked fighter
                        var r = (MeshRenderer)a.c;
                        Vector3 basePos = a.follow != null ? a.follow.position : a.from;
                        float e = u * u * (3f - 2f * u);
                        r.transform.position = basePos + Vector3.up * Mathf.Lerp(0.05f, a.size1, e);
                        r.transform.localScale = Vector3.one * a.size0 * (1f + 0.12f * Mathf.Sin(u * Mathf.PI));
                        float sf = Mathf.Sin(u * Mathf.PI);
                        c *= sf * (0.8f + 0.2f * Random.value);
                        mpb.SetColor(IdColor, c);
                        r.SetPropertyBlock(mpb);
                        break;
                    }
                    case Kind.Crack:
                    {
                        var r = (MeshRenderer)a.c;
                        float cu = Mathf.Clamp01((u - 0.6f) / 0.4f);
                        c.a = 1f - cu * cu * (3f - 2f * cu);
                        mpb.SetColor(IdColor, c);
                        r.SetPropertyBlock(mpb);
                        break;
                    }
                    case Kind.Wire:
                    {
                        var lr = (LineRenderer)a.c;
                        Vector3 start = a.follow != null ? a.follow.position : a.from;
                        WireShape(lr, start, a.to, u, a.size0);
                        float flicker = 0.85f + 0.15f * Mathf.Sin(a.t * 90f) * Random.value;
                        float wf = u < 0.6f ? 1f : 1f - (u - 0.6f) / 0.4f;
                        Color cc = Color.Lerp(Color.white, c, 0.35f) * 1.6f * wf * wf * (3f - 2f * wf) * flicker;
                        lr.startColor = cc * 0.7f; lr.endColor = cc;
                        Vector3 tip = lr.GetPosition(WireSegs - 1);
                        if (u < 0.4f && Random.value < 0.5f) Trail(tip, c, 0.16f);
                        // the moment the wire goes taut: crackle, sparks and a glow at the tip (once per wire)
                        if (u >= 0.35f && a.size1 == 0f && a.life > 0.1f)
                        {
                            a.size1 = 1f;
                            Electric(tip, c, 0.55f, 1);
                            Sfx.Play(SfxId.Clash, 0.35f, 2.1f); // the whip-crack when the filament goes taut
                            Glow(tip, Color.Lerp(Color.white, c, 0.5f), 0.9f, 0.12f);
                            int n = Scaled(6);
                            for (int k = 0; k < n; k++)
                                Emit(sparks, tip, (Random.insideUnitSphere + (tip - start).normalized) * Random.Range(3f, 7f), Color.Lerp(Color.white, c, 0.4f), 0.05f, Random.Range(0.12f, 0.25f));
                        }
                        // sparks shed along the length while it lashes out
                        if (u < 0.5f && Random.value < 0.35f * GraphicsQuality.ParticleMul)
                            Emit(sparks, lr.GetPosition(Random.Range(2, WireSegs - 1)), Random.insideUnitSphere * 2f, c, 0.035f, 0.15f);
                        break;
                    }
                    case Kind.Bolt:
                    {
                        var lr = (LineRenderer)a.c;
                        if (Random.value < 0.5f) JaggedLine(lr, a.from, a.to);
                        Color cc = c * Mathf.Pow(1f - u, 1.5f);
                        lr.startColor = cc; lr.endColor = cc;
                        break;
                    }
                }
                if (u >= 1f)
                {
                    switch (a.kind)
                    {
                        case Kind.Ring:
                        case Kind.Scan: ringPool.Release((MeshRenderer)a.c); break;
                        case Kind.Sphere: spherePool.Release((MeshRenderer)a.c); break;
                        case Kind.Slash: slashPool.Release((MeshRenderer)a.c); break;
                        case Kind.Crack: crackPool.Release((MeshRenderer)a.c); break;
                        case Kind.Bolt: boltPool.Release((LineRenderer)a.c); break;
                        case Kind.Wire: wirePool.Release((LineRenderer)a.c); break;
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
            inst.ringPool.ReleaseAll(); inst.slashPool.ReleaseAll(); inst.crackPool.ReleaseAll(); inst.spherePool.ReleaseAll(); inst.boltPool.ReleaseAll(); inst.wirePool.ReleaseAll();
            inst.sparks.Clear(); inst.smoke.Clear(); inst.fire.Clear(); inst.debris.Clear(); inst.glow.Clear();
            foreach (var ps in new[] { inst.ringGold, inst.ringEnergy, inst.ringVoid, inst.ringLightning, inst.ringFire, inst.fireBurst, inst.explosionFlip, inst.groundRing, inst.puffWhite, inst.puffBlack, inst.puffFire, inst.flashPs,
                                       inst.slashFlip, inst.hitStar, inst.hitBurst, inst.electric, inst.flames, inst.dirtPs, inst.glyphs })
                if (ps != null) ps.Clear();
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

        /// <summary>Debris chunk: a jagged, flat-shaded shard (a lumpy octahedron) with light baked into the faces,
        /// so flying and settled bits read as broken rock/wood instead of flat unlit cubes.</summary>
        static Mesh CubeMesh()
        {
            var mb = new MeshBuilder();
            Vector3[] p =
            {
                new Vector3(0.55f, 0.05f, 0.1f), new Vector3(-0.5f, -0.05f, -0.12f),
                new Vector3(0.05f, 0.34f, -0.08f), new Vector3(-0.1f, -0.3f, 0.06f),
                new Vector3(0.12f, 0.02f, 0.48f), new Vector3(-0.06f, 0.08f, -0.52f),
            };
            int[] f = { 0,2,4, 2,1,4, 1,3,4, 3,0,4, 2,0,5, 1,2,5, 3,1,5, 0,3,5 };
            Vector3 light = new Vector3(0.35f, 0.85f, 0.25f).normalized;
            for (int i = 0; i < f.Length; i += 3)
            {
                Vector3 a = p[f[i]], b = p[f[i + 1]], c = p[f[i + 2]];
                Vector3 n = Vector3.Cross(b - a, c - a).normalized;
                if (Vector3.Dot(n, a + b + c) < 0f) { var t = b; b = c; c = t; n = -n; }
                byte v = (byte)(255f * Mathf.Lerp(0.5f, 1f, Vector3.Dot(n, light) * 0.5f + 0.5f));
                var col = new Color32(v, v, v, 255);
                int s = mb.V.Count;
                foreach (var q in new[] { a, b, c })
                {
                    mb.V.Add(q); mb.N.Add(n); mb.C.Add(col); mb.UV.Add(Vector2.zero); mb.Bone.Add(0);
                }
                mb.T.Add(s); mb.T.Add(s + 1); mb.T.Add(s + 2);
            }
            return mb.ToMesh("chunk");
        }
    }
}
