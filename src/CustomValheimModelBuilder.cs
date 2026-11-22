using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;

namespace StandsOfValheim
{
    /// <summary>
    /// Loads, skins, and builds the edited Valheim-styled custom 3D model for Aurvandil's Fylgja
    /// (resources/aurvandils_fylgja.obj + aurvandils_fylgja_tex.png + aurvandils_fylgja_emission.png),
    /// retargeting and binding it seamlessly onto Valheim's Player humanoid Armature and Animator.
    /// Also provides the 3D studded barrage fist mesh for ORA flurry attacks.
    /// </summary>
    internal static class CustomValheimModelBuilder
    {
        private sealed class ParsedObjData
        {
            public Vector3[] Positions;
            public Vector2[] UVs;
            public Vector3[] Normals;
            public int[] Triangles;
        }

        // Canonical 20-bone humanoid rig matching our OBJ T-pose anatomical pivots
        private const int BoneCount = 20;
        private static readonly int[] s_boneParents =
        {
            -1, // 0 Hips
             0, // 1 Spine
             1, // 2 Spine1 (Chest)
             2, // 3 Spine2 (UpperChest)
             3, // 4 Neck
             4, // 5 Head
             3, // 6 LeftShoulder (+X in OBJ)
             6, // 7 LeftUpperArm (+X in OBJ)
             7, // 8 LeftLowerArm (+X in OBJ)
             8, // 9 LeftHand     (+X in OBJ)
             3, // 10 RightShoulder (-X in OBJ)
            10, // 11 RightUpperArm (-X in OBJ)
            11, // 12 RightLowerArm (-X in OBJ)
            12, // 13 RightHand     (-X in OBJ)
             0, // 14 LeftUpperLeg  (+X in OBJ)
            14, // 15 LeftLowerLeg  (+X in OBJ)
            15, // 16 LeftFoot      (+X in OBJ)
             0, // 17 RightUpperLeg (-X in OBJ)
            17, // 18 RightLowerLeg (-X in OBJ)
            18, // 19 RightFoot     (-X in OBJ)
        };

        // Primary child bone index along each segment for exact joint-to-joint retargeting (-1 = terminal)
        private static readonly int[] s_primaryChild =
        {
             1, // 0 Hips -> Spine
             2, // 1 Spine -> Spine1
             3, // 2 Spine1 -> Spine2
             4, // 3 Spine2 -> Neck
             5, // 4 Neck -> Head
            -1, // 5 Head (terminal)
             7, // 6 LeftShoulder -> LeftUpperArm
             8, // 7 LeftUpperArm -> LeftLowerArm
             9, // 8 LeftLowerArm -> LeftHand
            -1, // 9 LeftHand (terminal)
            11, // 10 RightShoulder -> RightUpperArm
            12, // 11 RightUpperArm -> RightLowerArm
            13, // 12 RightLowerArm -> RightHand
            -1, // 13 RightHand (terminal)
            15, // 14 LeftUpperLeg -> LeftLowerLeg
            16, // 15 LeftLowerLeg -> LeftFoot
            -1, // 16 LeftFoot (terminal)
            18, // 17 RightUpperLeg -> RightLowerLeg
            19, // 18 RightLowerLeg -> RightFoot
            -1, // 19 RightFoot (terminal)
        };

        private static readonly Vector3[] s_objPivots =
        {
            new Vector3( 0.000f, 1.160f, -0.010f), // 0 Hips
            new Vector3( 0.000f, 1.275f, -0.010f), // 1 Spine
            new Vector3( 0.000f, 1.395f, -0.015f), // 2 Spine1
            new Vector3( 0.000f, 1.545f, -0.020f), // 3 Spine2
            new Vector3( 0.000f, 1.685f, -0.015f), // 4 Neck
            new Vector3( 0.000f, 1.765f,  0.005f), // 5 Head
            new Vector3( 0.115f, 1.645f, -0.035f), // 6 LeftShoulder
            new Vector3( 0.215f, 1.658f, -0.045f), // 7 LeftUpperArm
            new Vector3( 0.545f, 1.658f, -0.045f), // 8 LeftLowerArm
            new Vector3( 0.770f, 1.658f, -0.045f), // 9 LeftHand
            new Vector3(-0.115f, 1.645f, -0.035f), // 10 RightShoulder
            new Vector3(-0.215f, 1.658f, -0.045f), // 11 RightUpperArm
            new Vector3(-0.545f, 1.658f, -0.045f), // 12 RightLowerArm
            new Vector3(-0.770f, 1.658f, -0.045f), // 13 RightHand
            new Vector3( 0.102f, 1.160f, -0.010f), // 14 LeftUpperLeg
            new Vector3( 0.105f, 0.680f,  0.015f), // 15 LeftLowerLeg
            new Vector3( 0.096f, 0.120f, -0.045f), // 16 LeftFoot
            new Vector3(-0.102f, 1.160f, -0.010f), // 17 RightUpperLeg
            new Vector3(-0.105f, 0.680f,  0.015f), // 18 RightLowerLeg
            new Vector3(-0.096f, 0.120f, -0.045f), // 19 RightFoot
        };

        private static ParsedObjData s_characterObj;
        private static BoneWeight[] s_canonicalWeights;
        private static Mesh s_barrageFistMesh;
        private static Material s_fylgjaMaterial;
        private static Material s_barrageFistMaterial;
        private static Texture2D s_albedoTex;
        private static Texture2D s_emissionTex;
        private static Texture2D s_normalTex;

        public static void InvalidateMaterials()
        {
            s_fylgjaMaterial = null;
            s_barrageFistMaterial = null;
        }

