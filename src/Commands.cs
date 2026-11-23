using System;
using System.Globalization;
using HarmonyLib;
using UnityEngine;

namespace StandsOfValheim
{
    /// <summary>Dev commands (requires `devcommands`) to equip Aurvandil's Fylgja, adjust visuals, and inspect Deep North stone / Lord Reto coordinates.</summary>
    [HarmonyPatch(typeof(Terminal), "InitTerminal")]
    internal static class Commands
    {
        [HarmonyPostfix]
        private static void Postfix()
        {
            new Terminal.ConsoleCommand("fylgja",
                "equip Aurvandil's Fylgja as your forsaken power (subcommands: 'fylgja model [custom|classic]', 'fylgja opacity [0.2-1.0]', 'fylgja stone [pin]')", args =>
                {
                    if (args.Length >= 2)
                    {
                        if (string.Equals(args[1], "model", StringComparison.OrdinalIgnoreCase))
                        {
                            HandleModelCommand(args, startIndex: 2);
                            return;
                        }
                        if (string.Equals(args[1], "opacity", StringComparison.OrdinalIgnoreCase))
                        {
                            HandleOpacityCommand(args, startIndex: 2);
                            return;
                        }
                        if (string.Equals(args[1], "stone", StringComparison.OrdinalIgnoreCase))
                        {
                            HandleStoneCommand(args, startIndex: 2);
                            return;
                        }
                    }

                    var player = Player.m_localPlayer;
                    if (player == null) { args.Context.AddString("No local player."); return; }
                    player.SetGuardianPower(SE_AurvandilsFylgja.EffectName);
                    args.Context.AddString($"Aurvandil's Fylgja equipped (ModelStyle={Plugin.ModelStyleConfig?.Value}, Opacity={Plugin.ModelOpacity:0.00}). Press the forsaken power key to summon.");
                }, isCheat: true);

            new Terminal.ConsoleCommand("fylgja_model",
                "switch Aurvandil's Fylgja 3D model style: 'fylgja_model custom' (Valheim-styled custom 3D model) or 'fylgja_model classic' (procedural Valheim humanoid)", args =>
                {
                    HandleModelCommand(args, startIndex: 1);
                }, isCheat: true);

            new Terminal.ConsoleCommand("fylgja_opacity",
                "set Aurvandil's Fylgja spectral opacity (0.20 to 1.00, default 0.72), e.g. 'fylgja_opacity 0.65'", args =>
                {
                    HandleOpacityCommand(args, startIndex: 1);
                }, isCheat: true);

            new Terminal.ConsoleCommand("fylgja_stone",
                "show coordinates & live status of Aurvandil's Fylgja's Deep North Sacrificial Stone and nearest Lord Reto's Tomb (subcommands: 'pin', 'fix')", args =>
                {
                    HandleStoneCommand(args, startIndex: 1);
                }, isCheat: true);

            new Terminal.ConsoleCommand("stand",
                "alias for 'fylgja': equip Aurvandil's Fylgja as your forsaken power", args =>
                {
                    var player = Player.m_localPlayer;
                    if (player == null) { args.Context.AddString("No local player."); return; }
                    player.SetGuardianPower(SE_AurvandilsFylgja.EffectName);
                    args.Context.AddString("Aurvandil's Fylgja equipped. Press the forsaken power key to summon.");
                }, isCheat: true);
        }

        private static void HandleOpacityCommand(Terminal.ConsoleEventArgs args, int startIndex)
        {
            if (Plugin.ModelOpacityConfig == null)
            {
                args.Context.AddString("ModelOpacity configuration is not initialized.");
                return;
            }

            if (args.Length <= startIndex)
            {
                args.Context.AddString($"Current Aurvandil's Fylgja ModelOpacity = {Plugin.ModelOpacity:0.00} (range 0.20 - 1.00). Usage: fylgja_opacity 0.72");
                return;
            }

            if (!float.TryParse(args[startIndex], NumberStyles.Float, CultureInfo.InvariantCulture, out float val))
            {
                args.Context.AddString($"Invalid opacity '{args[startIndex]}'. Enter a number between 0.20 and 1.00.");
                return;
            }

            float clamped = Mathf.Clamp(val, 0.20f, 1.00f);
            Plugin.ModelOpacityConfig.Value = clamped;
            args.Context.AddString($"Aurvandil's Fylgja ModelOpacity set to {clamped:0.00} (saved to BepInEx/config/{Plugin.Guid}.cfg).");
        }

