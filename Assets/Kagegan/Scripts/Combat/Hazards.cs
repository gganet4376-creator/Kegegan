using System.Collections.Generic;
using UnityEngine;

namespace Kagegan
{
    /// <summary>Channelled energy beam (pooled). Ticks damage along its length; can be absorbed tick-by-tick.</summary>
    public class Beam : MonoBehaviour
    {
        static Pool<Beam> pool;
        public static readonly List<Beam> Live = new List<Beam>();
        static Mesh mesh;
        static MaterialPropertyBlock mpb;
        static readonly int IdColor = Shader.PropertyToID("_Color");

        public Fighter Owner;
        public Transform Origin;
        public Vector3 OriginOffset;
        public Vector3 FixedDir;
        public bool FollowOwner = true;
        public float Length = 14f, Width = 0.6f, Duration = 1.2f, TickDamage = 6f, TickRate = 0.1f, Knock = 3f;
        public Color Color = Color.white;
        public bool Absorbable = true;
        public System.Action<Fighter> OnTickHit;
        float t, tick;
        MeshRenderer mr, core;
        readonly Collider[] overlap = new Collider[6];

        public static void InitPool(Transform parent)
        {
            mpb = new MaterialPropertyBlock();
            var mb = new MeshBuilder();
            mb.SetColor(new Color32(255, 255, 255, 255));
            mb.Limb(Vector3.zero, Vector3.forward, 1f, 1f, 10, false);
            mesh = mb.ToMesh("beam");
            pool = new Pool<Beam>(() =>
            {
                var go = new GameObject("Beam");
                go.transform.SetParent(parent, false);
                var b = go.AddComponent<Beam>();
                b.mr = MakePart(go.transform, "Outer");
                b.core = MakePart(go.transform, "Core");
                go.SetActive(false);
                return b;
            }, 2);
        }

        static MeshRenderer MakePart(Transform parent, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = Mat.FxAddSolid;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return r;
        }

        public static Beam Fire(Fighter owner, Transform origin, Vector3 offset, float length, float width, float duration, float tickDamage, Color color)
        {
            var b = pool.Get();
            b.Owner = owner; b.Origin = origin; b.OriginOffset = offset; b.Length = length; b.Width = width; b.Duration = duration;
            b.TickDamage = tickDamage; b.Color = color; b.t = 0f; b.tick = 0f; b.FollowOwner = true; b.Absorbable = true; b.OnTickHit = null; b.Knock = 3f;
            b.FixedDir = owner != null ? owner.transform.forward : Vector3.forward;
            Live.Add(b);
            b.UpdateTransform();
            return b;
        }

        public void Stop() { t = Duration; }

        public static void ClearAll() { for (int i = Live.Count - 1; i >= 0; i--) { var b = Live[i]; Live.RemoveAt(i); pool.Release(b); } }

        void UpdateTransform()
        {
            Vector3 o = Origin != null ? Origin.position + (Owner != null ? Owner.transform.rotation * OriginOffset : OriginOffset) : transform.position;
            Vector3 dir = FollowOwner && Owner != null ? Owner.transform.forward : FixedDir;
            transform.SetPositionAndRotation(o, Quaternion.LookRotation(dir));
        }

        void Update()
        {
            float dt = Time.deltaTime;
            t += dt;
            UpdateTransform();
            float grow = Mathf.Clamp01(t / 0.12f), fade = Mathf.Clamp01((Duration - t) / 0.2f);
            float w = Width * grow * fade * (1f + Mathf.Sin(t * 40f) * 0.06f);
            float len = Length;
            mr.transform.localScale = new Vector3(w, w, len);
            core.transform.localScale = new Vector3(w * 0.45f, w * 0.45f, len);
            mpb.SetColor(IdColor, Color * 0.8f);
            mr.SetPropertyBlock(mpb);
            mpb.SetColor(IdColor, Color.white);
            core.SetPropertyBlock(mpb);
            Fx.Glow(transform.position, Color, w * 2.2f, 0.06f);
            if (Time.frameCount % 2 == 0) Fx.Trail(transform.position + transform.forward * len, Color, w * 1.5f);
            WorldLighting.Flash(transform.position + transform.forward * 2f, Color * 1.5f, 6f, 0.08f);

            tick += dt;
            if (tick >= TickRate && t > 0.1f)
            {
                tick = 0f;
                DoTick(len);
            }
            if (t >= Duration) { Live.Remove(this); pool.Release(this); }
        }

