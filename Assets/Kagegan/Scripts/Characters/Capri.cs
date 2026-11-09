using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Kagegan
{
    /// <summary>
    /// Capri: a digital daemon Lucy (ReinaKit) calls out of the net for a limited time. She is a 2D flipbook puppet
    /// (Resources/Kagegan/Vfx/capri_atlas, 8x8 cells) drawn on a camera-facing quad by the Kagegan/Capri shader, so one
    /// material covers her and every afterimage. She is not a Fighter: she can't be hit, she floats next to Lucy and
    /// acts on her own with pauses between actions. All her hits go through ReceiveHit with Lucy as the attacker,
    /// so dodges, blocks, invulnerability, armor, stats and KO rules all apply.
    /// </summary>
    public class Capri : MonoBehaviour
    {
        public static readonly Color Red = new Color(1f, 0.16f, 0.33f);
        public static readonly Color Green = new Color(0.25f, 1f, 0.42f);
        public const float Lifetime = 20f;
        const float Size = 1.9f;          // quad height in metres (the figure fills most of the cell)
        const float HoverHeight = 1.45f;  // quad centre above the ground

        public Fighter Owner { get; private set; }
        ReinaKit kit;
        public bool Present => phase == Phase.Live;
        public bool Leaving => phase == Phase.Vanish;
        /// <summary>The joint ultimate drives her: the brain stops and despawn waits for the cinematic to end.</summary>
        public bool Scripted;
        public float TimeLeft => phase == Phase.Live ? Mathf.Max(0f, endAt - Time.time) : 0f;
        public Vector3 Pos => transform.position;

        enum Phase { Summon, Live, Vanish }
        Phase phase = Phase.Summon;
        float endAt;

        // ---------- shared resources (one material, one quad mesh) ----------
        static Material mat, riftMat, shadowMat;
        static Mesh quad, shadowQuad;
        static bool resTried;
        struct ClipDef { public int start, count; public float fps; public bool loop; }
        // matches capri_atlas.png (see the generator notes in CREDITS.md)
        static readonly Dictionary<string, ClipDef> Clips = new Dictionary<string, ClipDef>
        {
            { "idle", new ClipDef { start = 0, count = 8, fps = 8, loop = true } },
            { "look", new ClipDef { start = 8, count = 6, fps = 8 } },
            { "laugh", new ClipDef { start = 14, count = 6, fps = 12, loop = true } },
            { "surprise", new ClipDef { start = 20, count = 4, fps = 10 } },
            { "appear", new ClipDef { start = 24, count = 6, fps = 10 } },
            { "blink", new ClipDef { start = 30, count = 4, fps = 16 } },
            { "attack", new ClipDef { start = 34, count = 6, fps = 14 } },
            { "hack", new ClipDef { start = 40, count = 6, fps = 10, loop = true } },
            { "hurt", new ClipDef { start = 46, count = 4, fps = 12 } },
            { "possess", new ClipDef { start = 50, count = 4, fps = 14 } },
            { "burst", new ClipDef { start = 54, count = 4, fps = 12 } },
            { "talk", new ClipDef { start = 58, count = 4, fps = 8, loop = true } },
            { "duo", new ClipDef { start = 62, count = 2, fps = 4, loop = true } },
        };

        static readonly int IdFrame = Shader.PropertyToID("_Frame"), IdFlip = Shader.PropertyToID("_Flip"),
            IdGlitch = Shader.PropertyToID("_Glitch"), IdDissolve = Shader.PropertyToID("_Dissolve"),
            IdTint = Shader.PropertyToID("_Tint"), IdTintAmt = Shader.PropertyToID("_TintAmt"),
            IdAlpha = Shader.PropertyToID("_Alpha"), IdQuality = Shader.PropertyToID("_Quality"), IdSeed = Shader.PropertyToID("_Seed"),
            IdTilt = Shader.PropertyToID("_Tilt"), IdShear = Shader.PropertyToID("_Shear");

        static bool LoadResources()
        {
            if (mat != null) return true;
            if (resTried) return false;
            resTried = true;
            var sh = Resources.Load<Shader>("Kagegan/Shaders/KageCapri");
            var atlas = Resources.Load<Texture2D>("Kagegan/Vfx/capri_atlas");
            if (sh == null || !sh.isSupported || atlas == null) { Debug.LogWarning("Kagegan: Capri shader or atlas missing"); return false; }
            atlas.wrapMode = TextureWrapMode.Clamp;
            mat = new Material(sh) { name = "Capri", mainTexture = atlas };
            mat.SetFloat("_Grid", 8f);
            Mat.Build();
            var rift = Resources.Load<Texture2D>("Kagegan/Vfx/capri_rift");
            riftMat = Mat.FxTex(rift, false, "CapriRift");
            shadowMat = Mat.FxAlpha;
            quad = MakeQuad(Color.white, "CapriQuad");
            shadowQuad = MakeQuad(new Color(0f, 0f, 0f, 0.42f), "CapriShadow");
            return true;
        }

        static Mesh MakeQuad(Color c, string name)
        {
            var m = new Mesh { name = name };
            m.vertices = new[] { new Vector3(-0.5f, -0.5f, 0), new Vector3(0.5f, -0.5f, 0), new Vector3(-0.5f, 0.5f, 0), new Vector3(0.5f, 0.5f, 0) };
            m.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1) };
            m.colors = new[] { c, c, c, c };
            m.triangles = new[] { 0, 2, 1, 1, 2, 3 };
            // the Capri shader turns the quad to the camera, so give it room in every axis or it gets culled edge-on
            m.bounds = new Bounds(Vector3.zero, Vector3.one * 1.5f);
            return m;
        }

        // ---------- per-instance ----------
        MeshRenderer body;
        MaterialPropertyBlock mpb;
        Transform shadow, riftT;
        MeshRenderer riftR;
        GameObject holder;
        Camera cam;
        float seed;

        string clip = "idle";
        float clipT;
        float glitch, glitchTarget = 0.08f, dissolve = 1f, alpha = 1f, flip = 1f;
        float laughUntil;

        class GhostQ { public MeshRenderer r; public MaterialPropertyBlock b; public float life, max; public int frame; public Color tint; public float flip, tilt, shear; }

        // 2D puppet motion: lean into movement, smear on fast moves, squash/stretch spring kicked by actions
        Vector3 lastPos;
        float tilt, shear, sqX, sqY, sqVX, sqVY, tiltKick;
        Vector3 lookAhead;

        /// <summary>Kicks the squash/stretch spring: x &gt; 0 widens, y &gt; 0 stretches up.</summary>
        void Pop(float x, float y) { sqVX += x; sqVY += y; }
        readonly List<GhostQ> ghosts = new List<GhostQ>();

        // brain
        Vector3 vel;
        Vector3 offsetSide = Vector3.right;
        float nextActionAt, nextIdleAt, nextDodgeAt, followUpAt, relocateAt, noEnemySince = -1f;
        float blinkReady, corruptReady, burstReady, possessReady, clawReady;
        bool busy, dealing;
        Coroutine action;

        /// <summary>Starts the summon sequence next to Lucy. Null if the art or shader is missing (the kit then refunds).</summary>
        public static Capri Summon(Fighter owner, ReinaKit kit)
        {
            if (owner == null || !LoadResources()) return null;
            var go = new GameObject("Capri");
            var c = go.AddComponent<Capri>();
            c.Init(owner, kit);
            return c;
        }

        void Init(Fighter owner, ReinaKit k)
        {
            Owner = owner; kit = k;
            seed = Random.value * 10f;
            cam = Camera.main;
            mpb = new MaterialPropertyBlock();
            holder = new GameObject("CapriFx");

            var bgo = new GameObject("Body");
            bgo.transform.SetParent(transform, false);
            bgo.transform.localScale = new Vector3(Size, Size, 1f);
            bgo.AddComponent<MeshFilter>().sharedMesh = quad;
            body = bgo.AddComponent<MeshRenderer>();
            SetupRenderer(body, mat);

            var sgo = new GameObject("Shadow");
            sgo.transform.SetParent(holder.transform, false);
            sgo.AddComponent<MeshFilter>().sharedMesh = shadowQuad;
            SetupRenderer(sgo.AddComponent<MeshRenderer>(), shadowMat);
            shadow = sgo.transform;
            shadow.rotation = Quaternion.Euler(90f, 0f, 0f);
            shadow.localScale = new Vector3(1.1f, 0.7f, 1f);

            var rgo = new GameObject("Rift");
            rgo.transform.SetParent(holder.transform, false);
            rgo.AddComponent<MeshFilter>().sharedMesh = quad;
            riftR = rgo.AddComponent<MeshRenderer>();
            SetupRenderer(riftR, riftMat);
            riftT = rgo.transform;
            rgo.SetActive(false);

            int ghostCount = GraphicsQuality.Current <= GraphicsPreset.Low ? 3 : 6;
            for (int i = 0; i < ghostCount; i++)
            {
                var g = new GameObject("CapriGhost");
                g.transform.SetParent(holder.transform, false);
                g.transform.localScale = new Vector3(Size, Size, 1f);
                g.AddComponent<MeshFilter>().sharedMesh = quad;
                var r = g.AddComponent<MeshRenderer>();
                SetupRenderer(r, mat);
                g.SetActive(false);
                ghosts.Add(new GhostQ { r = r, b = new MaterialPropertyBlock() });
            }

            // side away from the current target, a little behind Lucy
            offsetSide = owner.Target != null && Vector3.Dot(owner.Target.transform.position - owner.transform.position, owner.transform.right) > 0f ? Vector3.left : Vector3.right;
            transform.position = HoverPoint();
            lastPos = transform.position;
            float now = Time.time;
            blinkReady = now + 2f; corruptReady = now + 3f; burstReady = now + 1f; possessReady = now + 5f; clawReady = now;
            StartCoroutine(SummonRoutine());
        }

        static void SetupRenderer(MeshRenderer r, Material m)
        {
            r.sharedMaterial = m;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            r.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
        }

        void OnDestroy()
        {
            if (holder != null) Destroy(holder);
            if (kit != null) kit.OnCapriGone(this);
        }

        // ---------- flipbook ----------
        public void PlayClip(string name)
        {
            if (!Clips.ContainsKey(name)) return;
            clip = name;
            clipT = 0f;
            switch (name)
            {
                case "attack": Pop(-2.2f, 3.2f); break;    // whip-out stretch
                case "burst": Pop(4f, -3f); break;         // wide shockwave squash
                case "blink": Pop(-3f, 3.5f); break;
                case "surprise": Pop(-1.5f, 4f); break;
                case "hurt": Pop(2.5f, -2.5f); tiltKick = 0.35f * flip; break;
                case "laugh": Pop(1.2f, -1f); break;
                case "possess": Pop(-3.5f, 2f); break;
                case "appear": Pop(-2f, 4f); break;
            }
        }

        int Frame(string name, float t)
        {
            var c = Clips[name];
            int i = (int)(t * c.fps);
            i = c.loop ? i % c.count : Mathf.Min(i, c.count - 1);
            return c.start + i;
        }

        bool ClipDone => !Clips[clip].loop && clipT * Clips[clip].fps >= Clips[clip].count;

        // ---------- per frame ----------
        void Update()
        {
            float dt = Time.deltaTime;
            if (cam == null) cam = Camera.main;
            CheckDespawn();

            clipT += dt;
            if (ClipDone && phase != Phase.Vanish && !Scripted && clip != "appear")
                PlayClip(Time.time < laughUntil ? "laugh" : "idle");

            if (phase == Phase.Live && !Scripted)
            {
                if (!busy) Follow(dt);
                Brain();
            }

            // face whoever she is busy with; the art faces right
            if (cam != null)
            {
                Vector3 look = FocusPoint();
                float d = Vector3.Dot(look - transform.position, cam.transform.right);
                if (Mathf.Abs(d) > 0.15f) flip = d >= 0f ? 1f : -1f;
            }

            glitch = Mathf.MoveTowards(glitch, glitchTarget, dt * 3f);
            float flicker = phase == Phase.Live && Random.value < dt * 0.8f ? 0.5f : 0f; // a rare unstable twitch
            Apply(body, mpb, Frame(clip, clipT), flip, Mathf.Clamp01(glitch + flicker), dissolve, Color.white, 0f, alpha);
            Puppet(dt);

            // ground blob under her (soft, darker when she floats low)
            float h = Mathf.Max(0.2f, transform.position.y);
            shadow.position = new Vector3(transform.position.x, 0.03f, transform.position.z);
            float sh = Mathf.Clamp01(1.4f / (h + 0.4f)) * (1f - dissolve) * alpha;
            shadow.localScale = new Vector3(1.1f, 0.7f, 1f) * (0.75f + 0.25f * sh);
            shadow.gameObject.SetActive(sh > 0.05f);

            for (int i = 0; i < ghosts.Count; i++)
            {
                var g = ghosts[i];
                if (g.life <= 0f) continue;
                g.life -= dt;
                if (g.life <= 0f) { g.r.gameObject.SetActive(false); continue; }
                float k = g.life / g.max;
                Apply(g.r, g.b, g.frame, g.flip, 0.6f, 1f - k * 0.9f, g.tint, 0.85f, k * 0.75f);
                g.b.SetFloat(IdTilt, g.tilt); g.b.SetFloat(IdShear, g.shear * k);
                g.r.SetPropertyBlock(g.b);
            }

            if (riftT.gameObject.activeSelf && cam != null) riftT.rotation = Quaternion.LookRotation(cam.transform.forward, Vector3.up);
        }

        /// <summary>
        /// Makes the flat drawing move like an animated character rather than a sticker: she leans into her motion and
        /// toward whoever she is about to hit, smears on fast moves, breathes, and squashes/stretches on every action.
        /// </summary>
        void Puppet(float dt)
        {
            if (dt <= 0f) return;
            Vector3 d = transform.position - lastPos;
            lastPos = transform.position;
            if (d.sqrMagnitude > 2.5f * 2.5f) d = Vector3.zero; // a blink is a cut, not a move
            Vector3 v = d / dt;
            lookAhead = Vector3.Lerp(lookAhead, v, 1f - Mathf.Exp(-dt * 10f));
            Vector3 right = cam_right();
            float vx = Vector3.Dot(lookAhead, right), vy = lookAhead.y;
            float speed = lookAhead.magnitude;

            // lean: into the motion, toward the target while attacking, plus a slow idle sway
            float lean = -Mathf.Clamp(vx * 0.045f, -0.4f, 0.4f);
            if (clip == "attack" || clip == "burst" || clip == "possess")
            {
                float toT = Vector3.Dot(FocusPoint() - transform.position, right);
                lean += -Mathf.Sign(toT) * 0.22f;
            }
            else if (clip == "laugh") lean += Mathf.Sin(Time.time * 15f) * 0.07f;
            else if (clip == "hurt" || clip == "surprise") lean += flip * 0.18f;
            lean += Mathf.Sin(Time.time * 1.3f + seed) * 0.06f;
            tiltKick = Mathf.MoveTowards(tiltKick, 0f, dt * 2.5f);
            tilt = Mathf.Lerp(tilt, lean + tiltKick, 1f - Mathf.Exp(-dt * 9f));
            // smear: the top trails behind on fast moves (anime smear frame)
            shear = Mathf.Lerp(shear, -Mathf.Clamp(vx * 0.03f, -0.35f, 0.35f), 1f - Mathf.Exp(-dt * 14f));

            // squash/stretch spring around 0; velocity stretches along the move
            const float K = 260f, C = 15f;
            sqVX += (-K * sqX - C * sqVX) * dt; sqX += sqVX * dt;
            sqVY += (-K * sqY - C * sqVY) * dt; sqY += sqVY * dt;
            sqX = Mathf.Clamp(sqX, -0.35f, 0.4f); sqY = Mathf.Clamp(sqY, -0.35f, 0.45f);
            float stretch = Mathf.Clamp(speed * 0.012f, 0f, 0.22f);
            float horiz = Mathf.Abs(vx) > Mathf.Abs(vy) ? 1f : 0f;
            float breathe = Mathf.Sin(Time.time * 2.6f + seed) * 0.025f;
            float sx = 1f + sqX + stretch * horiz - stretch * (1f - horiz) * 0.5f - breathe * 0.5f;
            float sy = 1f + sqY + stretch * (1f - horiz) - stretch * horiz * 0.5f + breathe;
            body.transform.localScale = new Vector3(Size * Mathf.Max(0.5f, sx), Size * Mathf.Max(0.5f, sy), 1f);

            mpb.SetFloat(IdTilt, tilt);
            mpb.SetFloat(IdShear, shear);
            body.SetPropertyBlock(mpb);
        }

        void Apply(MeshRenderer r, MaterialPropertyBlock b, int frame, float fl, float gl, float dis, Color tint, float tintAmt, float a)
        {
            b.SetFloat(IdFrame, frame);
            b.SetFloat(IdFlip, fl);
            b.SetFloat(IdGlitch, gl);
            b.SetFloat(IdDissolve, dis);
            b.SetColor(IdTint, tint);
            b.SetFloat(IdTintAmt, tintAmt);
            b.SetFloat(IdAlpha, a);
            b.SetFloat(IdQuality, GraphicsQuality.Current <= GraphicsPreset.Low ? 0f : 1f);
            b.SetFloat(IdSeed, seed);
            r.SetPropertyBlock(b);
        }

        Vector3 FocusPoint()
        {
            var t = CurrentTarget();
            if (t != null) return t.Center;
            return Owner != null ? Owner.Center + Owner.transform.forward * 3f : transform.position + Vector3.right;
        }

        Vector3 HoverPoint()
        {
            var o = Owner.transform;
            float tt = Time.time + seed;
            // a lazy figure-eight drift instead of a fixed spot
            Vector3 drift = o.right * (Mathf.Sin(tt * 0.9f) * 0.3f) + Vector3.up * (Mathf.Sin(tt * 1.7f) * 0.14f + Mathf.Sin(tt * 3.1f) * 0.04f);
            // in a fight she prowls at the enemy's flank, not behind Lucy
            var t = phase == Phase.Live ? CurrentTarget() : null;
            if (t != null)
            {
                Vector3 to = KMath.Flat(t.transform.position - o.position);
                float dist = to.magnitude;
                if (dist > 0.5f && dist < 8f)
                {
                    Vector3 fwd = to / dist;
                    Vector3 side = Vector3.Cross(Vector3.up, fwd) * (Vector3.Dot(offsetSide, Vector3.right) >= 0f ? 1f : -1f);
                    Vector3 anchor = o.position + fwd * Mathf.Min(dist * 0.6f, dist - 1.1f);
                    return anchor + side * 1.5f + Vector3.up * HoverHeight + drift;
                }
            }
            return o.position + o.TransformDirection(offsetSide) * 1.15f - o.forward * 0.55f + Vector3.up * HoverHeight + drift;
        }

        void Follow(float dt)
        {
            transform.position = Vector3.SmoothDamp(transform.position, HoverPoint(), ref vel, 0.28f, 18f, dt);
        }

        Fighter CurrentTarget()
        {
            if (Owner == null) return null;
            var t = Owner.Target;
            if (t != null && t.Alive && t.Team != Owner.Team && t.Team != Team.Neutral) return t;
            return Combat.Nearest(Owner.transform.position, Owner.Team, 16f);
        }

        /// <summary>Afterimage of her current (or a given) frame left at a point, fading out.</summary>
        public void Ghost(Vector3 pos, Color tint, float life, int frame = -1)
        {
            GhostQ free = null;
            float oldest = float.MaxValue;
            for (int i = 0; i < ghosts.Count; i++)
            {
                if (ghosts[i].life <= 0f) { free = ghosts[i]; break; }
                if (ghosts[i].life < oldest) { oldest = ghosts[i].life; free = ghosts[i]; }
            }
            if (free == null) return;
            free.r.transform.position = pos;
            free.r.gameObject.SetActive(true);
            free.life = free.max = life;
            free.frame = frame >= 0 ? frame : Frame(clip, clipT);
            free.tint = tint;
            free.flip = flip;
            free.tilt = tilt;
            free.shear = shear;
        }

        // ---------- despawn ----------
        void CheckDespawn()
        {
            if (phase == Phase.Vanish) return;
            bool battleOver = Battle.Inst == null || !Battle.Inst.Running;
            if (Owner == null || !Owner.Alive || battleOver || (GameManager.Inst != null && GameManager.Inst.PlayerCtl != null && Owner.IsPlayer && GameManager.Inst.PlayerCtl.F != Owner))
            {
                Despawn(Owner == null || battleOver);
                return;
            }
            // round over: nobody left to fight for a moment
            if (Combat.Nearest(Owner.transform.position, Owner.Team) == null)
            {
                if (noEnemySince < 0f) noEnemySince = Time.time;
                else if (Time.time - noEnemySince > 1.5f && !Scripted) { Despawn(); return; }
            }
            else noEnemySince = -1f;
            // the ultimate was cut short (owner stunned out of it): take control back
            if (Scripted && Time.time - scriptSince > 7f) EndScript();
            if (phase == Phase.Live && !Scripted)
            {
                if (Time.time >= endAt) { Despawn(); return; }
                // Lucy was moved far away (next wave spawn point): catch up with a blink instead of flying across the map
                if ((transform.position - HoverPoint()).sqrMagnitude > 14f * 14f && !busy) BlinkTo(HoverPoint());
            }
        }

        /// <summary>Leaves through a reversed glitch: blink frames, dissolve, data streaming up. instant = destroy now.</summary>
        public void Despawn(bool instant = false)
        {
            if (this == null) return;
            if (instant) { Destroy(gameObject); return; }
            if (phase == Phase.Vanish) return;
            phase = Phase.Vanish;
            StopAllCoroutines();
            busy = false;
            Scripted = false;
            StartCoroutine(VanishRoutine());
        }

        IEnumerator VanishRoutine()
        {
            PlayClip(Random.value < 0.5f ? "laugh" : "talk");
            glitchTarget = 1f;
            Sfx.Play(SfxId.Teleport, 0.6f, 1.4f);
            Ghost(transform.position + Vector3.right * 0.15f, Red, 0.4f);
            Ghost(transform.position - Vector3.right * 0.15f, Green, 0.4f);
            float t = 0f;
            while (t < 0.55f)
            {
                t += Time.deltaTime;
                dissolve = Mathf.Clamp01(t / 0.5f);
                if (Random.value < 0.6f * GraphicsQuality.ParticleMul)
                    Fx.Glyph(transform.position + Random.insideUnitSphere * 0.6f, Vector3.up * Random.Range(1.5f, 3f), Random.value < 0.5f ? Red : Green, 0.12f, 0.6f);
                yield return null;
            }
            Destroy(gameObject);
        }

        // ---------- summon ----------
        IEnumerator SummonRoutine()
        {
            phase = Phase.Summon;
            dissolve = 1f;
            glitchTarget = glitch = 1f;
            var o = Owner;
            // 1. Lucy jacks in: seal pose, a netrunner interface ring and code around her hands
            if (o.CanAct) o.Anim.Play(Clip.Hold(PoseLib.Seal, 0.06f, 0.55f, 0.15f));
            o.Shout(o.DisplayName + ": «Капри, выходи поиграть.»");
            Sfx.Play(SfxId.Charge, 0.8f, 1.5f);
            Fx.Ring(o.transform.position + Vector3.up * 0.08f, Green, 0.4f, 2.6f, 0.5f, Quaternion.identity);
            Fx.Scan(o.transform, o.Height, 0.7f, Green, 0.5f);
            Vector3 hand = o.Rig != null ? o.Rig.BonePos(B.HandR) : o.Center;
            for (int i = 0; i < 10; i++)
            {
                float a = i / 10f * Mathf.PI * 2f;
                Vector3 p = hand + new Vector3(Mathf.Cos(a), Mathf.Sin(a) * 0.6f, Mathf.Sin(a)) * 0.35f;
                Fx.Glyph(p, (p - hand) * 0.8f, i % 2 == 0 ? Green : Red, 0.1f, 0.5f);
            }
            yield return new WaitForSeconds(0.25f);

            // 2. an unstable rift tears open where she will come through
            Vector3 at = HoverPoint();
            transform.position = at;
            riftT.gameObject.SetActive(true);
            riftT.position = at;
            Fx.HackStream(hand, at, Green, 6, 0.25f);
            Sfx.Play(SfxId.Energy, 0.8f, 0.7f);
            float t = 0f;
            while (t < 0.35f)
            {
                t += Time.deltaTime;
                float k = Mathf.Clamp01(t / 0.3f);
                float jitter = Random.value < 0.3f ? Random.Range(0.6f, 1.3f) : 1f;
                riftT.localScale = new Vector3(0.9f * k * jitter, 2.1f * Mathf.Lerp(0.4f, 1f, k), 1f);
                riftT.position = at + Random.insideUnitSphere * 0.03f;
                yield return null;
            }
            WorldLighting.Flash(at, Red * 1.5f, 6f, 0.3f);

            // 3. red and green fragments converge into the tear
            int n = Mathf.Max(6, Mathf.RoundToInt(16 * GraphicsQuality.ParticleMul));
            for (int i = 0; i < n; i++)
            {
                Vector3 from = at + Random.onUnitSphere * Random.Range(1.2f, 2f);
                Fx.Glyph(from, (at - from) * 2.6f, i % 2 == 0 ? Red : Green, 0.14f, 0.38f);
            }
            Fx.Electric(at, Red, 0.9f, 2);
            yield return new WaitForSeconds(0.2f);

            // 4-5. the face and eyes resolve first, then the silhouette assembles downwards
            PlayClip("appear");
            Sfx.Play(SfxId.Teleport, 0.8f, 0.9f);
            t = 0f;
            while (t < 0.6f)
            {
                t += Time.deltaTime;
                dissolve = 1f - Mathf.SmoothStep(0f, 1f, t / 0.6f);
                glitchTarget = glitch = Mathf.Lerp(1f, 0.35f, t / 0.6f);
                riftT.localScale = new Vector3(0.9f * (1f - t / 0.6f) + 0.05f, 2.1f, 1f);
                if (Random.value < 0.4f) Ghost(at + new Vector3(Random.Range(-0.2f, 0.2f), 0, 0), Random.value < 0.5f ? Red : Green, 0.15f);
                yield return null;
            }
            dissolve = 0f;
            riftT.gameObject.SetActive(false);

            // 6. snaps into place: flash, a little shake, a look around and a laugh, then she follows Lucy
            glitchTarget = 0.08f;
            glitch = 0.9f;
            Fx.Ring(at, Red, 0.3f, 2.2f, 0.3f, Quaternion.LookRotation(cam != null ? cam.transform.forward : Vector3.forward) * Quaternion.Euler(90, 0, 0));
            WorldLighting.Flash(at, Green * 1.5f, 6f, 0.25f);
            CameraRig.Shake(0.15f);
            if (o.IsPlayer) { Haptics.Medium(); Hud.Popup("КАПРИ", Red); }
            yield return new WaitForSeconds(0.25f);
            PlayClip("look");
            yield return new WaitForSeconds(0.7f);
            PlayClip("laugh");
            laughUntil = Time.time + 0.9f;
            Sfx.Play(SfxId.Energy, 0.4f, 2f);
            yield return new WaitForSeconds(0.5f);
            phase = Phase.Live;
            endAt = Time.time + Lifetime;
            nextActionAt = Time.time + 0.8f;
            nextIdleAt = Time.time + Random.Range(2.5f, 4f);
            relocateAt = Time.time + Random.Range(4f, 7f);
        }

        // ---------- brain ----------
        void Brain()
        {
            if (busy) return;
            float now = Time.time;
            var t = CurrentTarget();

            // self-preservation: an enemy swinging right at her makes her sidestep (she can't be hit, but she reacts)
            if (now >= nextDodgeAt)
            {
                var all = Fighter.All;
                for (int i = 0; i < all.Count; i++)
                {
                    var v = all[i];
                    if (v == null || !v.Alive || v.Team == Owner.Team || v.Team == Team.Neutral || !v.InActiveFrames) continue;
                    if ((v.Center - transform.position).sqrMagnitude > 2.2f * 2.2f) continue;
                    nextDodgeAt = now + 1.6f;
                    Run(DodgeRoutine(v));
                    return;
                }
            }

            if (t != null)
            {
                float toOwner = KMath.Flat(t.transform.position - Owner.transform.position).magnitude;
                float toTarget = KMath.Flat(t.transform.position - transform.position).magnitude;
                // a reaction may jump the queue: an enemy winding up on Lucy at close range
                if (now >= burstReady && toOwner < 3.2f && (t.InStartup || t.InActiveFrames) && now >= nextActionAt - 1.6f)
                { Run(BurstRoutine()); return; }
                if (now >= nextActionAt)
                {
                    bool comboing = Owner.ComboDealt >= 2 && (t.State == FState.Hitstun || t.State == FState.Juggle);
                    if (now >= possessReady && (comboing || Random.value < 0.25f) && toTarget < 12f && t.State != FState.Cinematic) { Run(PossessRoutine(t)); return; }
                    if (now >= corruptReady && toTarget < 14f && !t.Overloaded) { Run(CorruptRoutine(t)); return; }
                    if (now >= blinkReady && toTarget < 10f && t.State != FState.Cinematic) { Run(BlinkRoutine(t)); return; }
                    // her bread and butter: swoop in, two claw rakes, back out
                    if (now >= clawReady && toTarget < 9f && t.State != FState.Cinematic) { Run(ClawRoutine(t)); return; }
                }
            }

            // idle life: expressions and repositioning
            if (now >= nextIdleAt)
            {
                nextIdleAt = now + Random.Range(2.5f, 5f);
                float r = Random.value;
                if (r < 0.35f) PlayClip("look");
                else if (r < 0.65f) PlayClip("talk");
                else { PlayClip("laugh"); laughUntil = now + 0.8f; }
                if (clip == "talk") StartCoroutine(StopTalk());
            }
            if (now >= relocateAt)
            {
                relocateAt = now + Random.Range(4f, 7f);
                offsetSide = -offsetSide;
                Ghost(transform.position, Random.value < 0.5f ? Red : Green, 0.3f);
            }
        }

        IEnumerator StopTalk()
        {
            yield return new WaitForSeconds(Random.Range(0.8f, 1.4f));
            if (clip == "talk" && !busy) PlayClip("idle");
        }

        void Run(IEnumerator r)
        {
            busy = true;
            action = StartCoroutine(Wrap(r));
        }

        IEnumerator Wrap(IEnumerator r)
        {
            yield return r;
            busy = false;
            action = null;
            nextActionAt = Time.time + Random.Range(0.9f, 1.6f);
            glitchTarget = 0.08f;
            alpha = 1f;
        }

        bool Usable(Fighter t) => t != null && t.Alive && Owner != null && Owner.Alive && phase == Phase.Live;

        /// <summary>Hit in Lucy's name (so rage, combo, stats and KO credit go to her).</summary>
        bool Deal(Fighter v, float dmg, float knock, float stun, bool unblockable, HitKind kind, Color c, bool knockdown = false, float launch = 0f, float shake = 0.15f)
        {
            if (v == null || !v.Alive || Owner == null) return false;
            Vector3 dir = KMath.Flat(v.transform.position - transform.position);
            if (dir.sqrMagnitude < 0.001f) dir = Owner.transform.forward;
            var h = new HitInfo
            {
                Attacker = Owner, Damage = dmg * Owner.DamageMul, Stamina = dmg, Knockback = knock, Launch = launch, Hitstun = stun, Kind = kind,
                Unblockable = unblockable, Parryable = false, Knockdown = knockdown, Point = v.Center, Dir = dir.normalized,
                FxColor = c, Shake = shake, HitStop = 0.04f
            };
            dealing = true;
            var res = v.ReceiveHit(ref h);
            dealing = false;
            return res == HitResult.Hit;
        }

        /// <summary>Instant hop with a pair of red/green afterimages left behind.</summary>
        public void BlinkTo(Vector3 p)
        {
            Ghost(transform.position + cam_right() * 0.12f, Red, 0.32f);
            Ghost(transform.position - cam_right() * 0.12f, Green, 0.32f);
            transform.position = p;
            lastPos = p;
            vel = Vector3.zero;
            Pop(2.6f, -2.4f); // lands from the blink squashed, then springs back
            glitch = Mathf.Max(glitch, 0.8f);
            if (GraphicsQuality.ParticleMul > 0.5f) Fx.Electric(p, Random.value < 0.5f ? Red : Green, 0.6f, 1);
        }

        Vector3 cam_right() => cam != null ? cam.transform.right : Vector3.right;

        // Digital Blink: three hops around the target with claw strikes
        IEnumerator BlinkRoutine(Fighter t)
        {
            blinkReady = Time.time + 4f;
            Sfx.Play(SfxId.Teleport, 0.7f, 1.3f);
            float a0 = Random.value * Mathf.PI * 2f;
            for (int i = 0; i < 3; i++)
            {
                if (!Usable(t)) yield break;
                float a = a0 + i * 2.2f;
                PlayClip("blink");
                BlinkTo(t.transform.position + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * 1.5f + Vector3.up * 1.25f);
                yield return new WaitForSeconds(0.08f);
                PlayClip("attack");
                yield return new WaitForSeconds(0.1f);
                if (Deal(t, 5f, 1.2f, 0.35f, false, HitKind.Technique, i % 2 == 0 ? Red : Green))
                {
                    Fx.Slash(t.transform, new Vector3(0, 1f, 0), new Vector3(0, a * Mathf.Rad2Deg, Random.Range(-50f, 50f)), i % 2 == 0 ? Red : Green, 1.8f, 0.14f);
                    Sfx.Play(SfxId.Hit, 0.7f, 1.5f);
                }
                yield return new WaitForSeconds(0.14f);
            }
            PlayClip("laugh");
            laughUntil = Time.time + 0.6f;
            yield return new WaitForSeconds(0.25f);
            BlinkTo(HoverPoint());
        }

        // Claw Swoop: a fast smeared flight (not a blink) to the target's flank, wind-up, two rakes, flight back
        IEnumerator ClawRoutine(Fighter t)
        {
            clawReady = Time.time + 2.2f;
            Vector3 right = cam_right();
            float side = Vector3.Dot(transform.position - t.transform.position, right) >= 0f ? 1f : -1f;
            // anticipation: pull back and crouch before the dash
            PlayClip("look");
            Pop(1.8f, -2.2f);
            Vector3 start = transform.position;
            Vector3 back = start + right * side * 0.35f + Vector3.up * 0.15f;
            for (float k = 0f; k < 1f; k += Time.deltaTime / 0.14f)
            {
                transform.position = Vector3.Lerp(start, back, Mathf.SmoothStep(0f, 1f, k));
                yield return null;
            }
            if (!Usable(t)) yield break;
            // dash: ease-in flight with a couple of afterimages
            Sfx.Play(SfxId.Whoosh, 0.6f, 1.4f);
            PlayClip("attack");
            Vector3 from = transform.position;
            Vector3 dest = t.transform.position + right * side * 1.05f + Vector3.up * 1.15f;
            int ghostsLeft = GraphicsQuality.Current <= GraphicsPreset.Low ? 1 : 2;
            for (float k = 0f; k < 1f; k += Time.deltaTime / 0.16f)
            {
                if (!Usable(t)) yield break;
                dest = t.transform.position + right * side * 1.05f + Vector3.up * 1.15f;
                transform.position = Vector3.Lerp(from, dest, k * k);
                if (ghostsLeft > 0 && k > 0.4f * (3 - ghostsLeft)) { ghostsLeft--; Ghost(transform.position, ghostsLeft % 2 == 0 ? Red : Green, 0.18f); }
                yield return null;
            }
            for (int i = 0; i < 2; i++)
            {
                if (!Usable(t)) yield break;
                if (i > 0) { PlayClip("attack"); yield return new WaitForSeconds(0.07f); }
                Color c = i == 0 ? Red : Green;
                if (Deal(t, 3f, 0.6f, 0.25f, false, HitKind.Technique, c))
                {
                    Fx.Slash(t.transform, new Vector3(0, 1.05f, 0), new Vector3(0, Random.Range(0f, 360f), i == 0 ? -35f : 35f), c, 1.5f, 0.12f);
                    Sfx.Play(SfxId.Hit, 0.55f, 1.6f + i * 0.15f);
                    tiltKick = -side * 0.3f;
                }
                Pop(i == 0 ? 2.5f : -2f, i == 0 ? -2f : 2.5f);
                yield return new WaitForSeconds(0.13f);
            }
            PlayClip("laugh");
            laughUntil = Time.time + 0.5f;
            // recoil away instead of snapping: Follow takes over smoothly
            vel = (right * side * 6f + Vector3.up * 2f);
            yield return new WaitForSeconds(0.1f);
        }

        // Neural Corruption: slowed reactions, one technique locked, glitch noise on the victim
        IEnumerator CorruptRoutine(Fighter t)
        {
            corruptReady = Time.time + 9f;
            PlayClip("hack");
            glitchTarget = 0.4f;
            Sfx.Play(SfxId.Energy, 0.8f, 1.6f);
            float dur = 0.5f;
            for (float k = 0f; k < dur; k += 0.1f)
            {
                if (!Usable(t)) yield break;
                Fx.HackStream(transform.position, t.Center, k * 10f % 2f < 1f ? Red : Green, 3, 0.25f);
                yield return new WaitForSeconds(0.1f);
            }
            if (!Usable(t)) yield break;
            if (Deal(t, 4f, 0.2f, 0.2f, true, HitKind.Technique, Red, false, 0f, 0.1f))
            {
                t.ApplyOverload(Owner, 3.5f, 0.6f);
                var techs = t.Kit != null ? t.Kit.Techs : null;
                if (techs != null && techs.Length > 0)
                {
                    var tech = techs[Random.Range(0, techs.Length)];
                    if (tech != null) tech.Cd = Mathf.Max(tech.Cd, t.IsBoss ? 2f : 4f);
                }
                t.Flash(Red);
                int g = Mathf.Max(4, Mathf.RoundToInt(10 * GraphicsQuality.ParticleMul));
                for (int i = 0; i < g; i++)
                    Fx.Glyph(t.Center + Random.insideUnitSphere * 0.6f, Random.insideUnitSphere * 1.2f, i % 2 == 0 ? Red : Green, 0.12f, 0.5f);
                Fx.Electric(t.Center, Red, 1f, 2);
                if (Owner.IsPlayer || t.IsPlayer) Hud.Popup("НЕЙРОСБОЙ", Red);
            }
            PlayClip("laugh");
            laughUntil = Time.time + 0.5f;
        }

        // Blackwall Burst: a red/black pulse that shoves enemies away from Lucy and breaks their attack
        IEnumerator BurstRoutine()
        {
            burstReady = Time.time + 7f;
            Vector3 mid = Owner.transform.position + Owner.transform.forward * 1.1f + Vector3.up * 1.3f;
            BlinkTo(mid);
            PlayClip("burst");
            glitchTarget = 0.6f;
            yield return new WaitForSeconds(0.12f);
            Vector3 g = new Vector3(transform.position.x, 0.1f, transform.position.z);
            Fx.Ring(g, Red, 0.4f, 6.5f, 0.45f, Quaternion.identity);
            Fx.Ring(g + Vector3.up * 0.05f, new Color(0.1f, 0.02f, 0.04f), 0.3f, 5f, 0.4f, Quaternion.identity);
            Fx.Ring(transform.position, Green, 0.3f, 3.5f, 0.3f, Quaternion.LookRotation(cam != null ? cam.transform.forward : Vector3.forward) * Quaternion.Euler(90, 0, 0));
            WorldLighting.Flash(transform.position, Red * 2.2f, 8f, 0.35f);
            Sfx.Play(SfxId.Explosion, 0.6f, 1.35f);
            CameraRig.Shake(0.3f);
            var all = Fighter.All;
            for (int i = 0; i < all.Count; i++)
            {
                var v = all[i];
                if (v == null || !v.Alive || v.Team == Owner.Team || v.Team == Team.Neutral) continue;
                float d = Mathf.Min(KMath.Flat(v.transform.position - transform.position).magnitude, KMath.Flat(v.transform.position - Owner.transform.position).magnitude);
                if (d > 3.4f) continue;
                if (Deal(v, 10f, 8f, 0.5f, true, HitKind.Technique, Red, false, 1.5f, 0.3f))
                    Fx.Lightning(transform.position, v.Center, Red, 0.08f, 0.25f);
            }
            if (Owner.IsPlayer) Haptics.Medium();
            yield return new WaitForSeconds(0.3f);
        }

        // Digital Possession: she dives into the target, its body glitches in place (a free combo window), then she bursts out
        IEnumerator PossessRoutine(Fighter t)
        {
            possessReady = Time.time + 12f;
            PlayClip("possess");
            glitchTarget = 0.7f;
            Sfx.Play(SfxId.Whoosh, 0.8f, 1.5f);
            Vector3 from = transform.position;
            for (float k = 0f; k < 1f; k += Time.deltaTime / 0.18f)
            {
                if (!Usable(t)) yield break;
                transform.position = Vector3.Lerp(from, t.Center, k);
                if (Random.value < 0.5f) Ghost(transform.position, Random.value < 0.5f ? Red : Green, 0.2f);
                yield return null;
            }
            if (!Usable(t)) yield break;
            if (!Deal(t, 6f, 0f, 0.4f, true, HitKind.Technique, Green, false, 0f, 0.2f))
            {
                PlayClip("surprise");
                yield return new WaitForSeconds(0.3f);
                BlinkTo(HoverPoint());
                yield break;
            }
            alpha = 0f; // she is inside now
            float stun = t.IsBoss ? 0.6f : 1.6f;
            t.Stun(stun);
            if (Owner.IsPlayer || t.IsPlayer) Hud.Popup("ОДЕРЖИМОСТЬ", Green);
            Vector3 home = t.transform.position;
            float end = Time.time + stun;
            int step = 0;
            while (Time.time < end && t.Alive && t.State == FState.Stunned)
            {
                // the body stutters between positions like a dropped network packet
                t.Teleport(home + new Vector3(Random.Range(-0.18f, 0.18f), 0, Random.Range(-0.18f, 0.18f)));
                t.Flash(step++ % 2 == 0 ? Red : Green, 0.6f);
                transform.position = t.Center;
                if (Random.value < 0.6f * GraphicsQuality.ParticleMul)
                    Fx.Glyph(t.Center + Random.insideUnitSphere * 0.5f, Random.insideUnitSphere, step % 2 == 0 ? Red : Green, 0.1f, 0.35f);
                yield return new WaitForSeconds(0.11f);
            }
            if (t != null && t.Alive && t.State == FState.Stunned) t.Teleport(home);
            alpha = 1f;
            PlayClip("laugh");
            laughUntil = Time.time + 0.7f;
            Vector3 outAt = (t != null ? t.Center : transform.position) + Vector3.up * 0.8f + cam_right() * 0.6f;
            BlinkTo(outAt);
            if (t != null) Fx.Electric(t.Center, Green, 1f, 2);
            yield return new WaitForSeconds(0.35f);
            BlinkTo(HoverPoint());
        }

        IEnumerator DodgeRoutine(Fighter v)
        {
            PlayClip("surprise");
            Vector3 away = KMath.Flat(transform.position - v.transform.position).normalized;
            if (away.sqrMagnitude < 0.01f) away = cam_right();
            BlinkTo(transform.position + (away + Vector3.Cross(Vector3.up, away) * (Random.value < 0.5f ? 1f : -1f)).normalized * 1.6f);
            yield return new WaitForSeconds(0.35f);
            PlayClip("laugh");
            laughUntil = Time.time + 0.5f;
        }

        // ---------- reactions (called by ReinaKit) ----------
        public void OnOwnerHurt(Fighter attacker)
        {
            if (phase != Phase.Live || Scripted || busy) return;
            PlayClip(Random.value < 0.5f ? "surprise" : "hurt");
            glitch = Mathf.Max(glitch, 0.7f);
            // she takes it personally: a quick shove if the attacker is still in reach
            if (attacker != null && attacker.Alive && Time.time >= burstReady && KMath.Flat(attacker.transform.position - Owner.transform.position).magnitude < 3.2f)
            {
                nextActionAt = Mathf.Min(nextActionAt, Time.time + 0.4f);
                StartCoroutine(DelayedBurst());
            }
        }

        IEnumerator DelayedBurst()
        {
            yield return new WaitForSeconds(0.35f);
            if (phase == Phase.Live && !busy && !Scripted && Time.time >= burstReady) Run(BurstRoutine());
        }

        public void OnOwnerHit(Fighter target)
        {
            if (dealing || phase != Phase.Live || Scripted || busy || target == null || !target.Alive) return;
            if (Owner.ComboDealt < 2 || Time.time < followUpAt || Random.value > 0.6f) return;
            followUpAt = Time.time + 1.6f;
            Run(FollowUpRoutine(target));
        }

        // joins Lucy's combo with one claw strike from the other side
        IEnumerator FollowUpRoutine(Fighter t)
        {
            yield return new WaitForSeconds(0.12f);
            if (!Usable(t)) yield break;
            Vector3 side = KMath.Flat(t.transform.position - Owner.transform.position).normalized;
            PlayClip("blink");
            BlinkTo(t.transform.position + side * 1.2f + Vector3.up * 1.2f);
            yield return new WaitForSeconds(0.06f);
            PlayClip("attack");
            yield return new WaitForSeconds(0.1f);
            if (Deal(t, 4f, 0.8f, 0.3f, false, HitKind.Technique, Green))
                Fx.Slash(t.transform, new Vector3(0, 1f, 0), new Vector3(0, Random.Range(0f, 360f), 40f), Green, 1.6f, 0.12f);
            yield return new WaitForSeconds(0.2f);
            BlinkTo(HoverPoint());
        }

        // ---------- joint ultimate hooks (ReinaKit.OverdriveRoutine) ----------
        /// <summary>Stops whatever she was doing so the cinematic can drive her.</summary>
        public void BeginScript()
        {
            Scripted = true;
            scriptSince = Time.time;
            if (action != null) StopCoroutine(action);
            action = null;
            busy = false;
            alpha = 1f;
            glitchTarget = 0.3f;
        }

        float scriptSince;

        public void EndScript()
        {
            Scripted = false;
            glitchTarget = 0.08f;
            nextActionAt = Time.time + 2f;
            // the ultimate took time out of her stay; leave her a few seconds to celebrate
            endAt = Mathf.Max(endAt, Time.time + 3f);
        }

        /// <summary>Four tinted copies of her around a point that hold, then fade.</summary>
        public void Projections(Vector3 center, int count, float radius, float life)
        {
            for (int i = 0; i < count; i++)
            {
                float a = i / (float)count * Mathf.PI * 2f + 0.3f;
                Vector3 p = center + new Vector3(Mathf.Cos(a), 0.15f * (i % 2), Mathf.Sin(a)) * radius;
                Ghost(p, i % 2 == 0 ? Red : Green, life, Clips["attack"].start + (i % 3) + 2);
                if (GraphicsQuality.ParticleMul > 0.5f) Fx.Glyph(p, Vector3.up * 0.5f, i % 2 == 0 ? Red : Green, 0.12f, 0.4f);
            }
        }

        public void SetGlitch(float g) { glitchTarget = g; glitch = Mathf.Max(glitch, g); }
    }
}
