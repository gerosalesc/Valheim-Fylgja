using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace StandsOfValheim
{
    /// <summary>
    /// Builds Aurvandil's Fylgja's custom armor and accessories rigged onto the Fylgja's humanoid skeleton:
    /// dark forged-gold shoulder pads, waist belt, knee pads, and circlet; crimson neck scarf;
    /// matte light-grey loincloth; and black combat boots and gloves.
    /// </summary>
    internal static class StandEquipmentBuilder
    {
        public static readonly Color GoldArmorColor      = new Color(0.62f, 0.41f, 0.10f, 1.0f);
        public static readonly Color GoldCircletTint     = new Color(0.76f, 0.52f, 0.14f, 1.0f);
        public static readonly Color GoldEmission        = new Color(0.025f, 0.015f, 0.002f);
        public static readonly Color ScarfRedColor       = new Color(0.82f, 0.11f, 0.12f, 1.0f);
        public static readonly Color ScarfEmission       = new Color(0.08f, 0.01f, 0.01f);
        public static readonly Color LeatherBlackColor   = new Color(0.07f, 0.07f, 0.09f, 1.0f);
        public static readonly Color LeatherEmission     = new Color(0.02f, 0.01f, 0.04f);
        public static readonly Color LoinclothWhiteColor = new Color(0.68f, 0.68f, 0.72f, 1.0f);
        public static readonly Color LoinclothEmission   = Color.black;
        public static readonly Color HairBlackColor      = new Color(0.055f, 0.045f, 0.075f, 1.0f);
        public static readonly Color HairEmission        = new Color(0.016f, 0.010f, 0.028f);

        private static Material s_goldMaterial;
        private static Material s_scarfMaterial;
        private static Material s_leatherBlackMaterial;
        private static Material s_loinclothMaterial;
        private static Material s_hairMaterial;
        private static Material s_depthWriteMaterial;

        internal const int DepthPrepassQueue = 3001;
        internal const int SpectralTransparentQueue = 3002;

        public static void InvalidateMaterials()
        {
            s_goldMaterial = null;
            s_scarfMaterial = null;
            s_leatherBlackMaterial = null;
            s_loinclothMaterial = null;
            s_hairMaterial = null;
        }

        public static void AttachAurvandilsFylgjaHair(GameObject visual, Transform headBone, Transform neckBone, GameObject circlet)
        {
            if (visual == null || headBone == null) return;

            float scale = GetScaleFactor(visual);
            Vector3 headUp = visual.transform.up;
            if (neckBone != null && (headBone.position - neckBone.position).sqrMagnitude > 0.0001f)
            {
                headUp = Vector3.Slerp(visual.transform.up, (headBone.position - neckBone.position).normalized, 0.45f).normalized;
            }
            Vector3 headFwd = Vector3.ProjectOnPlane(visual.transform.forward, headUp);
            if (headFwd.sqrMagnitude < 0.0001f) headFwd = visual.transform.forward;
            headFwd.Normalize();

            // Default cranium center relative to head pivot, refined by actual equipped Dvergr circlet bounds
            Vector3 hairAnchor = headBone.position + headUp * (0.095f * scale) - headFwd * (0.005f * scale);
            if (circlet != null)
            {
                MeshFilter bestMf = null;
                float bestSize = 0f;
                foreach (var mf in circlet.GetComponentsInChildren<MeshFilter>(true))
                {
                    if (mf == null || mf.sharedMesh == null) continue;
                    float sz = mf.sharedMesh.bounds.size.sqrMagnitude;
                    if (sz > bestSize)
                    {
                        bestSize = sz;
                        bestMf = mf;
                    }
                }
                if (bestMf != null)
                {
                    Vector3 circletWorldCenter = bestMf.transform.TransformPoint(bestMf.sharedMesh.bounds.center);
                    if ((circletWorldCenter - headBone.position).sqrMagnitude < (0.35f * scale) * (0.35f * scale))
                    {
                        // Align vertically & fore-aft with the circlet ring while staying centered on headBone
                        Vector3 delta = circletWorldCenter - headBone.position;
                        hairAnchor = headBone.position
                            + headUp * Vector3.Dot(delta, headUp)
                            + headFwd * (Vector3.Dot(delta, headFwd) - 0.006f * scale);
                    }
                }
            }

            Quaternion worldRot = Quaternion.LookRotation(headFwd, headUp);
            var hairGo = CreateGearObject("StandHairMane", BuildAurvandilsFylgjaHairMesh(), GetHairMaterial(), applySpectralPrepass: true);
            PlaceAnchoredPiece(visual, hairGo, headBone, hairAnchor, worldRot);
        }

        public static void EquipStandOutfit(GameObject visual, SkinnedMeshRenderer bodyModel, Transform headBone, Func<HumanBodyBones, string, Transform> findBone)
        {
            if (visual == null) return;

            // Golden Shoulder Pads (anchored on UpperArm bones so they swing with punches and blocks)
            Transform leftUpperArm  = findBone(HumanBodyBones.LeftUpperArm, "LeftArm");
            Transform leftLowerArm  = findBone(HumanBodyBones.LeftLowerArm, "LeftForeArm");
            Transform rightUpperArm = findBone(HumanBodyBones.RightUpperArm, "RightArm");
            Transform rightLowerArm = findBone(HumanBodyBones.RightLowerArm, "RightForeArm");
            Transform upperChest    = findBone(HumanBodyBones.UpperChest, "Spine2") ?? findBone(HumanBodyBones.Chest, "Chest");
            Transform neckBone      = findBone(HumanBodyBones.Neck, "Neck");

            AttachShoulderPad(visual, leftUpperArm, leftLowerArm, upperChest, false);
            AttachShoulderPad(visual, rightUpperArm, rightLowerArm, upperChest, true);

            // Wrapped Crimson Red Scarf seated snugly around the neck
            AttachRedScarf(visual, headBone, neckBone, upperChest);

            // Black Gloves & Wrist Cuffs
            Transform leftHand    = findBone(HumanBodyBones.LeftHand, "LeftHand");
            Transform leftMiddle  = findBone(HumanBodyBones.LeftMiddleProximal, "LeftHandMiddle1");
            Transform rightHand   = findBone(HumanBodyBones.RightHand, "RightHand");
            Transform rightMiddle = findBone(HumanBodyBones.RightMiddleProximal, "RightHandMiddle1");

            AttachGlove(visual, leftLowerArm, leftHand, leftMiddle, "StandGlove_L");
            AttachGlove(visual, rightLowerArm, rightHand, rightMiddle, "StandGlove_R");

            // Black Boots & Golden Knee Pads
            Transform leftThigh     = findBone(HumanBodyBones.LeftUpperLeg, "LeftUpLeg");
            Transform rightThigh    = findBone(HumanBodyBones.RightUpperLeg, "RightUpLeg");
            Transform leftLowerLeg  = findBone(HumanBodyBones.LeftLowerLeg, "LeftLeg");
            Transform leftFoot      = findBone(HumanBodyBones.LeftFoot, "LeftFoot");
            Transform leftToes      = findBone(HumanBodyBones.LeftToes, "LeftToeBase");
            Transform rightLowerLeg = findBone(HumanBodyBones.RightLowerLeg, "RightLeg");
            Transform rightFoot     = findBone(HumanBodyBones.RightFoot, "RightFoot");
            Transform rightToes     = findBone(HumanBodyBones.RightToes, "RightToeBase");

            AttachBoot(visual, leftLowerLeg, leftFoot, leftToes, "StandBoot_L");
            AttachBoot(visual, rightLowerLeg, rightFoot, rightToes, "StandBoot_R");

            AttachKneePad(visual, leftThigh, leftLowerLeg, leftFoot, "StandKneePad_L");
            AttachKneePad(visual, rightThigh, rightLowerLeg, rightFoot, "StandKneePad_R");

            // White Loincloth & Golden Waist Belt anchored together on Hips
            Transform hips  = findBone(HumanBodyBones.Hips, "Hips");
            Transform spine = findBone(HumanBodyBones.Spine, "Spine");

            AttachLoincloth(visual, hips, spine, leftThigh, rightThigh, leftLowerLeg);
            AttachGoldBelt(visual, hips, spine, leftThigh, rightThigh);

            if (Plugin.Log != null)
            {
                Vector3 sampleBoneScale = hips != null ? hips.lossyScale : visual.transform.lossyScale;
                Plugin.Log.LogInfo($"StandEquipmentBuilder: Equipped outfit (visualScale={visual.transform.lossyScale}, boneScale={sampleBoneScale}).");
            }
        }

        private static void AttachShoulderPad(GameObject visual, Transform upperArm, Transform lowerArm, Transform chest, bool isRight)
        {
            if (upperArm == null) return;

            Vector3 chestPos = chest != null ? chest.position : visual.transform.position + visual.transform.up * 1.35f;
            Vector3 armDown = (lowerArm != null)
                ? (lowerArm.position - upperArm.position).normalized
                : -visual.transform.up;

            // True outward direction from torso center through shoulder, projected perpendicular to arm axis
            Vector3 outward = Vector3.ProjectOnPlane(upperArm.position - chestPos, visual.transform.up);
            if (outward.sqrMagnitude < 0.0001f)
            {
                outward = isRight ? visual.transform.right : -visual.transform.right;
            }
            outward.Normalize();

            Vector3 padUp = (visual.transform.up * 0.88f + outward * 0.25f).normalized;
            Vector3 padForward = Vector3.ProjectOnPlane(visual.transform.forward, outward);
            if (padForward.sqrMagnitude < 0.0001f) padForward = visual.transform.forward;
            padForward.Normalize();

            // Guarantee right-handed orthonormal frame where +X is ALWAYS outward (away from torso)
            Vector3 axisY = Vector3.ProjectOnPlane(padUp, outward).normalized;
            Vector3 axisZ = Vector3.Cross(outward, axisY).normalized; // +forward on Right, -forward on Left (front-back symmetric)
            Quaternion worldRot = Quaternion.LookRotation(axisZ, axisY);

            // Anchor directly at upperArm shoulder joint; mesh curvature wraps flush over the deltoid cap
            var go = CreateGearObject(isRight ? "StandShoulderPad_R" : "StandShoulderPad_L", BuildShoulderPadMesh(), GetGoldMaterial(), applySpectralPrepass: true);
            PlaceAnchoredPiece(visual, go, upperArm, upperArm.position, worldRot);
        }

        private static void AttachRedScarf(GameObject visual, Transform headBone, Transform neckBone, Transform upperChest)
        {
            Transform parent = neckBone ?? upperChest ?? visual.transform;
            float scale = GetScaleFactor(visual);

            Vector3 neckAxis = visual.transform.up;
            float neckLen = 0.12f;
            if (neckBone != null && headBone != null && (headBone.position - neckBone.position).sqrMagnitude > 0.0001f)
            {
                Vector3 span = headBone.position - neckBone.position;
                neckLen = Mathf.Clamp(span.magnitude / scale, 0.09f, 0.16f);
                neckAxis = Vector3.Slerp(visual.transform.up, span.normalized, 0.65f).normalized;
            }

            Vector3 neckAnchor;
            if (neckBone != null)
            {
                neckAnchor = neckBone.position + neckAxis * (neckLen * 0.28f * scale);
            }
            else if (upperChest != null)
            {
                neckAnchor = upperChest.position + visual.transform.up * (0.14f * scale);
            }
            else
            {
                neckAnchor = visual.transform.position + visual.transform.up * (1.52f * scale);
            }

            Vector3 scarfFwd = Vector3.ProjectOnPlane(visual.transform.forward, neckAxis);
            if (scarfFwd.sqrMagnitude < 0.0001f) scarfFwd = visual.transform.forward;
            scarfFwd.Normalize();

            Quaternion worldRot = Quaternion.LookRotation(scarfFwd, neckAxis);
            var go = CreateGearObject("StandScarf", BuildScarfMesh(neckLen), GetScarfMaterial(), applySpectralPrepass: true);
            PlaceAnchoredPiece(visual, go, parent, neckAnchor, worldRot);
        }

        private static void AttachGlove(GameObject visual, Transform lowerArm, Transform hand, Transform middleBone, string namePrefix)
        {
            if (hand == null) return;

            Vector3 forwardWorld;
            if (middleBone != null && (middleBone.position - hand.position).sqrMagnitude > 0.00001f)
            {
                forwardWorld = (middleBone.position - hand.position).normalized;
            }
            else if (hand.childCount > 0 && (hand.GetChild(0).position - hand.position).sqrMagnitude > 0.00001f)
            {
                forwardWorld = (hand.GetChild(0).position - hand.position).normalized;
            }
            else if (lowerArm != null && (hand.position - lowerArm.position).sqrMagnitude > 0.00001f)
            {
                forwardWorld = (hand.position - lowerArm.position).normalized;
            }
            else
            {
                forwardWorld = -visual.transform.up;
            }

            // Pick a clean stable up reference perpendicular to hand axis
            Vector3 upHint = Vector3.ProjectOnPlane(visual.transform.forward, forwardWorld);
            if (upHint.sqrMagnitude < 0.001f) upHint = Vector3.ProjectOnPlane(visual.transform.right, forwardWorld);
            upHint.Normalize();

            float scale = GetScaleFactor(visual);
            float measuredPalmLen = middleBone != null
                ? Vector3.Distance(hand.position, middleBone.position) / scale
                : 0.085f;
            float fistLength = Mathf.Clamp(measuredPalmLen * 2.35f, 0.175f, 0.235f);

            Quaternion handWorldRot = Quaternion.LookRotation(forwardWorld, upHint);
            var gloveGo = CreateGearObject(namePrefix + "_Fist", BuildGloveFistMesh(fistLength), GetLeatherBlackMaterial(), applySpectralPrepass: true);
            PlaceAnchoredPiece(visual, gloveGo, hand, hand.position, handWorldRot);

            // Forearm gauntlet wrist cuff overlapping cleanly at the wrist joint
            if (lowerArm != null)
            {
                Vector3 armAxis = (hand.position - lowerArm.position).normalized; // wrist = +Y if we point Y toward hand
                Vector3 armFwd = Vector3.ProjectOnPlane(visual.transform.forward, armAxis);
                if (armFwd.sqrMagnitude < 0.001f) armFwd = Vector3.ProjectOnPlane(visual.transform.right, armAxis);
                armFwd.Normalize();

                Quaternion cuffWorldRot = Quaternion.LookRotation(armFwd, armAxis);
                var cuffGo = CreateGearObject(namePrefix + "_Cuff", BuildWristCuffMesh(0.135f), GetLeatherBlackMaterial(), applySpectralPrepass: true);
                PlaceAnchoredPiece(visual, cuffGo, lowerArm, hand.position, cuffWorldRot);
            }
        }

        private static void AttachBoot(GameObject visual, Transform lowerLeg, Transform foot, Transform toes, string namePrefix)
        {
            if (lowerLeg == null || foot == null) return;

            float scale = GetScaleFactor(visual);
            float shinLen = Vector3.Distance(foot.position, lowerLeg.position) / scale;
            shinLen = Mathf.Clamp(shinLen, 0.28f, 0.55f);

            // Boot shaft on LowerLeg (+Y pointing up from ankle toward knee)
            Vector3 shinUp = (lowerLeg.position - foot.position).normalized;
            Vector3 shinFwd = Vector3.ProjectOnPlane(visual.transform.forward, shinUp).normalized;
            if (shinFwd.sqrMagnitude < 0.001f) shinFwd = visual.transform.forward;

            Quaternion shaftRot = Quaternion.LookRotation(shinFwd, shinUp);
            float shaftHeight = shinLen * 0.72f;
            var shaftGo = CreateGearObject(namePrefix + "_Shaft", BuildBootShaftMesh(shaftHeight), GetLeatherBlackMaterial(), applySpectralPrepass: true);
            PlaceAnchoredPiece(visual, shaftGo, lowerLeg, foot.position, shaftRot);

            // Boot foot & toe box on Foot (+Z forward toward toes, +Y up)
            Vector3 toeVec = toes != null
                ? Vector3.ProjectOnPlane(toes.position - foot.position, visual.transform.up)
                : visual.transform.forward * (0.14f * scale);

            float footLen = toeVec.magnitude / scale;
            Vector3 footFwd = toeVec.sqrMagnitude > 0.0001f ? toeVec.normalized : visual.transform.forward;
            float toeTipZ = Mathf.Clamp(footLen + 0.105f, 0.24f, 0.29f);
            float ankleHeight = Mathf.Clamp(visual.transform.InverseTransformPoint(foot.position).y, 0.065f, 0.130f);

            Quaternion footRot = Quaternion.LookRotation(footFwd, visual.transform.up);
            var footGo = CreateGearObject(namePrefix + "_Foot", BuildBootFootMesh(toeTipZ, ankleHeight), GetLeatherBlackMaterial(), applySpectralPrepass: true);
            PlaceAnchoredPiece(visual, footGo, foot, foot.position, footRot);
        }

        private static void AttachKneePad(GameObject visual, Transform upperLeg, Transform lowerLeg, Transform foot, string name)
        {
            if (lowerLeg == null) return;

            Vector3 shinUp = (foot != null && (lowerLeg.position - foot.position).sqrMagnitude > 0.0001f)
                ? (lowerLeg.position - foot.position).normalized
                : visual.transform.up;
            Vector3 kneeUp = upperLeg != null && (upperLeg.position - lowerLeg.position).sqrMagnitude > 0.0001f
                ? Vector3.Slerp(shinUp, (upperLeg.position - lowerLeg.position).normalized, 0.35f).normalized
                : shinUp;

            Vector3 kneeFwd = Vector3.ProjectOnPlane(visual.transform.forward, kneeUp);
            if (kneeFwd.sqrMagnitude < 0.0001f) kneeFwd = visual.transform.forward;
            kneeFwd.Normalize();

            Quaternion worldRot = Quaternion.LookRotation(kneeFwd, kneeUp);
            var go = CreateGearObject(name, BuildKneePadMesh(), GetGoldMaterial(), applySpectralPrepass: true);
            PlaceAnchoredPiece(visual, go, lowerLeg, lowerLeg.position, worldRot);
        }

        private static void ComputeHipMetrics(
            GameObject visual,
            Transform hips,
            Transform spine,
            Transform leftThigh,
            Transform rightThigh,
            out Transform parent,
            out Vector3 hipMid,
            out float halfHipSpan,
            out float hipToSpine,
            out float scale)
        {
            parent = hips ?? visual.transform;
            scale = GetScaleFactor(visual);

            if (leftThigh != null && rightThigh != null)
            {
                hipMid = 0.5f * (leftThigh.position + rightThigh.position);
                float span = Vector3.ProjectOnPlane(rightThigh.position - leftThigh.position, visual.transform.up).magnitude / scale;
                halfHipSpan = Mathf.Clamp(span * 0.5f, 0.075f, 0.130f);
            }
            else
            {
                hipMid = parent.position;
                halfHipSpan = 0.095f;
            }

            hipToSpine = spine != null
                ? Vector3.Dot(spine.position - hipMid, visual.transform.up) / scale
                : 0.13f;
            hipToSpine = Mathf.Clamp(hipToSpine, 0.09f, 0.18f);
        }

        private static void AttachLoincloth(
            GameObject visual,
            Transform hips,
            Transform spine,
            Transform leftThigh,
            Transform rightThigh,
            Transform leftLowerLeg)
        {
            ComputeHipMetrics(visual, hips, spine, leftThigh, rightThigh, out Transform parent, out Vector3 hipMid, out float halfHipSpan, out float hipToSpine, out float scale);

            float thighLen = (leftThigh != null && leftLowerLeg != null)
                ? Vector3.Distance(leftThigh.position, leftLowerLeg.position) / scale
                : 0.40f;
            thighLen = Mathf.Clamp(thighLen, 0.32f, 0.48f);

            Quaternion worldRot = Quaternion.LookRotation(visual.transform.forward, visual.transform.up);
            var go = CreateGearObject("StandLoincloth", BuildLoinclothMesh(halfHipSpan, hipToSpine, thighLen), GetLoinclothMaterial(), applySpectralPrepass: true);
            PlaceAnchoredPiece(visual, go, parent, hipMid, worldRot);
        }

        private static void AttachGoldBelt(
            GameObject visual,
            Transform hips,
            Transform spine,
            Transform leftThigh,
            Transform rightThigh)
        {
            ComputeHipMetrics(visual, hips, spine, leftThigh, rightThigh, out Transform parent, out Vector3 hipMid, out float halfHipSpan, out float hipToSpine, out _);

            Quaternion worldRot = Quaternion.LookRotation(visual.transform.forward, visual.transform.up);
            var go = CreateGearObject("StandBelt", BuildGoldBeltMesh(halfHipSpan, hipToSpine), GetGoldMaterial(), applySpectralPrepass: true);
            PlaceAnchoredPiece(visual, go, parent, hipMid, worldRot);
        }

        private static float GetScaleFactor(GameObject visual)
        {
            float s = visual.transform.lossyScale.y;
            return s > 0.01f ? s : 1f;
        }

        internal static GameObject CreateGearObject(string name, Mesh mesh, Material material, bool applySpectralPrepass = false)
        {
            var go = new GameObject(name);
            var mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = material;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
            if (applySpectralPrepass)
            {
                ApplyRendererSpectralMaterials(mr, material, Plugin.ModelOpacity);
            }
            return go;
        }

        private static void PlaceAnchoredPiece(GameObject visual, GameObject piece, Transform parentBone, Vector3 worldPos, Quaternion worldRot)
        {
            piece.transform.SetParent(parentBone, false);
            piece.transform.localPosition = parentBone.InverseTransformPoint(worldPos);
            piece.transform.localRotation = Quaternion.Inverse(parentBone.rotation) * worldRot;

            // Valheim's Armature uses 100x Blender-FBX bone scaling; normalize localScale so
            // our 1:1-meter meshes match visual.lossyScale instead of blowing up 100x.
            Vector3 targetScale = visual.transform.lossyScale;
            Vector3 boneScale = parentBone.lossyScale;
            float sx = Mathf.Abs(boneScale.x) > 0.0001f ? targetScale.x / boneScale.x : 1f;
            float sy = Mathf.Abs(boneScale.y) > 0.0001f ? targetScale.y / boneScale.y : 1f;
            float sz = Mathf.Abs(boneScale.z) > 0.0001f ? targetScale.z / boneScale.z : 1f;
            piece.transform.localScale = new Vector3(sx, sy, sz);
        }

        // ----------------------------------------------------------------------
        // Shared Materials
        // ----------------------------------------------------------------------

        public static Material GetGoldMaterial()
        {
            if (s_goldMaterial == null)
            {
                s_goldMaterial = CreateCrispMaterial(GoldArmorColor, metallic: 0.92f, glossiness: 0.78f, GoldEmission, CreateForgedMetalTexture(GoldArmorColor));
            }
            return s_goldMaterial;
        }

        public static Material GetScarfMaterial()
        {
            if (s_scarfMaterial == null)
            {
                s_scarfMaterial = CreateCrispMaterial(ScarfRedColor, metallic: 0.05f, glossiness: 0.24f, ScarfEmission);
            }
            return s_scarfMaterial;
        }

        public static Material GetLeatherBlackMaterial()
        {
            if (s_leatherBlackMaterial == null)
            {
                s_leatherBlackMaterial = CreateCrispMaterial(LeatherBlackColor, metallic: 0.18f, glossiness: 0.44f, LeatherEmission);
            }
            return s_leatherBlackMaterial;
        }

        public static Material GetLoinclothMaterial()
        {
            if (s_loinclothMaterial == null)
            {
                s_loinclothMaterial = CreateCrispMaterial(LoinclothWhiteColor, metallic: 0.0f, glossiness: 0.08f, LoinclothEmission);
            }
            return s_loinclothMaterial;
        }

        public static Material GetHairMaterial()
        {
            if (s_hairMaterial == null)
            {
                s_hairMaterial = CreateCrispMaterial(HairBlackColor, metallic: 0.08f, glossiness: 0.36f, HairEmission, CreateHairStrandTexture(HairBlackColor));
            }
            return s_hairMaterial;
        }

        public static void ApplyGoldTint(Material mat)
        {
            if (mat == null) return;
            float opacity = Plugin.ModelOpacity;
            if (opacity < 0.99f)
            {
                Shader transpShader = FindValheimTransparentShader();
                if (transpShader != null && mat.shader != transpShader)
                {
                    mat.shader = transpShader;
                }
            }
            // Low emission preserves HelmetDverger's native etched metal texture and normal map shading.
            ApplyMaterialProperties(mat, GoldCircletTint, metallic: 0.92f, glossiness: 0.78f, GoldEmission);
            if (opacity < 0.99f)
            {
                ApplySpectralTransparency(mat, GoldCircletTint, opacity, hasDepthPrepass: GetDepthWriteMaterial() != null);
            }
        }

        internal static Shader FindValheimTransparentShader()
        {
            // Custom/Creature is opaque/cutout-only in Valheim and has no alpha blend pass.
            // Prefer Valheim's shipped PBR 'Standard TwoSided' (supports _MainTex, _BumpMap, _EmissionMap,
            // _Metallic, _Glossiness, _ALPHAPREMULTIPLY_ON, and _Color.a), with always-included fallbacks.
            string[] preferred =
            {
                "Standard TwoSided",
                "Legacy Shaders/Transparent/Bumped Diffuse",
                "Legacy Shaders/Transparent/Diffuse",
                "Standard",
                "Unlit/Transparent"
            };

            foreach (var targetName in preferred)
            {
                var sh = Shader.Find(targetName);
                if (sh != null && sh.isSupported) return sh;
            }

            var loadedShaders = Resources.FindObjectsOfTypeAll<Shader>();
            foreach (var targetName in preferred)
            {
                foreach (var sh in loadedShaders)
                {
                    if (sh != null && sh.isSupported && sh.name == targetName)
                    {
                        return sh;
                    }
                }
            }

            return FindValheimOpaqueShader();
        }

        internal static Material GetDepthWriteMaterial()
        {
            if (s_depthWriteMaterial != null) return s_depthWriteMaterial;

            Shader depthShader = Shader.Find("Unlit/DepthWrite");
            if (depthShader == null || !depthShader.isSupported)
            {
                foreach (var sh in Resources.FindObjectsOfTypeAll<Shader>())
                {
                    if (sh != null && sh.isSupported && sh.name == "Unlit/DepthWrite")
                    {
                        depthShader = sh;
                        break;
                    }
                }
            }

            if (depthShader == null || !depthShader.isSupported) return null;

            s_depthWriteMaterial = new Material(depthShader)
            {
                name = "AurvandilsFylgja_DepthPrepassMat",
                mainTexture = Texture2D.whiteTexture,
                renderQueue = DepthPrepassQueue
            };
            if (s_depthWriteMaterial.HasProperty("_MainTex")) s_depthWriteMaterial.SetTexture("_MainTex", Texture2D.whiteTexture);
            if (s_depthWriteMaterial.HasProperty("_Cutoff")) s_depthWriteMaterial.SetFloat("_Cutoff", 0.0f);
            return s_depthWriteMaterial;
        }

        /// <summary>
        /// Configures Valheim's Custom/Creature (or Standard-compatible) material for clean spectral transparency
        /// using the same premultiplied-alpha mode Valheim uses for Ghost_mat (_ALPHAPREMULTIPLY_ON),
        /// preserving crisp specular/metallic edges and luminous _EmissionMap details.
        /// When opacity >= 0.99f, configures pure opaque rendering.
        /// </summary>
        internal static void ApplySpectralTransparency(Material mat, Color baseRgb, float opacity, bool hasDepthPrepass)
        {
            if (mat == null) return;

            float clampedOpacity = Mathf.Clamp(opacity, 0.20f, 1.00f);
            if (clampedOpacity >= 0.99f)
            {
                Color opaque = new Color(baseRgb.r, baseRgb.g, baseRgb.b, 1.0f);
                mat.color = opaque;
                if (mat.HasProperty("_Color")) mat.SetColor("_Color", opaque);
                if (mat.HasProperty("_Mode")) mat.SetFloat("_Mode", 0f);
                if (mat.HasProperty("_SrcBlend")) mat.SetInt("_SrcBlend", (int)BlendMode.One);
                if (mat.HasProperty("_DstBlend")) mat.SetInt("_DstBlend", (int)BlendMode.Zero);
                if (mat.HasProperty("_ZWrite")) mat.SetInt("_ZWrite", 1);
                if (mat.HasProperty("_Cull")) mat.SetInt("_Cull", (int)CullMode.Back);
                mat.DisableKeyword("_ALPHATEST_ON");
                mat.DisableKeyword("_ALPHABLEND_ON");
                mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                mat.SetOverrideTag("RenderType", "");
                mat.renderQueue = -1;
                return;
            }

            Color translucent = new Color(baseRgb.r, baseRgb.g, baseRgb.b, clampedOpacity);
            mat.color = translucent;
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", translucent);
            if (mat.HasProperty("_Mode")) mat.SetFloat("_Mode", 3f); // Premultiplied Transparent (matches Ghost_mat)
            if (mat.HasProperty("_SrcBlend")) mat.SetInt("_SrcBlend", (int)BlendMode.One);
            if (mat.HasProperty("_DstBlend")) mat.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            if (mat.HasProperty("_ZWrite")) mat.SetInt("_ZWrite", hasDepthPrepass ? 0 : 1);
            if (mat.HasProperty("_Cull")) mat.SetInt("_Cull", (int)CullMode.Back);
            mat.DisableKeyword("_ALPHATEST_ON");
            mat.DisableKeyword("_ALPHABLEND_ON");
            mat.EnableKeyword("_ALPHAPREMULTIPLY_ON");
            mat.SetOverrideTag("RenderType", "Transparent");
            mat.renderQueue = SpectralTransparentQueue;
        }

        /// <summary>
        /// Duplicates a single-submesh Mesh into a 2-submesh Mesh where SubMesh 0 runs the Unlit/DepthWrite
        /// pre-pass (renderQueue 3001) and SubMesh 1 runs the spectral transparent surface pass (renderQueue 3002),
        /// eliminating internal back-face / limb overlap while keeping the outer silhouette silky translucent.
        /// </summary>
        internal static Mesh BuildDepthPrepassMesh(Mesh source)
        {
            if (source == null || source.subMeshCount != 1) return source;

            try
            {
                Mesh copy = UnityEngine.Object.Instantiate(source);
                copy.name = source.name + "_SpectralPrepass";
                int[] tris = source.GetTriangles(0);
                copy.subMeshCount = 2;
                copy.SetTriangles(tris, 0, calculateBounds: false);
                copy.SetTriangles(tris, 1, calculateBounds: false);
                copy.bounds = source.bounds;
                return copy;
            }
            catch (Exception ex)
            {
                Plugin.Log?.LogWarning($"StandEquipmentBuilder: BuildDepthPrepassMesh fallback: {ex.Message}");
                return source;
            }
        }

        internal static void ApplyRendererSpectralMaterials(Renderer renderer, Material transparentMat, float opacity)
        {
            if (renderer == null || transparentMat == null) return;

            Material depthMat = opacity < 0.99f ? GetDepthWriteMaterial() : null;
            if (depthMat != null)
            {
                if (renderer is SkinnedMeshRenderer smr && smr.sharedMesh != null && smr.sharedMesh.subMeshCount == 1)
                {
                    Mesh prepassMesh = BuildDepthPrepassMesh(smr.sharedMesh);
                    if (prepassMesh != null && prepassMesh.subMeshCount == 2)
                    {
                        smr.sharedMesh = prepassMesh;
                        smr.sharedMaterials = new[] { depthMat, transparentMat };
                        return;
                    }
                }
                else if (renderer is MeshRenderer mr)
                {
                    var mf = mr.GetComponent<MeshFilter>();
                    if (mf != null && mf.sharedMesh != null && mf.sharedMesh.subMeshCount == 1)
                    {
                        Mesh prepassMesh = BuildDepthPrepassMesh(mf.sharedMesh);
                        if (prepassMesh != null && prepassMesh.subMeshCount == 2)
                        {
                            mf.sharedMesh = prepassMesh;
                            mr.sharedMaterials = new[] { depthMat, transparentMat };
                            return;
                        }
                    }
                }
            }

            // Fallback when opaque or if Unlit/DepthWrite wasn't found (transparentMat already uses _ZWrite = 1)
            renderer.sharedMaterials = new[] { transparentMat };
        }

        internal static Shader FindValheimOpaqueShader()
        {
            // Never pick Unity's stripped "Standard", "Custom/Player" (cutout skin), or any particle/transparent shader.
            string[] preferred = { "Custom/Creature", "Custom/Piece" };

            // 1. Check loaded Valheim shaders in memory and Shader.Find first
            foreach (var targetName in preferred)
            {
                var sh = Shader.Find(targetName);
                if (sh != null && sh.isSupported) return sh;
            }

            var loadedShaders = Resources.FindObjectsOfTypeAll<Shader>();
            foreach (var targetName in preferred)
            {
                foreach (var sh in loadedShaders)
                {
                    if (sh != null && sh.isSupported && sh.name == targetName)
                    {
                        return sh;
                    }
                }
            }

            // 2. Inspect solid MeshRenderer/SkinnedMeshRenderer materials under equipment "attach" children
            if (ObjectDB.instance != null)
            {
                string[] itemPrefabs = { "HelmetDverger", "ShieldBronzeBuckler", "ArmorBronzeChest", "ArmorIronChest", "CapeLinen" };
                foreach (var prefabName in itemPrefabs)
                {
                    var prefab = ObjectDB.instance.GetItemPrefab(prefabName);
                    if (prefab == null) continue;

                    for (int c = 0; c < prefab.transform.childCount; c++)
                    {
                        Transform child = prefab.transform.GetChild(c);
                        if (!child.name.StartsWith("attach")) continue;

                        foreach (var r in child.GetComponentsInChildren<Renderer>(true))
                        {
                            if (!(r is MeshRenderer || r is SkinnedMeshRenderer)) continue;
                            var mat = r.sharedMaterial;
                            var sh = mat != null ? mat.shader : null;
                            if (!IsValidOpaqueShader(sh, mat)) continue;
                            return sh;
                        }
                    }
                }
            }

            return null;
        }

        private static bool IsValidOpaqueShader(Shader sh, Material mat)
        {
            if (sh == null || !sh.isSupported) return false;
            string n = sh.name ?? string.Empty;
            if (n == "Standard" || n == "Custom/Player") return false;
            if (n.IndexOf("Particle", StringComparison.OrdinalIgnoreCase) >= 0) return false;
            if (n.IndexOf("Transparent", StringComparison.OrdinalIgnoreCase) >= 0) return false;
            if (n.IndexOf("Additive", StringComparison.OrdinalIgnoreCase) >= 0) return false;
            if (n.IndexOf("Alpha", StringComparison.OrdinalIgnoreCase) >= 0) return false;
            if (n.IndexOf("Sprites", StringComparison.OrdinalIgnoreCase) >= 0) return false;
            if (n.IndexOf("Unlit", StringComparison.OrdinalIgnoreCase) >= 0) return false;
            if (n.StartsWith("Hidden/", StringComparison.OrdinalIgnoreCase)) return false;
            if (n.StartsWith("UI/", StringComparison.OrdinalIgnoreCase)) return false;
            if (mat != null && mat.renderQueue >= 2500) return false;
            return true;
        }

        internal static Texture2D CreateSolidTexture(Color color)
        {
            var tex = new Texture2D(1, 1, TextureFormat.RGBA32, false)
            {
                name = $"StandSolidTex_{color.r:F2}_{color.g:F2}_{color.b:F2}",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Point
            };
            tex.SetPixel(0, 0, new Color(color.r, color.g, color.b, 1.0f));
            tex.Apply(false, false);
            return tex;
        }

        private static Texture2D CreateForgedMetalTexture(Color baseColor)
        {
            const int size = 128;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, mipChain: true)
            {
                name = "StandForgedGoldMetalTex",
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Bilinear
            };

            var pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
            {
                float v = (y / (float)size) * Mathf.PI * 2f;
                // Anisotropic brushed metal grain along horizontal rolling lines
                float rowStreak = 0.055f * Mathf.Sin(v * 19f) + 0.035f * Mathf.Cos(v * 43f + 0.7f);

                for (int x = 0; x < size; x++)
                {
                    float u = (x / (float)size) * Mathf.PI * 2f;
                    // Seamless multi-frequency hammered bronze patina & micro-scratches
                    float brush = 0.05f * Mathf.Sin(u * 3f + v * 17f)
                                + 0.035f * Mathf.Cos(u * 11f - v * 29f);
                    float hammer = 0.075f * Mathf.Sin(u * 4f + 0.4f) * Mathf.Cos(v * 4f - 0.3f)
                                 + 0.055f * Mathf.Cos(u * 7f - v * 6f);
                    // Subtle micro-speckling deterministic from tiled coords
                    float micro = (((x * 73856093 ^ y * 19349663) & 15) / 15f - 0.5f) * 0.05f;

                    float shade = Mathf.Clamp(1.0f + rowStreak + brush + hammer + micro, 0.72f, 1.24f);
                    // Slightly warmer dark bronze patina in crevices, brighter burnished gold on highs
                    float r = Mathf.Clamp01(baseColor.r * shade * 1.03f);
                    float g = Mathf.Clamp01(baseColor.g * Mathf.Pow(shade, 1.08f));
                    float b = Mathf.Clamp01(baseColor.b * Mathf.Pow(shade, 1.25f));
                    pixels[y * size + x] = new Color(r, g, b, 1f);
                }
            }

            tex.SetPixels(pixels);
            tex.Apply(updateMipmaps: true, makeNoLongerReadable: false);
            return tex;
        }

        private static Texture2D CreateHairStrandTexture(Color baseColor)
        {
            const int size = 64;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, mipChain: true)
            {
                name = "StandHairStrandTex",
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Bilinear
            };

            var pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
            {
                float v = (y / (float)size) * Mathf.PI * 2f;
                for (int x = 0; x < size; x++)
                {
                    float u = (x / (float)size) * Mathf.PI * 2f;
                    // Seamless dark violet-charcoal longitudinal strand highlights
                    float strand = 0.16f * Mathf.Sin(u * 14f + Mathf.Sin(v * 3f) * 1.4f)
                                 + 0.09f * Mathf.Cos(u * 29f - v * 2f);
                    float shade = Mathf.Clamp(1.0f + strand, 0.72f, 1.38f);
                    pixels[y * size + x] = new Color(
                        Mathf.Clamp01(baseColor.r * shade),
                        Mathf.Clamp01(baseColor.g * shade * 0.92f),
                        Mathf.Clamp01(baseColor.b * shade * 1.15f),
                        1f);
                }
            }

            tex.SetPixels(pixels);
            tex.Apply(updateMipmaps: true, makeNoLongerReadable: false);
            return tex;
        }

        internal static Material CreateCrispMaterial(
            Color color,
            float metallic,
            float glossiness,
            Color emission,
            Texture2D customTexture = null,
            float? opacityOverride = null,
            bool useDepthPrepass = true)
        {
            float opacity = opacityOverride ?? Plugin.ModelOpacity;
            bool wantTransparent = opacity < 0.99f;

            Material mat = null;
            Shader shader = wantTransparent ? FindValheimTransparentShader() : FindValheimOpaqueShader();
            if (shader != null)
            {
                mat = new Material(shader);
                Plugin.Log?.LogInfo($"StandEquipmentBuilder: Creating equipment material using shader '{shader.name}' (opacity={opacity:0.00}).");
            }
            else
            {
                Plugin.Log?.LogWarning("StandEquipmentBuilder: Falling back to StandMaterialManager for equipment material.");
                mat = StandMaterialManager.GetMaterial(color);
            }

            if (mat != null)
            {
                mat.renderQueue = -1;
                mat.mainTexture = customTexture != null ? customTexture : CreateSolidTexture(color);
                // Texture already carries exact albedo tint; keep _Color white so both _MainTex*_Color
                // shaders and texture-only shaders render vibrant true colors (never washed white).
                ApplyMaterialProperties(mat, Color.white, metallic, glossiness, emission);

                bool hasPrepass = useDepthPrepass && GetDepthWriteMaterial() != null;
                ApplySpectralTransparency(mat, Color.white, opacity, hasDepthPrepass: hasPrepass);
            }
            return mat;
        }

        private static void ApplyMaterialProperties(Material mat, Color color, float metallic, float glossiness, Color emission)
        {
            mat.color = color;
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", color);
            if (mat.HasProperty("_TintColor")) mat.SetColor("_TintColor", color);
            if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", metallic);
            if (mat.HasProperty("_Glossiness")) mat.SetFloat("_Glossiness", glossiness);
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", glossiness);
            if (mat.HasProperty("_EmissionColor"))
            {
                mat.SetColor("_EmissionColor", emission);
                mat.EnableKeyword("_EMISSION");
            }
        }

        // ----------------------------------------------------------------------
        // Sculpted Geometry Builders
        // ----------------------------------------------------------------------

        private static Mesh BuildShoulderPadMesh()
        {
            var verts = new List<Vector3>();
            var tris = new List<int>();

            // 1. Main arched sweeping golden pauldron wing (thick curved armor shell)
            // Control stations from inner trap (-0.045 X) out to proud winged tip (+0.178 X)
            var wingStations = new (float x, float y, float zHalfWidth, float halfThick)[]
            {
                (-0.045f,  0.042f, 0.088f, 0.011f),
                ( 0.015f,  0.064f, 0.112f, 0.013f),
                ( 0.078f,  0.056f, 0.118f, 0.014f),
                ( 0.135f,  0.040f, 0.104f, 0.012f),
                ( 0.178f,  0.056f, 0.072f, 0.008f),
            };
            AppendArmorShellStrip(verts, tris, wingStations, 5, ridgeHeight: 0.014f);

            // 2. Sculpted outer beveled armor trim rim framing the wing edge
            var trimStations = new (float x, float y, float zHalfWidth, float halfThick)[]
            {
                ( 0.102f,  0.050f, 0.116f, 0.017f),
                ( 0.145f,  0.044f, 0.106f, 0.016f),
                ( 0.184f,  0.060f, 0.075f, 0.011f),
            };
            AppendArmorShellStrip(verts, tris, trimStations, 5, ridgeHeight: 0.008f);

            // 3. Lower overlapping deltoid guard lame curving smoothly below the outer edge
            var lowerStations = new (float x, float y, float zHalfWidth, float halfThick)[]
            {
                ( 0.068f,  0.024f, 0.098f, 0.010f),
                ( 0.118f, -0.028f, 0.094f, 0.011f),
                ( 0.132f, -0.078f, 0.076f, 0.009f),
            };
            AppendArmorShellStrip(verts, tris, lowerStations, 5, ridgeHeight: 0.007f);

            return FinalizeMesh("StandShoulderPadMesh", verts, tris, uvScale: 4.5f);
        }

        private static Mesh BuildScarfMesh(float neckLen)
        {
            var verts = new List<Vector3>();
            var tris = new List<int>();

            float vScale = Mathf.Clamp(neckLen / 0.12f, 0.85f, 1.25f);
            int segs = 22;
            int tubeSegs = 8;

            // 1. Wide base scarf collar resting clearly ON the clavicles/traps & sternum (not buried inside torso)
            var baseWrap = new List<Vector3[]>(segs);
            for (int i = 0; i < segs; i++)
            {
                float theta = (i / (float)segs) * Mathf.PI * 2f;
                float cx = 0.144f * Mathf.Sin(theta);
                float cz = 0.140f * Mathf.Cos(theta) + 0.010f;
                float cy = (-0.016f + 0.006f * Mathf.Sin(theta * 3f)) * vScale;
                Vector3 center = new Vector3(cx, cy, cz);
                Vector3 radialOut = new Vector3(Mathf.Sin(theta), 0f, Mathf.Cos(theta)).normalized;
                baseWrap.Add(CreateEllipseRing(center, radialOut, Vector3.up, 0.032f, 0.030f * vScale, tubeSegs));
            }
            AppendClosedLoopTorus(verts, tris, baseWrap);

            // 2. Plump mid-neck wrapped muffler fold hugging around the neck column
            var midWrap = new List<Vector3[]>(segs);
            for (int i = 0; i < segs; i++)
            {
                float theta = (i / (float)segs) * Mathf.PI * 2f;
                float cx = 0.130f * Mathf.Sin(theta);
                float cz = 0.126f * Mathf.Cos(theta) + 0.008f;
                float cy = (0.022f + 0.005f * Mathf.Cos(theta * 2f)) * vScale;
                Vector3 center = new Vector3(cx, cy, cz);
                Vector3 radialOut = new Vector3(Mathf.Sin(theta), 0f, Mathf.Cos(theta)).normalized;
                midWrap.Add(CreateEllipseRing(center, radialOut, Vector3.up, 0.030f, 0.028f * vScale, tubeSegs));
            }
            AppendClosedLoopTorus(verts, tris, midWrap);

            // 3. Upper neck rim fold sitting right under the jaw & nape
            var topWrap = new List<Vector3[]>(segs);
            for (int i = 0; i < segs; i++)
            {
                float theta = (i / (float)segs) * Mathf.PI * 2f;
                float cx = 0.118f * Mathf.Sin(theta);
                float cz = 0.114f * Mathf.Cos(theta) + 0.006f;
                float cy = (0.054f - 0.006f * Mathf.Cos(theta)) * vScale;
                Vector3 center = new Vector3(cx, cy, cz);
                Vector3 radialOut = new Vector3(Mathf.Sin(theta), 0f, Mathf.Cos(theta)).normalized;
                topWrap.Add(CreateEllipseRing(center, radialOut, Vector3.up, 0.024f, 0.022f * vScale, tubeSegs));
            }
            AppendClosedLoopTorus(verts, tris, topWrap);

            // 4. Two short upper-back scarf tails clearing the upper back/shoulder blades
            for (int side = -1; side <= 1; side += 2)
            {
                var tail = new List<Vector3[]>();
                Vector3 rightDir = new Vector3(0.92f, 0f, side * 0.25f).normalized;
                Vector3 thickDir = Vector3.Cross(rightDir, Vector3.up).normalized;

                tail.Add(CreateEllipseRing(new Vector3(side * 0.048f,  0.008f * vScale, -0.140f), rightDir, thickDir, 0.044f, 0.015f, 8));
                tail.Add(CreateEllipseRing(new Vector3(side * 0.082f, -0.075f,          -0.192f), rightDir, thickDir, 0.040f, 0.012f, 8));
                tail.Add(CreateEllipseRing(new Vector3(side * 0.112f, -0.175f,          -0.218f), rightDir, thickDir, 0.032f, 0.008f, 8));
                tail.Add(CreateEllipseRing(new Vector3(side * 0.130f, -0.255f,          -0.232f), rightDir, thickDir, 0.020f, 0.005f, 8));
                AppendLoftedRings(verts, tris, tail, capStart: true, capEnd: true);
            }

            return FinalizeMesh("StandScarfMesh", verts, tris, uvScale: 3.5f);
        }

        private static Mesh BuildGloveFistMesh(float fistLength)
        {
            var verts = new List<Vector3>();
            var tris = new List<int>();

            int segs = 12;
            var rings = new List<Vector3[]>
            {
                CreateEllipseRing(new Vector3(0f, 0f, -0.040f),              Vector3.right, Vector3.up, 0.074f, 0.066f, segs),
                CreateEllipseRing(new Vector3(0f, 0.004f, fistLength * 0.24f), Vector3.right, Vector3.up, 0.084f, 0.076f, segs),
                CreateEllipseRing(new Vector3(0f, 0.005f, fistLength * 0.54f), Vector3.right, Vector3.up, 0.086f, 0.078f, segs),
                CreateEllipseRing(new Vector3(0f, 0.002f, fistLength * 0.82f), Vector3.right, Vector3.up, 0.076f, 0.070f, segs),
                CreateEllipseRing(new Vector3(0f, -0.002f, fistLength),        Vector3.right, Vector3.up, 0.052f, 0.046f, segs),
            };
            AppendLoftedRings(verts, tris, rings, capStart: true, capEnd: true);

            return FinalizeMesh("StandGloveFistMesh", verts, tris, uvScale: 4.0f);
        }

        private static Mesh BuildWristCuffMesh(float length)
        {
            var verts = new List<Vector3>();
            var tris = new List<int>();

            int segs = 12;
            var rings = new List<Vector3[]>
            {
                CreateEllipseRing(new Vector3(0f,  0.032f, 0f),          Vector3.right, Vector3.forward, 0.074f, 0.070f, segs),
                CreateEllipseRing(new Vector3(0f, -length * 0.45f, 0f),  Vector3.right, Vector3.forward, 0.082f, 0.076f, segs),
                CreateEllipseRing(new Vector3(0f, -length * 0.86f, 0f),  Vector3.right, Vector3.forward, 0.088f, 0.082f, segs),
                CreateEllipseRing(new Vector3(0f, -length, 0f),          Vector3.right, Vector3.forward, 0.082f, 0.076f, segs),
            };
            AppendLoftedRings(verts, tris, rings, capStart: true, capEnd: true);

            return FinalizeMesh("StandWristCuffMesh", verts, tris, uvScale: 4.0f);
        }

        private static Mesh BuildBootShaftMesh(float height)
        {
            var verts = new List<Vector3>();
            var tris = new List<int>();

            int segs = 12;
            var rings = new List<Vector3[]>
            {
                CreateEllipseRing(new Vector3(0f, -0.048f, 0f),          Vector3.right, Vector3.forward, 0.078f, 0.086f, segs),
                CreateEllipseRing(new Vector3(0f, height * 0.25f, -0.002f), Vector3.right, Vector3.forward, 0.086f, 0.094f, segs),
                CreateEllipseRing(new Vector3(0f, height * 0.62f, -0.008f), Vector3.right, Vector3.forward, 0.100f, 0.112f, segs),
                CreateEllipseRing(new Vector3(0f, height * 0.88f, -0.004f), Vector3.right, Vector3.forward, 0.098f, 0.108f, segs),
                CreateEllipseRing(new Vector3(0f, height * 0.96f, 0f),      Vector3.right, Vector3.forward, 0.106f, 0.115f, segs),
                CreateEllipseRing(new Vector3(0f, height, 0f),              Vector3.right, Vector3.forward, 0.100f, 0.108f, segs),
            };
            AppendLoftedRings(verts, tris, rings, capStart: true, capEnd: true);

            return FinalizeMesh("StandBootShaftMesh", verts, tris, uvScale: 3.5f);
        }

        private static Mesh BuildBootFootMesh(float toeTipZ, float ankleHeight)
        {
            var verts = new List<Vector3>();
            var tris = new List<int>();

            float soleY = -(ankleHeight + 0.018f);
            float toeSoleY = soleY + 0.004f;

            // Oversized armored combat boot profile covering heel, sole, arch, outer foot rim, and toes cleanly
            var stations = new (float z, float yBottom, float yTop, float halfW)[]
            {
                (-0.095f,          soleY,    0.055f, 0.072f), // Heel back
                (-0.015f,          soleY,    0.080f, 0.084f), // Ankle / arch
                (toeTipZ * 0.36f,  soleY,    0.054f, 0.086f), // Instep / bridge
                (toeTipZ * 0.74f,  soleY,    0.026f, 0.084f), // Ball of foot
                (toeTipZ,          toeSoleY,-0.002f, 0.062f), // Armored toe box tip
            };

            int segs = 12;
            var rings = new List<Vector3[]>();
            foreach (var st in stations)
            {
                float cy = 0.5f * (st.yBottom + st.yTop);
                float ry = 0.5f * (st.yTop - st.yBottom);
                rings.Add(CreateBootSectionRing(st.z, st.yBottom, st.yTop, st.halfW, cy, ry, segs));
            }
            AppendLoftedRings(verts, tris, rings, capStart: true, capEnd: true);

            return FinalizeMesh("StandBootFootMesh", verts, tris, uvScale: 3.5f);
        }

        private static Mesh BuildLoinclothMesh(float halfHipSpan, float hipToSpine, float thighLen)
        {
            var verts = new List<Vector3>();
            var tris = new List<int>();

            float waistY = Mathf.Clamp(hipToSpine * 0.54f, 0.048f, 0.088f);
            float flapDrop = Mathf.Clamp(thighLen * 0.80f, 0.28f, 0.36f);

            // 1. Full 360-degree wrapped hip skirt/briefs nested cleanly under StandBelt and covering Valheim's default underwear
            AppendHipSkirtWrap(verts, tris, halfHipSpan, waistY);

            // 2. Sculpted Front Loincloth Panel (wider & prouder so pelvis/underwear never poke through)
            var frontApron = new (float y, float zCenter, float xHalfWidth, float halfThick, float bowZ, float vTipDrop)[]
            {
                (waistY + 0.016f,   0.156f, 0.126f, 0.010f, -0.016f, 0.000f),
                (waistY - 0.032f,   0.178f, 0.144f, 0.011f, -0.014f, 0.000f),
                (-0.045f,           0.186f, 0.152f, 0.011f, -0.012f, 0.000f),
                (-flapDrop * 0.36f, 0.184f, 0.142f, 0.010f, -0.010f, 0.005f),
                (-flapDrop * 0.68f, 0.182f, 0.132f, 0.009f, -0.008f, 0.012f),
                (-flapDrop * 0.90f, 0.186f, 0.138f, 0.007f, -0.006f, 0.020f),
                (-flapDrop,         0.190f, 0.124f, 0.006f, -0.004f, 0.030f),
            };
            AppendClothPanelStrip(verts, tris, frontApron, arcSteps: 9, faceDir: +1f, pleatAmp: 0.004f);

            // 3. Symmetric Back Loincloth Panel clearing glutes and covering rear underwear
            var backApron = new (float y, float zCenter, float xHalfWidth, float halfThick, float bowZ, float vTipDrop)[]
            {
                (waistY + 0.016f,   -0.158f, 0.134f, 0.010f, 0.016f, 0.000f),
                (waistY - 0.032f,   -0.184f, 0.150f, 0.011f, 0.014f, 0.000f),
                (-0.048f,           -0.194f, 0.158f, 0.011f, 0.012f, 0.000f),
                (-flapDrop * 0.36f, -0.188f, 0.148f, 0.010f, 0.010f, 0.005f),
                (-flapDrop * 0.68f, -0.184f, 0.138f, 0.009f, 0.008f, 0.012f),
                (-flapDrop * 0.90f, -0.188f, 0.142f, 0.007f, 0.006f, 0.020f),
                (-flapDrop,         -0.192f, 0.128f, 0.006f, 0.004f, 0.028f),
            };
            AppendClothPanelStrip(verts, tris, backApron, arcSteps: 9, faceDir: -1f, pleatAmp: 0.004f);

            // 4. Broad under-crotch cloth bridge joining front & back wraps seamlessly between inner thighs
            float underHalfW = Mathf.Clamp(halfHipSpan * 0.70f, 0.056f, 0.074f);
            var underBand = new List<Vector3[]>
            {
                CreateEllipseRing(new Vector3(0f, -0.022f,  0.155f), Vector3.right, new Vector3(0f, 0.7f, -0.7f).normalized, underHalfW * 1.35f, 0.015f, 10),
                CreateEllipseRing(new Vector3(0f, -0.078f,  0.075f), Vector3.right, new Vector3(0f, 0.9f, -0.4f).normalized, underHalfW,         0.015f, 10),
                CreateEllipseRing(new Vector3(0f, -0.092f, -0.005f), Vector3.right, Vector3.up,                              underHalfW * 0.92f, 0.015f, 10),
                CreateEllipseRing(new Vector3(0f, -0.080f, -0.078f), Vector3.right, new Vector3(0f, 0.9f,  0.4f).normalized, underHalfW,         0.015f, 10),
                CreateEllipseRing(new Vector3(0f, -0.022f, -0.162f), Vector3.right, new Vector3(0f, 0.7f,  0.7f).normalized, underHalfW * 1.38f, 0.015f, 10),
            };
            AppendLoftedRings(verts, tris, underBand, capStart: true, capEnd: true);

            return FinalizeMesh("StandLoinclothMesh", verts, tris, uvScale: 3.5f);
        }

        private static void AppendHipSkirtWrap(List<Vector3> verts, List<int> tris, float halfHipSpan, float waistY)
        {
            const int segs = 28;
            var rings = new List<Vector3[]>(segs);

            float rxTop = halfHipSpan + 0.096f;
            float rzTop = 0.148f;
            float rxMid = halfHipSpan + 0.118f;
            float rzMid = 0.172f;
            float rxBot = halfHipSpan + 0.122f;
            float rzBot = 0.178f;

            for (int i = 0; i < segs; i++)
            {
                float theta = (i / (float)segs) * Mathf.PI * 2f;
                float sin = Mathf.Sin(theta);
                float cos = Mathf.Cos(theta);

                float yTop = (waistY + 0.028f) - 0.018f * cos;
                float yMid = 0.005f;
                float yBot = -0.105f - 0.026f * Mathf.Abs(cos);

                Vector3 pTopOuter = new Vector3(rxTop * sin, yTop, -0.004f + rzTop * cos);
                Vector3 pMidOuter = new Vector3(rxMid * sin, yMid, -0.004f + rzMid * cos);
                Vector3 pBotOuter = new Vector3(rxBot * sin, yBot, -0.004f + rzBot * cos);

                Vector3 pTopInner = new Vector3((rxTop - 0.024f) * sin, yTop, -0.004f + (rzTop - 0.024f) * cos);
                Vector3 pMidInner = new Vector3((rxMid - 0.024f) * sin, yMid, -0.004f + (rzMid - 0.024f) * cos);
                Vector3 pBotInner = new Vector3((rxBot - 0.024f) * sin, yBot, -0.004f + (rzBot - 0.024f) * cos);

                rings.Add(new[] { pTopOuter, pMidOuter, pBotOuter, pBotInner, pMidInner, pTopInner });
            }

            AppendClosedLoopTorus(verts, tris, rings);
        }

        private static Mesh BuildGoldBeltMesh(float halfHipSpan, float hipToSpine)
        {
            var verts = new List<Vector3>();
            var tris = new List<int>();

            float waistY = Mathf.Clamp(hipToSpine * 0.54f, 0.048f, 0.088f);
            float beltRx = halfHipSpan + 0.104f;
            float beltRz = 0.156f;

            // 1. Beveled hexagonal metallic waist belt wrapping snugly above the white loincloth with front V-dip
            const int segs = 28;
            var beltRings = new List<Vector3[]>(segs);
            for (int i = 0; i < segs; i++)
            {
                float theta = (i / (float)segs) * Mathf.PI * 2f;
                float cos = Mathf.Cos(theta);
                float sin = Mathf.Sin(theta);
                float frontFactor = Mathf.Max(0f, cos);

                float cx = beltRx * sin;
                // Slightly proud of white sash tie strip in front to prevent z-fighting
                float cz = -0.004f + beltRz * cos + 0.012f * frontFactor * frontFactor;
                float cy = (waistY + 0.038f) - 0.026f * cos + 0.005f * Mathf.Cos(theta * 2f);

                Vector3 center = new Vector3(cx, cy, cz);
                Vector3 radialOut = new Vector3(sin, 0f, cos).normalized;
                beltRings.Add(CreateBeveledStrapRing(center, radialOut, Vector3.up, halfThick: 0.017f, halfHeight: 0.022f));
            }
            AppendClosedLoopTorus(verts, tris, beltRings);

            // 2. Twin iconic circular golden belt medallions framing top corners of the white loincloth
            for (int side = -1; side <= 1; side += 2)
            {
                Vector3 studCenter = new Vector3(side * 0.090f, waistY + 0.022f, 0.156f);
                Vector3 studNormal = new Vector3(side * 0.38f, 0f, 0.92f).normalized;
                AppendDiscMedallion(verts, tris, studCenter, studNormal, outerRadius: 0.028f, domeDepth: 0.022f, segs: 14);
            }

            // 3. Center front golden belt clasp stud at the V-dip vertex
            AppendDiscMedallion(verts, tris, new Vector3(0f, waistY + 0.012f, 0.168f), Vector3.forward, outerRadius: 0.020f, domeDepth: 0.016f, segs: 12);

            return FinalizeMesh("StandBeltMesh", verts, tris, uvScale: 5.5f);
        }

        private static Mesh BuildKneePadMesh()
        {
            var verts = new List<Vector3>();
            var tris = new List<int>();

            // 1. Beveled golden strap collar encircling the knee joint cleanly
            const int segs = 20;
            var strapRings = new List<Vector3[]>(segs);
            for (int i = 0; i < segs; i++)
            {
                float theta = (i / (float)segs) * Mathf.PI * 2f;
                float cx = 0.094f * Mathf.Sin(theta);
                float cz = 0.096f * Mathf.Cos(theta);
                Vector3 center = new Vector3(cx, 0.006f, cz);
                Vector3 radialOut = new Vector3(Mathf.Sin(theta), 0f, Mathf.Cos(theta)).normalized;
                strapRings.Add(CreateBeveledStrapRing(center, radialOut, Vector3.up, halfThick: 0.012f, halfHeight: 0.018f));
            }
            AppendClosedLoopTorus(verts, tris, strapRings);

            // 2. Circular domed golden knee shield (poleyn) centered proud over the kneecap
            AppendDiscMedallion(verts, tris, new Vector3(0f, 0.008f, 0.088f), Vector3.forward, outerRadius: 0.068f, domeDepth: 0.034f, segs: 18);

            // 3. Lower golden shin-guard tongue wing below the knee cap matching Aurvandil's Fylgja silhouette
            var lowerWing = new (float y, float zCenter, float xHalfWidth, float halfThick, float bowZ, float vTipDrop)[]
            {
                (-0.018f, 0.102f, 0.048f, 0.009f, -0.024f, 0.000f),
                (-0.056f, 0.098f, 0.036f, 0.008f, -0.020f, 0.008f),
                (-0.096f, 0.092f, 0.018f, 0.006f, -0.012f, 0.014f),
            };
            AppendClothPanelStrip(verts, tris, lowerWing, arcSteps: 5, faceDir: +1f, pleatAmp: 0.002f);

            return FinalizeMesh("StandKneePadMesh", verts, tris, uvScale: 5.5f);
        }

        private static Mesh BuildAurvandilsFylgjaHairMesh()
        {
            var verts = new List<Vector3>();
            var tris = new List<int>();

            // 1. Fitted black skull cap & temple/nape shell anchored cleanly above the golden circlet
            AppendHairScalpCap(verts, tris);

            // 2. Jagged upward-swept front bangs/crest erupting right above & behind the circlet rim
            AppendHairLock(verts, tris, baseWidth: 0.046f, baseThick: 0.026f, rollDeg: 0f,
                new Vector3(0.000f, 0.010f, 0.078f),
                new Vector3(0.005f, 0.095f, 0.088f),
                new Vector3(0.018f, 0.180f, 0.020f),
                new Vector3(0.032f, 0.220f, -0.068f));

            AddMirroredLocks(verts, tris, baseWidth: 0.044f, baseThick: 0.024f, rollDeg: 18f,
                new Vector3(0.044f, 0.012f, 0.072f),
                new Vector3(0.070f, 0.092f, 0.066f),
                new Vector3(0.095f, 0.168f, -0.005f),
                new Vector3(0.114f, 0.198f, -0.082f));

            // Sharp temple "horn" spikes jutting out/up right beside the sides of the golden headband
            AddMirroredLocks(verts, tris, baseWidth: 0.038f, baseThick: 0.022f, rollDeg: 28f,
                new Vector3(0.078f, 0.005f, 0.046f),
                new Vector3(0.126f, 0.052f, 0.038f),
                new Vector3(0.164f, 0.110f, -0.020f),
                new Vector3(0.175f, 0.142f, -0.082f));

            // 3. Signature towering windswept S-curve billowing flames sweeping up and back
            AppendHairLock(verts, tris, baseWidth: 0.068f, baseThick: 0.036f, rollDeg: 10f,
                new Vector3(0.018f, 0.052f, 0.032f),
                new Vector3(0.048f, 0.188f, -0.045f),
                new Vector3(0.102f, 0.252f, -0.178f),
                new Vector3(0.150f, 0.232f, -0.295f),
                new Vector3(0.175f, 0.254f, -0.342f));

            AppendHairLock(verts, tris, baseWidth: 0.066f, baseThick: 0.035f, rollDeg: -10f,
                new Vector3(-0.022f, 0.056f, 0.028f),
                new Vector3(-0.045f, 0.198f, -0.035f),
                new Vector3(-0.050f, 0.268f, -0.148f),
                new Vector3(-0.015f, 0.252f, -0.255f),
                new Vector3(0.022f, 0.272f, -0.308f));

            AppendHairLock(verts, tris, baseWidth: 0.056f, baseThick: 0.030f, rollDeg: -22f,
                new Vector3(-0.055f, 0.050f, 0.010f),
                new Vector3(-0.104f, 0.178f, -0.048f),
                new Vector3(-0.138f, 0.246f, -0.138f),
                new Vector3(-0.150f, 0.272f, -0.208f));

            AppendHairLock(verts, tris, baseWidth: 0.058f, baseThick: 0.030f, rollDeg: 22f,
                new Vector3(0.064f, 0.046f, 0.008f),
                new Vector3(0.118f, 0.158f, -0.058f),
                new Vector3(0.164f, 0.208f, -0.172f),
                new Vector3(0.198f, 0.198f, -0.268f));

            // Center & inner upper-crown filler waves
            AppendHairLock(verts, tris, baseWidth: 0.056f, baseThick: 0.028f, rollDeg: 0f,
                new Vector3(0.000f, 0.058f, 0.000f),
                new Vector3(0.012f, 0.162f, -0.090f),
                new Vector3(0.038f, 0.198f, -0.205f),
                new Vector3(0.062f, 0.188f, -0.275f));

            AddMirroredLocks(verts, tris, baseWidth: 0.052f, baseThick: 0.026f, rollDeg: 14f,
                new Vector3(0.045f, 0.052f, -0.018f),
                new Vector3(0.088f, 0.145f, -0.110f),
                new Vector3(0.118f, 0.175f, -0.215f),
                new Vector3(0.136f, 0.160f, -0.275f));

            // 4. Swept-back side wings above ears & mid-occiput flares (prominent in 3rd-person side/rear views)
            AddMirroredLocks(verts, tris, baseWidth: 0.050f, baseThick: 0.026f, rollDeg: 35f,
                new Vector3(0.075f, 0.028f, -0.020f),
                new Vector3(0.138f, 0.075f, -0.088f),
                new Vector3(0.180f, 0.118f, -0.180f),
                new Vector3(0.195f, 0.138f, -0.242f));

            AddMirroredLocks(verts, tris, baseWidth: 0.046f, baseThick: 0.024f, rollDeg: 40f,
                new Vector3(0.072f, -0.010f, -0.045f),
                new Vector3(0.128f, 0.015f, -0.115f),
                new Vector3(0.160f, 0.052f, -0.195f),
                new Vector3(0.168f, 0.078f, -0.238f));

            // 5. Rear occiput & nape hooked spikes clearing cleanly above the neck scarf
            AppendHairLock(verts, tris, baseWidth: 0.054f, baseThick: 0.026f, rollDeg: 0f,
                new Vector3(0.000f, 0.018f, -0.078f),
                new Vector3(0.010f, 0.058f, -0.165f),
                new Vector3(0.022f, 0.105f, -0.238f),
                new Vector3(0.028f, 0.135f, -0.268f));

            AddMirroredLocks(verts, tris, baseWidth: 0.048f, baseThick: 0.024f, rollDeg: 20f,
                new Vector3(0.054f, 0.010f, -0.074f),
                new Vector3(0.098f, 0.042f, -0.155f),
                new Vector3(0.124f, 0.085f, -0.222f),
                new Vector3(0.132f, 0.112f, -0.248f));

            AddMirroredLocks(verts, tris, baseWidth: 0.042f, baseThick: 0.020f, rollDeg: 25f,
                new Vector3(0.046f, -0.038f, -0.072f),
                new Vector3(0.082f, -0.028f, -0.142f),
                new Vector3(0.098f, -0.002f, -0.192f),
                new Vector3(0.104f, 0.024f, -0.205f));

            return FinalizeMesh("StandHairManeMesh", verts, tris, uvScale: 4.5f);
        }

        private static void AppendHairScalpCap(List<Vector3> verts, List<int> tris)
        {
            // Stacked closed rings forming the fitted black skull cap above the forehead circlet down to the nape
            var levels = new (float yFront, float yBack, float rx, float rz, float zShift)[]
            {
                (0.006f, -0.070f, 0.105f, 0.112f, -0.006f), // Above front headband & down to nape
                (0.032f, -0.032f, 0.108f, 0.116f, -0.008f),
                (0.062f,  0.036f, 0.104f, 0.112f, -0.010f),
                (0.086f,  0.078f, 0.084f, 0.092f, -0.012f),
                (0.100f,  0.098f, 0.042f, 0.048f, -0.014f),
            };

            const int segs = 20;
            var rings = new List<Vector3[]>(levels.Length);
            foreach (var lv in levels)
            {
                var ring = new Vector3[segs];
                for (int i = 0; i < segs; i++)
                {
                    float theta = (i / (float)segs) * Mathf.PI * 2f;
                    float cos = Mathf.Cos(theta); // +1 front (+Z), -1 back (-Z)
                    float frontMix = 0.5f * (cos + 1f);
                    float y = Mathf.Lerp(lv.yBack, lv.yFront, frontMix);
                    ring[i] = new Vector3(lv.rx * Mathf.Sin(theta), y, lv.zShift + lv.rz * cos);
                }
                rings.Add(ring);
            }

            AppendLoftedRings(verts, tris, rings, capStart: true, capEnd: true);
        }

        private static void AddMirroredLocks(
            List<Vector3> verts,
            List<int> tris,
            float baseWidth,
            float baseThick,
            float rollDeg,
            params Vector3[] rightControlPoints)
        {
            AppendHairLock(verts, tris, baseWidth, baseThick, rollDeg, rightControlPoints);

            var leftControlPoints = new Vector3[rightControlPoints.Length];
            for (int i = 0; i < rightControlPoints.Length; i++)
            {
                Vector3 p = rightControlPoints[i];
                leftControlPoints[i] = new Vector3(-p.x, p.y, p.z);
            }
            AppendHairLock(verts, tris, baseWidth, baseThick, -rollDeg, leftControlPoints);
        }

        private static void AppendHairLock(
            List<Vector3> verts,
            List<int> tris,
            float baseWidth,
            float baseThick,
            float rollDeg,
            params Vector3[] ctrl)
        {
            if (ctrl == null || ctrl.Length < 3) return;

            const int steps = 8;
            var centers = new Vector3[steps + 1];
            for (int s = 0; s <= steps; s++)
            {
                centers[s] = EvaluateCatmullRom(ctrl, s / (float)steps);
            }

            Vector3 prevTangent = (centers[1] - centers[0]).normalized;
            Vector3 radialHint = centers[0].sqrMagnitude > 0.0001f ? centers[0].normalized : Vector3.up;
            Vector3 widthAxis = Vector3.Cross(prevTangent, radialHint);
            if (widthAxis.sqrMagnitude < 0.0001f) widthAxis = Vector3.Cross(prevTangent, Vector3.right);
            widthAxis.Normalize();
            Vector3 thickAxis = Vector3.Cross(widthAxis, prevTangent).normalized;

            if (Mathf.Abs(rollDeg) > 0.01f)
            {
                Quaternion roll = Quaternion.AngleAxis(rollDeg, prevTangent);
                widthAxis = roll * widthAxis;
                thickAxis = roll * thickAxis;
            }

            var rings = new List<Vector3[]>(steps + 1);
            for (int s = 0; s <= steps; s++)
            {
                float t = s / (float)steps;
                Vector3 tangent = s < steps
                    ? (centers[Mathf.Min(steps, s + 1)] - centers[Mathf.Max(0, s - 1)]).normalized
                    : (centers[steps] - centers[steps - 1]).normalized;

                if (s > 0 && tangent.sqrMagnitude > 0.0001f)
                {
                    Quaternion rot = Quaternion.FromToRotation(prevTangent, tangent);
                    widthAxis = (rot * widthAxis).normalized;
                    thickAxis = Vector3.Cross(widthAxis, tangent).normalized;
                    prevTangent = tangent;
                }

                // Plump anime lock silhouette tapering cleanly into a sharp hooked tip
                float profile = s == steps
                    ? 0.04f
                    : Mathf.Sin(Mathf.Clamp01(1f - t * 0.92f) * Mathf.PI * 0.5f) * (1f + 0.18f * Mathf.Sin(t * Mathf.PI));
                float w = Mathf.Max(0.002f, baseWidth * profile);
                float h = Mathf.Max(0.0015f, baseThick * profile);

                Vector3 c = centers[s];
                rings.Add(new[]
                {
                    c + widthAxis * w,
                    c + widthAxis * (0.38f * w) + thickAxis * h,
                    c - widthAxis * (0.38f * w) + thickAxis * h,
                    c - widthAxis * w,
                    c - widthAxis * (0.38f * w) - thickAxis * (0.60f * h),
                    c + widthAxis * (0.38f * w) - thickAxis * (0.60f * h),
                });
            }

            AppendLoftedRings(verts, tris, rings, capStart: false, capEnd: true);
        }

        private static Vector3 EvaluateCatmullRom(Vector3[] pts, float u)
        {
            int n = pts.Length;
            float scaled = Mathf.Clamp01(u) * (n - 1);
            int seg = Mathf.Min(n - 2, Mathf.FloorToInt(scaled));
            float t = scaled - seg;

            Vector3 p0 = seg > 0 ? pts[seg - 1] : (2f * pts[0] - pts[1]);
            Vector3 p1 = pts[seg];
            Vector3 p2 = pts[seg + 1];
            Vector3 p3 = seg + 2 < n ? pts[seg + 2] : (2f * pts[n - 1] - pts[n - 2]);

            float t2 = t * t;
            float t3 = t2 * t;
            return 0.5f * (
                (2f * p1) +
                (-p0 + p2) * t +
                (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 +
                (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
        }

        private static void AppendClothPanelStrip(
            List<Vector3> verts,
            List<int> tris,
            (float y, float zCenter, float xHalfWidth, float halfThick, float bowZ, float vTipDrop)[] stations,
            int arcSteps,
            float faceDir,
            float pleatAmp)
        {
            if (stations.Length < 2) return;

            int ringSize = arcSteps * 2;
            int baseVertIdx = verts.Count;

            for (int s = 0; s < stations.Length; s++)
            {
                var st = stations[s];
                var ring = new Vector3[ringSize];

                // Outer face left-to-right (-X to +X)
                for (int j = 0; j < arcSteps; j++)
                {
                    float u = (j / (float)(arcSteps - 1)) * 2f - 1f;
                    float x = u * st.xHalfWidth;
                    float y = st.y - st.vTipDrop * (1f - Mathf.Abs(u));
                    float pleat = pleatAmp * Mathf.Cos(u * Mathf.PI * 2f);
                    float z = st.zCenter + st.bowZ * (u * u) + faceDir * (st.halfThick + pleat);
                    ring[j] = new Vector3(x, y, z);
                }

                // Inner face returning right-to-left (+X to -X)
                for (int j = 0; j < arcSteps; j++)
                {
                    float u = 1f - (j / (float)(arcSteps - 1)) * 2f;
                    float x = u * st.xHalfWidth * 0.94f;
                    float y = st.y - st.vTipDrop * (1f - Mathf.Abs(u));
                    float z = st.zCenter + st.bowZ * (u * u) - faceDir * st.halfThick;
                    ring[arcSteps + j] = new Vector3(x, y, z);
                }

                verts.AddRange(ring);
            }

            Vector3 outerHint = new Vector3(0f, 0f, faceDir);
            Vector3 innerHint = new Vector3(0f, 0f, -faceDir);

            for (int r = 0; r < stations.Length - 1; r++)
            {
                int rowA = baseVertIdx + r * ringSize;
                int rowB = baseVertIdx + (r + 1) * ringSize;

                for (int i = 0; i < ringSize; i++)
                {
                    int next = (i + 1) % ringSize;
                    int a = rowA + i;
                    int b = rowA + next;
                    int c = rowB + next;
                    int d = rowB + i;

                    Vector3 hint;
                    if (i < arcSteps - 1) hint = outerHint;
                    else if (i == arcSteps - 1) hint = Vector3.right;
                    else if (i < ringSize - 1) hint = innerHint;
                    else hint = Vector3.left;

                    EmitTriangleOutward(verts, tris, a, b, c, hint);
                    EmitTriangleOutward(verts, tris, a, c, d, hint);
                }
            }

            // Bridge fabric thickness cleanly across top & bottom hems (avoids pinched/concave center fan)
            int lastRowOffset = baseVertIdx + (stations.Length - 1) * ringSize;
            for (int j = 0; j < arcSteps - 1; j++)
            {
                int topF0 = baseVertIdx + j;
                int topF1 = baseVertIdx + j + 1;
                int topB0 = baseVertIdx + (ringSize - 1 - j);
                int topB1 = baseVertIdx + (ringSize - 2 - j);
                EmitTriangleOutward(verts, tris, topF0, topF1, topB1, Vector3.up);
                EmitTriangleOutward(verts, tris, topF0, topB1, topB0, Vector3.up);

                int botF0 = lastRowOffset + j;
                int botF1 = lastRowOffset + j + 1;
                int botB0 = lastRowOffset + (ringSize - 1 - j);
                int botB1 = lastRowOffset + (ringSize - 2 - j);
                EmitTriangleOutward(verts, tris, botF0, botB1, botF1, Vector3.down);
                EmitTriangleOutward(verts, tris, botF0, botB0, botB1, Vector3.down);
            }
        }

        // ----------------------------------------------------------------------
        // Reusable Mesh / Ring / Outward-Normal Helpers
        // ----------------------------------------------------------------------

        internal static Vector3[] CreateEllipseRing(Vector3 center, Vector3 axisU, Vector3 axisV, float rU, float rV, int count)
        {
            var pts = new Vector3[count];
            for (int i = 0; i < count; i++)
            {
                float a = (i / (float)count) * Mathf.PI * 2f;
                pts[i] = center + axisU * (Mathf.Cos(a) * rU) + axisV * (Mathf.Sin(a) * rV);
            }
            return pts;
        }

        private static Vector3[] CreateBootSectionRing(float z, float yBot, float yTop, float halfW, float cy, float ry, int count)
        {
            var pts = new Vector3[count];
            for (int i = 0; i < count; i++)
            {
                float a = (i / (float)count) * Mathf.PI * 2f;
                float cos = Mathf.Cos(a);
                float sin = Mathf.Sin(a);
                float x = cos * halfW;
                // Flatten bottom slightly so the boot sole sits sturdy on ground
                float y = sin >= 0f ? cy + sin * ry : Mathf.Lerp(cy, yBot, Mathf.Clamp01(-sin * 1.35f));
                pts[i] = new Vector3(x, y, z);
            }
            return pts;
        }

        private static void AppendArmorShellStrip(
            List<Vector3> verts,
            List<int> tris,
            (float x, float y, float zHalfWidth, float halfThick)[] stations,
            int arcSteps,
            float ridgeHeight)
        {
            // Build closed cross-section rings around Z arc + top/bottom thickness at each X station
            var rings = new List<Vector3[]>();
            for (int s = 0; s < stations.Length; s++)
            {
                var st = stations[s];
                int ringSize = arcSteps * 2;
                var ring = new Vector3[ringSize];

                // Top arc from -Z to +Z
                for (int j = 0; j < arcSteps; j++)
                {
                    float u = (j / (float)(arcSteps - 1)) * 2f - 1f; // -1..+1 across Z
                    float z = u * st.zHalfWidth;
                    float archDrop = -0.024f * (u * u);
                    float centerCrest = ridgeHeight * (1f - Mathf.Abs(u));
                    ring[j] = new Vector3(st.x, st.y + st.halfThick + archDrop + centerCrest, z);
                }

                // Bottom arc returning +Z to -Z
                for (int j = 0; j < arcSteps; j++)
                {
                    float u = 1f - (j / (float)(arcSteps - 1)) * 2f; // +1..-1 across Z
                    float z = u * st.zHalfWidth * 0.94f;
                    float archDrop = -0.022f * (u * u);
                    ring[arcSteps + j] = new Vector3(st.x, st.y - st.halfThick + archDrop, z);
                }

                rings.Add(ring);
            }

            AppendLoftedRings(verts, tris, rings, capStart: true, capEnd: true);
        }

        internal static void AppendLoftedRings(List<Vector3> verts, List<int> tris, List<Vector3[]> rings, bool capStart, bool capEnd)
        {
            if (rings.Count < 2) return;
            int n = rings[0].Length;
            int baseVertIdx = verts.Count;

            for (int r = 0; r < rings.Count; r++)
            {
                verts.AddRange(rings[r]);
            }

            for (int r = 0; r < rings.Count - 1; r++)
            {
                int rowA = baseVertIdx + r * n;
                int rowB = baseVertIdx + (r + 1) * n;
                Vector3 centroid = 0.5f * (RingCenter(rings[r]) + RingCenter(rings[r + 1]));

                for (int i = 0; i < n; i++)
                {
                    int next = (i + 1) % n;
                    int a = rowA + i;
                    int b = rowA + next;
                    int c = rowB + next;
                    int d = rowB + i;

                    Vector3 outward = (0.25f * (verts[a] + verts[b] + verts[c] + verts[d])) - centroid;
                    EmitTriangleOutward(verts, tris, a, b, c, outward);
                    EmitTriangleOutward(verts, tris, a, c, d, outward);
                }
            }

            if (capStart)
            {
                Vector3 capCenter = RingCenter(rings[0]);
                Vector3 nextCenter = RingCenter(rings[1]);
                Vector3 outward = (capCenter - nextCenter).normalized;
                int centerIdx = verts.Count;
                verts.Add(capCenter);
                int row0 = baseVertIdx;
                for (int i = 0; i < n; i++)
                {
                    int next = (i + 1) % n;
                    EmitTriangleOutward(verts, tris, centerIdx, row0 + next, row0 + i, outward);
                }
            }

            if (capEnd)
            {
                int lastR = rings.Count - 1;
                Vector3 capCenter = RingCenter(rings[lastR]);
                Vector3 prevCenter = RingCenter(rings[lastR - 1]);
                Vector3 outward = (capCenter - prevCenter).normalized;
                int centerIdx = verts.Count;
                verts.Add(capCenter);
                int rowLast = baseVertIdx + lastR * n;
                for (int i = 0; i < n; i++)
                {
                    int next = (i + 1) % n;
                    EmitTriangleOutward(verts, tris, centerIdx, rowLast + i, rowLast + next, outward);
                }
            }
        }

        internal static void AppendClosedLoopTorus(List<Vector3> verts, List<int> tris, List<Vector3[]> rings)
        {
            if (rings.Count < 3) return;
            int count = rings.Count;
            int n = rings[0].Length;
            int baseVertIdx = verts.Count;

            for (int r = 0; r < count; r++)
            {
                verts.AddRange(rings[r]);
            }

            for (int r = 0; r < count; r++)
            {
                int rNext = (r + 1) % count;
                int rowA = baseVertIdx + r * n;
                int rowB = baseVertIdx + rNext * n;
                Vector3 centroid = 0.5f * (RingCenter(rings[r]) + RingCenter(rings[rNext]));

                for (int i = 0; i < n; i++)
                {
                    int next = (i + 1) % n;
                    int a = rowA + i;
                    int b = rowA + next;
                    int c = rowB + next;
                    int d = rowB + i;

                    Vector3 outward = (0.25f * (verts[a] + verts[b] + verts[c] + verts[d])) - centroid;
                    EmitTriangleOutward(verts, tris, a, b, c, outward);
                    EmitTriangleOutward(verts, tris, a, c, d, outward);
                }
            }
        }

        private static Vector3 RingCenter(Vector3[] ring)
        {
            Vector3 sum = Vector3.zero;
            for (int i = 0; i < ring.Length; i++) sum += ring[i];
            return sum / ring.Length;
        }

        internal static Mesh FinalizeMesh(string name, List<Vector3> verts, List<int> tris, float uvScale)
        {
            var mesh = new Mesh { name = name };
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            AssignBoxUVs(mesh, verts, uvScale);
            mesh.RecalculateBounds();
            return mesh;
        }

        private static void AssignBoxUVs(Mesh mesh, List<Vector3> verts, float uvScale)
        {
            var normals = mesh.normals;
            var uvs = new Vector2[verts.Count];
            for (int i = 0; i < verts.Count; i++)
            {
                Vector3 v = verts[i] * uvScale;
                Vector3 n = i < normals.Length ? normals[i] : Vector3.up;
                float ax = Mathf.Abs(n.x);
                float ay = Mathf.Abs(n.y);
                float az = Mathf.Abs(n.z);

                if (ax >= ay && ax >= az)
                {
                    uvs[i] = new Vector2(v.z, v.y);
                }
                else if (ay >= ax && ay >= az)
                {
                    uvs[i] = new Vector2(v.x, v.z);
                }
                else
                {
                    uvs[i] = new Vector2(v.x, v.y);
                }
            }
            mesh.SetUVs(0, uvs);
        }

        private static Vector3[] CreateBeveledStrapRing(Vector3 center, Vector3 radialOut, Vector3 up, float halfThick, float halfHeight)
        {
            // Chamfered hexagonal armor cross-section catches crisp metallic rim specular highlights
            float bevel = halfHeight * 0.58f;
            return new[]
            {
                center - radialOut * (halfThick * 0.7f) + up * halfHeight,
                center + radialOut * (halfThick * 0.65f) + up * bevel,
                center + radialOut * halfThick,
                center + radialOut * (halfThick * 0.65f) - up * bevel,
                center - radialOut * (halfThick * 0.7f) - up * halfHeight,
                center - radialOut * halfThick,
            };
        }

        private static void AppendDiscMedallion(
            List<Vector3> verts,
            List<int> tris,
            Vector3 center,
            Vector3 normal,
            float outerRadius,
            float domeDepth,
            int segs)
        {
            Vector3 upHint = Mathf.Abs(Vector3.Dot(normal, Vector3.up)) < 0.9f ? Vector3.up : Vector3.right;
            Vector3 tangentU = Vector3.Cross(upHint, normal).normalized;
            Vector3 tangentV = Vector3.Cross(normal, tangentU).normalized;

            var rings = new List<Vector3[]>
            {
                CreateEllipseRing(center - normal * (domeDepth * 0.25f), tangentU, tangentV, outerRadius,         outerRadius,         segs),
                CreateEllipseRing(center + normal * (domeDepth * 0.35f), tangentU, tangentV, outerRadius * 0.92f, outerRadius * 0.92f, segs),
                CreateEllipseRing(center + normal * (domeDepth * 0.55f), tangentU, tangentV, outerRadius * 0.72f, outerRadius * 0.72f, segs),
                CreateEllipseRing(center + normal * (domeDepth * 0.88f), tangentU, tangentV, outerRadius * 0.44f, outerRadius * 0.44f, segs),
                CreateEllipseRing(center + normal * domeDepth,           tangentU, tangentV, outerRadius * 0.14f, outerRadius * 0.14f, segs),
            };
            AppendLoftedRings(verts, tris, rings, capStart: true, capEnd: true);
        }

        private static void EmitTriangleOutward(List<Vector3> verts, List<int> tris, int i0, int i1, int i2, Vector3 outwardHint)
        {
            Vector3 normal = Vector3.Cross(verts[i1] - verts[i0], verts[i2] - verts[i0]);
            if (Vector3.Dot(normal, outwardHint) >= 0f)
            {
                tris.Add(i0);
                tris.Add(i1);
                tris.Add(i2);
            }
            else
            {
                tris.Add(i0);
                tris.Add(i2);
                tris.Add(i1);
            }
        }
    }
}
