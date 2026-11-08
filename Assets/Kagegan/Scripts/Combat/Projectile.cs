using System.Collections.Generic;
using UnityEngine;

namespace Kagegan
{
    public enum ProjStyle { Orb, Fire, Shuriken, Rod, Snake, Kunai, Spear, Lightning, Meteor }

    /// <summary>
    /// Pooled projectile. Collides with fighters of other teams (distance tests), clashes with enemy projectiles
    /// (higher power wins), can be absorbed (Mugen) or reflected by a parry, and damages destructible props.
    /// </summary>
    public class Projectile : MonoBehaviour
    {
        static Pool<Projectile> pool;
        public static readonly List<Projectile> Live = new List<Projectile>(32);
        static readonly Dictionary<ProjStyle, Mesh> meshes = new Dictionary<ProjStyle, Mesh>();
        static MaterialPropertyBlock mpb;
        static readonly int IdColor = Shader.PropertyToID("_Color");

        public Fighter Owner;
        public Team Team;
        public Vector3 Vel;
        public float Radius = 0.35f, Life = 3f, Damage = 20f, Power = 3f, Knockback = 5f, Launch, Hitstun = 0.5f, Gravity;
        public bool Knockdown, Absorbable = true, Explode, Pierce, Unblockable, GroundHug, Harmless;
        public float ExplodeRadius = 2.5f;
        public Color Color = Color.white;
        public ProjStyle Style;
        public Fighter Homing;
        public float HomingRate;
        public float Size = 1f;
        public System.Action<Projectile, Fighter> OnHitFighter;
        public System.Action<Projectile> OnExpire;
        MeshRenderer mr;
        MeshFilter mf;
        float age;
        readonly List<Fighter> hit = new List<Fighter>(4);
        static readonly Collider[] overlap = new Collider[8];

        public static void InitPool(Transform parent)
        {
            mpb = new MaterialPropertyBlock();
            pool = new Pool<Projectile>(() =>
            {
                var go = new GameObject("Projectile");
                go.transform.SetParent(parent, false);
                var p = go.AddComponent<Projectile>();
                p.mf = go.AddComponent<MeshFilter>();
                p.mr = go.AddComponent<MeshRenderer>();
                p.mr.sharedMaterial = Mat.FxAddSolid;
                p.mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                p.mr.receiveShadows = false;
                go.SetActive(false);
                return p;
            }, 16);
        }

        public static Projectile Spawn(Fighter owner, ProjStyle style, Vector3 pos, Vector3 vel, float damage, Color color, float size = 1f)
        {
            var p = pool.Get();
            p.Reset();
            p.Owner = owner; p.Team = owner != null ? owner.Team : Team.Enemy;
            p.Style = style; p.Vel = vel; p.Damage = damage; p.Color = color; p.Size = size;
            p.Radius = 0.3f * size;
            p.transform.position = pos;
            p.transform.rotation = vel.sqrMagnitude > 0.01f ? Quaternion.LookRotation(vel) : Quaternion.identity;
            p.transform.localScale = Vector3.one * size;
            p.mf.sharedMesh = MeshFor(style);
            p.mr.sharedMaterial = style == ProjStyle.Fire || style == ProjStyle.Meteor || style == ProjStyle.Orb || style == ProjStyle.Lightning ? Mat.FxAdd : Mat.FxAlphaSolid;
            if (style == ProjStyle.Rod || style == ProjStyle.Kunai || style == ProjStyle.Snake || style == ProjStyle.Spear || style == ProjStyle.Shuriken) p.mr.sharedMaterial = Mat.FxAddSolid;
            mpb.SetColor(IdColor, color);
            p.mr.SetPropertyBlock(mpb);
            Live.Add(p);
            return p;
        }

        void Reset()
        {
            Owner = null; Homing = null; HomingRate = 0f; Gravity = 0f; Life = 3f; age = 0f; Power = 3f; Knockback = 5f; Launch = 0f; Hitstun = 0.5f;
            Knockdown = false; Harmless = false; Absorbable = true; Explode = false; Pierce = false; Unblockable = false; GroundHug = false; ExplodeRadius = 2.5f;
            OnHitFighter = null; OnExpire = null; hit.Clear();
        }

        public void Despawn()
        {
            if (!gameObject.activeSelf) return;
            Live.Remove(this);
            pool.Release(this);
        }