        public static bool TryBuild(GameObject root, StandFollower follower, Player owner)
        {
            // 1. Locate source player Visual hierarchy for Animator + Armature + SkinnedMeshRenderer
            GameObject sourceVisual = null;
            if (Game.instance != null && Game.instance.m_playerPrefab != null)
            {
                var v = Game.instance.m_playerPrefab.transform.Find("Visual");
                if (v != null) sourceVisual = v.gameObject;
            }
            if (sourceVisual == null && owner != null)
            {
                var v = owner.transform.Find("Visual");
                if (v != null) sourceVisual = v.gameObject;
            }

            if (sourceVisual == null)
            {
                Plugin.Log?.LogWarning("CustomValheimModelBuilder: Could not find source player Visual.");
                return false;
            }

            ParsedObjData objData = GetCharacterObjData();
            if (objData == null || objData.Positions == null || objData.Positions.Length == 0)
            {
                Plugin.Log?.LogError("CustomValheimModelBuilder: Embedded aurvandils_fylgja.obj could not be loaded.");
                return false;
            }

            // 2. Clone the visual hierarchy
            GameObject visual = UnityEngine.Object.Instantiate(sourceVisual, root.transform);
            visual.name = "Visual";
            visual.SetActive(true);
            visual.transform.localPosition = Vector3.zero;
            visual.transform.localRotation = Quaternion.identity;

            foreach (var animEvent in visual.GetComponentsInChildren<CharacterAnimEvent>(true))
            {
                if (animEvent == null) continue;
                animEvent.enabled = false;
                UnityEngine.Object.DestroyImmediate(animEvent);
            }

            var lod = visual.GetComponent<LODGroup>();
            if (lod != null)
            {
                lod.enabled = false;
                UnityEngine.Object.Destroy(lod);
            }

            AurvandilsFylgja.CleanExistingEquipment(visual);

            // 3. Locate SkinnedMeshRenderer and ensure base male player mesh reference for bindposes
            SkinnedMeshRenderer bodyModel = visual.GetComponentInChildren<SkinnedMeshRenderer>();
            if (bodyModel == null)
            {
                Plugin.Log?.LogWarning("CustomValheimModelBuilder: SkinnedMeshRenderer not found in visual.");
                UnityEngine.Object.Destroy(visual);
                return false;
            }

            var ownerVis = owner != null ? owner.GetComponent<VisEquipment>() : null;
            if (ownerVis != null && ownerVis.m_models != null && ownerVis.m_models.Length > 0)
            {
                var baseModel = ownerVis.m_models[0];
                if (baseModel.m_mesh != null) bodyModel.sharedMesh = baseModel.m_mesh;
            }

            var animator = visual.GetComponent<Animator>();

            // 4. Retarget and skin the Valheim-styled custom 3D mesh onto bodyModel's skeleton & bindposes
            Mesh skinnedCustomMesh = BuildSkinnedMeshForSkeleton(objData, bodyModel, animator, visual);
            if (skinnedCustomMesh == null)
            {
                Plugin.Log?.LogError("CustomValheimModelBuilder: Failed to skin custom 3D mesh onto Valheim skeleton.");
                UnityEngine.Object.Destroy(visual);
                return false;
            }

            float opacity = Plugin.ModelOpacity;
            Material mat = GetFylgjaMaterial();
            bodyModel.sharedMesh = skinnedCustomMesh;
            bodyModel.localBounds = skinnedCustomMesh.bounds;
            bodyModel.updateWhenOffscreen = true;
            bodyModel.shadowCastingMode = ShadowCastingMode.Off;
            bodyModel.receiveShadows = false;
            if (mat != null)
            {
                // Use a dedicated per-instance material so subtle breathing opacity pulses don't mutate shared state
                Material instanceMat = new Material(mat);
                StandEquipmentBuilder.ApplyRendererSpectralMaterials(bodyModel, instanceMat, opacity);
                follower.RegisterSpectralMaterial(instanceMat);
            }

            // 5. Wire limb & leg bones and animator to StandFollower, then settle pose & play OOB activation anim
            follower.StandAnimator = animator;
            follower.Head      = AurvandilsFylgja.FindBone(bodyModel, visual, HumanBodyBones.Head, "Head");
            follower.Torso     = AurvandilsFylgja.FindBone(bodyModel, visual, HumanBodyBones.Chest, "Chest") ?? visual.transform;
            follower.Hips      = AurvandilsFylgja.FindBone(bodyModel, visual, HumanBodyBones.Hips, "Hips");
            follower.ArmL      = AurvandilsFylgja.FindBone(bodyModel, visual, HumanBodyBones.LeftLowerArm, "LeftArm");
            follower.ArmR      = AurvandilsFylgja.FindBone(bodyModel, visual, HumanBodyBones.RightLowerArm, "RightArm");
            follower.FistL     = AurvandilsFylgja.FindBone(bodyModel, visual, HumanBodyBones.LeftHand, "LeftHand");
            follower.FistR     = AurvandilsFylgja.FindBone(bodyModel, visual, HumanBodyBones.RightHand, "RightHand");
            follower.UpperLegL = AurvandilsFylgja.FindBone(bodyModel, visual, HumanBodyBones.LeftUpperLeg, "LeftUpLeg");
            follower.LowerLegL = AurvandilsFylgja.FindBone(bodyModel, visual, HumanBodyBones.LeftLowerLeg, "LeftLeg");
            follower.FootL     = AurvandilsFylgja.FindBone(bodyModel, visual, HumanBodyBones.LeftFoot, "LeftFoot");
            follower.UpperLegR = AurvandilsFylgja.FindBone(bodyModel, visual, HumanBodyBones.RightUpperLeg, "RightUpLeg");
            follower.LowerLegR = AurvandilsFylgja.FindBone(bodyModel, visual, HumanBodyBones.RightLowerLeg, "RightLeg");
            follower.FootR     = AurvandilsFylgja.FindBone(bodyModel, visual, HumanBodyBones.RightFoot, "RightFoot");

            AurvandilsFylgja.InitializeStandAnimator(animator, follower);

            string shaderName = mat != null && mat.shader != null ? mat.shader.name : "null";
            Plugin.Log?.LogInfo($"CustomValheimModelBuilder: Built Valheim-styled custom 3D Aurvandil's Fylgja ({skinnedCustomMesh.vertexCount} vertices, {skinnedCustomMesh.triangles.Length / 3} triangles, shader='{shaderName}', opacity={opacity:0.00}).");
            return true;
        }

