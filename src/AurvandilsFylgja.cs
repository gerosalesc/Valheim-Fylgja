using System;
using System.Collections.Generic;
using UnityEngine;

namespace StandsOfValheim
{
    /// <summary>
    /// Builds Aurvandil's Fylgja using the base bulked Valheim humanoid rigged model:
    /// spectral purple musculature with dark forged-gold shoulder pads, waist belt, knee pads &amp; Dvergr circlet,
    /// red neck scarf, light grey loincloth, black boots &amp; gloves, and swept-up spiky black anime mane.
    /// </summary>
    internal static class AurvandilsFylgja
    {
        private static readonly Color StandPurple  = new Color(0.58f, 0.20f, 0.92f, 1.0f);
        private static readonly Color HairColor    = new Color(0.02f, 0.02f, 0.03f, 1.0f);
        private static readonly Color EmissionGlow = new Color(0.30f, 0.10f, 0.62f);

        public static GameObject Build(Player owner)
        {
            var root = new GameObject("AurvandilsFylgja");
            var follower = root.AddComponent<StandFollower>();
            follower.Owner = owner;

            // Bulked scale: Aurvandil's Fylgja is noticeably larger and more imposing than a normal human
            root.transform.localScale = new Vector3(1.22f, 1.22f, 1.22f);

            // Initial position and rotation matching player facing direction (not camera)
            root.transform.position = owner.transform.TransformPoint(follower.IdleOffset);
            root.transform.rotation = owner.transform.rotation;

            bool builtVisual = false;
            try
            {
                if (Plugin.UseCustom3DModel)
                {
                    builtVisual = CustomValheimModelBuilder.TryBuild(root, follower, owner);
                    if (!builtVisual)
                    {
                        Plugin.Log?.LogWarning("AurvandilsFylgja: CustomValheim3D model failed, falling back to ClassicProcedural model.");
                        builtVisual = TryBuildAssetModel(root, follower, owner);
                    }
                }
                else
                {
                    builtVisual = TryBuildAssetModel(root, follower, owner);
                    if (!builtVisual)
                    {
                        Plugin.Log?.LogWarning("AurvandilsFylgja: ClassicProcedural model failed, falling back to CustomValheim3D model.");
                        builtVisual = CustomValheimModelBuilder.TryBuild(root, follower, owner);
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.Log?.LogError($"AurvandilsFylgja: Failed to build asset model: {ex}");
            }

            if (!builtVisual)
            {
                Plugin.Log?.LogWarning("AurvandilsFylgja: Falling back to procedural primitive model.");
                BuildPrimitiveFallback(root, follower);
            }

            // Remove all physics colliders from the Fylgja so it floats freely without physics interference
            foreach (var col in root.GetComponentsInChildren<Collider>(true))
            {
                UnityEngine.Object.Destroy(col);
            }

            // --- Fylgja Aura Point Light ---
            var lightObj = new GameObject("StandAura");
            lightObj.transform.SetParent(root.transform, false);
            lightObj.transform.localPosition = new Vector3(0, 1.35f, 0);
            var aura = lightObj.AddComponent<Light>();
            aura.type = LightType.Point;
            aura.color = new Color(0.70f, 0.25f, 1.0f);
            aura.range = 5.4f;
            aura.intensity = 3.2f;
            aura.shadows = LightShadows.None;
            follower.Aura = aura;

            // --- Rising Astral Levitation Motes ---
            AttachFloatingAuraParticles(root);

            // --- Audio Source (ORA Barrage) ---
            var audioSource = root.AddComponent<AudioSource>();
            audioSource.spatialBlend = 0.8f;
            audioSource.playOnAwake = false;
            audioSource.loop = true;
            audioSource.minDistance = 2f;
            audioSource.maxDistance = 35f;
            audioSource.volume = 1.0f;
            follower.StandAudio = audioSource;

            // --- Guardian Power OOB Activation VFX/Lights & Camera Shake ---
            SpawnEntranceVFX(root.transform.position, root.transform.rotation, root.transform);

            return root;
        }

        private static void SpawnEntranceVFX(Vector3 position, Quaternion rotation, Transform standRoot)
        {
            try
            {
                bool spawnedActivationFx = false;
                var activationEffects = SE_AurvandilsFylgja.ResolveActivationEffects();
                if (activationEffects != null)
                {
                    Vector3 effectCenter = position + Vector3.up * 1.15f;
                    var created = activationEffects.Create(effectCenter, rotation, standRoot, 1.0f);
                    spawnedActivationFx = created != null && created.Length > 0;
                }

                if (!spawnedActivationFx && ZNetScene.instance != null)
                {
                    string[] vfxCandidates = { "vfx_ghost_spawn", "vfx_spawn_large", "vfx_spawn", "vfx_prespawn" };
                    foreach (var name in vfxCandidates)
                    {
                        var prefab = ZNetScene.instance.GetPrefab(name);
                        if (prefab != null)
                        {
                            UnityEngine.Object.Instantiate(prefab, position, rotation);
                            break;
                        }
                    }
                }

                if (GameCamera.instance != null)
                {
                    GameCamera.instance.AddShake(position, 10f, 0.35f, false);
                }
            }
            catch (Exception ex)
            {
                Plugin.Log?.LogWarning($"AurvandilsFylgja: Entrance VFX failed: {ex.Message}");
            }
        }

        private static bool TryBuildAssetModel(GameObject root, StandFollower follower, Player owner)
        {
            // 1. Locate source player Visual hierarchy
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
                Plugin.Log?.LogWarning("AurvandilsFylgja: Could not find source player Visual.");
                return false;
            }

            // 2. Clone the visual hierarchy
            GameObject visual = UnityEngine.Object.Instantiate(sourceVisual, root.transform);
            visual.name = "Visual";
            visual.SetActive(true);
            visual.transform.localPosition = Vector3.zero;
            visual.transform.localRotation = Quaternion.identity;

            // Remove all CharacterAnimEvent components synchronously so Animator never invokes Player RPCs or crashes
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

            // Strip any existing clothes/equipment and beards attached to source player
            CleanExistingEquipment(visual);

            // 3. Setup Body SkinnedMeshRenderer
            SkinnedMeshRenderer bodyModel = visual.GetComponentInChildren<SkinnedMeshRenderer>();
            if (bodyModel == null)
            {
                Plugin.Log?.LogWarning("AurvandilsFylgja: SkinnedMeshRenderer not found in visual.");
                return false;
            }

            // Ensure base male/muscular mesh and material
            var ownerVis = owner != null ? owner.GetComponent<VisEquipment>() : null;
            if (ownerVis != null && ownerVis.m_models != null && ownerVis.m_models.Length > 0)
            {
                var baseModel = ownerVis.m_models[0];
                if (baseModel.m_mesh != null) bodyModel.sharedMesh = baseModel.m_mesh;
                if (baseModel.m_baseMaterial != null) bodyModel.material = new Material(baseModel.m_baseMaterial);
            }

            // 4. Locate Head bone & Equip Golden Dvergr Circlet + swept-up Aurvandil's Fylgja spiky black mane
            var animator = visual.GetComponent<Animator>();
            Transform headBone = FindBone(bodyModel, visual, HumanBodyBones.Head, "Head");
            Transform neckBone = FindBone(bodyModel, visual, HumanBodyBones.Neck, "Neck");

            if (headBone != null)
            {
                GameObject circlet = EquipHeadPiece("HelmetDverger", headBone, visual, ownerVis);
                StandEquipmentBuilder.AttachAurvandilsFylgjaHair(visual, headBone, neckBone, circlet);
            }

            // 5. Equip Aurvandil's Fylgja's custom outfit: golden shoulder pads, red scarf, light grey loincloth, black gloves, black boots
            StandEquipmentBuilder.EquipStandOutfit(visual, bodyModel, headBone, (bone, fallback) => FindBone(bodyModel, visual, bone, fallback));

            // 6. Apply spectral materials (translucent purple body, crisp gold circlet, black hair)
            ApplySpectralStandMaterials(visual, follower);

            // 7. Wire limb & leg bones and animator to StandFollower, then settle pose & play OOB activation anim
            follower.StandAnimator = animator;
            follower.Head      = headBone;
            follower.Torso     = FindBone(bodyModel, visual, HumanBodyBones.Chest, "Chest") ?? visual.transform;
            follower.Hips      = FindBone(bodyModel, visual, HumanBodyBones.Hips, "Hips");
            follower.ArmL      = FindBone(bodyModel, visual, HumanBodyBones.LeftLowerArm, "LeftArm");
            follower.ArmR      = FindBone(bodyModel, visual, HumanBodyBones.RightLowerArm, "RightArm");
            follower.FistL     = FindBone(bodyModel, visual, HumanBodyBones.LeftHand, "LeftHand");
            follower.FistR     = FindBone(bodyModel, visual, HumanBodyBones.RightHand, "RightHand");
            follower.UpperLegL = FindBone(bodyModel, visual, HumanBodyBones.LeftUpperLeg, "LeftUpLeg");
            follower.LowerLegL = FindBone(bodyModel, visual, HumanBodyBones.LeftLowerLeg, "LeftLeg");
            follower.FootL     = FindBone(bodyModel, visual, HumanBodyBones.LeftFoot, "LeftFoot");
            follower.UpperLegR = FindBone(bodyModel, visual, HumanBodyBones.RightUpperLeg, "RightUpLeg");
            follower.LowerLegR = FindBone(bodyModel, visual, HumanBodyBones.RightLowerLeg, "RightLeg");
            follower.FootR     = FindBone(bodyModel, visual, HumanBodyBones.RightFoot, "RightFoot");

            InitializeStandAnimator(animator, follower);

            Plugin.Log?.LogInfo("AurvandilsFylgja: Constructed Fylgja with dark forged-gold shoulder pads, belt, knee pads & circlet, neck red scarf, light grey loincloth, black boots & gloves, and swept-up spiky black mane.");
            return true;
        }

        internal static void InitializeStandAnimator(Animator animator, StandFollower follower)
        {
            if (animator == null) return;

            animator.enabled = true;
            // Disable AnimationEvent dispatch (Speed, TrailOn, Hit, FootStep, GPower) on cloned visual
            animator.fireEvents = false;
            // Re-pose bones every render frame so StandFollower.LateUpdate offsets never stack on a stale pose
            animator.updateMode = AnimatorUpdateMode.Normal;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

            var paramTypes = new Dictionary<string, AnimatorControllerParameterType>();
            var triggers = new List<int>();
            foreach (var p in animator.parameters)
            {
                paramTypes[p.name] = p.type;
                if (p.type == AnimatorControllerParameterType.Trigger)
                {
                    triggers.Add(p.nameHash);
                }
            }

            // Clear Valheim's default spawn-sitting ("wakeup") & transient status params immediately
            string[] falseBools =
            {
                "wakeup", "intro", "sitting", "crouching", "crouch", "cutscene",
                "dead", "sleeping", "blocking", "inWater", "encumbered", "falling",
                "flying", "freeze", "skategliding", "skating", "slipping"
            };
            foreach (var b in falseBools)
            {
                if (paramTypes.TryGetValue(b, out var t) && t == AnimatorControllerParameterType.Bool)
                {
                    animator.SetBool(b, false);
                }
            }

            if (paramTypes.TryGetValue("onGround", out var ogType) && ogType == AnimatorControllerParameterType.Bool)
            {
                animator.SetBool("onGround", true);
            }

            string[] zeroFloats = { "forward_speed", "sideway_speed", "turn_speed", "tilt", "statef" };
            foreach (var f in zeroFloats)
            {
                if (paramTypes.TryGetValue(f, out var t) && t == AnimatorControllerParameterType.Float)
                {
                    animator.SetFloat(f, 0f);
                }
            }

            string[] zeroInts = { "statei", "crafting", "emote" };
            foreach (var n in zeroInts)
            {
                if (paramTypes.TryGetValue(n, out var t) && t == AnimatorControllerParameterType.Int)
                {
                    animator.SetInteger(n, 0);
                }
            }

            foreach (int trigHash in triggers)
            {
                animator.ResetTrigger(trigHash);
            }

            // Synchronously advance past the ~1-2s wakeup stand-up transition before the first rendered frame
            for (int i = 0; i < 30; i++)
            {
                animator.Update(0.1f);
            }

            // Snapshot each layer's resting upright state so punching/blocking can cancel gpower instantaneously
            int[] idleStateHashes = new int[animator.layerCount];
            for (int layer = 0; layer < idleStateHashes.Length; layer++)
            {
                idleStateHashes[layer] = animator.GetCurrentAnimatorStateInfo(layer).fullPathHash;
            }
            follower.ConfigureAnimatorStates(idleStateHashes);

            if (paramTypes.TryGetValue("gpower", out var gpType) && gpType == AnimatorControllerParameterType.Trigger)
            {
                animator.SetTrigger("gpower");
                animator.Update(0.01f);
                follower.NotifyIntroGPowerStarted();
            }
        }

        internal static void CleanExistingEquipment(GameObject visual)
        {
            // Remove any beard objects in hierarchy
            foreach (var t in visual.GetComponentsInChildren<Transform>(true))
            {
                if (t != null && t.gameObject.name.ToLower().Contains("beard"))
                {
                    t.gameObject.SetActive(false);
                    UnityEngine.Object.Destroy(t.gameObject);
                }
            }

            for (int i = visual.transform.childCount - 1; i >= 0; i--)
            {
                Transform child = visual.transform.GetChild(i);
                string n = child.gameObject.name;
                if (n == "Armature" || n.StartsWith("body") || n.StartsWith("PlayerModel"))
                {
                    continue;
                }
                if (n.Contains("(Clone)") || n.StartsWith("attach_") || child.GetComponent<ItemDrop>() != null)
                {
                    child.gameObject.SetActive(false);
                    UnityEngine.Object.Destroy(child.gameObject);
                }
            }
        }

        private static GameObject EquipHeadPiece(string itemName, Transform headBone, GameObject visual, VisEquipment ownerVis)
        {
            if (headBone == null || ObjectDB.instance == null) return null;
            GameObject itemPrefab = ObjectDB.instance.GetItemPrefab(itemName);
            if (itemPrefab == null)
            {
                Plugin.Log?.LogWarning($"AurvandilsFylgja: Item prefab '{itemName}' not found in ObjectDB.");
                return null;
            }

            Transform attachSource = null;
            for (int i = 0; i < itemPrefab.transform.childCount; i++)
            {
                Transform child = itemPrefab.transform.GetChild(i);
                if (child.name == "attach" || child.name.StartsWith("attach_"))
                {
                    attachSource = child;
                    break;
                }
            }
            if (attachSource == null) return null;

            // Attach to Valheim's exact Helmet mount point ("Helmet_attach") inside our cloned visual
            // when available, and explicitly normalize scale against Armature's 100x bone scaling.
            Transform attachParent = null;
            VisEquipment refVis = ownerVis ?? (Game.instance != null && Game.instance.m_playerPrefab != null
                ? Game.instance.m_playerPrefab.GetComponent<VisEquipment>()
                : null);
            if (refVis != null && refVis.m_helmet != null && visual != null)
            {
                attachParent = Utils.FindChild(visual.transform, refVis.m_helmet.name);
            }
            if (attachParent == null)
            {
                attachParent = headBone;
            }

            Transform equipOffset = itemPrefab.transform.Find("equipoffset");
            Vector3 offsetPos = equipOffset != null ? equipOffset.position : Vector3.zero;
            Quaternion offsetRot = equipOffset != null ? equipOffset.rotation : Quaternion.identity;

            GameObject attached = UnityEngine.Object.Instantiate(attachSource.gameObject);
            attached.name = "StandCirclet";
            attached.SetActive(true);
            attached.transform.SetParent(attachParent, false);

            Vector3 visualScale = visual != null ? visual.transform.lossyScale : Vector3.one;
            Vector3 parentScale = attachParent.lossyScale;

            // Match VisEquipment's Helmet offset in meters cleanly even if attached to a 100x Armature bone
            float posNormX = Mathf.Abs(parentScale.x) > 10f ? visualScale.x / parentScale.x : 1f;
            float posNormY = Mathf.Abs(parentScale.y) > 10f ? visualScale.y / parentScale.y : 1f;
            float posNormZ = Mathf.Abs(parentScale.z) > 10f ? visualScale.z / parentScale.z : 1f;
            attached.transform.localPosition = new Vector3(offsetPos.x * posNormX, offsetPos.y * posNormY, offsetPos.z * posNormZ);
            attached.transform.localRotation = offsetRot;

            // Normalize world scale to attachSource * visualScale * 1.05f (clearance over Painter Curls)
            const float circletClearance = 1.05f;
            Vector3 desiredWorldScale = new Vector3(
                attachSource.localScale.x * visualScale.x * circletClearance,
                attachSource.localScale.y * visualScale.y * circletClearance,
                attachSource.localScale.z * visualScale.z * circletClearance);
            attached.transform.localScale = new Vector3(
                Mathf.Abs(parentScale.x) > 0.0001f ? desiredWorldScale.x / parentScale.x : attachSource.localScale.x,
                Mathf.Abs(parentScale.y) > 0.0001f ? desiredWorldScale.y / parentScale.y : attachSource.localScale.y,
                Mathf.Abs(parentScale.z) > 0.0001f ? desiredWorldScale.z / parentScale.z : attachSource.localScale.z);

            Plugin.Log?.LogInfo($"AurvandilsFylgja: Equipped StandCirclet on '{attachParent.name}' (parentScale={parentScale}, localScale={attached.transform.localScale}, worldScale={attached.transform.lossyScale}).");

            // Keep forehead jewel visual active while removing headlamp SpotLights and stray colliders
            Transform equipedChild = attached.transform.Find("equiped");
            if (equipedChild != null)
            {
                equipedChild.gameObject.SetActive(true);
            }
            foreach (var lightComp in attached.GetComponentsInChildren<Light>(true))
            {
                lightComp.enabled = false;
                UnityEngine.Object.Destroy(lightComp);
            }
            foreach (var col in attached.GetComponentsInChildren<Collider>(true))
            {
                col.enabled = false;
                UnityEngine.Object.Destroy(col);
            }

            foreach (var r in attached.GetComponentsInChildren<Renderer>(true))
            {
                if (r == null || !(r is MeshRenderer || r is SkinnedMeshRenderer)) continue;
                var mats = r.materials;
                if (mats != null)
                {
                    foreach (var mat in mats)
                    {
                        StandEquipmentBuilder.ApplyGoldTint(mat);
                    }
                    r.materials = mats;
                }
            }

            return attached;
        }

        private static bool HasNameInHierarchy(Transform current, Transform stopAt, params string[] keywords)
        {
            while (current != null && current != stopAt)
            {
                string n = current.name.ToLower();
                for (int i = 0; i < keywords.Length; i++)
                {
                    if (n.Contains(keywords[i])) return true;
                }
                current = current.parent;
            }
            return false;
        }

        private static Texture2D s_softMoteTexture;
        private static Material s_softMoteMaterial;

        private static void AttachFloatingAuraParticles(GameObject root)
        {
            try
            {
                var moteObj = new GameObject("StandFloatMotes");
                moteObj.transform.SetParent(root.transform, false);
                moteObj.transform.localPosition = new Vector3(0f, 0.35f, 0f);

                var ps = moteObj.AddComponent<ParticleSystem>();
                var main = ps.main;
                main.loop = true;
                main.playOnAwake = true;
                main.simulationSpace = ParticleSystemSimulationSpace.World;
                main.startLifetime = new ParticleSystem.MinMaxCurve(1.0f, 1.85f);
                main.startSpeed = new ParticleSystem.MinMaxCurve(0.28f, 0.65f);
                main.startSize = new ParticleSystem.MinMaxCurve(0.06f, 0.14f);
                main.startColor = new ParticleSystem.MinMaxGradient(
                    new Color(0.72f, 0.38f, 1.00f, 0.65f),
                    new Color(0.95f, 0.72f, 1.00f, 0.55f));
                main.gravityModifier = -0.14f;
                main.maxParticles = 32;

                var emission = ps.emission;
                emission.enabled = true;
                emission.rateOverTime = 14f;

                var shape = ps.shape;
                shape.enabled = true;
                shape.shapeType = ParticleSystemShapeType.ConeVolume;
                shape.angle = 6f;
                shape.radius = 0.42f;
                shape.radiusThickness = 0.85f;
                shape.length = 0.55f;
                shape.rotation = new Vector3(-90f, 0f, 0f);

                var vel = ps.velocityOverLifetime;
                vel.enabled = true;
                vel.space = ParticleSystemSimulationSpace.Local;
                vel.y = new ParticleSystem.MinMaxCurve(0.25f, 0.55f);

                var colOverLife = ps.colorOverLifetime;
                colOverLife.enabled = true;
                var grad = new Gradient();
                grad.SetKeys(
                    new[]
                    {
                        new GradientColorKey(new Color(0.68f, 0.32f, 1.00f), 0.0f),
                        new GradientColorKey(new Color(0.92f, 0.70f, 1.00f), 0.55f),
                        new GradientColorKey(new Color(0.55f, 0.22f, 0.95f), 1.0f),
                    },
                    new[]
                    {
                        new GradientAlphaKey(0.0f, 0.0f),
                        new GradientAlphaKey(0.65f, 0.22f),
                        new GradientAlphaKey(0.45f, 0.72f),
                        new GradientAlphaKey(0.0f, 1.0f),
                    });
                colOverLife.color = new ParticleSystem.MinMaxGradient(grad);

                var sizeOverLife = ps.sizeOverLifetime;
                sizeOverLife.enabled = true;
                var curve = new AnimationCurve(
                    new Keyframe(0.0f, 0.35f),
                    new Keyframe(0.30f, 1.0f),
                    new Keyframe(1.0f, 0.15f));
                sizeOverLife.size = new ParticleSystem.MinMaxCurve(1.0f, curve);

                var psr = moteObj.GetComponent<ParticleSystemRenderer>();
                if (psr != null)
                {
                    psr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    psr.receiveShadows = false;
                    Material moteMat = GetSoftMoteMaterial();
                    if (moteMat != null)
                    {
                        psr.sharedMaterial = moteMat;
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.Log?.LogWarning($"AurvandilsFylgja: Could not attach floating aura particles: {ex.Message}");
            }
        }

        private static Material GetSoftMoteMaterial()
        {
            if (s_softMoteMaterial != null) return s_softMoteMaterial;

            if (s_softMoteTexture == null)
            {
                const int sz = 32;
                s_softMoteTexture = new Texture2D(sz, sz, TextureFormat.RGBA32, false)
                {
                    name = "AurvandilsFylgja_FloatMoteTex",
                    wrapMode = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Bilinear
                };
                var px = new Color[sz * sz];
                float half = (sz - 1) * 0.5f;
                for (int y = 0; y < sz; y++)
                {
                    for (int x = 0; x < sz; x++)
                    {
                        float dx = (x - half) / half;
                        float dy = (y - half) / half;
                        float r = Mathf.Sqrt(dx * dx + dy * dy);
                        float falloff = Mathf.Clamp01(1f - r);
                        float glow = falloff * falloff * falloff;
                        px[y * sz + x] = new Color(1f, 1f, 1f, glow);
                    }
                }
                s_softMoteTexture.SetPixels(px);
                s_softMoteTexture.Apply(false, false);
            }

            string[] candidateShaders =
            {
                "Custom/AlphaParticle",
                "Particles/Standard Unlit",
                "Legacy Shaders/Particles/Additive",
                "Sprites/Default",
                "UI/Default"
            };
            Shader sh = null;
            foreach (var sname in candidateShaders)
            {
                var found = Shader.Find(sname);
                if (found != null && found.isSupported)
                {
                    sh = found;
                    break;
                }
            }
            if (sh == null)
            {
                foreach (var loaded in Resources.FindObjectsOfTypeAll<Shader>())
                {
                    if (loaded != null && loaded.isSupported &&
                        (loaded.name == "Custom/AlphaParticle" || loaded.name == "Sprites/Default"))
                    {
                        sh = loaded;
                        break;
                    }
                }
            }
            if (sh == null) return null;

            s_softMoteMaterial = new Material(sh)
            {
                name = "AurvandilsFylgja_FloatMoteMat",
                mainTexture = s_softMoteTexture,
                renderQueue = 3003
            };
            if (s_softMoteMaterial.HasProperty("_MainTex")) s_softMoteMaterial.SetTexture("_MainTex", s_softMoteTexture);
            if (s_softMoteMaterial.HasProperty("_Color")) s_softMoteMaterial.SetColor("_Color", Color.white);
            if (s_softMoteMaterial.HasProperty("_TintColor")) s_softMoteMaterial.SetColor("_TintColor", Color.white);
            return s_softMoteMaterial;
        }

        private static void ApplySpectralStandMaterials(GameObject visual, StandFollower follower)
        {
            float opacity = Plugin.ModelOpacity;
            bool translucent = opacity < 0.99f;
            bool hasPrepass = StandEquipmentBuilder.GetDepthWriteMaterial() != null;

            var renderers = visual.GetComponentsInChildren<Renderer>(true);
            foreach (var r in renderers)
            {
                if (r == null) continue;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;

                // Preserve dedicated PBR materials built by StandEquipmentBuilder (already configured with spectral transparency)
                if (HasNameInHierarchy(r.transform, visual.transform, "shoulderpad", "scarf", "glove", "boot", "loincloth", "belt", "kneepad", "hairmane"))
                {
                    if (translucent && follower != null)
                    {
                        var eqMats = r.materials;
                        for (int m = 0; m < eqMats.Length; m++)
                        {
                            if (eqMats[m] != null && eqMats[m].renderQueue == StandEquipmentBuilder.SpectralTransparentQueue)
                            {
                                follower.RegisterSpectralMaterial(eqMats[m]);
                            }
                        }
                        r.materials = eqMats;
                    }
                    continue;
                }

                bool isHair = HasNameInHierarchy(r.transform, visual.transform, "hair");
                bool isCirclet = HasNameInHierarchy(r.transform, visual.transform, "dverger", "circlet");

                if (isCirclet && translucent)
                {
                    var mats = r.materials;
                    if (mats != null && mats.Length == 1 && mats[0] != null)
                    {
                        StandEquipmentBuilder.ApplyGoldTint(mats[0]);
                        StandEquipmentBuilder.ApplyRendererSpectralMaterials(r, mats[0], opacity);
                        follower?.RegisterSpectralMaterial(mats[0]);
                        continue;
                    }
                }

                if (!isHair && !isCirclet && translucent)
                {
                    // Swap ClassicProcedural body from Custom/Player (alpha-cutout skin mask) to Valheim's transparent
                    // PBR shader ('Standard TwoSided') with solid-alpha purple texture so spectral translucency renders cleanly.
                    Shader transparentShader = StandEquipmentBuilder.FindValheimTransparentShader();
                    if (transparentShader != null)
                    {
                        var bodyMat = new Material(transparentShader)
                        {
                            name = "AurvandilsFylgja_ClassicSpectralBodyMat",
                            mainTexture = StandEquipmentBuilder.CreateSolidTexture(StandPurple)
                        };
                        if (bodyMat.HasProperty("_MainTex")) bodyMat.SetTexture("_MainTex", bodyMat.mainTexture);
                        if (bodyMat.HasProperty("_Metallic")) bodyMat.SetFloat("_Metallic", 0.14f);
                        if (bodyMat.HasProperty("_Glossiness")) bodyMat.SetFloat("_Glossiness", 0.38f);
                        if (bodyMat.HasProperty("_EmissionColor"))
                        {
                            bodyMat.SetColor("_EmissionColor", EmissionGlow);
                            bodyMat.EnableKeyword("_EMISSION");
                        }

                        StandEquipmentBuilder.ApplySpectralTransparency(bodyMat, Color.white, opacity, hasDepthPrepass: hasPrepass);
                        StandEquipmentBuilder.ApplyRendererSpectralMaterials(r, bodyMat, opacity);
                        follower?.RegisterSpectralMaterial(bodyMat);
                        continue;
                    }
                }

                var fallbackMats = r.materials;
                for (int i = 0; i < fallbackMats.Length; i++)
                {
                    var mat = fallbackMats[i];
                    if (mat == null) continue;

                    if (isHair)
                    {
                        mat.renderQueue = -1;
                        mat.color = HairColor;
                        if (mat.HasProperty("_SkinColor")) mat.SetColor("_SkinColor", HairColor);
                        if (mat.HasProperty("_Color")) mat.SetColor("_Color", HairColor);
                        continue;
                    }

                    if (isCirclet)
                    {
                        StandEquipmentBuilder.ApplyGoldTint(mat);
                        continue;
                    }

                    // Opaque mode (ModelOpacity == 1.0): keep Custom/Player in its native opaque G-buffer queue with alpha 1.0
                    // so Valheim's player cutout mask never shreds bare skin into holes.
                    mat.renderQueue = -1;
                    mat.color = StandPurple;

                    if (mat.HasProperty("_Color"))
                    {
                        Color c = mat.GetColor("_Color");
                        c.r = Mathf.Lerp(c.r, StandPurple.r, 0.65f);
                        c.g = Mathf.Lerp(c.g, StandPurple.g, 0.65f);
                        c.b = Mathf.Lerp(c.b, StandPurple.b, 0.65f);
                        c.a = 1.0f;
                        mat.SetColor("_Color", c);
                    }

                    if (mat.HasProperty("_SkinColor"))
                    {
                        mat.SetColor("_SkinColor", StandPurple);
                    }

                    if (mat.HasProperty("_EmissionColor"))
                    {
                        mat.SetColor("_EmissionColor", EmissionGlow);
                        mat.EnableKeyword("_EMISSION");
                    }
                }
                r.materials = fallbackMats;
            }
        }

        internal static Transform FindBone(SkinnedMeshRenderer smr, GameObject visual, HumanBodyBones boneId, string fallbackName)
        {
            var anim = visual.GetComponent<Animator>();
            if (anim != null && anim.isHuman)
            {
                var b = anim.GetBoneTransform(boneId);
                if (b != null) return b;
            }

            if (smr != null && smr.bones != null)
            {
                string lower = fallbackName.ToLower();
                foreach (var b in smr.bones)
                {
                    if (b != null && b.name.ToLower().Contains(lower)) return b;
                }
            }

            return Utils.FindChild(visual.transform, fallbackName);
        }

        private static void BuildPrimitiveFallback(GameObject root, StandFollower follower)
        {
            var goldTrim = new Color(0.95f, 0.82f, 0.22f);
            follower.Torso = Part(root, PrimitiveType.Capsule, "Torso", new Vector3(0, 1.15f, 0), new Vector3(0.55f, 0.45f, 0.35f), StandPurple);
            follower.Head = Part(root, PrimitiveType.Sphere, "Head", new Vector3(0, 1.75f, 0), new Vector3(0.35f, 0.40f, 0.35f), StandPurple);
            follower.ArmL = Part(root, PrimitiveType.Capsule, "ArmL", new Vector3(-0.48f, 1.10f, 0.05f), new Vector3(0.18f, 0.36f, 0.18f), StandPurple);
            follower.ArmR = Part(root, PrimitiveType.Capsule, "ArmR", new Vector3(0.48f, 1.10f, 0.05f), new Vector3(0.18f, 0.36f, 0.18f), StandPurple);
            follower.FistL = Part(root, PrimitiveType.Sphere, "FistL", new Vector3(-0.48f, 0.68f, 0.15f), new Vector3(0.25f, 0.25f, 0.25f), StandPurple);
            follower.FistR = Part(root, PrimitiveType.Sphere, "FistR", new Vector3(0.48f, 0.68f, 0.15f), new Vector3(0.25f, 0.25f, 0.25f), StandPurple);
        }

        private static Transform Part(GameObject parent, PrimitiveType type, string name, Vector3 localPos, Vector3 localScale, Color color)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            var col = go.GetComponent<Collider>();
            if (col != null) UnityEngine.Object.Destroy(col);

            go.transform.SetParent(parent.transform, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = localScale;

            var renderer = go.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.material = StandMaterialManager.GetMaterial(color);
            }
            return go.transform;
        }
    }
}
