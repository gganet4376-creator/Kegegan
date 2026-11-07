using UnityEngine;

namespace Kagegan
{
    /// <summary>
    /// Fighting-game camera: views the duel from the side (perpendicular to the player-target axis),
    /// zooms with distance, never flips sides abruptly, adds trauma-based shake and cinematic close-ups.
    /// </summary>
    public class CameraRig : MonoBehaviour
    {
        public static CameraRig Inst;
        public Camera Cam;
        public Transform Player, Target;
        Vector3 side = Vector3.back;
        Vector3 pos, look;
        float trauma;
        float cineUntil, cineDist, cineHeight;
        Vector3 cineFocus, cineDir;
        bool orbit;
        float orbitAngle;
        public bool MenuMode;
        /// <summary>Side-on fighting-game camera along the arena lane (never orbits).</summary>
        public static bool LaneMode = true;
        public Vector3 MenuFocus;
        public float MenuDistance = 4.2f;

        public static CameraRig Create()
        {
            var go = new GameObject("MainCamera");
            go.tag = "MainCamera";
            var cam = go.AddComponent<Camera>();
            cam.fieldOfView = 50f;
            cam.nearClipPlane = 0.2f;
            cam.farClipPlane = 160f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.75f, 0.85f, 0.95f);
            cam.allowHDR = false;
            cam.allowMSAA = true;
            go.AddComponent<AudioListener>();
            var rig = go.AddComponent<CameraRig>();
            rig.Cam = cam;
            Inst = rig;
            return rig;
        }

        public static void Shake(float amount) { if (Inst != null) Inst.trauma = Mathf.Min(1f, Inst.trauma + amount); }

        public static void Cinematic(Vector3 focus, Vector3 viewDir, float distance, float height, float seconds)
        {
            if (Inst == null) return;
            Inst.cineFocus = focus; Inst.cineDir = viewDir.sqrMagnitude > 0.01f ? viewDir.normalized : Vector3.back;
            Inst.cineDist = distance; Inst.cineHeight = height;
            Inst.cineUntil = Time.unscaledTime + seconds;
            Inst.orbit = false;
        }

        public static void Orbit(Vector3 focus, float distance, float height, float seconds)
        {
            Cinematic(focus, Vector3.back, distance, height, seconds);
            if (Inst != null) { Inst.orbit = true; Inst.orbitAngle = 0f; }
        }

        public static void EndCinematic() { if (Inst != null) Inst.cineUntil = 0f; }

        public void Snap()
        {
            ComputeTarget(out pos, out look);
            transform.position = pos;
            transform.LookAt(look);
        }

        void ComputeTarget(out Vector3 p, out Vector3 l)
        {
            if (MenuMode)
            {
                l = MenuFocus + Vector3.up * 1.05f;
                p = MenuFocus + new Vector3(0.9f, 1.5f, MenuDistance);
                return;
            }
            if (Time.unscaledTime < cineUntil)
            {
                if (orbit)
                {
                    orbitAngle += Time.unscaledDeltaTime * 18f;
                    cineDir = Quaternion.Euler(0, orbitAngle, 0) * Vector3.back;
                }
                l = cineFocus;
                p = cineFocus + cineDir * cineDist + Vector3.up * cineHeight;
                return;
            }
            if (Player == null) { p = transform.position; l = transform.position + transform.forward; return; }
            if (LaneMode) { LaneTarget(out p, out l); return; }

            Vector3 a = Player.position;
            Vector3 b = Target != null ? Target.position : a + KMath.Flat(Player.forward) * 3f;
            Vector3 axis = KMath.Flat(b - a);
            float dist = axis.magnitude;
            if (dist > 0.3f && Target != null)
            {
                Vector3 perp = Vector3.Cross(Vector3.up, axis / dist);
                if (Vector3.Dot(perp, side) < 0f) perp = -perp;
                side = Vector3.Slerp(side, perp, 1f - Mathf.Exp(-2.2f * Time.deltaTime)).normalized;
            }
            Vector3 mid = Target != null ? Vector3.Lerp(a, b, 0.42f) : a;
            float d = Target != null ? Mathf.Clamp(5.2f + dist * 0.55f, 6f, 13f) : 7f;
            // air combos: rise and pull back with the highest fighter so both stay in frame
            float top = Mathf.Max(a.y, Target != null ? b.y : 0f);
            float yLift = Mathf.Max(0f, top * 0.6f);
            d += Mathf.Max(0f, top - 1f) * 0.6f;
            l = mid + Vector3.up * (1.15f + yLift);
            p = mid + side * d + Vector3.up * (2.0f + d * 0.13f + yLift);
            // keep camera inside the arena walls
            Vector3 flat = KMath.Flat(p);
            if (flat.magnitude > 24f) { flat = flat.normalized * 24f; p = new Vector3(flat.x, p.y, flat.z); }
        }

