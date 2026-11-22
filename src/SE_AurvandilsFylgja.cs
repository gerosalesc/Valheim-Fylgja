using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace StandsOfValheim
{
    /// <summary>
    /// Aurvandil's Fylgja as a Valheim guardian (forsaken) power. Enforces "one at a time",
    /// the cooldown (10m / 600s), active duration (5m / 300s), the HUD slot, and activation via the 'F' power key.
    /// </summary>
    public class SE_AurvandilsFylgja : SE_Stats
    {
        public const string EffectName = "GP_AurvandilsFylgja";
        public const string LegacyEffectName = "GP_StarPlatinum";
        private const string ActivationEffectPrefabName = "fx_GP_Activation";

        private static readonly string[] BossKeywords =
        {
            "eikthyr", "elder", "bonemass", "moder", "yagluth", "queen", "fader"
        };

        public override void Setup(Character character)
        {
            EnsureActivationEffects();
            base.Setup(character);
            if (character is Player p) StandManager.Summon(p);
        }

        public override void Stop()
        {
            base.Stop();
            if (m_character is Player p) StandManager.Dismiss(p);
        }

        internal void EnsureActivationEffects(ObjectDB db = null)
        {
            m_activationAnimation = "gpower";
            if (HasValidEffects(m_startEffects)) return;

            var resolved = ResolveActivationEffects(db);
            if (HasValidEffects(resolved))
            {
                m_startEffects = resolved;
            }
        }

        internal static EffectList ResolveActivationEffects(ObjectDB db = null)
        {
            db = db ?? ObjectDB.instance;
            if (db != null && db.m_StatusEffects != null)
            {
                // Prefer GP_Eikthyr first, then any other vanilla guardian power for the shared activation FX
                var templates = new List<StatusEffect>();
                var eikthyr = db.m_StatusEffects.Find(s => s != null && s.name == "GP_Eikthyr");
                if (eikthyr != null) templates.Add(eikthyr);

                foreach (var se in db.m_StatusEffects)
                {
                    if (se != null && se != eikthyr &&
                        se.name.StartsWith("GP_", StringComparison.Ordinal) &&
                        se.name != EffectName &&
                        se.name != LegacyEffectName)
                    {
                        templates.Add(se);
                    }
                }

                foreach (var template in templates)
                {
                    var cloned = CloneNonBossEffects(template.m_startEffects);
                    if (HasValidEffects(cloned)) return cloned;
                }
            }

            if (ZNetScene.instance != null)
            {
                var fxPrefab = ZNetScene.instance.GetPrefab(ActivationEffectPrefabName);
                if (fxPrefab != null)
                {
                    return new EffectList
                    {
                        m_effectPrefabs = new[]
                        {
                            new EffectList.EffectData
                            {
                                m_prefab = fxPrefab,
                                m_enabled = true,
                                m_variant = -1,
                                m_attach = true
                            }
                        }
                    };
                }
            }

            return null;
        }

        private static bool HasValidEffects(EffectList list)
        {
            if (list == null || list.m_effectPrefabs == null) return false;
            foreach (var d in list.m_effectPrefabs)
            {
                if (d != null && d.m_enabled && d.m_prefab != null) return true;
            }
            return false;
        }

        private static EffectList CloneNonBossEffects(EffectList source)
        {
            if (source == null || source.m_effectPrefabs == null || source.m_effectPrefabs.Length == 0)
            {
                return null;
            }

            var kept = new List<EffectList.EffectData>();
            foreach (var d in source.m_effectPrefabs)
            {
                if (d == null || !d.m_enabled || d.m_prefab == null) continue;
                string lower = d.m_prefab.name.ToLowerInvariant();
                bool isActivation = lower.Contains("gp_activation");
                if (!isActivation && ContainsBossKeyword(lower)) continue;

                kept.Add(new EffectList.EffectData
                {
                    m_prefab = d.m_prefab,
                    m_enabled = d.m_enabled,
                    m_variant = d.m_variant,
                    m_attach = d.m_attach,
                    m_follow = d.m_follow,
                    m_inheritParentRotation = d.m_inheritParentRotation,
                    m_inheritParentScale = d.m_inheritParentScale,
                    m_multiplyParentVisualScale = d.m_multiplyParentVisualScale,
                    m_randomRotation = d.m_randomRotation,
                    m_scale = d.m_scale,
                    m_childTransform = d.m_childTransform
                });
            }

            if (kept.Count == 0) return null;
            return new EffectList { m_effectPrefabs = kept.ToArray() };
        }

        private static bool ContainsBossKeyword(string lowerName)
        {
            foreach (var kw in BossKeywords)
            {
                if (lowerName.Contains(kw)) return true;
            }
            return false;
        }
    }

    /// <summary>Registers the status effect into ObjectDB so it can be equipped as a guardian power.</summary>
    [HarmonyPatch]
    internal static class ObjectDBPatches
    {
        [HarmonyPatch(typeof(ObjectDB), "Awake")]
        [HarmonyPostfix]
        private static void Awake(ObjectDB __instance) => Register(__instance);

        [HarmonyPatch(typeof(ObjectDB), "CopyOtherDB")]
        [HarmonyPostfix]
        private static void CopyOtherDB(ObjectDB __instance) => Register(__instance);

        private static void Register(ObjectDB db)
        {
            if (db.m_StatusEffects.Count == 0) return;

            var icon = IconManager.AurvandilsFylgjaIcon;
            if (icon == null)
            {
                var template = db.m_StatusEffects.Find(s => s != null && s.name == "GP_Eikthyr");
                if (template != null) icon = template.m_icon;
            }

            RegisterOrUpdateEffect(db, SE_AurvandilsFylgja.EffectName, icon);
            RegisterOrUpdateEffect(db, SE_AurvandilsFylgja.LegacyEffectName, icon);

            if (Player.m_localPlayer != null)
            {
                string currentGp = Player.m_localPlayer.GetGuardianPowerName();
                if (currentGp == SE_AurvandilsFylgja.EffectName || currentGp == SE_AurvandilsFylgja.LegacyEffectName)
                {
                    Player.m_localPlayer.SetGuardianPower(SE_AurvandilsFylgja.EffectName);
                }
            }
        }

        private static void RegisterOrUpdateEffect(ObjectDB db, string effectName, Sprite icon)
        {
            var existing = db.m_StatusEffects.Find(s => s != null && s.name == effectName);
            if (existing != null)
            {
                ConfigureStatusEffect(existing, db, icon);
                return;
            }

            var se = ScriptableObject.CreateInstance<SE_AurvandilsFylgja>();
            se.name = effectName;
            ConfigureStatusEffect(se, db, icon);

            db.m_StatusEffects.Add(se);
            Plugin.Log?.LogInfo($"Registered {effectName} in ObjectDB with Aurvandil's Fylgja runic bust icon.");
        }

        private static void ConfigureStatusEffect(StatusEffect effect, ObjectDB db, Sprite icon)
        {
            effect.m_name = "Aurvandil's Fylgja";
            effect.m_tooltip = "Summon the Aurvandil's Fylgja (5m active, 10m cooldown). Free your hands for Fylgja punch, astral barrage, and Fylgja shield defense!";
            effect.m_startMessageType = MessageHud.MessageType.Center;
            effect.m_startMessage = "AURVANDIL'S FYLGJA!";
            effect.m_ttl = 300f;
            effect.m_cooldown = 600f;
            effect.m_activationAnimation = "gpower";
            if (icon != null) effect.m_icon = icon;

            if (effect is SE_Stats stats)
            {
                stats.m_addMaxCarryWeight = 100f;
                stats.m_staminaRegenMultiplier = 1f;
            }

            if (effect is SE_AurvandilsFylgja af)
            {
                af.EnsureActivationEffects(db);
            }
            else
            {
                var fx = SE_AurvandilsFylgja.ResolveActivationEffects(db);
                if (fx != null) effect.m_startEffects = fx;
            }
        }
    }
}
