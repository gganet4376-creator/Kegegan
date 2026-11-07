using UnityEngine;

namespace Kagegan
{
    public enum B
    {
        Hips, Spine, Chest, Neck, Head,
        UArmL, LArmL, HandL, UArmR, LArmR, HandR,
        ThighL, ShinL, FootL, ThighR, ShinR, FootR,
        EyeL, EyeR, LidL, LidR, Count
    }

    public enum HairStyle { Spiky, LongSpiky, LongStraight, Slick, Hood, LongWhite, Short }

    /// <summary>Appearance description for a procedural anime fighter.</summary>
    public class Look
    {
        public Color skin = new Color(1f, 0.86f, 0.74f);
        public Color top = Color.white, sleeve = Color.white, top2 = Color.gray;
        public Color pants = new Color(0.15f, 0.15f, 0.2f), shoes = new Color(0.12f, 0.1f, 0.1f);
        public Color hair = Color.black, eye = new Color(0.2f, 0.4f, 0.9f), accent = Color.red;
        public HairStyle hairStyle = HairStyle.Spiky;
        public float height = 1f, bulk = 1f, eyeGlow = 0.25f;
        public bool headband, cape, horns, robe, armor, mask, palmEyes, scarf, bareForearms = true, longSleeves;
        public float hairLength = 1f;
        public Color outline = new Color(0.07f, 0.05f, 0.09f);
    }

    /// <summary>
    /// Procedural humanoid: a 21-bone hierarchy plus ONE SkinnedMeshRenderer built from primitives
    /// (1 bone per vertex). Characters therefore cost one draw call (+1 for the outline on Medium/High).
    /// </summary>
    public class HumanoidRig : MonoBehaviour
    {
        public Transform[] Bones = new Transform[(int)B.Count];
        public Quaternion[] RestRot = new Quaternion[(int)B.Count];
        public Vector3 HipsRestPos;
        public SkinnedMeshRenderer Smr;
        public Look Look;
        MaterialPropertyBlock mpb;
        Material[] withOutline, noOutline;
        static readonly int IdFlash = Shader.PropertyToID("_Flash");
        static readonly int IdFlashColor = Shader.PropertyToID("_FlashColor");
        static readonly int IdEmit = Shader.PropertyToID("_Emit");
        static readonly int IdEmitColor = Shader.PropertyToID("_EmitColor");
        static readonly int IdOutlineColor = Shader.PropertyToID("_OutlineColor");

        public Transform this[B b] => Bones[(int)b];

        public static HumanoidRig Build(Transform parent, Look look, string name)
        {
            var root = new GameObject(name + "_Rig");
            root.transform.SetParent(parent, false);
            var rig = root.AddComponent<HumanoidRig>();
            rig.Look = look;
            rig.Create(look);
            return rig;
        }

        Transform MakeBone(B b, B parentBone, Vector3 localPos, bool rootParent = false)
        {
            var t = new GameObject(b.ToString()).transform;
            t.SetParent(rootParent ? transform : Bones[(int)parentBone], false);
            t.localPosition = localPos;
            Bones[(int)b] = t;
            return t;
        }