        public static Mesh GetBarrageFistMesh()
        {
            if (s_barrageFistMesh != null) return s_barrageFistMesh;

            ParsedObjData data = LoadEmbeddedObj("aurvandils_fylgja_barrage_fist.obj");
            if (data == null || data.Positions == null || data.Positions.Length == 0)
            {
                return null;
            }

            var mesh = new Mesh
            {
                name = "AurvandilsFylgja_CustomBarrageFist"
            };
            mesh.vertices = data.Positions;
            mesh.normals = data.Normals;
            mesh.uv = data.UVs;
            mesh.triangles = data.Triangles;
            mesh.RecalculateBounds();
            try { mesh.RecalculateTangents(); } catch { }
            s_barrageFistMesh = mesh;
            return s_barrageFistMesh;
        }

        public static Material GetBarrageFistMaterial()
        {
            if (s_barrageFistMaterial != null) return s_barrageFistMaterial;
            Material baseMat = GetFylgjaMaterial();
            if (baseMat == null) return null;

            s_barrageFistMaterial = new Material(baseMat)
            {
                name = "AurvandilsFylgja_CustomBarrageFistMat"
            };
            if (s_barrageFistMaterial.HasProperty("_EmissionColor"))
            {
                s_barrageFistMaterial.SetColor("_EmissionColor", new Color(1.15f, 0.80f, 1.48f, 1f));
                s_barrageFistMaterial.EnableKeyword("_EMISSION");
            }
            float fistOpacity = Plugin.ModelOpacity < 0.99f
                ? Mathf.Clamp(Plugin.ModelOpacity * 0.85f, 0.30f, 0.85f)
                : 1.0f;
            StandEquipmentBuilder.ApplySpectralTransparency(s_barrageFistMaterial, Color.white, fistOpacity, hasDepthPrepass: false);
            return s_barrageFistMaterial;
        }

        public static Material GetFylgjaMaterial()
        {
            if (s_fylgjaMaterial != null) return s_fylgjaMaterial;

            EnsureTexturesLoaded();

            float opacity = Plugin.ModelOpacity;
            bool translucent = opacity < 0.99f;
            Shader shader = translucent
                ? StandEquipmentBuilder.FindValheimTransparentShader()
                : StandEquipmentBuilder.FindValheimOpaqueShader();
            Material mat = shader != null
                ? new Material(shader)
                : StandMaterialManager.GetMaterial(Color.white);

            if (mat != null)
            {
                mat.name = "AurvandilsFylgja_CustomValheimMat";
                mat.renderQueue = -1;
                if (s_albedoTex != null)
                {
                    mat.mainTexture = s_albedoTex;
                    if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", s_albedoTex);
                }

                mat.color = Color.white;
                if (mat.HasProperty("_Color")) mat.SetColor("_Color", Color.white);
                if (mat.HasProperty("_TintColor")) mat.SetColor("_TintColor", Color.white);
                if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", 0.22f);
                if (mat.HasProperty("_Glossiness")) mat.SetFloat("_Glossiness", 0.36f);

                if (s_normalTex != null && mat.HasProperty("_BumpMap"))
                {
                    mat.SetTexture("_BumpMap", s_normalTex);
                    if (mat.HasProperty("_BumpScale")) mat.SetFloat("_BumpScale", 0.85f);
                    mat.EnableKeyword("_NORMALMAP");
                }

                if (mat.HasProperty("_EmissionColor"))
                {
                    if (s_emissionTex != null && mat.HasProperty("_EmissionMap"))
                    {
                        mat.SetTexture("_EmissionMap", s_emissionTex);
                        // Boost glowing astral eyes, forehead amethyst gem & runes gently when translucent
                        mat.SetColor("_EmissionColor", translucent
                            ? new Color(1.05f, 0.86f, 1.32f, 1f)
                            : new Color(0.85f, 0.72f, 1.05f, 1f));
                    }
                    else
                    {
                        mat.SetColor("_EmissionColor", new Color(0.18f, 0.08f, 0.34f, 1f));
                    }
                    mat.EnableKeyword("_EMISSION");
                }

                bool hasPrepass = StandEquipmentBuilder.GetDepthWriteMaterial() != null;
                StandEquipmentBuilder.ApplySpectralTransparency(mat, Color.white, opacity, hasDepthPrepass: hasPrepass);
            }

            s_fylgjaMaterial = mat;
            return s_fylgjaMaterial;
        }

        private static void EnsureTexturesLoaded()
        {
            if (s_albedoTex == null)
            {
                s_albedoTex = IconManager.LoadTextureResource(
                    "aurvandils_fylgja_tex.png",
                    "AurvandilsFylgja_CustomAlbedo",
                    new[]
                    {
                        "aurvandils_fylgja_tex.png",
                        Path.Combine("resources", "aurvandils_fylgja_tex.png")
                    });
                if (s_albedoTex != null)
                {
                    // Valheim crisp pixel-art texel filtering
                    s_albedoTex.filterMode = FilterMode.Point;
                    s_albedoTex.wrapMode = TextureWrapMode.Clamp;
                }
            }

            if (s_emissionTex == null)
            {
                s_emissionTex = IconManager.LoadTextureResource(
                    "aurvandils_fylgja_emission.png",
                    "AurvandilsFylgja_CustomEmission",
                    new[]
                    {
                        "aurvandils_fylgja_emission.png",
                        Path.Combine("resources", "aurvandils_fylgja_emission.png")
                    });
                if (s_emissionTex != null)
                {
                    s_emissionTex.filterMode = FilterMode.Bilinear;
                    s_emissionTex.wrapMode = TextureWrapMode.Clamp;
                }
            }

            if (s_normalTex == null && s_albedoTex != null)
            {
                s_normalTex = BuildNormalMapFromAlbedo(s_albedoTex);
            }
        }