        public static void ClearAll()
        {
            for (int i = Live.Count - 1; i >= 0; i--) Live[i].Despawn();
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            age += dt;
            if (Homing != null && Homing.Alive && HomingRate > 0f)
            {
                Vector3 want = (Homing.Center - transform.position).normalized * Vel.magnitude;
                Vel = Vector3.RotateTowards(Vel, want, HomingRate * dt, 0f);
            }
            Vel.y -= Gravity * dt;
            transform.position += Vel * dt;
            if (GroundHug) transform.position = new Vector3(transform.position.x, 0.35f * Size, transform.position.z);
            if (Vel.sqrMagnitude > 0.01f) transform.rotation = Quaternion.LookRotation(Vel);
            if (Style == ProjStyle.Shuriken) transform.rotation *= Quaternion.Euler(0, 0, age * 1400f);
            if (Style == ProjStyle.Snake) transform.position += transform.right * Mathf.Sin(age * 14f) * 0.08f;
            TrailFx();

            if (!Harmless)
            {
                if (CheckProjectileClash()) return;
                if (CheckFighters()) return;
                if (CheckEnvironment()) return;
            }

            if (age >= Life || transform.position.y < -0.2f || !Arena.Inside(transform.position, 2f))
            {
                if (Explode) Detonate();
                OnExpire?.Invoke(this);
                Despawn();
            }
        }

        void TrailFx()
        {
            switch (Style)
            {
                case ProjStyle.Fire:
                case ProjStyle.Meteor:
                    Fx.Fire(transform.position, 0.5f * Size, 2);
                    break;
                case ProjStyle.Orb:
                case ProjStyle.Lightning:
                    Fx.Trail(transform.position, Color, 0.5f * Size);
                    break;
                default:
                    if (Random.value < 0.4f) Fx.Trail(transform.position, Color * 0.7f, 0.25f * Size);
                    break;
            }
        }

        bool CheckProjectileClash()
        {
            for (int i = 0; i < Live.Count; i++)
            {
                var o = Live[i];
                if (o == this || o.Team == Team || o.Harmless || !o.gameObject.activeSelf) continue;
                if ((o.transform.position - transform.position).sqrMagnitude > (Radius + o.Radius) * (Radius + o.Radius)) continue;
                Vector3 mid = (o.transform.position + transform.position) * 0.5f;
                Fx.Parry(mid);
                Sfx.Play(SfxId.Clash, 0.8f);
                if (Mathf.Abs(Power - o.Power) < 0.5f)
                {
                    Fx.Explosion(mid, 1.5f, Color.Lerp(Color, o.Color, 0.5f));
                    o.Despawn(); Despawn();
                }
                else if (Power > o.Power) { Power -= o.Power; o.Despawn(); }
                else { o.Power -= Power; Despawn(); }
                return true;
            }
            return false;
        }

        bool CheckFighters()
        {
            for (int i = 0; i < Fighter.All.Count; i++)
            {
                var f = Fighter.All[i];
                if (!f.Alive || f.Team == Team || hit.Contains(f)) continue;
                if (f.State == FState.Knockdown || f.State == FState.GetUp) continue;
                float d = KMath.PointSegmentDistance(transform.position, f.transform.position + Vector3.up * 0.2f, f.transform.position + Vector3.up * f.Height);
                if (d > Radius + f.BodyRadius) continue;
                hit.Add(f);
                Vector3 dir = KMath.Flat(Vel).normalized;
                if (dir.sqrMagnitude < 0.01f) dir = KMath.Flat(f.transform.position - transform.position).normalized;
                var h = new HitInfo
                {
                    Attacker = Owner, Proj = this, Damage = Damage * (Owner != null ? Owner.DamageMul : 1f), Stamina = 18f, Knockback = Knockback,
                    Launch = Launch, Hitstun = Hitstun, Kind = HitKind.Projectile, Unblockable = Unblockable, Knockdown = Knockdown,
                    Parryable = true, Absorbable = Absorbable, Point = transform.position, Dir = dir, FxColor = Color,
                    Shake = 0.2f + Damage * 0.004f, HitStop = 0.05f
                };
                var res = f.ReceiveHit(ref h);
                if (res == HitResult.Miss || res == HitResult.PerfectDodge) continue;
                if (res == HitResult.Parried)
                {
                    // reflect back at the shooter
                    Team = f.Team; Owner = f; Vel = -Vel * 1.2f; Homing = h.Attacker; HomingRate = 3f; hit.Clear(); hit.Add(f);
                    Sfx.Play(SfxId.Parry, 0.8f, 1.2f);
                    return false;
                }
                if (res == HitResult.Absorbed) { Despawn(); return true; }
                if (res == HitResult.Hit || res == HitResult.Armored) OnHitFighter?.Invoke(this, f);
                if (Explode) Detonate();
                if (!Pierce || res == HitResult.Blocked) { Despawn(); return true; }
            }
            return false;
        }

        bool CheckEnvironment()
        {
            if (Time.frameCount % 3 != 0) return false;
            int n = Physics.OverlapSphereNonAlloc(transform.position, Radius + 0.2f, overlap, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                var d = overlap[i].GetComponentInParent<Destructible>();
                if (d == null || d.Broken) continue;
                d.Damage(Damage * 1.5f, transform.position, Vel.normalized);
                if (Explode) Detonate();
                if (!Pierce) { OnExpire?.Invoke(this); Despawn(); return true; }
                return false;
            }
            if (transform.position.y <= 0.15f && Explode && !GroundHug) { Detonate(); Despawn(); return true; }
            return false;
        }