        void Create(Look L)
        {
            float h = L.height, w = L.bulk;
            // ----- skeleton (rest pose: arms down, facing +Z) -----
            MakeBone(B.Hips, B.Hips, new Vector3(0, 0.95f * h, 0), true);
            MakeBone(B.Spine, B.Hips, new Vector3(0, 0.1f * h, 0));
            MakeBone(B.Chest, B.Spine, new Vector3(0, 0.2f * h, 0));
            MakeBone(B.Neck, B.Chest, new Vector3(0, 0.19f * h, 0));
            MakeBone(B.Head, B.Neck, new Vector3(0, 0.07f * h, 0));
            MakeBone(B.UArmL, B.Chest, new Vector3(-0.2f * w, 0.14f * h, 0));
            MakeBone(B.LArmL, B.UArmL, new Vector3(0, -0.28f * h, 0));
            MakeBone(B.HandL, B.LArmL, new Vector3(0, -0.25f * h, 0));
            MakeBone(B.UArmR, B.Chest, new Vector3(0.2f * w, 0.14f * h, 0));
            MakeBone(B.LArmR, B.UArmR, new Vector3(0, -0.28f * h, 0));
            MakeBone(B.HandR, B.LArmR, new Vector3(0, -0.25f * h, 0));
            MakeBone(B.ThighL, B.Hips, new Vector3(-0.095f * w, -0.06f * h, 0));
            MakeBone(B.ShinL, B.ThighL, new Vector3(0, -0.43f * h, 0));
            MakeBone(B.FootL, B.ShinL, new Vector3(0, -0.41f * h, 0));
            MakeBone(B.ThighR, B.Hips, new Vector3(0.095f * w, -0.06f * h, 0));
            MakeBone(B.ShinR, B.ThighR, new Vector3(0, -0.43f * h, 0));
            MakeBone(B.FootR, B.ShinR, new Vector3(0, -0.41f * h, 0));
            MakeBone(B.EyeL, B.Head, new Vector3(-0.047f, 0.122f, 0.098f));
            MakeBone(B.EyeR, B.Head, new Vector3(0.047f, 0.122f, 0.098f));
            MakeBone(B.LidL, B.Head, new Vector3(-0.047f, 0.157f, 0.104f));
            MakeBone(B.LidR, B.Head, new Vector3(0.047f, 0.157f, 0.104f));
            for (int i = 0; i < Bones.Length; i++) RestRot[i] = Bones[i].localRotation;
            HipsRestPos = Bones[(int)B.Hips].localPosition;

            // ----- mesh -----
            var mb = new MeshBuilder();
            Color32 skin = Mat.C(L.skin), top = Mat.C(L.top), sleeve = Mat.C(L.sleeve), top2 = Mat.C(L.top2);
            Color32 pants = Mat.C(L.pants), shoes = Mat.C(L.shoes), hair = Mat.C(L.hair), accent = Mat.C(L.accent);
            Color32 dark = Mat.C(0.08f, 0.06f, 0.08f), white = Mat.C(1f, 1f, 1f, 0.15f);

            // pelvis + belt
            mb.SetBone((int)B.Hips).SetColor(pants);
            mb.Limb(new Vector3(0, 0.04f, 0), new Vector3(0, -0.07f, 0), 0.145f * w, 0.14f * w, 8, true, 0.75f);
            mb.SetColor(top2);
            mb.Limb(new Vector3(0, 0.06f, 0), new Vector3(0, 0.02f, 0), 0.155f * w, 0.155f * w, 8, false, 0.78f);
            if (L.robe)
            {
                mb.SetColor(top);
                mb.Cone(new Vector3(0, -0.78f * h, 0), new Vector3(0, 0.25f, 0), 0.36f * w, 10, true);
                mb.SetColor(accent);
                mb.Limb(new Vector3(0, -0.74f * h, 0), new Vector3(0, -0.78f * h, 0), 0.355f * w, 0.36f * w, 10, false);
            }

            // torso
            mb.SetBone((int)B.Spine).SetColor(top);
            mb.Limb(Vector3.zero, new Vector3(0, 0.21f * h, 0), 0.13f * w, 0.15f * w, 8, true, 0.72f);
            mb.SetBone((int)B.Chest).SetColor(top);
            mb.Limb(Vector3.zero, new Vector3(0, 0.15f * h, 0), 0.165f * w, 0.13f * w, 8, true, 0.7f);
            mb.Sphere(new Vector3(-0.17f * w, 0.135f * h, 0), 0.075f * w, 4, 8);
            mb.Sphere(new Vector3(0.17f * w, 0.135f * h, 0), 0.075f * w, 4, 8);
            mb.SetColor(top2); // collar
            mb.Limb(new Vector3(0, 0.15f * h, 0), new Vector3(0, 0.19f * h, 0), 0.085f, 0.075f, 8, false);
            if (L.armor)
            {
                mb.SetColor(accent);
                mb.Box(new Vector3(0, 0.07f * h, 0.105f * w), new Vector3(0.26f * w, 0.2f * h, 0.05f), Quaternion.Euler(-6, 0, 0));
                mb.Box(new Vector3(0, -0.12f * h, 0.1f * w), new Vector3(0.24f * w, 0.12f * h, 0.05f));
            }
            if (L.cape)
            {
                mb.SetColor(top2);
                mb.Box(new Vector3(0, -0.2f * h, -0.14f * w), new Vector3(0.36f * w, 0.75f * h, 0.025f), Quaternion.Euler(8, 0, 0));
            }
            if (L.scarf)
            {
                mb.SetColor(accent);
                mb.Limb(new Vector3(0, 0.18f * h, 0), new Vector3(0, 0.2f * h, 0), 0.11f, 0.1f, 8, false);
                mb.Box(new Vector3(0.05f, 0.02f * h, -0.14f), new Vector3(0.08f, 0.32f, 0.02f), Quaternion.Euler(15, 0, 10));
            }

            // neck + head
            mb.SetBone((int)B.Neck).SetColor(skin);
            mb.Limb(Vector3.zero, new Vector3(0, 0.09f, 0), 0.048f, 0.045f, 6, false);
            mb.SetBone((int)B.Head).SetColor(skin);
            mb.Sphere(new Vector3(0, 0.12f, 0.005f), new Vector3(0.112f, 0.13f, 0.105f), 8, 12);
            mb.Sphere(new Vector3(0, 0.05f, 0.05f), new Vector3(0.07f, 0.06f, 0.07f), 4, 8); // jaw
            // eye whites
            mb.SetColor(white);
            mb.Sphere(new Vector3(-0.047f, 0.122f, 0.1f), new Vector3(0.03f, 0.033f, 0.016f), 4, 8);
            mb.Sphere(new Vector3(0.047f, 0.122f, 0.1f), new Vector3(0.03f, 0.033f, 0.016f), 4, 8);
            // brows + mouth
            mb.SetColor(Mat.C(L.hair * 0.6f));
            mb.Box(new Vector3(-0.05f, 0.168f, 0.106f), new Vector3(0.05f, 0.009f, 0.012f), Quaternion.Euler(0, 0, -12));
            mb.Box(new Vector3(0.05f, 0.168f, 0.106f), new Vector3(0.05f, 0.009f, 0.012f), Quaternion.Euler(0, 0, 12));
            mb.SetColor(dark);
            mb.Box(new Vector3(0, 0.055f, 0.121f), new Vector3(0.032f, 0.005f, 0.006f));
            if (L.mask)
            {
                mb.SetColor(top2);
                mb.Sphere(new Vector3(0, 0.06f, 0.035f), new Vector3(0.105f, 0.065f, 0.095f), 4, 10);
            }
            BuildHair(mb, L);
            if (L.headband)
            {
                mb.SetColor(top2);
                mb.Sphere(new Vector3(0, 0.19f, 0.0f), new Vector3(0.125f, 0.024f, 0.13f), 3, 12);
                mb.SetColor(Mat.C(0.75f, 0.78f, 0.82f, 0.1f));
                mb.Box(new Vector3(0, 0.19f, 0.124f), new Vector3(0.1f, 0.04f, 0.012f));
                mb.SetColor(top2);
                mb.Box(new Vector3(0.03f, 0.15f, -0.14f), new Vector3(0.03f, 0.14f, 0.01f), Quaternion.Euler(20, 0, 15));
                mb.Box(new Vector3(-0.03f, 0.14f, -0.14f), new Vector3(0.03f, 0.16f, 0.01f), Quaternion.Euler(25, 0, -10));
            }
            if (L.horns)
            {
                mb.SetColor(Mat.C(0.95f, 0.93f, 0.9f));
                mb.Cone(new Vector3(-0.06f, 0.22f, 0.02f), new Vector3(-0.1f, 0.36f, -0.05f), 0.022f, 6);
                mb.Cone(new Vector3(0.06f, 0.22f, 0.02f), new Vector3(0.1f, 0.36f, -0.05f), 0.022f, 6);
            }

            // eyes (iris on eye bones so they can move)
            Color32 iris = Mat.C(L.eye, L.eyeGlow);
            mb.SetBone((int)B.EyeL).SetColor(iris);
            mb.Sphere(new Vector3(0, 0, 0.011f), new Vector3(0.017f, 0.022f, 0.008f), 3, 6);
            mb.SetBone((int)B.EyeR).SetColor(iris);
            mb.Sphere(new Vector3(0, 0, 0.011f), new Vector3(0.017f, 0.022f, 0.008f), 3, 6);
            // eyelids: skin caps hanging from the lid bone, scaled on Y to blink
            mb.SetBone((int)B.LidL).SetColor(skin);
            mb.Sphere(new Vector3(0, -0.033f, 0.002f), new Vector3(0.034f, 0.036f, 0.019f), 4, 8);
            mb.SetBone((int)B.LidR).SetColor(skin);
            mb.Sphere(new Vector3(0, -0.033f, 0.002f), new Vector3(0.034f, 0.036f, 0.019f), 4, 8);

            // arms
            for (int side = 0; side < 2; side++)
            {
                B ua = side == 0 ? B.UArmL : B.UArmR, la = side == 0 ? B.LArmL : B.LArmR, hand = side == 0 ? B.HandL : B.HandR;
                mb.SetBone((int)ua).SetColor(sleeve);
                mb.Limb(Vector3.zero, new Vector3(0, -0.27f * h, 0), 0.057f * w, 0.047f * w, 7, true);
                if (L.armor)
                {
                    mb.SetColor(accent);
                    mb.Sphere(new Vector3(0, -0.02f, 0), new Vector3(0.09f * w, 0.06f, 0.09f * w), 4, 8);
                }
                mb.SetBone((int)la).SetColor(L.longSleeves ? sleeve : (L.bareForearms ? skin : top2));
                mb.Limb(Vector3.zero, new Vector3(0, -0.24f * h, 0), 0.046f * w, 0.036f * w, 7, true);
                if (L.longSleeves)
                {
                    mb.SetColor(sleeve);
                    mb.Cone(new Vector3(0, -0.24f * h, 0), new Vector3(0, -0.02f, 0), 0.085f * w, 8, true);
                }
                mb.SetBone((int)hand).SetColor(skin);
                mb.Sphere(new Vector3(0, -0.05f, 0.005f), new Vector3(0.042f * w, 0.055f, 0.038f * w), 4, 8);
                if (L.palmEyes)
                {
                    mb.SetColor(Mat.C(0.8f, 0.6f, 1f, 1f));
                    mb.Sphere(new Vector3(0, -0.055f, 0.036f), new Vector3(0.018f, 0.022f, 0.008f), 3, 6);
                }
            }

            // legs
            for (int side = 0; side < 2; side++)
            {
                B th = side == 0 ? B.ThighL : B.ThighR, sh = side == 0 ? B.ShinL : B.ShinR, ft = side == 0 ? B.FootL : B.FootR;
                mb.SetBone((int)th).SetColor(pants);
                mb.Limb(Vector3.zero, new Vector3(0, -0.42f * h, 0), 0.078f * w, 0.062f * w, 7, true);
                mb.SetBone((int)sh).SetColor(pants);
                mb.Limb(Vector3.zero, new Vector3(0, -0.39f * h, 0), 0.06f * w, 0.047f * w, 7, true);
                mb.SetColor(top2);
                mb.Limb(new Vector3(0, -0.28f * h, 0), new Vector3(0, -0.38f * h, 0), 0.054f * w, 0.05f * w, 7, false);
                mb.SetBone((int)ft).SetColor(shoes);
                mb.Box(new Vector3(0, -0.025f, 0.045f), new Vector3(0.09f * w, 0.06f, 0.2f));
            }

            // bind poses
            var boneToRoot = new Matrix4x4[Bones.Length];
            var bind = new Matrix4x4[Bones.Length];
            for (int i = 0; i < Bones.Length; i++)
            {
                boneToRoot[i] = transform.worldToLocalMatrix * Bones[i].localToWorldMatrix;
                bind[i] = Bones[i].worldToLocalMatrix * transform.localToWorldMatrix;
            }
            var mesh = mb.ToSkinnedMesh("Fighter", boneToRoot, bind);

            var meshGo = new GameObject("Body");
            meshGo.transform.SetParent(transform, false);
            Smr = meshGo.AddComponent<SkinnedMeshRenderer>();
            Smr.sharedMesh = mesh;
            Smr.bones = Bones;
            Smr.rootBone = Bones[(int)B.Hips];
            Smr.quality = SkinQuality.Bone1;
            Smr.updateWhenOffscreen = false;
            Smr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            Smr.receiveShadows = false;
            Smr.localBounds = new Bounds(new Vector3(0, 0, 0), new Vector3(3f, 3.4f, 3f));
            noOutline = new[] { Mat.Toon };
            withOutline = new[] { Mat.Toon, Mat.Outline };
            ApplyOutline();
            mpb = new MaterialPropertyBlock();
            mpb.SetColor(IdOutlineColor, L.outline);
            mpb.SetColor(IdFlashColor, Color.white);
            mpb.SetFloat("_CharFx", 1f); // back-light edge + toon highlight, fighters only
            Smr.SetPropertyBlock(mpb);
            SetLids(0.1f);
        }