        private static Texture2D BuildNormalMapFromAlbedo(Texture2D albedo)
        {
            try
            {
                int w = albedo.width;
                int h = albedo.height;
                Color[] src = albedo.GetPixels();
                float[] lum = new float[w * h];
                for (int i = 0; i < src.Length; i++)
                {
                    Color c = src[i];
                    lum[i] = 0.299f * c.r + 0.587f * c.g + 0.114f * c.b;
                }

                Color[] dst = new Color[w * h];
                const float strength = 2.6f;
                for (int y = 0; y < h; y++)
                {
                    int ym = y > 0 ? y - 1 : y;
                    int yp = y < h - 1 ? y + 1 : y;
                    for (int x = 0; x < w; x++)
                    {
                        int xm = x > 0 ? x - 1 : x;
                        int xp = x < w - 1 ? x + 1 : x;
                        float dx = (lum[y * w + xm] - lum[y * w + xp]) * strength;
                        float dy = (lum[ym * w + x] - lum[yp * w + x]) * strength;
                        Vector3 n = new Vector3(dx, dy, 1.0f).normalized;
                        float nx = Mathf.Clamp01(n.x * 0.5f + 0.5f);
                        float ny = Mathf.Clamp01(n.y * 0.5f + 0.5f);
                        float nz = Mathf.Clamp01(n.z * 0.5f + 0.5f);
                        // Pack both standard RGB and Unity DXT5nm (x in A, y in G) so any normal unpacker works
                        dst[y * w + x] = new Color(nx, ny, nz, nx);
                    }
                }

                var normTex = new Texture2D(w, h, TextureFormat.RGBA32, true)
                {
                    name = "AurvandilsFylgja_CustomNormal",
                    wrapMode = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Bilinear
                };
                normTex.SetPixels(dst);
                normTex.Apply(updateMipmaps: true, makeNoLongerReadable: false);
                return normTex;
            }
            catch (Exception ex)
            {
                Plugin.Log?.LogWarning($"CustomValheimModelBuilder: Could not build normal map: {ex.Message}");
                return null;
            }
        }