        static readonly RaycastHit[] occHits = new RaycastHit[12];
        float occTimer;
        public static float ShakeScale = 1f;

        /// <summary>Fade breakable props (trees, walls, pillars) that sit between the camera and the fighters.</summary>
        void FadeOccluders()
        {
            occTimer -= Time.unscaledDeltaTime;
            if (occTimer > 0f) return;
            occTimer = 0.08f;
            CheckLine(Player);
            CheckLine(Target);
        }

        void CheckLine(Transform t)
        {
            if (t == null) return;
            Vector3 from = transform.position;
            Vector3 to = t.position + Vector3.up * 1.1f;
            Vector3 d = to - from;
            float len = d.magnitude;
            if (len < 0.5f) return;
            int n = Physics.SphereCastNonAlloc(from, 0.45f, d / len, occHits, len - 0.6f, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                var dz = occHits[i].collider.GetComponentInParent<Destructible>();
                if (dz != null && !dz.Broken) dz.RequestFade(0.3f);
            }
        }

        void LateUpdate()
        {
            ComputeTarget(out Vector3 tp, out Vector3 tl);
            float dt = Time.unscaledDeltaTime;
            float rate = Time.unscaledTime < cineUntil ? 6f : (LaneMode ? 4f : 5f);
            pos = KMath.Damp(pos, tp, rate, dt);
            look = KMath.Damp(look, tl, rate * 1.4f, dt);
            transform.position = pos;
            transform.rotation = Quaternion.LookRotation(look - pos);

            FadeOccluders();
            if (trauma > 0f)
            {
                float s = trauma * trauma * ShakeScale;
                float t = Time.unscaledTime * 32f;
                transform.position += transform.right * (Mathf.PerlinNoise(t, 0.1f) - 0.5f) * 0.6f * s
                                    + transform.up * (Mathf.PerlinNoise(0.3f, t) - 0.5f) * 0.6f * s;
                transform.rotation *= Quaternion.Euler(0, 0, (Mathf.PerlinNoise(t, t) - 0.5f) * 6f * s);
                trauma = Mathf.Max(0f, trauma - dt * 1.6f);
            }
        }

        void LaneTarget(out Vector3 p, out Vector3 l)
        {
            Vector3 a = Player.position;
            bool hasT = Target != null && Mathf.Abs(Target.position.x - a.x) < 13f;
            Vector3 b = hasT ? Target.position : a + Vector3.right * (Player.forward.x >= 0f ? 2.5f : -2.5f);
            Vector3 mid = Vector3.Lerp(a, b, 0.5f);
            float dx = Mathf.Abs(b.x - a.x), dz = Mathf.Abs(b.z - a.z);
            float d = Mathf.Clamp(5.4f + dx * 0.5f + dz * 0.35f, 6.4f, 11.5f);
            // air combos: rise and pull back so both fighters stay in frame
            float top = Mathf.Max(a.y, b.y);
            float lift = Mathf.Max(0f, top * 0.55f);
            d += Mathf.Max(0f, top - 1.2f) * 0.8f;
            float cx = Mathf.Clamp(mid.x, -Arena.LaneHalf + 2f, Arena.LaneHalf - 2f);
            p = new Vector3(cx, 1.45f + d * 0.11f + lift, mid.z * 0.5f - d);
            l = new Vector3(cx, 1.1f + lift, mid.z * 0.5f + 1.5f);
        }

        /// <summary>Camera-relative movement on the ground plane.</summary>
        public Vector3 ToWorld(Vector2 stick)
        {
            if (LaneMode && !MenuMode) return new Vector3(stick.x, 0f, stick.y * 0.75f);
            Vector3 f = KMath.Flat(transform.forward).normalized;
            Vector3 r = KMath.Flat(transform.right).normalized;
            return r * stick.x + f * stick.y;
        }
    }
}