        void BuildHair(MeshBuilder mb, Look L)
        {
            mb.SetBone((int)B.Head).SetColor(Mat.C(L.hair));
            float len = L.hairLength;
            switch (L.hairStyle)
            {
                case HairStyle.Hood:
                    mb.Sphere(new Vector3(0, 0.15f, -0.02f), new Vector3(0.14f, 0.13f, 0.14f), 5, 10);
                    return;
                case HairStyle.Slick:
                    mb.Sphere(new Vector3(0, 0.162f, -0.03f), new Vector3(0.125f, 0.1f, 0.13f), 5, 10);
                    for (int i = 0; i < 4; i++)
                        mb.Cone(new Vector3(-0.05f + i * 0.035f, 0.2f, -0.06f), new Vector3(-0.07f + i * 0.045f, 0.1f, -0.22f * len), 0.035f, 5);
                    return;
                case HairStyle.Short:
                    mb.Sphere(new Vector3(0, 0.165f, -0.02f), new Vector3(0.128f, 0.1f, 0.13f), 5, 10);
                    for (int i = 0; i < 5; i++)
                        mb.Cone(new Vector3(-0.07f + i * 0.035f, 0.22f, 0.06f), new Vector3(-0.08f + i * 0.04f, 0.15f, 0.13f), 0.03f, 5);
                    return;
            }
            // shared cap
            mb.Sphere(new Vector3(0, 0.168f, -0.025f), new Vector3(0.132f, 0.1f, 0.13f), 5, 10);
            if (L.hairStyle == HairStyle.Spiky || L.hairStyle == HairStyle.LongSpiky)
            {
                // spikes radiating up and back
                for (int i = 0; i < 9; i++)
                {
                    float a = (i / 8f - 0.5f) * 2.4f;
                    Vector3 dir = new Vector3(Mathf.Sin(a) * 0.9f, 0.55f + 0.25f * Mathf.Cos(a), -0.6f).normalized;
                    Vector3 b = new Vector3(Mathf.Sin(a) * 0.09f, 0.2f, -0.03f);
                    mb.Cone(b, b + dir * (0.15f + 0.04f * (i % 3)) * len, 0.045f, 5);
                }
                // bangs over forehead
                for (int i = 0; i < 4; i++)
                {
                    float x = -0.06f + i * 0.04f;
                    mb.Cone(new Vector3(x, 0.215f, 0.07f), new Vector3(x * 1.3f, 0.13f, 0.14f), 0.03f, 5);
                }
            }
            if (L.hairStyle == HairStyle.LongSpiky)
            {
                for (int i = 0; i < 7; i++)
                {
                    float x = -0.1f + i * 0.033f;
                    mb.Cone(new Vector3(x, 0.15f, -0.08f), new Vector3(x * 2.2f, -0.45f * len - (i % 2) * 0.08f, -0.2f), 0.06f, 5);
                }
            }
            if (L.hairStyle == HairStyle.LongStraight || L.hairStyle == HairStyle.LongWhite)
            {
                // straight curtain down the back + side locks
                mb.Box(new Vector3(0, -0.2f * len + 0.1f, -0.12f), new Vector3(0.24f, 0.62f * len, 0.05f), Quaternion.Euler(6, 0, 0));
                mb.Box(new Vector3(-0.115f, 0.0f, 0.04f), new Vector3(0.03f, 0.32f, 0.08f));
                mb.Box(new Vector3(0.115f, 0.0f, 0.04f), new Vector3(0.03f, 0.32f, 0.08f));
                for (int i = 0; i < 3; i++)
                {
                    float x = -0.04f + i * 0.04f;
                    mb.Cone(new Vector3(x, 0.22f, 0.07f), new Vector3(x, 0.15f, 0.13f), 0.035f, 5);
                }
                if (L.hairStyle == HairStyle.LongWhite)
                    mb.Cone(new Vector3(0, 0.0f, -0.13f), new Vector3(0, -1.25f * len, -0.3f), 0.15f, 8);
            }
        }

