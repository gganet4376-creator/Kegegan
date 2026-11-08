using System.Collections.Generic;
using UnityEngine;

namespace Kagegan
{
    /// <summary>Temporarily fades scenery intersecting a camera sight line, including models without colliders.</summary>
    public class CameraOccluder : MonoBehaviour
    {
        static readonly List<CameraOccluder> Props = new List<CameraOccluder>();
        static readonly int FadeId = Shader.PropertyToID("_Fade");
        static MaterialPropertyBlock block;

        Renderer[] renderers;
        Destructible destructible;
        float fade = 1f, holdUntil;

        public void Init(Renderer[] items, Destructible owner = null)
        {
            renderers = items;
            destructible = owner;
            Props.Add(this);
            enabled = false;
        }

        void OnDestroy() => Props.Remove(this);

        public static void CheckLine(Vector3 from, Vector3 to)
        {
            Vector3 direction = to - from;
            float length = direction.magnitude - 0.35f;
            if (length <= 0f) return;
            var ray = new Ray(from, direction.normalized);

            foreach (var prop in Props)
            {
                if (!prop.gameObject.activeInHierarchy || (prop.destructible != null && prop.destructible.Broken)) continue;
                foreach (var renderer in prop.renderers)
                {
                    if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;
                    Bounds bounds = renderer.bounds;
                    bounds.Expand(0.32f);
                    if (!bounds.Contains(from) && (!bounds.IntersectRay(ray, out float distance) || distance > length)) continue;
                    prop.holdUntil = Time.unscaledTime + 0.18f;
                    prop.enabled = true;
                    break;
                }
            }
        }

        void LateUpdate()
        {
            // fully hidden while it stands between the camera and a fighter: nothing may cover the fight
            float target = Time.unscaledTime < holdUntil ? 0f : 1f;
            float next = Mathf.MoveTowards(fade, target, Time.unscaledDeltaTime * (target < fade ? 10f : 4f));
            SetFade(next);
            if (fade == 1f && target == 1f) enabled = false;
        }

        public void ResetFade()
        {
            holdUntil = 0f;
            SetFade(1f);
            enabled = false;
        }

        void SetFade(float value)
        {
            if (fade == value) return;
            fade = value;
            if (block == null) block = new MaterialPropertyBlock();
            foreach (var renderer in renderers)
            {
                if (renderer == null) continue;
                // fully visible again: drop the block so SRP batching/instancing works as before
                if (fade >= 1f) { renderer.SetPropertyBlock(null); continue; }
                renderer.GetPropertyBlock(block);
                block.SetFloat(FadeId, fade);
                renderer.SetPropertyBlock(block);
            }
        }
    }
}