        private static void HandleStoneCommand(Terminal.ConsoleEventArgs args, int startIndex)
        {
            string sub = args.Length > startIndex ? args[startIndex].Trim().ToLowerInvariant() : string.Empty;
            bool pin = sub == "pin";
            bool fix = sub == "fix" || sub == "respawn" || sub == "ground";

            if (fix)
            {
                args.Context.AddString(SacrificialStoneManager.ForceFixOrSpawnDeepNorthStone());
            }

            if (SacrificialStoneManager.TryResolveDeepNorthStonePosition(out Vector3 stonePos))
            {
                string distStr = "";
                if (Player.m_localPlayer != null)
                {
                    float d = Vector3.Distance(Player.m_localPlayer.transform.position, stonePos);
                    distStr = $", dist={d:0.0}m";
                }
                args.Context.AddString($"Aurvandil's Fylgja Deep North Sacrificial Stone: ({stonePos.x:0}, {stonePos.y:0}, {stonePos.z:0}{distStr}) [goto {stonePos.x:0} {stonePos.z:0}]");
                args.Context.AddString($"Stone Status: {SacrificialStoneManager.GetDeepNorthStoneDiagnostics()}");
                if (pin && Game.instance != null && Player.m_localPlayer != null)
                {
                    Game.instance.DiscoverClosestLocation(
                        SacrificialStoneManager.DeepNorthLocationKey,
                        Player.m_localPlayer.transform.position,
                        SacrificialStoneManager.DeepNorthPinName,
                        (int)Minimap.PinType.Boss,
                        showMap: true,
                        discoverAll: false);
                }
            }
            else
            {
                args.Context.AddString("Could not resolve Deep North Sacrificial Stone location (WorldGenerator not ready).");
            }

            if (ZoneSystem.instance != null && Player.m_localPlayer != null)
            {
                if (ZoneSystem.instance.FindClosestLocation(
                    SacrificialStoneManager.LordRetoLocationName,
                    Player.m_localPlayer.transform.position,
                    out ZoneSystem.LocationInstance retoLoc))
                {
                    Vector3 rp = retoLoc.m_position;
                    args.Context.AddString($"Nearest Lord Reto's Tomb ({SacrificialStoneManager.LordRetoLocationName}): ({rp.x:0}, {rp.y:0}, {rp.z:0}) [goto {rp.x:0} {rp.z:0}]");
                }
            }
        }

        private static void HandleModelCommand(Terminal.ConsoleEventArgs args, int startIndex)
        {
            if (Plugin.ModelStyleConfig == null)
            {
                args.Context.AddString("ModelStyle configuration is not initialized.");
                return;
            }

            FylgjaModelStyle target;
            if (args.Length <= startIndex)
            {
                // Toggle between the two styles if no argument was passed
                target = Plugin.ModelStyleConfig.Value == FylgjaModelStyle.CustomValheim3D
                    ? FylgjaModelStyle.ClassicProcedural
                    : FylgjaModelStyle.CustomValheim3D;
            }
            else
            {
                string mode = args[startIndex].Trim().ToLowerInvariant();
                if (mode == "custom" || mode == "custom3d" || mode == "customvalheim3d" || mode == "new" || mode == "3d")
                {
                    target = FylgjaModelStyle.CustomValheim3D;
                }
                else if (mode == "classic" || mode == "procedural" || mode == "classicprocedural" || mode == "old" || mode == "current")
                {
                    target = FylgjaModelStyle.ClassicProcedural;
                }
                else
                {
                    args.Context.AddString($"Unknown model style '{args[startIndex]}'. Use 'custom' (CustomValheim3D) or 'classic' (ClassicProcedural).");
                    return;
                }
            }

            Plugin.ModelStyleConfig.Value = target;
            args.Context.AddString($"Aurvandil's Fylgja ModelStyle set to: {target} (saved to BepInEx/config/{Plugin.Guid}.cfg).");
        }
    }
}