        private static Mesh BuildSkinnedMeshForSkeleton(
            ParsedObjData objData,
            SkinnedMeshRenderer bodyModel,
            Animator animator,
            GameObject visual)
        {
            Mesh refMesh = bodyModel.sharedMesh;
            if (refMesh == null) return null;

            Matrix4x4[] refBindposes = refMesh.bindposes;
            Transform[] bones = bodyModel.bones;
            if (refBindposes == null || bones == null || refBindposes.Length != bones.Length || bones.Length == 0)
            {
                return null;
            }

            // 1. Resolve canonical 20 humanoid bones to indices in bodyModel.bones
            int[] boneIdx = ResolveCanonicalBoneIndices(bones, animator);

            // 2. Compute exact bind-pose positions of the 20 bones in refMesh vertex coordinate space
            Vector3[] refPivots = new Vector3[BoneCount];
            for (int b = 0; b < BoneCount; b++)
            {
                int idx = boneIdx[b];
                Matrix4x4 invBind = refBindposes[idx].inverse;
                refPivots[b] = invBind.MultiplyPoint3x4(Vector3.zero);
            }

            // 3. Establish orthonormal frame (Ex, Ey, Ez) in refMesh vertex space matching OBJ axes:
            //    +Ey = Up (Feet -> Head)
            //    +Ex = OBJ +X (RightHand -> LeftHand, since OBJ +X is character's Left arm)
            //    +Ez = Forward (Back/Heel -> Face/Toes)
            Vector3 ankleMidRef = 0.5f * (refPivots[16] + refPivots[19]);
            Vector3 ey = (refPivots[5] - ankleMidRef);
            float ankleToHeadRef = ey.magnitude;
            if (ankleToHeadRef < 0.0001f)
            {
                ey = Vector3.up;
                ankleToHeadRef = 1.645f;
            }
            else
            {
                ey /= ankleToHeadRef;
            }

            Vector3 rightToLeftRef = Vector3.ProjectOnPlane(refPivots[9] - refPivots[13], ey);
            Vector3 ex = rightToLeftRef.sqrMagnitude > 0.00001f
                ? rightToLeftRef.normalized
                : Vector3.left;

            Vector3 ez = Vector3.Cross(ex, ey).normalized;

            // Verify forward sign of Ez using actual Hips skinning transform into visual local space (+Z = forward)
            int hipsIdx = boneIdx[0];
            if (bones[hipsIdx] != null && visual != null)
            {
                Matrix4x4 hipsSkinToVisual = visual.transform.worldToLocalMatrix
                                           * bones[hipsIdx].localToWorldMatrix
                                           * refBindposes[hipsIdx];
                Vector3 ezInVisual = hipsSkinToVisual.MultiplyVector(ez);
                if (Vector3.Dot(ezInVisual, Vector3.forward) < 0f)
                {
                    ez = -ez;
                }
            }
            else
            {
                int lToeIdx = FindBoneIndexByName(bones, animator, HumanBodyBones.LeftToes, "LeftToeBase", "LeftToes");
                if (lToeIdx >= 0)
                {
                    Vector3 toePosRef = refBindposes[lToeIdx].inverse.MultiplyPoint3x4(Vector3.zero);
                    if (Vector3.Dot(toePosRef - refPivots[16], ez) < 0f)
                    {
                        ez = -ez;
                    }
                }
            }

            // Uniform body scale s in refMesh space (OBJ ankle Y=0.120 to Head Y=1.765 is 1.645m)
            float uniformScale = ankleToHeadRef / 1.645f;

            Vector3 ToRefVec(Vector3 vObj) => ex * vObj.x + ey * vObj.y + ez * vObj.z;

            // 4. Build per-bone segment rotations and axial scales so every parent-child joint matches
            //    T_parent(P_obj[child]) == P_ref[child] == T_child(P_obj[child]) with zero seam gap
            Quaternion[] segRot = new Quaternion[BoneCount];
            float[] segAxialScale = new float[BoneCount];
            Vector3[] segAxisObj = new Vector3[BoneCount];

            for (int b = 0; b < BoneCount; b++)
            {
                int child = s_primaryChild[b];
                if (child >= 0 && boneIdx[b] != boneIdx[child])
                {
                    Vector3 uObj = s_objPivots[child] - s_objPivots[b];
                    float lenObj = uObj.magnitude;
                    Vector3 wRef = refPivots[child] - refPivots[b];
                    float lenRef = wRef.magnitude;

                    if (lenObj > 0.0001f && lenRef > 0.0001f)
                    {
                        Vector3 uAxisObj = uObj / lenObj;
                        Vector3 uAxisRef0 = ToRefVec(uAxisObj).normalized;
                        Vector3 wAxisRef = wRef / lenRef;
                        segRot[b] = Quaternion.FromToRotation(uAxisRef0, wAxisRef);
                        segAxialScale[b] = lenRef / lenObj;
                        segAxisObj[b] = uAxisObj;
                        continue;
                    }
                }

                segRot[b] = Quaternion.identity;
                segAxialScale[b] = uniformScale;
                segAxisObj[b] = Vector3.up;
            }

            // Clenched hands (9, 13) inherit their forearm's orientation (8, 12) with 100% uniform scale
            segRot[9] = segRot[8];
            segAxialScale[9] = uniformScale;
            segAxisObj[9] = segAxisObj[8];

            segRot[13] = segRot[12];
            segAxialScale[13] = uniformScale;
            segAxisObj[13] = segAxisObj[12];

            // 5. Transform all vertices and normals from OBJ T-pose into refMesh bind-pose space
            BoneWeight[] canonicalWeights = GetCanonicalBoneWeights(objData.Positions);
            int vCount = objData.Positions.Length;
            var mappedVerts = new Vector3[vCount];
            var mappedNormals = new Vector3[vCount];
            var meshBoneWeights = new BoneWeight[vCount];

            Vector3 MapVertexForBone(int b, Vector3 pObj)
            {
                Vector3 delta = pObj - s_objPivots[b];
                Vector3 axis = segAxisObj[b];
                Vector3 dPar = Vector3.Dot(delta, axis) * axis;
                Vector3 dPerp = delta - dPar;
                Vector3 scaledObj = dPar * segAxialScale[b] + dPerp * uniformScale;
                return refPivots[b] + (segRot[b] * ToRefVec(scaledObj));
            }

            Vector3 MapNormalForBone(int b, Vector3 nObj)
            {
                return segRot[b] * ToRefVec(nObj);
            }

            for (int i = 0; i < vCount; i++)
            {
                Vector3 pObj = objData.Positions[i];
                Vector3 nObj = objData.Normals[i];
                BoneWeight cw = canonicalWeights[i];

                Vector3 pRef = Vector3.zero;
                Vector3 nRef = Vector3.zero;

                if (cw.weight0 > 0f)
                {
                    pRef += MapVertexForBone(cw.boneIndex0, pObj) * cw.weight0;
                    nRef += MapNormalForBone(cw.boneIndex0, nObj) * cw.weight0;
                }
                if (cw.weight1 > 0f)
                {
                    pRef += MapVertexForBone(cw.boneIndex1, pObj) * cw.weight1;
                    nRef += MapNormalForBone(cw.boneIndex1, nObj) * cw.weight1;
                }
                if (cw.weight2 > 0f)
                {
                    pRef += MapVertexForBone(cw.boneIndex2, pObj) * cw.weight2;
                    nRef += MapNormalForBone(cw.boneIndex2, nObj) * cw.weight2;
                }
                if (cw.weight3 > 0f)
                {
                    pRef += MapVertexForBone(cw.boneIndex3, pObj) * cw.weight3;
                    nRef += MapNormalForBone(cw.boneIndex3, nObj) * cw.weight3;
                }

                mappedVerts[i] = pRef;
                mappedNormals[i] = nRef.sqrMagnitude > 0.00001f ? nRef.normalized : ey;

                meshBoneWeights[i] = MergeDuplicateBoneIndices(
                    boneIdx[cw.boneIndex0], cw.weight0,
                    boneIdx[cw.boneIndex1], cw.weight1,
                    boneIdx[cw.boneIndex2], cw.weight2,
                    boneIdx[cw.boneIndex3], cw.weight3);
            }

            // 6. Preserve outward triangle winding in Unity's coordinate handedness
            float basisDet = Vector3.Dot(Vector3.Cross(ex, ey), ez);
            int[] srcTris = objData.Triangles;
            int[] dstTris = new int[srcTris.Length];
            if (basisDet >= 0f)
            {
                Array.Copy(srcTris, dstTris, srcTris.Length);
            }
            else
            {
                for (int t = 0; t < srcTris.Length; t += 3)
                {
                    dstTris[t]     = srcTris[t];
                    dstTris[t + 1] = srcTris[t + 2];
                    dstTris[t + 2] = srcTris[t + 1];
                }
            }

            var mesh = new Mesh
            {
                name = "AurvandilsFylgja_CustomValheimSkinnedMesh"
            };
            mesh.vertices = mappedVerts;
            mesh.normals = mappedNormals;
            mesh.uv = objData.UVs;
            mesh.triangles = dstTris;
            mesh.boneWeights = meshBoneWeights;
            mesh.bindposes = refBindposes;
            mesh.RecalculateBounds();
            try { mesh.RecalculateTangents(); } catch { }

            return mesh;
        }