        /// <summary>
        /// Runtime Humanoid avatar for this procedural skeleton so Unity can retarget motion-captured clips onto it.
        /// The reference pose handed to Unity is a T-pose (upper arms raised sideways); everything else is the rest pose.
        /// Returns null if Unity rejects the description (the fighter then keeps its procedural animation).
        /// </summary>
        public Avatar BuildAvatar()
        {
            try
            {
                var map = new (B bone, string human)[]
                {
                    (B.Hips, "Hips"), (B.Spine, "Spine"), (B.Chest, "Chest"), (B.Neck, "Neck"), (B.Head, "Head"),
                    (B.UArmL, "LeftUpperArm"), (B.LArmL, "LeftLowerArm"), (B.HandL, "LeftHand"),
                    (B.UArmR, "RightUpperArm"), (B.LArmR, "RightLowerArm"), (B.HandR, "RightHand"),
                    (B.ThighL, "LeftUpperLeg"), (B.ShinL, "LeftLowerLeg"), (B.FootL, "LeftFoot"),
                    (B.ThighR, "RightUpperLeg"), (B.ShinR, "RightLowerLeg"), (B.FootR, "RightFoot"),
                };
                var human = new HumanBone[map.Length];
                for (int i = 0; i < map.Length; i++)
                {
                    human[i] = new HumanBone { boneName = Bones[(int)map[i].bone].name, humanName = map[i].human };
                    human[i].limit.useDefaultValues = true;
                }
                var skel = new System.Collections.Generic.List<SkeletonBone>();
                skel.Add(new SkeletonBone { name = gameObject.name, position = Vector3.zero, rotation = Quaternion.identity, scale = Vector3.one });
                for (int i = 0; i < Bones.Length; i++)
                {
                    var t = Bones[i];
                    Quaternion r = RestRot[i];
                    if (i == (int)B.UArmL) r = Quaternion.Euler(0f, 0f, -90f);
                    if (i == (int)B.UArmR) r = Quaternion.Euler(0f, 0f, 90f);
                    Vector3 pos = i == (int)B.Hips ? HipsRestPos : t.localPosition;
                    skel.Add(new SkeletonBone { name = t.name, position = pos, rotation = r, scale = Vector3.one });
                }
                var hd = new HumanDescription
                {
                    human = human,
                    skeleton = skel.ToArray(),
                    upperArmTwist = 0.5f, lowerArmTwist = 0.5f, upperLegTwist = 0.5f, lowerLegTwist = 0.5f,
                    armStretch = 0.05f, legStretch = 0.05f, feetSpacing = 0f, hasTranslationDoF = false
                };
                var avatar = AvatarBuilder.BuildHumanAvatar(gameObject, hd);
                if (avatar == null || !avatar.isValid || !avatar.isHuman)
                {
                    Debug.LogWarning("Kagegan: humanoid avatar invalid for " + name + ", using procedural animation.");
                    return null;
                }
                avatar.name = name + "_Avatar";
                return avatar;
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("Kagegan: avatar build failed (" + e.Message + "), using procedural animation.");
                return null;
            }
        }

        public void ApplyOutline()
        {
            if (Smr != null) Smr.sharedMaterials = GraphicsQuality.Outlines ? withOutline : noOutline;
        }

        public void SetFlash(float amount, Color color)
        {
            mpb.SetFloat(IdFlash, amount);
            mpb.SetColor(IdFlashColor, color);
            Smr.SetPropertyBlock(mpb);
        }

        public void SetEmit(float amount, Color color)
        {
            mpb.SetFloat(IdEmit, amount);
            mpb.SetColor(IdEmitColor, color);
            Smr.SetPropertyBlock(mpb);
        }

        public void SetLids(float closed)
        {
            float s = Mathf.Lerp(0.08f, 1.05f, closed);
            Bones[(int)B.LidL].localScale = new Vector3(1, s, 1);
            Bones[(int)B.LidR].localScale = new Vector3(1, s, 1);
        }

        public Vector3 BonePos(B b) => Bones[(int)b].position;

        void OnEnable() { GraphicsQuality.Changed += ApplyOutline; }
        void OnDisable() { GraphicsQuality.Changed -= ApplyOutline; }
    }
}