        void DoTick(float len)
        {
            Vector3 a = transform.position, b = a + transform.forward * len;
            Team team = Owner != null ? Owner.Team : Team.Enemy;
            for (int i = 0; i < Fighter.All.Count; i++)
            {
                var f = Fighter.All[i];
                if (!f.Alive || f.Team == team) continue;
                float d = KMath.SegmentSegmentDistance(a, b, f.transform.position + Vector3.up * 0.2f, f.transform.position + Vector3.up * f.Height);
                if (d > Width * 0.6f + f.BodyRadius) continue;
                var h = new HitInfo
                {
                    Attacker = Owner, Damage = TickDamage * (Owner != null ? Owner.DamageMul : 1f), Stamina = 6f, Knockback = Knock, Hitstun = 0.25f,
                    Kind = HitKind.Technique, Parryable = false, Absorbable = Absorbable, Point = f.Center, Dir = transform.forward,
                    FxColor = Color, Shake = 0.08f, HitStop = 0.0f
                };
                var r = f.ReceiveHit(ref h);
                if (r == HitResult.Hit) OnTickHit?.Invoke(f);
            }
            int n = Physics.OverlapCapsuleNonAlloc(a, b, Width * 0.5f, overlap, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                var d = overlap[i].GetComponentInParent<Destructible>();
                if (d != null && !d.Broken) d.Damage(TickDamage * 3f, d.transform.position, transform.forward);
            }
        }
    }

    public enum HazardKind { LightningStrike, Meteor, VoidImplosion, FireField, PoisonCloud, SnakeBurst, BoneSpikes }

    /// <summary>Telegraphed area attack: a warning ring on the ground, then a strike or a damaging field.</summary>
    public class Hazard : MonoBehaviour
    {
        static Pool<Hazard> pool;
        public static readonly List<Hazard> Live = new List<Hazard>();
        static Mesh quadMesh, ringMesh, discMesh;
        static Material[] runeMats;   // OpenGameArt "Summoning circles" (CC0), one pattern per attack family
        static Material fillMat;
        static MaterialPropertyBlock mpb;
        static readonly int IdColor = Shader.PropertyToID("_Color");
        const float FadeOut = 0.4f;

        public Fighter Owner;
        public HazardKind Kind;
        public float Radius = 2f, Delay = 0.8f, Duration = 0f, Damage = 25f;
        public Color Color = Color.red;
        float t, tick, spin, spinSpeed;
        bool fired;
        MeshRenderer ring, disc;
        bool textured;

        public static void InitPool(Transform parent)
        {
            mpb = new MaterialPropertyBlock();
            var mb = new MeshBuilder();
            mb.Col = new Color32(255, 255, 255, 255);
            mb.Quad(Vector3.zero, Vector3.right, Vector3.back); // faces up
            quadMesh = mb.ToMesh("hzquad");
            mb.Clear();
            mb.RingXZ(Vector3.zero, 0.93f, 1f, 48, new Color32(255, 255, 255, 255), new Color32(255, 255, 255, 255));
            ringMesh = mb.ToMesh("hzring");
            mb.Clear();
            mb.RingXZ(Vector3.zero, 0f, 1f, 32, new Color32(255, 255, 255, 60), new Color32(255, 255, 255, 140));
            discMesh = mb.ToMesh("hzdisc");

            string[] names = { "rune_circle4", "rune_circle7", "rune_circle6" };
            runeMats = new Material[names.Length];
            for (int i = 0; i < names.Length; i++)
            {
                var tex = Resources.Load<Texture2D>("Kagegan/Vfx/" + names[i]);
                if (tex == null) { runeMats = null; break; }
                tex.wrapMode = TextureWrapMode.Clamp;
                runeMats[i] = Mat.FxTex(tex, true, "Hazard_" + names[i]);
            }
            fillMat = Mat.FxTex(Mat.SoftDot, true, "HazardFill");

            pool = new Pool<Hazard>(() =>
            {
                var go = new GameObject("Hazard");
                go.transform.SetParent(parent, false);
                var h = go.AddComponent<Hazard>();
                h.textured = runeMats != null;
                h.ring = Part(go.transform, h.textured ? quadMesh : ringMesh, h.textured ? runeMats[0] : Mat.FxAlphaSolid);
                h.disc = Part(go.transform, h.textured ? quadMesh : discMesh, h.textured ? fillMat : Mat.FxAlphaSolid);
                go.SetActive(false);
                return h;
            }, 6);
        }

        static MeshRenderer Part(Transform p, Mesh m, Material mat)
        {
            var go = new GameObject("p");
            go.transform.SetParent(p, false);
            go.AddComponent<MeshFilter>().sharedMesh = m;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            return r;
        }