        private static BoneWeight MergeDuplicateBoneIndices(
            int b0, float w0,
            int b1, float w1,
            int b2, float w2,
            int b3, float w3)
        {
            int[] idx = { b0, b1, b2, b3 };
            float[] w = { w0, w1, w2, w3 };

            for (int i = 0; i < 4; i++)
            {
                if (w[i] <= 0f) continue;
                for (int j = i + 1; j < 4; j++)
                {
                    if (w[j] > 0f && idx[i] == idx[j])
                    {
                        w[i] += w[j];
                        w[j] = 0f;
                    }
                }
            }

            // Sort descending by weight
            for (int i = 0; i < 3; i++)
            {
                for (int j = i + 1; j < 4; j++)
                {
                    if (w[j] > w[i])
                    {
                        float tw = w[i]; w[i] = w[j]; w[j] = tw;
                        int ti = idx[i]; idx[i] = idx[j]; idx[j] = ti;
                    }
                }
            }

            float sum = w[0] + w[1] + w[2] + w[3];
            if (sum > 0.0001f)
            {
                w[0] /= sum; w[1] /= sum; w[2] /= sum; w[3] /= sum;
            }
            else
            {
                w[0] = 1f;
            }

            return new BoneWeight
            {
                boneIndex0 = idx[0], weight0 = w[0],
                boneIndex1 = idx[1], weight1 = w[1],
                boneIndex2 = idx[2], weight2 = w[2],
                boneIndex3 = idx[3], weight3 = w[3],
            };
        }

        private static int[] ResolveCanonicalBoneIndices(Transform[] bones, Animator animator)
        {
            int[] idx = new int[BoneCount];
            idx[0]  = FindBoneIndexByName(bones, animator, HumanBodyBones.Hips, "Hips", "Pelvis");
            if (idx[0] < 0) idx[0] = 0;

            idx[1]  = FindBoneIndexByName(bones, animator, HumanBodyBones.Spine, "Spine");
            if (idx[1] < 0) idx[1] = idx[0];

            idx[2]  = FindBoneIndexByName(bones, animator, HumanBodyBones.Chest, "Spine1", "Chest");
            if (idx[2] < 0) idx[2] = idx[1];

            idx[3]  = FindBoneIndexByName(bones, animator, HumanBodyBones.UpperChest, "Spine2", "UpperChest");
            if (idx[3] < 0) idx[3] = idx[2];

            idx[4]  = FindBoneIndexByName(bones, animator, HumanBodyBones.Neck, "Neck");
            if (idx[4] < 0) idx[4] = idx[3];

            idx[5]  = FindBoneIndexByName(bones, animator, HumanBodyBones.Head, "Head");
            if (idx[5] < 0) idx[5] = idx[4];

            idx[6]  = FindBoneIndexByName(bones, animator, HumanBodyBones.LeftShoulder, "LeftShoulder", "L_Shoulder");
            if (idx[6] < 0) idx[6] = idx[3];

            idx[7]  = FindBoneIndexByName(bones, animator, HumanBodyBones.LeftUpperArm, "LeftArm", "LeftUpperArm");
            if (idx[7] < 0) idx[7] = idx[6];

            idx[8]  = FindBoneIndexByName(bones, animator, HumanBodyBones.LeftLowerArm, "LeftForeArm", "LeftLowerArm");
            if (idx[8] < 0) idx[8] = idx[7];

            idx[9]  = FindBoneIndexByName(bones, animator, HumanBodyBones.LeftHand, "LeftHand");
            if (idx[9] < 0) idx[9] = idx[8];

            idx[10] = FindBoneIndexByName(bones, animator, HumanBodyBones.RightShoulder, "RightShoulder", "R_Shoulder");
            if (idx[10] < 0) idx[10] = idx[3];

            idx[11] = FindBoneIndexByName(bones, animator, HumanBodyBones.RightUpperArm, "RightArm", "RightUpperArm");
            if (idx[11] < 0) idx[11] = idx[10];

            idx[12] = FindBoneIndexByName(bones, animator, HumanBodyBones.RightLowerArm, "RightForeArm", "RightLowerArm");
            if (idx[12] < 0) idx[12] = idx[11];

            idx[13] = FindBoneIndexByName(bones, animator, HumanBodyBones.RightHand, "RightHand");
            if (idx[13] < 0) idx[13] = idx[12];

            idx[14] = FindBoneIndexByName(bones, animator, HumanBodyBones.LeftUpperLeg, "LeftUpLeg", "LeftUpperLeg");
            if (idx[14] < 0) idx[14] = idx[0];

            idx[15] = FindBoneIndexByName(bones, animator, HumanBodyBones.LeftLowerLeg, "LeftLeg", "LeftLowerLeg");
            if (idx[15] < 0) idx[15] = idx[14];

            idx[16] = FindBoneIndexByName(bones, animator, HumanBodyBones.LeftFoot, "LeftFoot");
            if (idx[16] < 0) idx[16] = idx[15];

            idx[17] = FindBoneIndexByName(bones, animator, HumanBodyBones.RightUpperLeg, "RightUpLeg", "RightUpperLeg");
            if (idx[17] < 0) idx[17] = idx[0];

            idx[18] = FindBoneIndexByName(bones, animator, HumanBodyBones.RightLowerLeg, "RightLeg", "RightLowerLeg");
            if (idx[18] < 0) idx[18] = idx[17];

            idx[19] = FindBoneIndexByName(bones, animator, HumanBodyBones.RightFoot, "RightFoot");
            if (idx[19] < 0) idx[19] = idx[18];

            return idx;
        }

        private static int FindBoneIndexByName(Transform[] bones, Animator animator, HumanBodyBones humanBone, params string[] names)
        {
            if (animator != null && animator.isHuman)
            {
                Transform hb = animator.GetBoneTransform(humanBone);
                if (hb != null)
                {
                    for (int i = 0; i < bones.Length; i++)
                    {
                        if (bones[i] == hb) return i;
                    }
                }
            }

            // Exact match first
            foreach (var name in names)
            {
                for (int i = 0; i < bones.Length; i++)
                {
                    if (bones[i] != null && string.Equals(bones[i].name, name, StringComparison.OrdinalIgnoreCase))
                    {
                        return i;
                    }
                }
            }

            // Substring match fallback
            foreach (var name in names)
            {
                string lower = name.ToLowerInvariant();
                for (int i = 0; i < bones.Length; i++)
                {
                    if (bones[i] != null && bones[i].name.ToLowerInvariant().Contains(lower))
                    {
                        return i;
                    }
                }
            }

            return -1;
        }

