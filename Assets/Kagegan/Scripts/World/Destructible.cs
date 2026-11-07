using System.Collections.Generic;
using UnityEngine;

namespace Kagegan
{
    public enum DestructKind { Tree, Wall, Pillar, Crate, Rock, Lantern, Dummy }

    /// <summary>
    /// Breakable environment piece. Trees and pillars topple (and hurt whoever they land on),
    /// walls/rocks/crates shatter into pooled debris particles with dust.
    /// </summary>
    public class Destructible : MonoBehaviour
    {
        public static readonly List<Destructible> All = new List<Destructible>(64);
        public DestructKind Kind;
        public float MaxHp = 80f, Hp = 80f;
        public Color DebrisColor = new Color(0.5f, 0.45f, 0.4f);
        public float Size = 1f;
        public bool Broken { get; private set; }
        MeshRenderer mr;
        Collider col;
        float wobble;
        Vector3 fallAxis;
        float fallT = -1f;
        Quaternion baseRot;
        float sinkT = -1f;
        Vector3 basePos;

        public static Destructible Create(Transform parent, DestructKind kind, Vector3 pos, Mesh mesh, float hp, Color debris, float size, Collider colliderTemplate = null)
        {
            var go = new GameObject(kind.ToString());
            go.transform.SetParent(parent, false);
            go.transform.position = pos;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var d = go.AddComponent<Destructible>();
            d.mr = go.AddComponent<MeshRenderer>();
            d.mr.sharedMaterial = Mat.Toon;
            d.mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            d.mr.receiveShadows = false;
            d.Kind = kind; d.MaxHp = d.Hp = hp; d.DebrisColor = debris; d.Size = size;
            switch (kind)
            {
                case DestructKind.Tree:
                case DestructKind.Pillar:
                case DestructKind.Lantern:
                case DestructKind.Dummy:
                {
                    var c = go.AddComponent<CapsuleCollider>();
                    c.radius = kind == DestructKind.Pillar ? 0.55f * size : 0.35f * size;
                    c.height = kind == DestructKind.Lantern || kind == DestructKind.Dummy ? 1.8f : 6f * size;
                    c.center = new Vector3(0, c.height * 0.5f, 0);
                    d.col = c;
                    break;
                }
                default:
                {
                    var b = go.AddComponent<BoxCollider>();
                    b.center = mesh.bounds.center;
                    b.size = mesh.bounds.size;
                    d.col = b;
                    break;
                }
            }
            d.baseRot = go.transform.rotation;
            d.basePos = pos;
            All.Add(d);
            return d;
        }

        void OnDestroy() { All.Remove(this); }

        public static void ImpactAt(Vector3 p, float radius, float damage, Fighter source)
        {
            for (int i = 0; i < All.Count; i++)
            {
                var d = All[i];
                if (d.Broken) continue;
                Vector3 c = d.transform.position; c.y = Mathf.Clamp(p.y, c.y, c.y + 3f * d.Size);
                float dist = (KMath.Flat(c - p)).magnitude - 0.4f * d.Size;
                if (dist > radius) continue;
                float falloff = Mathf.Clamp01(1f - dist / Mathf.Max(0.1f, radius)) * 0.6f + 0.4f;
                d.Damage(damage * falloff, p, KMath.Flat(c - p).normalized);
            }
        }

        public void Damage(float amount, Vector3 point, Vector3 dir)
        {
            if (Broken) return;
            Hp -= amount;
            wobble = 1f;
            if (Kind == DestructKind.Tree && amount > 5f) Fx.Leaves(transform.position + Vector3.up * 4f * Size, 4);
            else if (amount > 5f) Fx.Debris(point, DebrisColor, 2, 3f);
            if (Hp <= 0f) Break(dir);
        }

        public void Break(Vector3 dir)
        {
            if (Broken) return;
            Broken = true;
            if (dir.sqrMagnitude < 0.01f) dir = Random.onUnitSphere;
            dir = KMath.Flat(dir).normalized;
            switch (Kind)
            {
                case DestructKind.Tree:
                case DestructKind.Pillar:
                    fallAxis = Vector3.Cross(Vector3.up, dir);
                    fallT = 0f;
                    Sfx.Play(Kind == DestructKind.Tree ? SfxId.Crumble : SfxId.Crumble, 0.8f, Kind == DestructKind.Tree ? 0.8f : 0.6f);
                    Fx.Debris(transform.position + Vector3.up * 0.5f, DebrisColor, GraphicsQuality.DebrisPerBreak, 4f);
                    if (col != null) col.enabled = false;
                    break;
                default:
                    Shatter();
                    break;
            }
        }

