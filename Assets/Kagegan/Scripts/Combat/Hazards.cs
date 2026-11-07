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
        static Mesh ringMesh, discMesh;
        static MaterialPropertyBlock mpb;
        static readonly int IdColor = Shader.PropertyToID("_Color");

        public Fighter Owner;
        public HazardKind Kind;
        public float Radius = 2f, Delay = 0.8f, Duration = 0f, Damage = 25f;
        public Color Color = Color.red;
        float t, tick;
        bool fired;
        MeshRenderer ring, disc;

        public static void InitPool(Transform parent)
        {
            mpb = new MaterialPropertyBlock();
            var mb = new MeshBuilder();
            mb.RingXZ(Vector3.zero, 0.93f, 1f, 48, new Color32(255, 255, 255, 255), new Color32(255, 255, 255, 255));
            ringMesh = mb.ToMesh("hzring");
            mb.Clear();
            mb.RingXZ(Vector3.zero, 0f, 1f, 32, new Color32(255, 255, 255, 60), new Color32(255, 255, 255, 140));
            discMesh = mb.ToMesh("hzdisc");
            pool = new Pool<Hazard>(() =>
            {
                var go = new GameObject("Hazard");
                go.transform.SetParent(parent, false);
                var h = go.AddComponent<Hazard>();
                h.ring = Part(go.transform, ringMesh);
                h.disc = Part(go.transform, discMesh);
                go.SetActive(false);
                return h;
            }, 6);
        }

        static MeshRenderer Part(Transform p, Mesh m)
        {
            var go = new GameObject("p");
            go.transform.SetParent(p, false);
            go.AddComponent<MeshFilter>().sharedMesh = m;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = Mat.FxAlphaSolid;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return r;
        }

        public static Hazard Spawn(Fighter owner, HazardKind kind, Vector3 pos, float radius, float delay, float damage, Color color, float duration = 0f)
        {
            var h = pool.Get();
            h.Owner = owner; h.Kind = kind; h.Radius = radius; h.Delay = delay; h.Damage = damage; h.Color = color; h.Duration = duration;
            h.t = 0f; h.tick = 0f; h.fired = false;
            h.transform.position = new Vector3(pos.x, 0.05f, pos.z);
            h.transform.localScale = Vector3.one * radius;
            Live.Add(h);
            return h;
        }

        public static void ClearAll() { for (int i = Live.Count - 1; i >= 0; i--) { var h = Live[i]; Live.RemoveAt(i); pool.Release(h); } }

        void Update()
        {
            float dt = Time.deltaTime;
            t += dt;
            Team team = Owner != null ? Owner.Team : Team.Enemy;
            if (!fired)
            {
                // readable but unobtrusive telegraph: thin pulsing outline + faint fill growing to the edge
                float u = Mathf.Clamp01(t / Mathf.Max(0.01f, Delay));
                Color c = Color; c.a = 0.45f + 0.3f * Mathf.Sin(t * 18f);
                mpb.SetColor(IdColor, c); ring.SetPropertyBlock(mpb);
                disc.transform.localScale = Vector3.one * u;
                Color dc = Color; dc.a = 0.16f + 0.1f * u;
                mpb.SetColor(IdColor, dc); disc.SetPropertyBlock(mpb);
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
                mpb.SetColor(IdColor, c); ring.SetPropertyBlock(mpb); disc.SetPropertyBlock(mpb);
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