        private static float SmoothStep(float e0, float e1, float x)
        {
            float t = Mathf.Clamp01((x - e0) / (e1 - e0));
            return t * t * (3f - 2f * t);
        }

        private static BoneWeight[] GetCanonicalBoneWeights(Vector3[] positions)
        {
            if (s_canonicalWeights != null && s_canonicalWeights.Length == positions.Length)
            {
                return s_canonicalWeights;
            }

            int n = positions.Length;
            var weights = new BoneWeight[n];

            for (int i = 0; i < n; i++)
            {
                Vector3 p = positions[i];
                float x = p.x;
                float y = p.y;
                float z = p.z;
                float ax = Mathf.Abs(x);
                bool isLeft = x >= 0f;

                // 1. Head, Hair Mane, Forehead Circlet & Astral Eyes
                if (y > 1.72f && ax < 0.16f)
                {
                    float wHead = SmoothStep(1.72f, 1.77f, y);
                    weights[i] = MakeWeight(5, wHead, 4, 1f - wHead);
                    continue;
                }

                // 2. Arms, Forged Viking Pauldrons & Clenched Combat Fists
                if (y > 1.44f && ax > 0.18f)
                {
                    int bSh = isLeft ? 6 : 10;
                    int bUa = isLeft ? 7 : 11;
                    int bLa = isLeft ? 8 : 12;
                    int bHa = isLeft ? 9 : 13;

                    // Shoulder pauldron plate & rivets
                    if (ax < 0.365f && y > 1.648f)
                    {
                        float tOut = SmoothStep(0.18f, 0.28f, ax);
                        weights[i] = MakeWeight(
                            bUa, 0.78f * tOut + 0.35f * (1f - tOut),
                            bSh, 0.22f * tOut + 0.45f * (1f - tOut),
                            3,   0.20f * (1f - tOut));
                        continue;
                    }

                    if (ax < 0.25f)
                    {
                        float t = SmoothStep(0.18f, 0.25f, ax);
                        weights[i] = MakeWeight(bUa, t, bSh, (1f - t) * 0.65f, 3, (1f - t) * 0.35f);
                    }
                    else if (ax < 0.50f)
                    {
                        weights[i] = MakeWeight(bUa, 1f);
                    }
                    else if (ax < 0.59f)
                    {
                        float t = SmoothStep(0.50f, 0.59f, ax);
                        weights[i] = MakeWeight(bUa, 1f - t, bLa, t);
                    }
                    else if (ax < 0.74f)
                    {
                        weights[i] = MakeWeight(bLa, 1f);
                    }
                    else if (ax < 0.79f)
                    {
                        float t = SmoothStep(0.74f, 0.79f, ax);
                        weights[i] = MakeWeight(bLa, 1f - t, bHa, t);
                    }
                    else
                    {
                        weights[i] = MakeWeight(bHa, 1f);
                    }
                    continue;
                }

                // 3. Front & Back Loincloth Panels (keep symmetric across centerline so panel never splits)
                if (ax <= 0.153f && y >= 0.79f && y <= 1.24f && (z > 0.08f || z < -0.10f))
                {
                    float tDown = SmoothStep(1.20f, 0.82f, y);
                    weights[i] = MakeWeight(0, 1f - 0.50f * tDown, 14, 0.25f * tDown, 17, 0.25f * tDown);
                    continue;
                }

                // 4. Legs, Knee Guards & Boots
                if (y < 1.18f)
                {
                    int bUl = isLeft ? 14 : 17;
                    int bLl = isLeft ? 15 : 18;
                    int bFt = isLeft ? 16 : 19;

                    if (y > 1.06f)
                    {
                        float t = SmoothStep(1.06f, 1.18f, y);
                        weights[i] = MakeWeight(0, t, bUl, 1f - t);
                    }
                    else if (y > 0.73f)
                    {
                        weights[i] = MakeWeight(bUl, 1f);
                    }
                    else if (y > 0.62f)
                    {
                        float t = SmoothStep(0.62f, 0.73f, y);
                        weights[i] = MakeWeight(bUl, t, bLl, 1f - t);
                    }
                    else if (y > 0.17f)
                    {
                        weights[i] = MakeWeight(bLl, 1f);
                    }
                    else if (y > 0.09f)
                    {
                        float t = SmoothStep(0.09f, 0.17f, y);
                        weights[i] = MakeWeight(bLl, t, bFt, 1f - t);
                    }
                    else
                    {
                        weights[i] = MakeWeight(bFt, 1f);
                    }
                    continue;
                }

                // 5. Torso, Spine, Crimson Scarf & Clavicle Root
                if (y < 1.25f)
                {
                    float t = SmoothStep(1.18f, 1.25f, y);
                    weights[i] = MakeWeight(0, 1f - t, 1, t);
                }
                else if (y < 1.38f)
                {
                    float t = SmoothStep(1.25f, 1.38f, y);
                    weights[i] = MakeWeight(1, 1f - t, 2, t);
                }
                else if (y < 1.52f)
                {
                    float t = SmoothStep(1.38f, 1.52f, y);
                    weights[i] = MakeWeight(2, 1f - t, 3, t);
                }
                else if (y < 1.66f)
                {
                    float shW = SmoothStep(0.10f, 0.20f, ax) * 0.45f;
                    int bSh = isLeft ? 6 : 10;
                    weights[i] = MakeWeight(3, 1f - shW, bSh, shW);
                }
                else
                {
                    float t = SmoothStep(1.66f, 1.73f, y);
                    weights[i] = MakeWeight(3, 1f - t, 4, t);
                }
            }

            s_canonicalWeights = weights;
            return s_canonicalWeights;
        }