        static int RuneFor(HazardKind k)
        {
            switch (k)
            {
                case HazardKind.PoisonCloud: case HazardKind.SnakeBurst: return 0;   // spiral
                case HazardKind.LightningStrike: case HazardKind.Meteor: return 1;   // eight-point star
                default: return 2;                                                   // flower of life
            }
        }

        public static Hazard Spawn(Fighter owner, HazardKind kind, Vector3 pos, float radius, float delay, float damage, Color color, float duration = 0f)
        {
            var h = pool.Get();
            h.Owner = owner; h.Kind = kind; h.Radius = radius; h.Delay = delay; h.Damage = damage; h.Color = color; h.Duration = duration;
            h.t = 0f; h.tick = 0f; h.fired = false;
            h.spin = Random.value * 360f;
            h.spinSpeed = (Random.value < 0.5f ? -1f : 1f) * Random.Range(25f, 45f);
            if (h.textured) h.ring.sharedMaterial = runeMats[RuneFor(kind)];
            // a hair above the ground, staggered so overlapping circles never z-fight
            h.transform.position = new Vector3(pos.x, 0.04f + (Live.Count % 6) * 0.004f, pos.z);
            h.transform.localScale = Vector3.one * radius;
            h.ring.transform.localPosition = Vector3.zero;
            h.disc.transform.localPosition = Vector3.up * 0.002f;
            Live.Add(h);
            return h;
        }

        public static void ClearAll() { for (int i = Live.Count - 1; i >= 0; i--) { var h = Live[i]; Live.RemoveAt(i); pool.Release(h); } }

        void SetCol(MeshRenderer r, Color c)
        {
            mpb.SetColor(IdColor, c);
            r.SetPropertyBlock(mpb);
        }

        /// <summary>Additive glow: the shader premultiplies by alpha, so intensity goes into alpha (above 1 brightens the tint).</summary>
        void SetGlow(MeshRenderer r, Color c, float intensity)
        {
            intensity = Mathf.Max(0f, intensity);
            float boost = Mathf.Max(1f, intensity);
            SetCol(r, new Color(c.r * boost, c.g * boost, c.b * boost, Mathf.Min(1f, intensity)));
        }

        void Update()
        {
            float dt = Time.deltaTime;
            t += dt;
            Team team = Owner != null ? Owner.Team : Team.Enemy;
            if (!textured) { UpdateFlat(dt, team); return; }

            // glowing rune circle: unfolds, turns slowly, the inside fills up until the strike, then flares and fades
            spin += spinSpeed * dt * (fired ? 0.4f : 1f);
            Color glow = Color.Lerp(Color, Color.white, 0.2f);
            if (!fired)
            {
                float u = Mathf.Clamp01(t / Mathf.Max(0.01f, Delay));
                float open = 1f - Mathf.Pow(1f - Mathf.Clamp01(t / 0.25f), 3f); // ease-out
                float s = Mathf.Lerp(0.55f, 1.06f, open);
                ring.transform.localRotation = Quaternion.Euler(0f, spin, 0f);
                ring.transform.localScale = Vector3.one * s;
                // brightens and pulses faster as the strike nears
                float pulse = 0.85f + 0.15f * Mathf.Sin(t * Mathf.Lerp(8f, 26f, u));
                SetGlow(ring, glow, Mathf.Lerp(0.6f, 1.15f, u) * pulse * open);
                disc.transform.localScale = Vector3.one * Mathf.Max(0.02f, u) * 1.15f;
                SetGlow(disc, Color, 0.22f + 0.33f * u * u);
                if (t >= Delay) { fired = true; Fire(team); }
                return;
            }

            float since = t - Delay;
            if (Duration > 0f)
            {
                tick += dt;
                if (tick >= 0.35f && since < Duration)
                {
                    tick = 0f;
                    Combat.AreaDamage(Owner, team, transform.position + Vector3.up * 0.8f, Radius, Damage * 0.18f, 1f, 0f, false, Color, Kind != HazardKind.PoisonCloud);
                }
                if (since < Duration) FieldFx();
            }
            // flare on the strike, settle to a dim glow for lasting fields, then fade smoothly
            float flare = Mathf.Exp(-since * 9f);
            float fade = 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((since - Duration) / FadeOut));
            float hold = Duration > 0f ? 0.45f : 0f;
            ring.transform.localRotation = Quaternion.Euler(0f, spin, 0f);
            ring.transform.localScale = Vector3.one * (1.06f + 0.12f * (1f - flare) * (Duration > 0f ? 0f : 1f));
            SetGlow(ring, glow, (hold + 1.4f * flare) * fade);
            disc.transform.localScale = Vector3.one * 1.15f;
            SetGlow(disc, Color, ((Duration > 0f ? 0.25f : 0f) + 0.6f * flare) * fade);
            if (since >= Duration + FadeOut) { Live.Remove(this); pool.Release(this); }
        }