        public void Detonate()
        {
            Explode = false;
            Vector3 p = transform.position;
            Fx.Explosion(p, ExplodeRadius * 0.8f, Color);
            Sfx.Play(SfxId.Explosion, 0.9f);
            CameraRig.Shake(0.35f);
            Combat.AreaDamage(Owner, Team, p, ExplodeRadius, Damage * 0.6f, Knockback + 3f, 4f, true, Color, Absorbable);
            Destructible.ImpactAt(p, ExplodeRadius, Damage * 2f, Owner);
        }

        static Mesh MeshFor(ProjStyle s)
        {
            if (meshes.TryGetValue(s, out var m)) return m;
            var mb = new MeshBuilder();
            mb.SetColor(new Color32(255, 255, 255, 255));
            switch (s)
            {
                case ProjStyle.Rod:
                    mb.Box(Vector3.zero, new Vector3(0.05f, 0.05f, 1.1f));
                    mb.Box(new Vector3(0, 0, 0.35f), new Vector3(0.09f, 0.09f, 0.06f));
                    break;
                case ProjStyle.Kunai:
                    mb.Cone(new Vector3(0, 0, -0.1f), new Vector3(0, 0, 0.3f), 0.06f, 4);
                    mb.Box(new Vector3(0, 0, -0.22f), new Vector3(0.03f, 0.03f, 0.25f));
                    break;
                case ProjStyle.Spear:
                    mb.Cone(new Vector3(0, 0, -0.9f), new Vector3(0, 0, 0.9f), 0.12f, 6);
                    break;
                case ProjStyle.Snake:
                    mb.Cone(new Vector3(0, 0, -0.1f), new Vector3(0, 0, 0.45f), 0.2f, 6);
                    for (int i = 1; i < 6; i++) mb.Sphere(new Vector3(Mathf.Sin(i) * 0.12f, 0, -0.2f * i), 0.17f - i * 0.02f, 4, 6);
                    break;
                case ProjStyle.Shuriken:
                    for (int i = 0; i < 4; i++)
                    {
                        float a = i * Mathf.PI * 0.5f;
                        Vector3 d = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0);
                        mb.Cone(Vector3.zero, d * 0.9f, 0.18f, 4);
                    }
                    mb.Sphere(Vector3.zero, 0.35f, 6, 8);
                    break;
                default:
                    mb.Sphere(Vector3.zero, 0.4f, 7, 10);
                    break;
            }
            m = mb.ToMesh("proj_" + s);
            meshes[s] = m;
            return m;
        }
    }

    /// <summary>Static helpers shared by kits and AI.</summary>
    public static class Combat
    {
        public static int AttackTokensInUse;

        public static void AreaDamage(Fighter owner, Team team, Vector3 center, float radius, float damage, float knock, float launch, bool knockdown, Color color, bool absorbable = true, HitKind kind = HitKind.Technique)
        {
            GrassBend.Impact(new Vector3(center.x, 0f, center.z), radius * 0.3f, radius, 1.6f, center.y < 2.5f ? 1f : 0.5f);
            for (int i = 0; i < Fighter.All.Count; i++)
            {
                var f = Fighter.All[i];
                if (!f.Alive || f.Team == team) continue;
                Vector3 d = f.Center - center;
                if (d.magnitude > radius + f.BodyRadius) continue;
                Vector3 dir = KMath.Flat(d).normalized;
                if (dir.sqrMagnitude < 0.01f) dir = Vector3.forward;
                var h = new HitInfo
                {
                    Attacker = owner, Damage = damage * (owner != null ? owner.DamageMul : 1f), Stamina = 25f, Knockback = knock, Launch = launch,
                    Hitstun = 0.6f, Kind = kind, Knockdown = knockdown, Parryable = false, Absorbable = absorbable,
                    Point = f.Center, Dir = dir, FxColor = color, Shake = 0.3f, HitStop = 0.06f
                };
                f.ReceiveHit(ref h);
            }
        }

        public static Fighter Nearest(Vector3 from, Team notTeam, float maxDist = 999f, Fighter exclude = null)
        {
            Fighter best = null;
            float bd = maxDist * maxDist;
            for (int i = 0; i < Fighter.All.Count; i++)
            {
                var f = Fighter.All[i];
                if (!f.Alive || f.Team == notTeam || f == exclude || f.Team == Team.Neutral) continue;
                float d = (f.transform.position - from).sqrMagnitude;
                if (d < bd) { bd = d; best = f; }
            }
            return best;
        }

        public static int CountAlive(Team t, bool excludeClones = true)
        {
            int n = 0;
            for (int i = 0; i < Fighter.All.Count; i++)
            {
                var f = Fighter.All[i];
                if (f.Alive && f.Team == t && (!excludeClones || !f.IsClone)) n++;
            }
            return n;
        }
    }
}