        void Shatter()
        {
            Vector3 c = mr.bounds.center;
            Fx.Debris(c, DebrisColor, GraphicsQuality.DebrisPerBreak + 2, 6f);
            Fx.Dust(new Vector3(c.x, 0.2f, c.z), 1.4f * Size, 10);
            if (Kind == DestructKind.Lantern) { Fx.Fire(c, 0.5f, 8); WorldLighting.Flash(c, new Color(1f, 0.7f, 0.3f), 5f, 0.6f); }
            Sfx.Play(SfxId.Crumble, 0.9f);
            CameraRig.Shake(0.15f);
            mr.enabled = false;
            if (col != null) col.enabled = false;
        }

        void Update()
        {
            if (wobble > 0f && fallT < 0f)
            {
                wobble = Mathf.Max(0f, wobble - Time.deltaTime * 3f);
                transform.rotation = baseRot * Quaternion.Euler(Mathf.Sin(Time.time * 40f) * wobble * 2.5f, 0, Mathf.Cos(Time.time * 37f) * wobble * 2.5f);
            }
            if (fallT >= 0f && fallT < 1f)
            {
                fallT += Time.deltaTime / 1.1f;
                float u = Mathf.Clamp01(fallT);
                float e = u * u; // accelerate like a real fall
                transform.rotation = Quaternion.AngleAxis(e * 88f, fallAxis) * baseRot;
                if (fallT >= 1f) Landed();
            }
            if (sinkT >= 0f)
            {
                sinkT += Time.deltaTime;
                if (sinkT > 8f)
                {
                    transform.position = basePos + Vector3.down * (sinkT - 8f) * 0.6f;
                    if (sinkT > 11f) { mr.enabled = false; sinkT = -1f; }
                }
            }
        }

        void Landed()
        {
            Vector3 dir = Vector3.Cross(fallAxis, Vector3.up);
            float len = (Kind == DestructKind.Tree ? 6f : 5f) * Size;
            Vector3 tip = transform.position + dir * len;
            for (float s = 1.5f; s <= len; s += 1.5f) Fx.Dust(transform.position + dir * s, 1f, 3);
            if (Kind == DestructKind.Tree) Fx.Leaves(tip, 14);
            else Fx.Debris(tip, DebrisColor, GraphicsQuality.DebrisPerBreak, 5f);
            Sfx.Play(SfxId.HeavyHit, 1f, 0.6f);
            CameraRig.Shake(0.3f);
            // anything under the trunk takes damage
            for (int i = 0; i < Fighter.All.Count; i++)
            {
                var f = Fighter.All[i];
                if (!f.Alive) continue;
                float d = KMath.PointSegmentDistance(KMath.Flat(f.transform.position), KMath.Flat(transform.position), KMath.Flat(tip));
                if (d < 0.9f)
                {
                    var h = new HitInfo { Damage = 30f, Stamina = 30f, Knockback = 3f, Launch = 3f, Kind = HitKind.Environment, Knockdown = true, Point = f.Center, Dir = dir, FxColor = DebrisColor, Shake = 0.3f, HitStop = 0.05f };
                    f.ReceiveHit(ref h);
                }
            }
            sinkT = 0f;
        }

        public void Restore()
        {
            Broken = false; Hp = MaxHp; fallT = -1f; sinkT = -1f; wobble = 0f;
            transform.SetPositionAndRotation(basePos, baseRot);
            mr.enabled = true;
            if (col != null) col.enabled = true;
        }
    }

    /// <summary>Cheap soft circular shadow under fighters (replaces real-time shadow maps on all presets).</summary>
    public static class BlobShadow
    {
        static Mesh quad;
        static Material mat;

        public static Transform Create(Transform owner, float size)
        {
            if (quad == null)
            {
                var mb = new MeshBuilder();
                mb.SetColor(new Color32(0, 0, 0, 150));
                mb.Quad(Vector3.zero, Vector3.right * 0.5f, Vector3.back * 0.5f);
                quad = mb.ToMesh("blob");
                mat = new Material(Mat.FxAlpha) { name = "Blob", renderQueue = 2460 };
            }
            var go = new GameObject("BlobShadow");
            go.AddComponent<MeshFilter>().sharedMesh = quad;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            go.transform.localScale = Vector3.one * size;
            var link = owner.gameObject.AddComponent<BlobLink>();
            link.blob = go;
            return go.transform;
        }

        public static void Track(Transform blob, Vector3 ownerPos, float size)
        {
            float h = Mathf.Max(0f, ownerPos.y);
            blob.position = new Vector3(ownerPos.x, 0.025f, ownerPos.z);
            float s = Mathf.Clamp(1f - h * 0.12f, 0.4f, 1f);
            blob.localScale = new Vector3(s, 1f, s) * size;
        }
    }

    /// <summary>Destroys the detached blob shadow together with its owner.</summary>
    public class BlobLink : MonoBehaviour
    {
        public GameObject blob;
        void OnDestroy() { if (blob != null) Destroy(blob); }
        void OnDisable() { if (blob != null) blob.SetActive(false); }
        void OnEnable() { if (blob != null) blob.SetActive(true); }
    }
}