        /// <summary>Fallback when the rune textures are missing: the old flat outline + fill.</summary>
        void UpdateFlat(float dt, Team team)
        {
            if (!fired)
            {
                float u = Mathf.Clamp01(t / Mathf.Max(0.01f, Delay));
                Color c = Color; c.a = 0.45f + 0.3f * Mathf.Sin(t * 18f);
                SetCol(ring, c);
                disc.transform.localScale = Vector3.one * u;
                Color dc = Color; dc.a = 0.16f + 0.1f * u;
                SetCol(disc, dc);
                if (t >= Delay) { fired = true; Fire(team); }
                return;
            }
            disc.transform.localScale = Vector3.one;
            if (Duration > 0f)
            {
                tick += dt;
                if (tick >= 0.35f)
                {
                    tick = 0f;
                    Combat.AreaDamage(Owner, team, transform.position + Vector3.up * 0.8f, Radius, Damage * 0.18f, 1f, 0f, false, Color, Kind != HazardKind.PoisonCloud);
                }
                FieldFx();
                Color c = Color; c.a = 0.2f * Mathf.Clamp01((Delay + Duration - t) / 0.4f);
                SetCol(ring, c); SetCol(disc, c);
            }
            if (t >= Delay + Duration + 0.05f) { Live.Remove(this); pool.Release(this); }
        }

        void FieldFx()
        {
            Vector3 p = transform.position + new Vector3(Random.Range(-Radius, Radius) * 0.7f, 0.2f, Random.Range(-Radius, Radius) * 0.7f);
            switch (Kind)
            {
                case HazardKind.FireField: Fx.Fire(p, 0.6f, 2); break;
                case HazardKind.PoisonCloud: if (Random.value < 0.18f) Fx.Smoke(p + Vector3.up * 0.15f, 0.45f, new Color(0.55f, 0.3f, 0.65f, 0.22f), 1); break;
                default: Fx.Trail(p, Color, 0.4f); break;
            }
        }

        void Fire(Team team)
        {
            Vector3 p = transform.position;
            switch (Kind)
            {
                case HazardKind.LightningStrike:
                    Fx.Lightning(p + Vector3.up * 18f + Random.insideUnitSphere * 2f, p, Color, 0.25f, 0.3f);
                    Fx.Hit(p + Vector3.up * 0.3f, Color, true);
                    Fx.Crack(p, Radius * 1.2f);
                    Sfx.Play(SfxId.Thunder, 0.9f);
                    break;
                case HazardKind.Meteor:
                    Fx.Explosion(p, Radius, Color);
                    Sfx.Play(SfxId.Explosion, 1f);
                    break;
                case HazardKind.VoidImplosion:
                    Fx.Burst(p + Vector3.up, Color, Radius * 1.2f, 0.4f);
                    Fx.Ring(p + Vector3.up * 0.1f, Color, Radius * 1.6f, 0.2f, 0.35f, Quaternion.identity);
                    Sfx.Play(SfxId.Release, 0.8f);
                    break;
                case HazardKind.SnakeBurst:
                    Fx.Dust(p, Radius, 10);
                    Fx.Debris(p, new Color(0.5f, 0.4f, 0.3f), 6, 5f);
                    Fx.Crack(p, Radius);
                    Sfx.Play(SfxId.Hiss, 1f);
                    break;
                case HazardKind.BoneSpikes:
                    Fx.Burst(p + Vector3.up * 0.5f, new Color(0.95f, 0.95f, 0.9f), Radius, 0.3f);
                    Fx.Debris(p, new Color(0.95f, 0.95f, 0.9f), 6, 6f);
                    Sfx.Play(SfxId.HeavyHit, 0.8f, 1.3f);
                    break;
                default:
                    Fx.Fire(p, Radius, 12);
                    Sfx.Play(SfxId.Fire, 0.8f);
                    break;
            }
            CameraRig.Shake(0.25f);
            if (Duration <= 0f)
            {
                bool absorbable = Kind != HazardKind.SnakeBurst && Kind != HazardKind.BoneSpikes;
                Combat.AreaDamage(Owner, team, p + Vector3.up * 0.8f, Radius, Damage, 4f, Kind == HazardKind.SnakeBurst || Kind == HazardKind.BoneSpikes ? 9f : 5f, true, Color, absorbable);
                Destructible.ImpactAt(p, Radius, Damage * 2f, Owner);
            }
        }
    }
}
