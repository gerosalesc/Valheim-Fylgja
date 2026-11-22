using System;
using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace StandsOfValheim
{
    /// <summary>
    /// Registers the custom Aurvandil's Fylgja Sacrificial Stone prefab ("BossStone_AurvandilsFylgja"),
    /// customizes its 3D mesh, textures, cosmic glow, RuneStone inscription, and ItemStand hook for TrophyFallenValkyrie,
    /// places the monolith at a random Deep North location per world seed (removing any legacy StartTemple copy),
    /// and places a glowing Astral Vegvisir inside Lord Reto's Tomb (Ashlands, PlaceofMystery3) to reveal its exact coordinates.
    /// </summary>
    internal static class SacrificialStoneManager
    {
        public const string PrefabName = "BossStone_AurvandilsFylgja";
        public const string LegacyPrefabName = "BossStone_StarPlatinum";
        public const string BaseStonePrefabName = "BossStone_Yagluth";
        public const string TrophyPrefabName = "TrophyFallenValkyrie";
        public const string StoneWorldKey = "defeated_fallenvalkyrie_af";
        public const string DeepNorthLocationKey = "AurvandilsFylgjaStone";
        public const string DeepNorthPinName = "Aurvandil's Fylgja";
        public const string LordRetoLocationName = "PlaceofMystery3";
        public const string VegvisirObjectName = "Vegvisir_AurvandilsFylgja";
        public const string StoneInscriptionText =
            "Hang the head of she who fell from the sky upon the star monolith. Purify her spirit and awaken the Fylgja of Aurvandil: an astral guardian whose fists strike faster than star light.";

        private static GameObject s_prefabContainer;
        private static GameObject s_stonePrefab;
        private static Mesh s_customStoneMesh;
        private static Material s_customStoneMaterial;
        private static Mesh s_vegvisirMesh;
        private static Material s_vegvisirMaterial;
        private static ZoneSystem.ZoneLocation s_virtualZoneLocation;

        // Cached per-world Deep North Sacrificial Stone coordinates & server housekeeping
        private static int s_cachedWorldSeed = int.MinValue;
        private static bool s_hasCachedStonePos;
        private static Vector3 s_cachedStonePos;
        private static bool s_legacyStartSweepDone;
        private static bool s_deepNorthStoneConfirmed;
        private static float s_nextZoneCheckTime;
        private static float s_nextZdoScanTime;
        private static System.Reflection.FieldInfo s_zdoDictField;
        private static float s_targetZoneFirstLoadedTime = -1f;

        internal static readonly Color IdleEmissiveColor = new Color(0.26f, 0.18f, 0.36f, 1.0f);
        internal static readonly Color ActiveEmissiveColor = new Color(0.52f, 0.34f, 0.68f, 1.0f);

        public static void RegisterPrefab(ZNetScene zns)
        {
            if (zns == null || zns.m_prefabs == null) return;

            try
            {
                int hash = PrefabName.GetStableHashCode();
                int legacyHash = LegacyPrefabName.GetStableHashCode();
                var namedPrefabs = AccessTools.Field(typeof(ZNetScene), "m_namedPrefabs")?.GetValue(zns) as Dictionary<int, GameObject>;

                if (s_stonePrefab != null)
                {
                    if (!zns.m_prefabs.Contains(s_stonePrefab))
                    {
                        zns.m_prefabs.Add(s_stonePrefab);
                    }
                    if (namedPrefabs != null)
                    {
                        namedPrefabs[hash] = s_stonePrefab;
                        namedPrefabs[legacyHash] = s_stonePrefab;
                    }
                    ConfigureStoneInstance(s_stonePrefab);
                    return;
                }

                GameObject basePrefab = zns.GetPrefab(BaseStonePrefabName);
                if (basePrefab == null)
                {
                    basePrefab = zns.m_prefabs.Find(p => p != null && p.name == BaseStonePrefabName);
                }
                if (basePrefab == null)
                {
                    Plugin.Log?.LogWarning($"SacrificialStoneManager: Could not find base prefab '{BaseStonePrefabName}' in ZNetScene.");
                    return;
                }

                if (s_prefabContainer == null)
                {
                    s_prefabContainer = new GameObject("StandsOfValheim_PrefabContainer");
                    UnityEngine.Object.DontDestroyOnLoad(s_prefabContainer);
                    s_prefabContainer.SetActive(false);
                }

                s_stonePrefab = UnityEngine.Object.Instantiate(basePrefab, s_prefabContainer.transform);
                s_stonePrefab.name = PrefabName;
                s_stonePrefab.transform.localPosition = Vector3.zero;
                s_stonePrefab.transform.localRotation = Quaternion.identity;

                if (s_stonePrefab.GetComponent<AurvandilsFylgjaBossStone>() == null)
                {
                    s_stonePrefab.AddComponent<AurvandilsFylgjaBossStone>();
                }

                ConfigureStoneInstance(s_stonePrefab);

                if (!zns.m_prefabs.Contains(s_stonePrefab))
                {
                    zns.m_prefabs.Add(s_stonePrefab);
                }
                if (namedPrefabs != null)
                {
                    namedPrefabs[hash] = s_stonePrefab;
                    namedPrefabs[legacyHash] = s_stonePrefab;
                }

                Plugin.Log?.LogInfo($"SacrificialStoneManager: Registered '{PrefabName}' in ZNetScene.");
            }
            catch (Exception ex)
            {
                Plugin.Log?.LogError($"SacrificialStoneManager: Failed to register '{PrefabName}': {ex}");
            }
        }

        public static void RefreshAllConfiguredStones()
        {
            try
            {
                if (s_stonePrefab != null)
                {
                    ConfigureStoneInstance(s_stonePrefab);
                }

                var activeStones = UnityEngine.Object.FindObjectsByType<AurvandilsFylgjaBossStone>(FindObjectsSortMode.None);
                if (activeStones != null)
                {
                    foreach (var afStone in activeStones)
                    {
                        if (afStone != null)
                        {
                            ConfigureStoneInstance(afStone.gameObject);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.Log?.LogWarning($"SacrificialStoneManager: RefreshAllConfiguredStones warning: {ex.Message}");
            }
        }

        public static bool IsAurvandilsFylgjaStone(BossStone stone)
        {
            if (stone == null) return false;
            if (stone.GetComponent<AurvandilsFylgjaBossStone>() != null) return true;
            string n = stone.gameObject.name;
            if (string.IsNullOrEmpty(n)) return false;
            return n.StartsWith(PrefabName, StringComparison.OrdinalIgnoreCase) ||
                   n.StartsWith(LegacyPrefabName, StringComparison.OrdinalIgnoreCase);
        }

        public static void ConfigureStoneInstance(GameObject stoneRoot)
        {
            if (stoneRoot == null) return;

            var bossStone = stoneRoot.GetComponent<BossStone>();
            if (bossStone == null) return;

            if (stoneRoot.GetComponent<AurvandilsFylgjaBossStone>() == null)
            {
                stoneRoot.AddComponent<AurvandilsFylgjaBossStone>();
            }

            // 1. Configure BossStone, RuneStone inscription, & ItemStand gameplay properties
            bossStone.m_completedMessage = "You have offered the Fallen Valkyrie. Aurvandil's Fylgja awaits!";
            bossStone.m_setsWorldKey = StoneWorldKey;
            bossStone.m_activeEmissiveColor = ActiveEmissiveColor;

            var runeStones = stoneRoot.GetComponentsInChildren<RuneStone>(true);
            if (runeStones != null)
            {
                foreach (var runeStone in runeStones)
                {
                    if (runeStone == null) continue;
                    runeStone.m_name = "$guardianstone_name";
                    runeStone.m_text = StoneInscriptionText;
                    runeStone.m_topic = "";
                    runeStone.m_label = "Aurvandil's Fylgja";
                    runeStone.m_pinName = "";
                    if (runeStone.m_randomTexts != null)
                    {
                        runeStone.m_randomTexts.Clear();
                    }
                }
            }

            var itemStand = bossStone.m_itemStand ?? stoneRoot.GetComponentInChildren<ItemStand>(true);
            if (itemStand != null)
            {
                bossStone.m_itemStand = itemStand;
                itemStand.m_name = "Aurvandil's Fylgja Sacrificial Stone";
                itemStand.m_canBeRemoved = false;
                itemStand.m_autoAttach = true;

                var trophyDrop = ResolveFallenValkyrieTrophy();
                if (trophyDrop != null)
                {
                    itemStand.m_supportedItems = new List<ItemDrop> { trophyDrop };
                    itemStand.m_supportedTypes = new List<ItemDrop.ItemData.ItemType>();
                }

                var guardianPower = ResolveAurvandilsFylgjaStatusEffect();
                if (guardianPower != null)
                {
                    itemStand.m_guardianPower = guardianPower;
                }
            }

            // 2. Apply custom 3D mesh (side-hung chains + central hook) and high-res Aurvandil's Fylgja stone material
            ApplyStoneVisuals(bossStone);
        }

        private static ItemDrop ResolveFallenValkyrieTrophy()
        {
            if (ObjectDB.instance != null)
            {
                var trophyGo = ObjectDB.instance.GetItemPrefab(TrophyPrefabName);
                if (trophyGo == null && ObjectDB.instance.m_items != null)
                {
                    trophyGo = ObjectDB.instance.m_items.Find(i =>
                        i != null && i.name.IndexOf("FallenValkyrie", StringComparison.OrdinalIgnoreCase) >= 0);
                }
                if (trophyGo != null)
                {
                    var drop = trophyGo.GetComponent<ItemDrop>();
                    if (drop != null) return drop;
                }
            }

            if (ZNetScene.instance != null)
            {
                var trophyGo = ZNetScene.instance.GetPrefab(TrophyPrefabName);
                if (trophyGo != null)
                {
                    var drop = trophyGo.GetComponent<ItemDrop>();
                    if (drop != null) return drop;
                }
            }

            return null;
        }

        private static StatusEffect ResolveAurvandilsFylgjaStatusEffect()
        {
            if (ObjectDB.instance == null) return null;

            var se = ObjectDB.instance.GetStatusEffect(SE_AurvandilsFylgja.EffectName.GetStableHashCode());
            if (se != null) return se;

            if (ObjectDB.instance.m_StatusEffects != null)
            {
                return ObjectDB.instance.m_StatusEffects.Find(s => s != null && s.name == SE_AurvandilsFylgja.EffectName);
            }

            return null;
        }

        public static void ApplyStoneVisuals(BossStone bossStone)
        {
            if (bossStone == null || bossStone.m_mesh == null) return;

            try
            {
                var meshRenderer = bossStone.m_mesh;
                var meshFilter = meshRenderer.GetComponent<MeshFilter>();
                if (meshFilter != null && meshFilter.sharedMesh != null)
                {
                    if (s_customStoneMesh == null)
                    {
                        s_customStoneMesh = BuildCustomStoneMesh(meshFilter.sharedMesh);
                    }
                    if (s_customStoneMesh != null && meshFilter.sharedMesh != s_customStoneMesh)
                    {
                        meshFilter.sharedMesh = s_customStoneMesh;
                    }
                }

                var sharedMats = meshRenderer.sharedMaterials;
                int matIdx = Mathf.Clamp(bossStone.m_emissiveMaterialIndex, 0, sharedMats.Length - 1);
                if (sharedMats != null && sharedMats.Length > 0 && sharedMats[matIdx] != null)
                {
                    if (s_customStoneMaterial == null)
                    {
                        s_customStoneMaterial = BuildCustomStoneMaterial(sharedMats[matIdx]);
                    }
                    if (s_customStoneMaterial != null)
                    {
                        var newMats = (Material[])sharedMats.Clone();
                        newMats[matIdx] = s_customStoneMaterial;
                        meshRenderer.sharedMaterials = newMats;
                    }
                }

                // Remove any legacy custom point light so only the vanilla BossStone active_effects aura is used
                Transform frontLightTr = bossStone.transform.Find("AurvandilsFylgjaStoneLight") ??
                                         bossStone.transform.Find("StarPlatinumStoneLight");
                if (frontLightTr != null)
                {
                    UnityEngine.Object.Destroy(frontLightTr.gameObject);
                }
            }
            catch (Exception ex)
            {
                Plugin.Log?.LogError($"SacrificialStoneManager: ApplyStoneVisuals failed: {ex}");
            }
        }

        /// <summary>
        /// Clones the BossStone_Yagluth mesh, keeping SubMesh 0 (stone monolith) and the central 3D trophy hook
        /// intact while shifting the two diagonal V-chains in SubMesh 1 out to hang vertically along the left
        /// and right outer edges of the stone so Aurvandil's Fylgja's carving on the front face is completely unobstructed.
        /// </summary>
        private static Mesh BuildCustomStoneMesh(Mesh origMesh)
        {
            try
            {
                Mesh mesh = UnityEngine.Object.Instantiate(origMesh);
                mesh.name = "BossStone_AurvandilsFylgja_Mesh";

                Vector3[] verts = mesh.vertices;
                if (verts != null && verts.Length > 110 && mesh.subMeshCount >= 2)
                {
                    var sub1Desc = mesh.GetSubMesh(1);
                    int startV = sub1Desc.firstVertex;
                    int endV = startV + sub1Desc.vertexCount;

                    for (int i = startV; i < endV && i < verts.Length; i++)
                    {
                        Vector3 v = verts[i];

                        // Keep the central curved metal hook at (px in [0.085, 0.172], py < 1.56) untouched
                        bool isCentralHook = v.y < 1.56f && v.x >= 0.085f && v.x <= 0.172f;
                        if (isCentralHook) continue;

                        // For the chains descending from the top iron band (py <= 4.22), shift X outward so they
                        // hang vertically down the left and right outer sides of the stone monolith.
                        if (v.y <= 4.22f)
                        {
                            float drop = Mathf.Clamp(4.22f - v.y, 0f, 2.75f);
                            if (v.x < 0.141f)
                            {
                                v.x -= drop * 0.56f;
                            }
                            else
                            {
                                v.x += drop * 0.50f;
                            }
                            verts[i] = v;
                        }
                    }

                    mesh.vertices = verts;

                    // Safeguard: remove any triangle in SubMesh 1 that spans > 0.75 units across the stone
                    int[] tris1 = mesh.GetTriangles(1);
                    if (tris1 != null && tris1.Length >= 3)
                    {
                        var cleanTris = new List<int>(tris1.Length);
                        const float MaxEdgeSqr = 0.75f * 0.75f;
                        for (int t = 0; t + 2 < tris1.Length; t += 3)
                        {
                            int i0 = tris1[t];
                            int i1 = tris1[t + 1];
                            int i2 = tris1[t + 2];
                            Vector3 p0 = verts[i0];
                            Vector3 p1 = verts[i1];
                            Vector3 p2 = verts[i2];
                            if ((p0 - p1).sqrMagnitude <= MaxEdgeSqr &&
                                (p1 - p2).sqrMagnitude <= MaxEdgeSqr &&
                                (p2 - p0).sqrMagnitude <= MaxEdgeSqr)
                            {
                                cleanTris.Add(i0);
                                cleanTris.Add(i1);
                                cleanTris.Add(i2);
                            }
                        }
                        if (cleanTris.Count != tris1.Length)
                        {
                            mesh.SetTriangles(cleanTris, 1);
                        }
                    }

                    mesh.RecalculateBounds();
                }

                return mesh;
            }
            catch (Exception ex)
            {
                Plugin.Log?.LogWarning($"SacrificialStoneManager: Could not reshape stone mesh, using original: {ex.Message}");
                return origMesh;
            }
        }

        /// <summary>
        /// Builds a 1024x1024 custom stone material from bossstone_yagluth_mat with the Aurvandil's Fylgja
        /// runic carving, 8-pointed Y star, horizontal iron bands, and glowing purple/gold cosmic nebula
        /// mapped onto the front stone face anchored to the 3D trophy hook.
        /// </summary>
        private static Material BuildCustomStoneMaterial(Material origMat)
        {
            try
            {
                Material mat = new Material(origMat);
                mat.name = "bossstone_aurvandilsfylgja_mat";

                Texture2D overlayTex = IconManager.BossStoneTexture;
                if (overlayTex == null)
                {
                    Plugin.Log?.LogWarning("SacrificialStoneManager: BossStoneTexture is null; keeping base stone texture.");
                    return mat;
                }

                const int Size = 1024;
                Texture origMain = origMat.HasProperty("_MainTex") ? origMat.GetTexture("_MainTex") : null;
                Texture origBump = origMat.HasProperty("_BumpMap") ? origMat.GetTexture("_BumpMap") : null;

                Color[] mainPixels = ReadTexturePixels(origMain, Size, Size, new Color(0.28f, 0.30f, 0.31f, 1f));
                Color[] bumpPixels = origBump != null ? ReadTexturePixels(origBump, Size, Size, new Color(0.5f, 0.5f, 1f, 0.5f)) : null;
                Color[] emisPixels = new Color[Size * Size];

                int ovW = overlayTex.width;
                int ovH = overlayTex.height;
                Color[] ovPixels = overlayTex.GetPixels();

                // Exact UV-to-image mapping anchored at the 3D hook bracket (u=0.7316, v=0.2585 <-> x_img=380, y_img=568.5)
                const float UHook = 0.7316f;
                const float VHook = 0.2585f;
                const float XImgHook = 380.0f;
                const float YImgHook = 568.5f;
                const float UvPerImgPx = 0.000646f;

                int xMin = Mathf.Clamp(Mathf.FloorToInt(0.54f * Size), 0, Size - 1);
                int xMax = Mathf.Clamp(Mathf.CeilToInt(0.93f * Size), 0, Size - 1);
                int yMin = Mathf.Clamp(Mathf.FloorToInt(0.08f * Size), 0, Size - 1);
                int yMax = Mathf.Clamp(Mathf.CeilToInt(0.65f * Size), 0, Size - 1);

                for (int y = yMin; y <= yMax; y++)
                {
                    float v = (y + 0.5f) / Size;
                    float yImgFromTop = YImgHook - (v - VHook) / UvPerImgPx;
                    if (yImgFromTop < 30f || yImgFromTop >= ovH - 30f) continue;

                    // Unity Texture2D has y=0 at bottom, whereas yImgFromTop has 0 at top
                    float ovY = (ovH - 1f) - yImgFromTop;

                    for (int x = xMin; x <= xMax; x++)
                    {
                        float u = (x + 0.5f) / Size;
                        float xImg = XImgHook + (u - UHook) / UvPerImgPx;
                        if (xImg < 95f || xImg >= ovW - 85f) continue;

                        Color ov = SampleBilinear(ovPixels, ovW, ovH, xImg, ovY);
                        float alpha = ov.a;
                        if (alpha <= 0.001f) continue;

                        int idx = y * Size + x;
                        Color baseCol = mainPixels[idx];

                        // Match overall stone exposure softly while keeping etched black lines & cosmic glow vibrant
                        Color targetRgb = new Color(ov.r, ov.g, ov.b, baseCol.a);
                        mainPixels[idx] = Color.Lerp(baseCol, targetRgb, alpha);

                        // Extract cosmic purple & gold nebula + 8-pointed star rim for _EmissiveTex without white blowout
                        float lum = 0.299f * ov.r + 0.587f * ov.g + 0.114f * ov.b;
                        float purpleGlow = Mathf.Max(0f, Mathf.Min(ov.r, ov.b) - ov.g * 0.86f);
                        float goldGlow = Mathf.Max(0f, Mathf.Min(ov.r, ov.g) - ov.b * 0.88f);
                        float emisStrength = Mathf.Clamp01((purpleGlow * 2.2f + goldGlow * 2.0f) * alpha);

                        if (emisStrength > 0.02f)
                        {
                            emisPixels[idx] = new Color(
                                ov.r * emisStrength,
                                ov.g * emisStrength,
                                ov.b * emisStrength,
                                1.0f);
                        }

                        // Clean out Yagluth's old normal-map grooves on the front stone face and emboss Aurvandil's Fylgja's etched lines
                        if (bumpPixels != null && v >= 0.15f && v <= 0.56f && u >= 0.57f && u <= 0.90f)
                        {
                            // Sample clean stone normal from the uncarved back/side UV region of the same normal map
                            int cleanX = ((x - xMin) % (Size / 8)) + (Size / 8);
                            int cleanY = ((y - yMin) % (Size / 8)) + (Size / 8);
                            Color cleanNorm = bumpPixels[cleanY * Size + cleanX];

                            // Compute local luminance gradient from overlay to emboss the etched lines
                            Color ovR = SampleBilinear(ovPixels, ovW, ovH, Mathf.Min(ovW - 1f, xImg + 1.5f), ovY);
                            Color ovU = SampleBilinear(ovPixels, ovW, ovH, xImg, Mathf.Min(ovH - 1f, ovY + 1.5f));
                            float lumR = 0.299f * ovR.r + 0.587f * ovR.g + 0.114f * ovR.b;
                            float lumU = 0.299f * ovU.r + 0.587f * ovU.g + 0.114f * ovU.b;
                            float dx = (lumR - lum) * 0.35f * alpha;
                            float dy = (lumU - lum) * 0.35f * alpha;

                            Color embossed = new Color(
                                Mathf.Clamp01(cleanNorm.r + dx),
                                Mathf.Clamp01(cleanNorm.g + dy),
                                cleanNorm.b,
                                Mathf.Clamp01(cleanNorm.a + dx));

                            bumpPixels[idx] = Color.Lerp(bumpPixels[idx], embossed, alpha);
                        }
                    }
                }

                var newMainTex = new Texture2D(Size, Size, TextureFormat.RGBA32, true);
                newMainTex.name = "AurvandilsFylgjaStone_d";
                newMainTex.SetPixels(mainPixels);
                newMainTex.Apply(true, false);
                mat.SetTexture("_MainTex", newMainTex);

                var newEmisTex = new Texture2D(Size, Size, TextureFormat.RGBA32, true);
                newEmisTex.name = "AurvandilsFylgjaStone_e";
                newEmisTex.SetPixels(emisPixels);
                newEmisTex.Apply(true, false);
                if (mat.HasProperty("_EmissiveTex")) mat.SetTexture("_EmissiveTex", newEmisTex);
                if (mat.HasProperty("_EmissionMap")) mat.SetTexture("_EmissionMap", newEmisTex);
                if (mat.HasProperty("_EmissionColor")) mat.SetColor("_EmissionColor", IdleEmissiveColor);
                if (mat.HasProperty("_EmissiveColor")) mat.SetColor("_EmissiveColor", IdleEmissiveColor);

                if (bumpPixels != null && mat.HasProperty("_BumpMap"))
                {
                    var newBumpTex = new Texture2D(Size, Size, TextureFormat.RGBA32, true);
                    newBumpTex.name = "AurvandilsFylgjaStone_n";
                    newBumpTex.SetPixels(bumpPixels);
                    newBumpTex.Apply(true, false);
                    mat.SetTexture("_BumpMap", newBumpTex);
                }

                Plugin.Log?.LogInfo("SacrificialStoneManager: Built 1024x1024 Aurvandil's Fylgja stone textures (_MainTex, _EmissiveTex, _BumpMap).");
                return mat;
            }
            catch (Exception ex)
            {
                Plugin.Log?.LogError($"SacrificialStoneManager: BuildCustomStoneMaterial failed: {ex}");
                return origMat;
            }
        }

        private static Color[] ReadTexturePixels(Texture src, int width, int height, Color fallback)
        {
            if (src != null)
            {
                RenderTexture prev = RenderTexture.active;
                RenderTexture rt = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default);
                try
                {
                    Graphics.Blit(src, rt);
                    RenderTexture.active = rt;
                    var tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
                    tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                    tex.Apply();
                    Color[] pixels = tex.GetPixels();
                    UnityEngine.Object.Destroy(tex);
                    return pixels;
                }
                catch (Exception ex)
                {
                    Plugin.Log?.LogWarning($"SacrificialStoneManager: ReadTexturePixels fallback: {ex.Message}");
                }
                finally
                {
                    RenderTexture.active = prev;
                    RenderTexture.ReleaseTemporary(rt);
                }
            }

            Color[] fill = new Color[width * height];
            for (int i = 0; i < fill.Length; i++) fill[i] = fallback;
            return fill;
        }

        private static Color SampleBilinear(Color[] pixels, int w, int h, float fx, float fy)
        {
            fx = Mathf.Clamp(fx, 0f, w - 1.001f);
            fy = Mathf.Clamp(fy, 0f, h - 1.001f);
            int x0 = (int)fx;
            int y0 = (int)fy;
            int x1 = Mathf.Min(x0 + 1, w - 1);
            int y1 = Mathf.Min(y0 + 1, h - 1);
            float tx = fx - x0;
            float ty = fy - y0;

            Color c00 = pixels[y0 * w + x0];
            Color c10 = pixels[y0 * w + x1];
            Color c01 = pixels[y1 * w + x0];
            Color c11 = pixels[y1 * w + x1];

            Color c0 = Color.LerpUnclamped(c00, c10, tx);
            Color c1 = Color.LerpUnclamped(c01, c11, tx);
            return Color.LerpUnclamped(c0, c1, ty);
        }

        // ----------------------------------------------------------------------
        // Deep North Random Placement & Legacy StartTemple Cleanup
        // ----------------------------------------------------------------------

        public static void ResetWorldCache()
        {
            s_cachedWorldSeed = int.MinValue;
            s_hasCachedStonePos = false;
            s_cachedStonePos = Vector3.zero;
            s_legacyStartSweepDone = false;
            s_deepNorthStoneConfirmed = false;
            s_nextZoneCheckTime = 0f;
            s_nextZdoScanTime = 0f;
            s_targetZoneFirstLoadedTime = -1f;
        }

        private static void EnsureCacheMatchesWorld()
        {
            if (WorldGenerator.instance == null) return;
            int seed = WorldGenerator.instance.GetSeed();
            if (seed != s_cachedWorldSeed)
            {
                s_cachedWorldSeed = seed;
                s_hasCachedStonePos = false;
                s_cachedStonePos = Vector3.zero;
                s_legacyStartSweepDone = false;
                s_deepNorthStoneConfirmed = false;
                s_nextZoneCheckTime = 0f;
                s_nextZdoScanTime = 0f;
                s_targetZoneFirstLoadedTime = -1f;
            }
        }

        public static bool IsLegacyStartTempleStone(Vector3 pos)
        {
            if (WorldGenerator.instance != null &&
                WorldGenerator.instance.GetBiome(pos) == Heightmap.Biome.DeepNorth)
            {
                return false;
            }

            // Anything near the world origin / StartTemple circle or adjacent to vanilla BossStones outside Deep North
            Vector2 horiz = new Vector2(pos.x, pos.z);
            if (horiz.magnitude < 650f) return true;

            var allStones = UnityEngine.Object.FindObjectsByType<BossStone>(FindObjectsSortMode.None);
            if (allStones != null)
            {
                foreach (var s in allStones)
                {
                    if (s == null || IsAurvandilsFylgjaStone(s)) continue;
                    if (Vector3.Distance(s.transform.position, pos) < 75f)
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        public static bool DestroyLegacyStoneIfNeeded(GameObject stoneObj)
        {
            if (stoneObj == null) return false;
            if (!IsLegacyStartTempleStone(stoneObj.transform.position)) return false;

            if (ZNet.instance != null && !ZNet.instance.IsServer())
            {
                return false;
            }

            Vector3 oldPos = stoneObj.transform.position;
            var nview = stoneObj.GetComponent<ZNetView>();
            if (nview != null && nview.IsValid())
            {
                nview.ClaimOwnership();
                if (ZNetScene.instance != null)
                {
                    ZNetScene.instance.Destroy(stoneObj);
                }
                else
                {
                    var zdo = nview.GetZDO();
                    if (zdo != null && ZDOMan.instance != null)
                    {
                        ZDOMan.instance.DestroyZDO(zdo);
                    }
                    UnityEngine.Object.Destroy(stoneObj);
                }
            }
            else
            {
                UnityEngine.Object.Destroy(stoneObj);
            }

            Plugin.Log?.LogInfo($"SacrificialStoneManager: Removed legacy StartTemple '{PrefabName}' at {oldPos} (now placed in Deep North).");
            return true;
        }

        private static Dictionary<ZDOID, ZDO> GetZdoDictionary()
        {
            if (ZDOMan.instance == null) return null;
            if (s_zdoDictField == null) s_zdoDictField = AccessTools.Field(typeof(ZDOMan), "m_objectsByID");
            return s_zdoDictField?.GetValue(ZDOMan.instance) as Dictionary<ZDOID, ZDO>;
        }

        private static long GetLocalZdoSessionId()
        {
            if (ZDOMan.instance != null)
            {
                var field = AccessTools.Field(typeof(ZDOMan), "m_sessionID");
                if (field != null && field.GetValue(ZDOMan.instance) is long sid)
                {
                    return sid;
                }
            }
            return ZNet.instance != null ? ZNet.GetUID() : 0L;
        }

        private static bool TryFindExistingDeepNorthStoneZdo(out Vector3 pos, bool force = false)
        {
            pos = Vector3.zero;
            var dict = GetZdoDictionary();
            if (dict == null || WorldGenerator.instance == null) return false;

            // Worlds can hold >1M ZDOs; a full scan is expensive, so never repeat it every frame/zone spawn.
            float now = Time.realtimeSinceStartup;
            if (!force && now < s_nextZdoScanTime) return false;
            s_nextZdoScanTime = now + 30f;

            int hash = PrefabName.GetStableHashCode();
            int legacyHash = LegacyPrefabName.GetStableHashCode();

            foreach (var kv in dict)
            {
                ZDO zdo = kv.Value;
                if (zdo == null || !zdo.IsValid()) continue;
                int p = zdo.GetPrefab();
                if (p != hash && p != legacyHash) continue;

                Vector3 zpos = zdo.GetPosition();
                if (WorldGenerator.instance.GetBiome(zpos) == Heightmap.Biome.DeepNorth)
                {
                    pos = zpos;
                    return true;
                }
            }
            return false;
        }

        private static void SweepLegacyStartTempleStoneZdos()
        {
            if (s_legacyStartSweepDone) return;
            if (ZNet.instance != null && !ZNet.instance.IsServer()) return;
            if (ZDOMan.instance == null || WorldGenerator.instance == null) return;

            var dict = GetZdoDictionary();
            if (dict == null) return;

            s_legacyStartSweepDone = true;

            int hash = PrefabName.GetStableHashCode();
            int legacyHash = LegacyPrefabName.GetStableHashCode();
            var toRemove = new List<ZDO>();

            foreach (var kv in dict)
            {
                ZDO zdo = kv.Value;
                if (zdo == null || !zdo.IsValid()) continue;
                int p = zdo.GetPrefab();
                if (p != hash && p != legacyHash) continue;

                Vector3 zpos = zdo.GetPosition();
                if (WorldGenerator.instance.GetBiome(zpos) == Heightmap.Biome.DeepNorth)
                {
                    s_cachedStonePos = zpos;
                    s_hasCachedStonePos = true;
                    s_deepNorthStoneConfirmed = true;
                }
                else if (new Vector2(zpos.x, zpos.z).magnitude < 650f)
                {
                    toRemove.Add(zdo);
                }
            }

            if (toRemove.Count > 0)
            {
                long sessionId = GetLocalZdoSessionId();
                foreach (var zdo in toRemove)
                {
                    if (!zdo.IsOwner() && sessionId != 0L)
                    {
                        zdo.SetOwner(sessionId);
                    }
                    ZDOMan.instance.DestroyZDO(zdo);
                }
                Plugin.Log?.LogInfo($"SacrificialStoneManager: Cleaned up {toRemove.Count} legacy StartTemple stone ZDO(s) from world save.");
            }
        }

        /// <summary>
        /// Deterministically selects (or resolves from existing world ZDO) a random walkable Deep North
        /// location for Aurvandil's Fylgja's Sacrificial Stone based on the current world seed.
        /// </summary>
        public static bool TryResolveDeepNorthStonePosition(out Vector3 position)
        {
            position = Vector3.zero;
            var wg = WorldGenerator.instance;
            if (wg == null) return false;

            EnsureCacheMatchesWorld();

            // Priority 1: If a Deep North stone ZDO already exists in ZDOMan, always return its exact persisted position
            if (!s_deepNorthStoneConfirmed)
            {
                if (TryFindExistingDeepNorthStoneZdo(out Vector3 existingPos))
                {
                    s_cachedStonePos = existingPos;
                    s_hasCachedStonePos = true;
                    s_deepNorthStoneConfirmed = true;
                }
            }

            if (s_hasCachedStonePos)
            {
                position = s_cachedStonePos;
                return true;
            }

            // Priority 2: Purely WorldGenerator-deterministic seeded random search across walkable Deep North snowfields
            // so dedicated servers and multiplayer clients always agree on the exact same Deep North coordinates.
            int seed = wg.GetSeed();
            int rngSeed = unchecked(seed * 1664525 + 1013904223 ^ 0x5A7F31C9);
            var rng = new System.Random(rngSeed);

            float waterLevel = ZoneSystem.c_WaterLevel;

            bool foundBest = false;
            Vector3 bestPos = Vector3.zero;
            float bestScore = float.MaxValue;
            int validFound = 0;

            const int MaxAttempts = 4500;
            for (int attempt = 0; attempt < MaxAttempts; attempt++)
            {
                float x = (float)(rng.NextDouble() * 13600.0 - 6800.0);
                float z = (float)(rng.NextDouble() * 4300.0 + 4900.0);
                if (x * x + z * z > 9150f * 9150f) continue;

                if (wg.GetBiome(x, z) != Heightmap.Biome.DeepNorth) continue;

                bool strict = attempt < 3000;
                if (strict && wg.GetBiomeArea(new Vector3(x, 0f, z)) != Heightmap.BiomeArea.Median)
                {
                    continue;
                }

                float h = wg.GetHeight(x, z);
                float minH = strict ? waterLevel + 5.0f : waterLevel + 2.5f;
                float maxH = strict ? 115f : 165f;
                if (h < minH || h > maxH) continue;

                // Ensure local 6m footprint is dry Deep North with comfortable walking slope
                float hE = wg.GetHeight(x + 6f, z);
                float hW = wg.GetHeight(x - 6f, z);
                float hN = wg.GetHeight(x, z + 6f);
                float hS = wg.GetHeight(x, z - 6f);
                if (wg.GetBiome(x + 6f, z) != Heightmap.Biome.DeepNorth ||
                    wg.GetBiome(x - 6f, z) != Heightmap.Biome.DeepNorth ||
                    wg.GetBiome(x, z + 6f) != Heightmap.Biome.DeepNorth ||
                    wg.GetBiome(x, z - 6f) != Heightmap.Biome.DeepNorth)
                {
                    continue;
                }
                if (hE < waterLevel + 2.5f || hW < waterLevel + 2.5f ||
                    hN < waterLevel + 2.5f || hS < waterLevel + 2.5f)
                {
                    continue;
                }

                float maxDelta = Mathf.Max(
                    Mathf.Max(Mathf.Abs(hE - h), Mathf.Abs(hW - h)),
                    Mathf.Max(Mathf.Abs(hN - h), Mathf.Abs(hS - h)));
                float maxAllowedDelta = strict ? 2.8f : 6.5f;
                if (maxDelta > maxAllowedDelta) continue;

                // Prefer flat, scenic moderate-elevation snowfields
                float score = maxDelta * 12f + Mathf.Abs(h - 52f) * 0.25f;
                if (score < bestScore)
                {
                    bestScore = score;
                    bestPos = new Vector3(x, h, z);
                    foundBest = true;
                }

                validFound++;
                if (validFound >= 12) break;
            }

            if (!foundBest)
            {
                Plugin.Log?.LogWarning("SacrificialStoneManager: Could not locate a Deep North snowfield via random sampling.");
                return false;
            }

            s_cachedStonePos = bestPos;
            s_hasCachedStonePos = true;
            position = bestPos;

            Plugin.Log?.LogInfo($"SacrificialStoneManager: Resolved random Deep North location for '{PrefabName}' at ({bestPos.x:0.0}, {bestPos.y:0.0}, {bestPos.z:0.0}) [seed={seed}].");
            return true;
        }

        public static void CheckDeepNorthStoneSpawnOnServer()
        {
            if (ZNetScene.instance == null || ZoneSystem.instance == null || WorldGenerator.instance == null) return;

            EnsureCacheMatchesWorld();
            if (ZNet.instance == null || ZNet.instance.IsServer())
            {
                SweepLegacyStartTempleStoneZdos();
            }

            if (s_deepNorthStoneConfirmed) return;
            if (!TryResolveDeepNorthStonePosition(out Vector3 targetPos)) return;

            Vector2s targetZone = ZoneSystem.GetZone(targetPos);
            if (!ZoneSystem.instance.IsZoneLoaded(targetZone))
            {
                s_targetZoneFirstLoadedTime = -1f;
                return;
            }

            // In Valheim dedicated-server multiplayer, the exploring client loads and owns the remote zone.
            // Wait a brief 1.5s sync grace window on clients so any existing ZDO from the server replicates first.
            bool isServer = ZNet.instance == null || ZNet.instance.IsServer();
            if (!isServer)
            {
                if (s_targetZoneFirstLoadedTime < 0f)
                {
                    s_targetZoneFirstLoadedTime = Time.time;
                    return;
                }
                if (Time.time - s_targetZoneFirstLoadedTime < 1.5f)
                {
                    return;
                }
                if (Player.m_localPlayer == null ||
                    Vector3.Distance(Player.m_localPlayer.transform.position, targetPos) > 75f)
                {
                    return;
                }
            }

            EnsureDeepNorthStoneSpawnedAt(targetPos);
        }

        public static void OnZoneSpawned(Vector2s zoneID, ZoneSystem.SpawnMode mode)
        {
            if (ZNetScene.instance == null || ZoneSystem.instance == null || WorldGenerator.instance == null) return;

            EnsureCacheMatchesWorld();
            if (s_deepNorthStoneConfirmed) return;
            if (!TryResolveDeepNorthStonePosition(out Vector3 targetPos)) return;

            if (ZoneSystem.GetZone(targetPos) == zoneID)
            {
                // Spawn immediately if server or if this peer is executing full first-time zone generation
                bool isServer = ZNet.instance == null || ZNet.instance.IsServer();
                if (isServer || mode == ZoneSystem.SpawnMode.Full)
                {
                    EnsureDeepNorthStoneSpawnedAt(targetPos);
                }
            }
        }

        public static bool DestroyDuplicateDeepNorthStoneIfNeeded(GameObject stoneObj)
        {
            if (stoneObj == null || WorldGenerator.instance == null) return false;
            if (WorldGenerator.instance.GetBiome(stoneObj.transform.position) != Heightmap.Biome.DeepNorth) return false;

            var myView = stoneObj.GetComponent<ZNetView>();
            if (myView == null || !myView.IsValid()) return false;
            ZDO myZdo = myView.GetZDO();
            if (myZdo == null) return false;

            var allStones = UnityEngine.Object.FindObjectsByType<BossStone>(FindObjectsSortMode.None);
            if (allStones == null) return false;

            foreach (var other in allStones)
            {
                if (other == null || other.gameObject == stoneObj) continue;
                if (!IsAurvandilsFylgjaStone(other)) continue;
                if (Vector3.Distance(other.transform.position, stoneObj.transform.position) > 96f) continue;

                var otherView = other.GetComponent<ZNetView>();
                ZDO otherZdo = otherView != null && otherView.IsValid() ? otherView.GetZDO() : null;
                if (otherZdo == null) continue;

                // Keep the older/lower-ID ZDO and cleanly remove the duplicate
                if (myZdo.m_uid.ID > otherZdo.m_uid.ID ||
                    (myZdo.m_uid.ID == otherZdo.m_uid.ID && myZdo.m_uid.UserID > otherZdo.m_uid.UserID))
                {
                    myView.ClaimOwnership();
                    if (ZNetScene.instance != null) ZNetScene.instance.Destroy(stoneObj);
                    else UnityEngine.Object.Destroy(stoneObj);
                    Plugin.Log?.LogInfo("SacrificialStoneManager: Removed duplicate Deep North stone instance.");
                    return true;
                }
            }
            return false;
        }

        private static void EnsureDeepNorthStoneSpawnedAt(Vector3 targetPos)
        {
            RegisterPrefab(ZNetScene.instance);
            if (s_stonePrefab == null) return;

            // Check live BossStone instances first
            var allStones = UnityEngine.Object.FindObjectsByType<BossStone>(FindObjectsSortMode.None);
            if (allStones != null)
            {
                foreach (var stone in allStones)
                {
                    if (stone != null && IsAurvandilsFylgjaStone(stone) &&
                        WorldGenerator.instance.GetBiome(stone.transform.position) == Heightmap.Biome.DeepNorth)
                    {
                        ConfigureStoneInstance(stone.gameObject);
                        s_cachedStonePos = stone.transform.position;
                        s_hasCachedStonePos = true;
                        s_deepNorthStoneConfirmed = true;
                        return;
                    }
                }
            }

            // Double-check ZDOMan so we never duplicate across zone unload/reload
            if (TryFindExistingDeepNorthStoneZdo(out Vector3 existingPos, true))
            {
                s_cachedStonePos = existingPos;
                s_hasCachedStonePos = true;
                s_deepNorthStoneConfirmed = true;
                return;
            }

            // Pick exact ground height & gently dodge any tree/boulder directly on targetPos
            Vector3 bestPos = targetPos;
            float groundY = targetPos.y;
            bool foundClear = false;
            Vector2[] offsets =
            {
                Vector2.zero,
                new Vector2(3.5f, 0f), new Vector2(-3.5f, 0f), new Vector2(0f, 3.5f), new Vector2(0f, -3.5f),
                new Vector2(5.5f, 5.5f), new Vector2(-5.5f, 5.5f), new Vector2(5.5f, -5.5f), new Vector2(-5.5f, -5.5f)
            };

            foreach (var off in offsets)
            {
                Vector3 cand = new Vector3(targetPos.x + off.x, targetPos.y, targetPos.z + off.y);
                float h;
                if (!ZoneSystem.instance.GetGroundHeight(cand, out h))
                {
                    h = WorldGenerator.instance.GetHeight(cand.x, cand.z);
                }
                if (h < ZoneSystem.c_WaterLevel + 1.5f) continue;

                Vector3 checkCenter = new Vector3(cand.x, h + 2.2f, cand.z);
                bool blocked = false;
                var cols = Physics.OverlapSphere(checkCenter, 1.85f);
                if (cols != null)
                {
                    foreach (var col in cols)
                    {
                        if (col == null || col.isTrigger) continue;
                        if (col.GetComponentInParent<Heightmap>() != null) continue;
                        if (col.bounds.max.y > h + 0.6f)
                        {
                            blocked = true;
                            break;
                        }
                    }
                }

                if (!blocked)
                {
                    bestPos = new Vector3(cand.x, h - 0.14f, cand.z);
                    groundY = h;
                    foundClear = true;
                    break;
                }
            }

            if (!foundClear)
            {
                if (!ZoneSystem.instance.GetGroundHeight(targetPos, out groundY))
                {
                    groundY = WorldGenerator.instance.GetHeight(targetPos.x, targetPos.z);
                }
                bestPos = new Vector3(targetPos.x, groundY - 0.14f, targetPos.z);
            }

            // Orient the monolith so its glowing cosmic carving & Fallen Valkyrie hook face South (toward world center)
            Vector3 toSouth = Vector3.zero - bestPos;
            toSouth.y = 0f;
            if (toSouth.sqrMagnitude < 0.001f) toSouth = Vector3.back;
            toSouth.Normalize();

            Vector3 localHookDir = new Vector3(0.212f, 0f, -1.004f).normalized;
            var prefabStand = s_stonePrefab.GetComponentInChildren<ItemStand>(true);
            if (prefabStand != null)
            {
                Vector3 localStand = s_stonePrefab.transform.InverseTransformPoint(prefabStand.transform.position);
                localStand.y = 0f;
                if (localStand.sqrMagnitude > 0.01f) localHookDir = localStand.normalized;
            }

            Quaternion bestRot = Quaternion.LookRotation(toSouth, Vector3.up) *
                                 Quaternion.Inverse(Quaternion.LookRotation(localHookDir, Vector3.up));

            GameObject spawned = UnityEngine.Object.Instantiate(s_stonePrefab, bestPos, bestRot);
            ConfigureStoneInstance(spawned);

            s_cachedStonePos = bestPos;
            s_hasCachedStonePos = true;
            s_deepNorthStoneConfirmed = true;

            Plugin.Log?.LogInfo($"SacrificialStoneManager: Spawned '{PrefabName}' in Deep North at ({bestPos.x:0.0}, {bestPos.y:0.0}, {bestPos.z:0.0}).");
        }

        internal static ZoneSystem.ZoneLocation GetOrCreateVirtualZoneLocation()
        {
            if (s_virtualZoneLocation == null)
            {
                s_virtualZoneLocation = new ZoneSystem.ZoneLocation
                {
                    m_prefabName = DeepNorthLocationKey,
                    m_biome = Heightmap.Biome.DeepNorth,
                    m_enable = true
                };
            }
            return s_virtualZoneLocation;
        }

        // ----------------------------------------------------------------------
        // Lord Reto's Location (Ashlands - PlaceofMystery3) Astral Vegvisir
        // ----------------------------------------------------------------------

        public static void ScheduleLordRetoVegvisirPlacement(Location loc)
        {
            if (loc == null) return;
            loc.StartCoroutine(AttachVegvisirToLordRetoTombCoroutine(loc));
        }

        private static IEnumerator AttachVegvisirToLordRetoTombCoroutine(Location loc)
        {
            yield return new WaitForSeconds(0.55f);
            if (loc == null) yield break;

            try
            {
                AttachVegvisirToLordRetoTomb(loc);
            }
            catch (Exception ex)
            {
                Plugin.Log?.LogError($"SacrificialStoneManager: Failed to place Astral Vegvisir in Lord Reto's Tomb: {ex}");
            }
        }

        private static void AttachVegvisirToLordRetoTomb(Location loc)
        {
            if (loc == null) return;
            if (Utils.FindChild(loc.transform, VegvisirObjectName) != null) return;

            Transform interior = loc.m_interiorTransform ?? loc.transform.Find("Interior");
            Teleport interiorEntry = null;
            Teleport[] teleports = loc.GetComponentsInChildren<Teleport>(true);

            if (teleports != null)
            {
                // Exterior mausoleum entrance Teleport points directly to the Interior arrival point (high Y)
                foreach (var tp in teleports)
                {
                    if (tp == null || tp.m_targetPoint == null) continue;
                    if (tp.m_targetPoint.transform.position.y > loc.transform.position.y + 100f)
                    {
                        interiorEntry = tp.m_targetPoint;
                        break;
                    }
                }

                // Fallback: any Teleport located in the high-altitude interior chamber
                if (interiorEntry == null)
                {
                    foreach (var tp in teleports)
                    {
                        if (tp != null && tp.transform.position.y > loc.transform.position.y + 100f)
                        {
                            interiorEntry = tp;
                            break;
                        }
                    }
                }
            }

            if (interior == null && interiorEntry != null)
            {
                interior = interiorEntry.transform.parent ?? loc.transform;
            }
            if (interior == null)
            {
                interior = loc.transform;
            }

            Vector3 entryPos = interiorEntry != null ? interiorEntry.transform.position : interior.position;
            Vector3 intoRoom = ResolveChamberForwardDirection(interior, interiorEntry, entryPos);
            Vector3 rightDir = Vector3.Cross(Vector3.up, intoRoom).normalized;

            Vector3 spawnPos;
            if (!TryFindClearInteriorFloorSpot(entryPos, intoRoom, rightDir, out spawnPos))
            {
                // Guaranteed unobstructed fallback just inside the tomb chamber
                spawnPos = entryPos + intoRoom * 2.4f + rightDir * 1.3f;
                if (Physics.Raycast(spawnPos + Vector3.up * 2.5f, Vector3.down, out RaycastHit fbHit, 6.0f, ~0, QueryTriggerInteraction.Ignore))
                {
                    spawnPos = fbHit.point;
                }
            }

            // Face the glowing rune carving back toward the entrance so arriving players see it immediately
            Vector3 lookDir = entryPos - spawnPos;
            lookDir.y = 0f;
            if (lookDir.sqrMagnitude < 0.01f) lookDir = -intoRoom;
            Quaternion spawnRot = Quaternion.LookRotation(lookDir.normalized, Vector3.up);

            GameObject vegvisirGo = BuildAstralVegvisirObject(interior, spawnPos, spawnRot);
            Plugin.Log?.LogInfo($"SacrificialStoneManager: Placed '{VegvisirObjectName}' inside Lord Reto's Tomb ({loc.gameObject.name}) at {vegvisirGo.transform.position}.");
        }

        private static Vector3 ResolveChamberForwardDirection(Transform interior, Teleport interiorEntry, Vector3 entryPos)
        {
            // 1. If Lord Reto's spawner is present in the hierarchy, point directly toward it
            foreach (var child in interior.GetComponentsInChildren<Transform>(true))
            {
                if (child == null) continue;
                if (child.name.IndexOf("Spawner_Charred_Dyrnwyn", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    Vector3 toBoss = child.position - entryPos;
                    toBoss.y = 0f;
                    if (toBoss.sqrMagnitude > 1.0f) return toBoss.normalized;
                }
            }

            // 2. Compute horizontal centroid of interior floor pieces (always present even on already-cleared worlds)
            Vector3 floorSum = Vector3.zero;
            int floorCount = 0;
            foreach (var r in interior.GetComponentsInChildren<Renderer>(true))
            {
                if (r == null) continue;
                string rn = r.gameObject.name;
                if (rn.IndexOf("floor", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    rn.IndexOf("caverock_floor", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    floorSum += r.bounds.center;
                    floorCount++;
                }
            }
            if (floorCount > 0)
            {
                Vector3 toCenter = (floorSum / floorCount) - entryPos;
                toCenter.y = 0f;
                if (toCenter.sqrMagnitude > 2.0f)
                {
                    return toCenter.normalized;
                }
            }

            // 3. Fallback: probe horizontal ray distances from entryPos to find the open axis into the chamber
            Vector3 baseFwd = interiorEntry != null ? interiorEntry.transform.forward : interior.forward;
            baseFwd.y = 0f;
            if (baseFwd.sqrMagnitude < 0.01f) baseFwd = Vector3.forward;
            baseFwd.Normalize();
            Vector3 baseRight = Vector3.Cross(Vector3.up, baseFwd).normalized;

            Vector3[] dirs = { baseFwd, -baseFwd, baseRight, -baseRight };
            Vector3 bestDir = baseFwd;
            float bestClearDist = -1f;
            Vector3 rayOrigin = entryPos + Vector3.up * 1.35f;

            foreach (var d in dirs)
            {
                float dist = Physics.Raycast(rayOrigin, d, out RaycastHit hit, 18f, ~0, QueryTriggerInteraction.Ignore)
                    ? hit.distance
                    : 18f;
                if (dist > bestClearDist)
                {
                    bestClearDist = dist;
                    bestDir = d;
                }
            }
            return bestDir;
        }

        private static bool TryFindClearInteriorFloorSpot(Vector3 entryPos, Vector3 intoRoom, Vector3 rightDir, out Vector3 floorPos)
        {
            floorPos = Vector3.zero;
            // Probe deeper spots first so the Vegvisir rests ceremonially inside Lord Reto's chamber (not blocking the doorway)
            float[] forwardDists = { 8.5f, 7.0f, 5.5f, 10.0f, 4.2f, 3.0f };
            float[] sideOffsets  = { 0.0f, 1.8f, -1.8f, 3.0f, -3.0f };

            foreach (float f in forwardDists)
            {
                foreach (float s in sideOffsets)
                {
                    Vector3 probe = entryPos + intoRoom * f + rightDir * s;
                    if (!Physics.Raycast(probe + Vector3.up * 3.0f, Vector3.down, out RaycastHit floorHit, 7.0f, ~0, QueryTriggerInteraction.Ignore))
                    {
                        continue;
                    }
                    if (floorHit.normal.y < 0.72f) continue;
                    if (Mathf.Abs(floorHit.point.y - entryPos.y) > 3.5f) continue;

                    // Verify there is clear headroom and no pillar/wall directly intersecting the Vegvisir tablet
                    Vector3 bodyCenter = floorHit.point + Vector3.up * 1.05f;
                    bool blocked = false;
                    var cols = Physics.OverlapSphere(bodyCenter, 0.58f, ~0, QueryTriggerInteraction.Ignore);
                    if (cols != null)
                    {
                        foreach (var col in cols)
                        {
                            if (col == null || col.isTrigger) continue;
                            if (col == floorHit.collider) continue;
                            if (col.bounds.max.y > floorHit.point.y + 0.18f)
                            {
                                blocked = true;
                                break;
                            }
                        }
                    }

                    if (!blocked)
                    {
                        floorPos = floorHit.point;
                        return true;
                    }
                }
            }
            return false;
        }

        private static GameObject BuildAstralVegvisirObject(Transform parent, Vector3 worldPos, Quaternion worldRot)
        {
            var go = new GameObject(VegvisirObjectName);
            go.transform.SetParent(parent, worldPositionStays: false);
            go.transform.position = worldPos;
            go.transform.rotation = worldRot;
            go.transform.localScale = Vector3.one;

            int pieceLayer = LayerMask.NameToLayer("piece");
            if (pieceLayer >= 0) go.layer = pieceLayer;

            var mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = GetAstralVegvisirMesh();

            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = GetAstralVegvisirMaterial();
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            mr.receiveShadows = true;

            var col = go.AddComponent<BoxCollider>();
            col.center = new Vector3(0f, 0.78f, 0f);
            col.size = new Vector3(0.76f, 1.56f, 0.38f);

            // Native Valheim Vegvisir component: interacting routes DiscoverClosestLocation("AurvandilsFylgjaStone")
            // to the server and pans the player's map straight to the random Deep North Sacrificial Stone!
            var vegvisir = go.AddComponent<Vegvisir>();
            vegvisir.m_name = "Vegvisir: Aurvandil's Fylgja";
            vegvisir.m_hoverName = "Vegvisir: Aurvandil's Fylgja";
            vegvisir.m_useText = "$piece_register_location";
            vegvisir.m_hoverOffset = 0.25f;
            vegvisir.m_locations = new List<Vegvisir.VegvisrLocation>
            {
                new Vegvisir.VegvisrLocation
                {
                    m_locationName = DeepNorthLocationKey,
                    m_pinName = DeepNorthPinName,
                    m_pinType = Minimap.PinType.Boss,
                    m_showMap = true,
                    m_discoverAll = false
                }
            };

            // Cosmic purple beacon light so the Vegvisir stands out clearly inside Lord Reto's dark Ashlands tomb
            var lightObj = new GameObject("AstralVegvisirLight");
            lightObj.transform.SetParent(go.transform, false);
            lightObj.transform.localPosition = new Vector3(0f, 1.05f, 0.45f);
            var light = lightObj.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(0.74f, 0.30f, 1.0f);
            light.range = 6.5f;
            light.intensity = 2.1f;
            light.shadows = LightShadows.None;

            return go;
        }

        private static Mesh GetAstralVegvisirMesh()
        {
            if (s_vegvisirMesh != null) return s_vegvisirMesh;

            var verts = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();

            void AddQuad(Vector3 bl, Vector3 br, Vector3 tr, Vector3 tl, Vector2 uvBL, Vector2 uvBR, Vector2 uvTR, Vector2 uvTL)
            {
                int i = verts.Count;
                Vector3 n = Vector3.Cross(tl - bl, br - bl).normalized;
                verts.Add(bl); verts.Add(br); verts.Add(tr); verts.Add(tl);
                normals.Add(n); normals.Add(n); normals.Add(n); normals.Add(n);
                uvs.Add(uvBL); uvs.Add(uvBR); uvs.Add(uvTR); uvs.Add(uvTL);
                tris.Add(i); tris.Add(i + 2); tris.Add(i + 1);
                tris.Add(i); tris.Add(i + 3); tris.Add(i + 2);
            }

            void AddBox(float halfW, float y0, float y1, float halfD, float topTaperX, float topTaperZ, bool mapFrontEmblem)
            {
                float hw0 = halfW, hw1 = halfW * topTaperX;
                float hd0 = halfD, hd1 = halfD * topTaperZ;

                Vector3 fbl = new Vector3(-hw0, y0,  hd0);
                Vector3 fbr = new Vector3( hw0, y0,  hd0);
                Vector3 ftr = new Vector3( hw1, y1,  hd1);
                Vector3 ftl = new Vector3(-hw1, y1,  hd1);

                Vector3 bbl = new Vector3(-hw0, y0, -hd0);
                Vector3 bbr = new Vector3( hw0, y0, -hd0);
                Vector3 btr = new Vector3( hw1, y1, -hd1);
                Vector3 btl = new Vector3(-hw1, y1, -hd1);

                Vector2 stoneUV0 = new Vector2(0.02f, 0.02f);
                Vector2 stoneUV1 = new Vector2(0.10f, 0.02f);
                Vector2 stoneUV2 = new Vector2(0.10f, 0.10f);
                Vector2 stoneUV3 = new Vector2(0.02f, 0.10f);

                // Front (+Z) — maps Aurvandil's Fylgja cosmic rune sigil when mapFrontEmblem is true
                if (mapFrontEmblem)
                {
                    AddQuad(fbr, fbl, ftl, ftr,
                        new Vector2(0.17f, 0.12f), new Vector2(0.83f, 0.12f),
                        new Vector2(0.83f, 0.90f), new Vector2(0.17f, 0.90f));
                }
                else
                {
                    AddQuad(fbr, fbl, ftl, ftr, stoneUV0, stoneUV1, stoneUV2, stoneUV3);
                }

                // Back (-Z) — also display etched sigil subtly so readable from behind
                if (mapFrontEmblem)
                {
                    AddQuad(bbl, bbr, btr, btl,
                        new Vector2(0.17f, 0.12f), new Vector2(0.83f, 0.12f),
                        new Vector2(0.83f, 0.90f), new Vector2(0.17f, 0.90f));
                }
                else
                {
                    AddQuad(bbl, bbr, btr, btl, stoneUV0, stoneUV1, stoneUV2, stoneUV3);
                }

                // Left (-X), Right (+X), Top (+Y)
                AddQuad(fbl, bbl, btl, ftl, stoneUV0, stoneUV1, stoneUV2, stoneUV3);
                AddQuad(bbr, fbr, ftr, btr, stoneUV0, stoneUV1, stoneUV2, stoneUV3);
                AddQuad(ftl, btl, btr, ftr, stoneUV0, stoneUV1, stoneUV2, stoneUV3);
            }

            // Stepped basalt stone plinth + tapered standing rune stele + chiseled apex cap
            AddBox(halfW: 0.44f, y0: -0.06f, y1: 0.22f, halfD: 0.24f, topTaperX: 0.90f, topTaperZ: 0.88f, mapFrontEmblem: false);
            AddBox(halfW: 0.34f, y0:  0.22f, y1: 1.38f, halfD: 0.14f, topTaperX: 0.80f, topTaperZ: 0.78f, mapFrontEmblem: true);
            AddBox(halfW: 0.27f, y0:  1.38f, y1: 1.56f, halfD: 0.11f, topTaperX: 0.36f, topTaperZ: 0.40f, mapFrontEmblem: false);

            s_vegvisirMesh = new Mesh
            {
                name = "AurvandilsFylgja_VegvisirMesh",
                vertices = verts.ToArray(),
                normals = normals.ToArray(),
                uv = uvs.ToArray(),
                triangles = tris.ToArray()
            };
            s_vegvisirMesh.RecalculateBounds();
            return s_vegvisirMesh;
        }

        private static Material GetAstralVegvisirMaterial()
        {
            if (s_vegvisirMaterial != null) return s_vegvisirMaterial;

            Shader shader = StandEquipmentBuilder.FindValheimOpaqueShader();
            Material mat = shader != null
                ? new Material(shader)
                : StandMaterialManager.GetMaterial(new Color(0.24f, 0.20f, 0.30f, 1f));

            mat.name = "AurvandilsFylgja_VegvisirMat";
            mat.renderQueue = -1;
            mat.color = Color.white;
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", Color.white);
            if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", 0.18f);
            if (mat.HasProperty("_Glossiness")) mat.SetFloat("_Glossiness", 0.35f);

            Texture2D srcTex = IconManager.BossStoneTexture;
            if (srcTex != null)
            {
                mat.mainTexture = srcTex;
                if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", srcTex);

                // Extract glowing violet & gold runes from bossstone_aurvandilsfylgja.png for crisp dark-tomb emission
                try
                {
                    int w = srcTex.width;
                    int h = srcTex.height;
                    Color[] srcPx = srcTex.GetPixels();
                    Color[] emisPx = new Color[srcPx.Length];
                    for (int i = 0; i < srcPx.Length; i++)
                    {
                        Color ov = srcPx[i];
                        float purpleGlow = Mathf.Max(0f, Mathf.Min(ov.r, ov.b) - ov.g * 0.82f);
                        float goldGlow = Mathf.Max(0f, Mathf.Min(ov.r, ov.g) - ov.b * 0.85f);
                        float strength = Mathf.Clamp01((purpleGlow * 3.0f + goldGlow * 2.4f) * ov.a);
                        emisPx[i] = strength > 0.02f
                            ? new Color(ov.r * strength, ov.g * strength, ov.b * strength, 1f)
                            : Color.black;
                    }
                    var emisTex = new Texture2D(w, h, TextureFormat.RGBA32, true)
                    {
                        name = "AurvandilsFylgja_VegvisirEmis",
                        wrapMode = TextureWrapMode.Clamp,
                        filterMode = FilterMode.Bilinear
                    };
                    emisTex.SetPixels(emisPx);
                    emisTex.Apply(true, false);

                    if (mat.HasProperty("_EmissionMap")) mat.SetTexture("_EmissionMap", emisTex);
                    if (mat.HasProperty("_EmissiveTex")) mat.SetTexture("_EmissiveTex", emisTex);
                }
                catch (Exception ex)
                {
                    Plugin.Log?.LogWarning($"SacrificialStoneManager: Could not build Vegvisir emission map: {ex.Message}");
                }
            }

            Color glow = new Color(0.85f, 0.55f, 1.25f, 1f);
            if (mat.HasProperty("_EmissionColor"))
            {
                mat.SetColor("_EmissionColor", glow);
                mat.EnableKeyword("_EMISSION");
            }
            if (mat.HasProperty("_EmissiveColor"))
            {
                mat.SetColor("_EmissiveColor", glow);
            }

            s_vegvisirMaterial = mat;
            return s_vegvisirMaterial;
        }

        internal static void TickServerUpdate()
        {
            if (Time.time < s_nextZoneCheckTime) return;
            s_nextZoneCheckTime = Time.time + 1.5f;
            CheckDeepNorthStoneSpawnOnServer();
        }
    }

    /// <summary>
    /// Component attached to every BossStone_AurvandilsFylgja instance to keep its visuals,
    /// emission glow, RuneStone inscription, and ItemStand trophy/power bindings active across zone loads,
    /// and automatically remove legacy StartTemple copies.
    /// </summary>
    public class AurvandilsFylgjaBossStone : MonoBehaviour
    {
        private BossStone m_bossStone;

        private void Awake()
        {
            m_bossStone = GetComponent<BossStone>();
            SacrificialStoneManager.ConfigureStoneInstance(gameObject);
        }

        private void Start()
        {
            if (SacrificialStoneManager.DestroyLegacyStoneIfNeeded(gameObject) ||
                SacrificialStoneManager.DestroyDuplicateDeepNorthStoneIfNeeded(gameObject))
            {
                return;
            }

            m_bossStone = GetComponent<BossStone>();
            SacrificialStoneManager.ConfigureStoneInstance(gameObject);
            UpdateEmissionAndLight();
        }

        public void UpdateEmissionAndLight()
        {
            if (m_bossStone == null) m_bossStone = GetComponent<BossStone>();
            if (m_bossStone == null) return;

            bool active = m_bossStone.IsActivated();
            Color targetEmis = active
                ? SacrificialStoneManager.ActiveEmissiveColor
                : SacrificialStoneManager.IdleEmissiveColor;

            if (m_bossStone.m_mesh != null && m_bossStone.m_mesh.materials != null &&
                m_bossStone.m_emissiveMaterialIndex >= 0 &&
                m_bossStone.m_emissiveMaterialIndex < m_bossStone.m_mesh.materials.Length)
            {
                Material mat = m_bossStone.m_mesh.materials[m_bossStone.m_emissiveMaterialIndex];
                if (mat != null)
                {
                    if (mat.HasProperty("_EmissionColor")) mat.SetColor("_EmissionColor", targetEmis);
                    if (mat.HasProperty("_EmissiveColor")) mat.SetColor("_EmissiveColor", targetEmis);
                }
            }
        }
    }

    [HarmonyPatch]
    internal static class SacrificialStonePatches
    {
        [HarmonyPatch(typeof(ZNetScene), "Awake")]
        [HarmonyPostfix]
        private static void ZNetScene_Awake_Postfix(ZNetScene __instance)
        {
            SacrificialStoneManager.ResetWorldCache();
            SacrificialStoneManager.RegisterPrefab(__instance);
        }

        [HarmonyPatch(typeof(ObjectDB), "Awake")]
        [HarmonyPostfix]
        private static void ObjectDB_Awake_Postfix()
        {
            SacrificialStoneManager.RefreshAllConfiguredStones();
        }

        [HarmonyPatch(typeof(ObjectDB), "CopyOtherDB")]
        [HarmonyPostfix]
        private static void ObjectDB_CopyOtherDB_Postfix()
        {
            SacrificialStoneManager.RefreshAllConfiguredStones();
        }

        [HarmonyPatch(typeof(ZoneSystem), "SpawnZone")]
        [HarmonyPostfix]
        private static void ZoneSystem_SpawnZone_Postfix(Vector2s zoneID, ZoneSystem.SpawnMode mode)
        {
            SacrificialStoneManager.OnZoneSpawned(zoneID, mode);
        }

        [HarmonyPatch(typeof(ZoneSystem), "Update")]
        [HarmonyPostfix]
        private static void ZoneSystem_Update_Postfix()
        {
            SacrificialStoneManager.TickServerUpdate();
        }

        [HarmonyPatch(typeof(Location), "Awake")]
        [HarmonyPostfix]
        private static void Location_Awake_Postfix(Location __instance)
        {
            if (__instance == null) return;
            string name = __instance.gameObject.name;
            if (!string.IsNullOrEmpty(name) &&
                name.IndexOf(SacrificialStoneManager.LordRetoLocationName, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                SacrificialStoneManager.ScheduleLordRetoVegvisirPlacement(__instance);
            }
        }

        [HarmonyPatch(typeof(Game), "RPC_DiscoverClosestLocation")]
        [HarmonyPrefix]
        private static bool Game_RPC_DiscoverClosestLocation_Prefix(
            long sender,
            string name,
            Vector3 point,
            string pinName,
            int pinType,
            bool showMap,
            bool discoverAll)
        {
            if (!string.Equals(name, SacrificialStoneManager.DeepNorthLocationKey, StringComparison.Ordinal))
            {
                return true;
            }

            if (SacrificialStoneManager.TryResolveDeepNorthStonePosition(out Vector3 stonePos) &&
                ZRoutedRpc.instance != null)
            {
                string resolvedPin = string.IsNullOrEmpty(pinName) ? SacrificialStoneManager.DeepNorthPinName : pinName;
                ZRoutedRpc.instance.InvokeRoutedRPC(
                    sender,
                    "DiscoverLocationResponse",
                    resolvedPin,
                    pinType,
                    stonePos,
                    showMap);
            }
            return false;
        }

        [HarmonyPatch(typeof(ZoneSystem), "FindClosestLocation")]
        [HarmonyPrefix]
        private static bool ZoneSystem_FindClosestLocation_Prefix(
            string name,
            Vector3 point,
            ref ZoneSystem.LocationInstance closest,
            ref bool __result)
        {
            if (!string.Equals(name, SacrificialStoneManager.DeepNorthLocationKey, StringComparison.Ordinal))
            {
                return true;
            }

            if (SacrificialStoneManager.TryResolveDeepNorthStonePosition(out Vector3 stonePos))
            {
                closest = new ZoneSystem.LocationInstance
                {
                    m_location = SacrificialStoneManager.GetOrCreateVirtualZoneLocation(),
                    m_position = stonePos,
                    m_placed = true
                };
                __result = true;
            }
            else
            {
                __result = false;
            }
            return false;
        }

        [HarmonyPatch(typeof(BossStone), "Start")]
        [HarmonyPostfix]
        private static void BossStone_Start_Postfix(BossStone __instance)
        {
            if (__instance == null) return;

            if (SacrificialStoneManager.IsAurvandilsFylgjaStone(__instance))
            {
                if (SacrificialStoneManager.DestroyLegacyStoneIfNeeded(__instance.gameObject))
                {
                    return;
                }

                SacrificialStoneManager.ConfigureStoneInstance(__instance.gameObject);
                __instance.GetComponent<AurvandilsFylgjaBossStone>()?.UpdateEmissionAndLight();
            }
        }

        [HarmonyPatch(typeof(BossStone), "UpdateVisual")]
        [HarmonyPostfix]
        private static void BossStone_UpdateVisual_Postfix(BossStone __instance)
        {
            if (__instance != null && SacrificialStoneManager.IsAurvandilsFylgjaStone(__instance))
            {
                __instance.GetComponent<AurvandilsFylgjaBossStone>()?.UpdateEmissionAndLight();
            }
        }
    }
}