        private static BoneWeight MakeWeight(int b0, float w0, int b1 = 0, float w1 = 0f, int b2 = 0, float w2 = 0f, int b3 = 0, float w3 = 0f)
        {
            return MergeDuplicateBoneIndices(b0, w0, b1, w1, b2, w2, b3, w3);
        }

        private static ParsedObjData GetCharacterObjData()
        {
            if (s_characterObj == null)
            {
                s_characterObj = LoadEmbeddedObj("aurvandils_fylgja.obj");
            }
            return s_characterObj;
        }

        private static ParsedObjData LoadEmbeddedObj(string resourceSuffix)
        {
            try
            {
                string text = null;

                // 1. Check disk override in plugins folder
                string dllDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                if (!string.IsNullOrEmpty(dllDir))
                {
                    string[] candidates =
                    {
                        resourceSuffix,
                        Path.Combine("resources", resourceSuffix)
                    };
                    foreach (var rel in candidates)
                    {
                        string path = Path.Combine(dllDir, rel);
                        if (File.Exists(path))
                        {
                            text = File.ReadAllText(path);
                            Plugin.Log?.LogInfo($"CustomValheimModelBuilder: Loaded OBJ from disk: {path}");
                            break;
                        }
                    }
                }

                // 2. Load from embedded assembly resource
                if (string.IsNullOrEmpty(text))
                {
                    var asm = Assembly.GetExecutingAssembly();
                    foreach (var resName in asm.GetManifestResourceNames())
                    {
                        if (resName.EndsWith(resourceSuffix, StringComparison.OrdinalIgnoreCase))
                        {
                            using (var stream = asm.GetManifestResourceStream(resName))
                            {
                                if (stream != null)
                                {
                                    using (var reader = new StreamReader(stream))
                                    {
                                        text = reader.ReadToEnd();
                                    }
                                    Plugin.Log?.LogInfo($"CustomValheimModelBuilder: Loaded embedded OBJ: {resName}");
                                    break;
                                }
                            }
                        }
                    }
                }

                if (string.IsNullOrEmpty(text)) return null;
                return ParseObjText(text);
            }
            catch (Exception ex)
            {
                Plugin.Log?.LogError($"CustomValheimModelBuilder: Error loading '{resourceSuffix}': {ex}");
                return null;
            }
        }

        private static ParsedObjData ParseObjText(string objText)
        {
            var rawV  = new List<Vector3>(1600);
            var rawVt = new List<Vector2>(1300);
            var rawVn = new List<Vector3>(3700);

            var outV  = new List<Vector3>(3800);
            var outVt = new List<Vector2>(3800);
            var outVn = new List<Vector3>(3800);
            var outTris = new List<int>(7800);
            var vertexMap = new Dictionary<long, int>(3800);

            var inv = CultureInfo.InvariantCulture;
            using (var sr = new StringReader(objText))
            {
                string line;
                while ((line = sr.ReadLine()) != null)
                {
                    line = line.Trim();
                    if (line.Length == 0 || line[0] == '#') continue;

                    if (line.StartsWith("v "))
                    {
                        string[] p = line.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                        if (p.Length >= 4)
                        {
                            rawV.Add(new Vector3(
                                float.Parse(p[1], inv),
                                float.Parse(p[2], inv),
                                float.Parse(p[3], inv)));
                        }
                    }
                    else if (line.StartsWith("vt "))
                    {
                        string[] p = line.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                        if (p.Length >= 3)
                        {
                            rawVt.Add(new Vector2(
                                float.Parse(p[1], inv),
                                float.Parse(p[2], inv)));
                        }
                    }
                    else if (line.StartsWith("vn "))
                    {
                        string[] p = line.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                        if (p.Length >= 4)
                        {
                            rawVn.Add(new Vector3(
                                float.Parse(p[1], inv),
                                float.Parse(p[2], inv),
                                float.Parse(p[3], inv)));
                        }
                    }
                    else if (line.StartsWith("f "))
                    {
                        string[] p = line.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                        if (p.Length < 4) continue;

                        int polyVerts = p.Length - 1;
                        int[] faceIdx = new int[polyVerts];
                        for (int k = 0; k < polyVerts; k++)
                        {
                            string[] sp = p[k + 1].Split('/');
                            int vi = int.Parse(sp[0], inv) - 1;
                            int ti = (sp.Length > 1 && sp[1].Length > 0) ? int.Parse(sp[1], inv) - 1 : 0;
                            int ni = (sp.Length > 2 && sp[2].Length > 0) ? int.Parse(sp[2], inv) - 1 : 0;

                            long key = ((long)vi << 40) | (((long)ti & 0xFFFFFL) << 20) | ((long)ni & 0xFFFFFL);
                            if (!vertexMap.TryGetValue(key, out int mergedIdx))
                            {
                                mergedIdx = outV.Count;
                                vertexMap[key] = mergedIdx;
                                outV.Add(vi >= 0 && vi < rawV.Count ? rawV[vi] : Vector3.zero);
                                outVt.Add(ti >= 0 && ti < rawVt.Count ? rawVt[ti] : Vector2.zero);
                                outVn.Add(ni >= 0 && ni < rawVn.Count ? rawVn[ni] : Vector3.up);
                            }
                            faceIdx[k] = mergedIdx;
                        }

                        for (int k = 1; k < polyVerts - 1; k++)
                        {
                            outTris.Add(faceIdx[0]);
                            outTris.Add(faceIdx[k]);
                            outTris.Add(faceIdx[k + 1]);
                        }
                    }
                }
            }

            return new ParsedObjData
            {
                Positions = outV.ToArray(),
                UVs = outVt.ToArray(),
                Normals = outVn.ToArray(),
                Triangles = outTris.ToArray()
            };
        }
    }
}
