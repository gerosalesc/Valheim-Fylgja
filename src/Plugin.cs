using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace StandsOfValheim
{
    public enum FylgjaModelStyle
    {
        CustomValheim3D,
        ClassicProcedural
    }

    [BepInPlugin(Guid, Name, Version)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "grosales.StandsOfValheim";
        public const string Name = "Valheim Fylgja";
        public const string Version = "0.2.2";

        internal static ManualLogSource Log;
        public static ConfigEntry<FylgjaModelStyle> ModelStyleConfig { get; private set; }
        public static ConfigEntry<float> ModelOpacityConfig { get; private set; }
        public static ConfigEntry<float> SinglePunchBaseDamageConfig { get; private set; }
        public static ConfigEntry<float> BarrageCooldownConfig { get; private set; }
        public static ConfigEntry<float> BarrageDamageMultiplierConfig { get; private set; }

        public static bool UseCustom3DModel =>
            ModelStyleConfig == null || ModelStyleConfig.Value == FylgjaModelStyle.CustomValheim3D;

        public static float ModelOpacity =>
            ModelOpacityConfig != null ? Mathf.Clamp(ModelOpacityConfig.Value, 0.20f, 1.00f) : 0.72f;

        public static float SinglePunchBaseDamage =>
            SinglePunchBaseDamageConfig != null ? Mathf.Clamp(SinglePunchBaseDamageConfig.Value, 10.0f, 500.0f) : 120.0f;

        public static float BarrageCooldownSeconds =>
            BarrageCooldownConfig != null ? Mathf.Max(0.5f, BarrageCooldownConfig.Value) : 6.0f;

        public static float BarrageDamageMultiplier =>
            BarrageDamageMultiplierConfig != null ? Mathf.Clamp(BarrageDamageMultiplierConfig.Value, 0.5f, 20.0f) : 4.0f;

        private void Awake()
        {
            Log = Logger;

            ModelStyleConfig = Config.Bind(
                "Visuals",
                "ModelStyle",
                FylgjaModelStyle.CustomValheim3D,
                "Selects which 3D model to use for Aurvandil's Fylgja: 'CustomValheim3D' (the Valheim-styled custom 3D mesh, default) or 'ClassicProcedural' (the original procedural Valheim humanoid + armor builder).");

            ModelOpacityConfig = Config.Bind(
                "Visuals",
                "ModelOpacity",
                0.72f,
                new ConfigDescription(
                    "Spectral opacity of Aurvandil's Fylgja (0.20 = very ghostly translucent, 0.72 = default astral spirit transparency, 1.00 = fully opaque).",
                    new AcceptableValueRange<float>(0.20f, 1.00f)));

            SinglePunchBaseDamageConfig = Config.Bind(
                "Combat",
                "SinglePunchBaseDamage",
                120.0f,
                new ConfigDescription(
                    "Base blunt damage for Aurvandil's Fylgja's single punch (Left Mouse Button) before Unarmed skill scaling (0.55 minimum factor), active buffs, and the 1.5x 3rd-hit combo finisher.",
                    new AcceptableValueRange<float>(10.0f, 500.0f)));

            BarrageCooldownConfig = Config.Bind(
                "Combat",
                "BarrageCooldown",
                6.0f,
                new ConfigDescription(
                    "Cooldown in seconds between 12-hit Astral Barrage (Middle Mouse Button) activations.",
                    new AcceptableValueRange<float>(0.5f, 60.0f)));

            BarrageDamageMultiplierConfig = Config.Bind(
                "Combat",
                "BarrageDamageMultiplier",
                4.0f,
                new ConfigDescription(
                    "Damage multiplier applied to the 35-blunt barrage base for every hit of the 12-hit Astral Barrage before Unarmed skill scaling (0.55 minimum factor) and active buffs (default 4.0 = 140 base blunt per hit).",
                    new AcceptableValueRange<float>(0.5f, 20.0f)));

            ModelStyleConfig.SettingChanged += (_, __) =>
            {
                Log?.LogInfo($"ModelStyle changed to {ModelStyleConfig.Value}.");
                RefreshActiveFylgjaVisuals();
            };

            ModelOpacityConfig.SettingChanged += (_, __) =>
            {
                Log?.LogInfo($"ModelOpacity changed to {ModelOpacity:0.00}.");
                CustomValheimModelBuilder.InvalidateMaterials();
                StandEquipmentBuilder.InvalidateMaterials();
                StandBarrageVfx.InvalidateMaterials();
                RefreshActiveFylgjaVisuals();
            };

            new Harmony(Guid).PatchAll();
            Log.LogInfo($"{Name} {Version} loaded (ModelStyle={ModelStyleConfig.Value}, ModelOpacity={ModelOpacity:0.00}). Use devcommands, then 'fylgja', 'fylgja_model', 'fylgja_opacity', or 'fylgja_stone' in the F5 console.");
        }

        private static void RefreshActiveFylgjaVisuals()
        {
            if (Player.m_localPlayer != null && StandManager.IsActive(Player.m_localPlayer))
            {
                StandManager.Summon(Player.m_localPlayer);
            }
        }
    }
}
