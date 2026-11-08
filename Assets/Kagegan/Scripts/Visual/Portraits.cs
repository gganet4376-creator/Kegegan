using System.Collections.Generic;
using UnityEngine;

namespace Kagegan
{
    /// <summary>
    /// Character portraits for the HUD: the procedural model is rendered once into a texture (head-and-shoulders shot)
    /// and cached. Falls back to null (coloured badge) if rendering is not possible.
    /// </summary>
    public static class Portraits
    {
        static readonly Dictionary<string, Texture2D> cache = new Dictionary<string, Texture2D>();

        public static Texture2D Get(string key, Look look)
        {
            if (look == null) return null;
            if (cache.TryGetValue(key, out var t)) return t;
            Texture2D tex = null;
            GameObject stage = null, camGo = null;
            RenderTexture rt = null;
            try
            {
                stage = new GameObject("PortraitStage");
                stage.transform.position = new Vector3(0f, -500f, 0f);
                var rig = HumanoidRig.Build(stage.transform, look, "Portrait");
                rig.transform.localPosition = Vector3.zero;
                rig.transform.localRotation = Quaternion.Euler(0f, -18f, 0f);
                camGo = new GameObject("PortraitCam");
                var cam = camGo.AddComponent<Camera>();
                cam.enabled = false;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
                cam.fieldOfView = 24f;
                cam.nearClipPlane = 0.05f;
                cam.farClipPlane = 10f;
                cam.allowHDR = false;
                cam.allowMSAA = false;
                Vector3 head = rig.BonePos(B.Head) + Vector3.up * 0.07f * look.height;
                // baked models have their own head proportions: frame them by the eyes (same offset as the procedural head)
                if (!string.IsNullOrEmpty(look.model)) head = 0.5f * (rig.BonePos(B.EyeL) + rig.BonePos(B.EyeR)) + Vector3.down * 0.05f;
                cam.transform.position = head + new Vector3(0.18f, 0.03f, 1.15f);
                cam.transform.LookAt(head + Vector3.down * 0.06f);
                rt = RenderTexture.GetTemporary(256, 256, 16, RenderTextureFormat.ARGB32);
                cam.targetTexture = rt;
                cam.Render();
                var prev = RenderTexture.active;
                RenderTexture.active = rt;
                tex = new Texture2D(256, 256, TextureFormat.RGBA32, false) { name = "Portrait_" + key };
                tex.ReadPixels(new Rect(0, 0, 256, 256), 0, 0);
                tex.Apply(false, false);
                RenderTexture.active = prev;
                cam.targetTexture = null;
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("Kagegan: portrait render failed: " + e.Message);
                tex = null;
            }
            finally
            {
                if (rt != null) RenderTexture.ReleaseTemporary(rt);
                if (camGo != null) Object.Destroy(camGo);
                if (stage != null) Object.Destroy(stage);
            }
            cache[key] = tex;
            return tex;
        }
    }
}
