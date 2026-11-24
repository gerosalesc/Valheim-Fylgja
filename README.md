# Valheim Fylgja (`StandsOfValheim`)

A **BepInEx** mod for **Valheim** (`v0.2.2`) that introduces summonable **Fylgja** astral guardian spirits integrated natively as **Forsaken Powers**. Currently featuring: **Aurvandil's Fylgja**.

For step-by-step Windows setup, multiplayer rules, configuration, and in-game usage, see the [Installation & Usage Guide (INSTALL.md)](INSTALL.md).

---

## Table of Contents
- [Features](#features)
- [Combat & Defensive Mechanics](#combat--defensive-mechanics)
- [Configurable 3D Models (`CustomValheim3D` vs. `ClassicProcedural`)](#configurable-3d-models-customvalheim3d-vs-classicprocedural)
- [Console Commands](#console-commands)
- [Configuration File (`grosales.StandsOfValheim.cfg`)](#configuration-file-grosalesstandsofvalheimcfg)
- [Installation](#installation)
- [Building from Source](#building-from-source)
- [Repository Architecture](#repository-architecture)
- [License](#license)

---

## Features

- **Deep North Sacrificial Stone & Ashlands Discovery Quest:**
  - Registers `BossStone_AurvandilsFylgja` in `ZNetScene` and places the custom rune-carved sacrificial monolith (with a violet cosmic beacon light) in an **open Deep North clearing** well away from trees, boulders, and ice ponds at a random walkable location per world seed (removed from the Start Temple, with automatic cleanup of any legacy Start Temple stones on existing worlds).
  - Explore the deepest sanctuary of **Lord Reto's Tomb** (`PlaceofMystery3`) in the **Ashlands** (past Lord Reto) to find a 3D carved weathered-granite **Astral Vegvisir** (`Vegvisir: Aurvandil's Fylgja`) glowing with cosmic violet runes—interacting with it pans your map north and pins the exact Deep North coordinates of the sacrificial monolith.
  - Travel to the Deep North monolith, hang a **Fallen Valkyrie Trophy** (`TrophyFallenValkyrie`) on its hook, and interact with the glowing stone to claim **Aurvandil's Fylgja** (`GP_AurvandilsFylgja`).
- **Spectral Transparency & Ethereal Floating Effect:**
  - Renders Aurvandil's Fylgja with depth-preserving **spectral PBR transparency** (`Unlit/DepthWrite` pre-pass + Valheim's `Standard TwoSided` premultiplied transparent pass, configurable via `[Visuals] ModelOpacity`, default `0.72`) so the guardian looks authentically ghostly with glowing eyes, amethyst forehead gem, and zero internal back-face bleed.
  - Floats effortlessly above the ground behind the player's shoulder with multi-harmonic levitation bobbing, figure-8 astral drift, velocity-reactive aerial lean/banking when running, weightless suspended leg posture, a breathing violet aura light, and rising astral motes.
- **Native Forsaken Power Integration:**
  - Summoned via the **`F`** Forsaken Power key (**`5 minutes`** active duration, **`10 minutes`** cooldown).
  - Displays a custom runic bust HUD icon (`icon_aurvandilsfylgja.png`) and triggers guardian power activation VFX, camera shake, and a dynamic purple point-light aura.
- **Passive Bonus (While Active):**
  - Grants **`+100` Max Carry Weight** (`m_addMaxCarryWeight = 100`) while summoned. No other passive stat modifiers are applied.
- **Two Selectable 3D Model Styles:**
  - Includes a Valheim-styled custom chiseled 3D model (**`CustomValheim3D`**, default) and the classic procedural humanoid + armor builder (**`ClassicProcedural`**), switchable via config or live in-game via the `F5` console. Both support spectral transparency and floating levitation.
- **Single-File Deployment:**
  - All 3D meshes (`.obj`), textures (`.png`), icons, sacrificial stone textures, and barrage audio (`.wav`) are embedded directly inside `StandsOfValheim.dll`.

---

## Combat & Defensive Mechanics

> **Hands-Free & Friendly Fire (PvP) Rules:**
> - **Hands-Free Requirement:** All Fylgja combat and shield actions only trigger when your **hands are completely free** (no weapon, shield, or tool equipped in either hand; press **`R`** to sheathe). When weapons or shields are equipped, normal player combat actions take priority.
> - **PvP / Friendly Fire Protection:** Aurvandil's Fylgja will **only** damage, stagger, or knock back another player if **both** you and the target player have **PvP (Friendly Fire)** enabled (`IsPVPEnabled()`). Tamed animals are also protected unless you have PvP enabled.

| Action | Input (Hands Free) | Stamina Cost | Damage & Properties |
| :--- | :--- | :--- | :--- |
| **Summon Fylgja** | **`F`** (Forsaken Power Key) | `0` | **5m** (`300s`) duration, **10m** (`600s`) cooldown, **`+100` Carry Weight** while active |
| **Fylgja Single Punch** | **Left Mouse Button** | `5` Stamina | **`120` Base Blunt Damage** (configurable via `SinglePunchBaseDamage`), scaled dynamically by `Unarmed` skill (`0.55` min factor up to `1.00` at level 100) and active status effects.<br>`0.34s` duration (`0.40s` cadence), 3-hit combo chain: hits 1–2 deal `1.0x` damage / `1.0x` stagger / `18` knockback; **3rd-hit combo finisher** deals **`1.5x` damage, `1.5x` stagger, `30` knockback**, and emits an impact shockwave ring. |
| **Astral Barrage (12 Hits)** | **Middle Mouse Button** | `16` Stamina<br>*(6.0s Cooldown)* | **Dynamic Damage:** **`4x`** the `35`-blunt barrage base (`140` base blunt per hit, configurable via `BarrageDamageMultiplier`), scaled dynamically by `Unarmed` skill (`0.55` min factor up to `1.00` at level 100) and active status effects.<br>Plays both the looping `ora_barrage.wav` audio and Valheim's normal punch swing & impact sound on **every hit**, trains `Unarmed` skill on hits, applies **`0.75x` stagger per hit** with a **`1.5x` stagger finisher** on hit 12, and keeps knockback calibrated to **50% of the Demolisher** (`SledgeDemolisher`). |
| **Fylgja Shield Guard** | **Hold Right Mouse Button** | Block drain | Moves Fylgja in front of you in a crossed-arm guard stance.<br>**Base Block Armor:** **`2x`** player body armor $\times (1 + 0.5 \times \text{BlockingSkillFactor})$ |
| **Fylgja Timed Parry** | **Right Mouse Button** (`< 0.25s` before hit) | Parry drain | **Parry Block Armor:** **`6x`** player body armor $\times (1 + 0.5 \times \text{BlockingSkillFactor})$<br>Staggers the attacker, triggers parry sparks, and applies `50` deflection knockback |

---

## Configurable 3D Models (`CustomValheim3D` vs. `ClassicProcedural`)

You can choose between two visual implementations for **Aurvandil's Fylgja**:

1. **`CustomValheim3D` (Default):**
   - Uses the custom Valheim-styled chiseled low-poly 3D mesh (`resources/aurvandils_fylgja.obj` — `1,566` vertices, `2,562` triangles) paired with a painterly pixel-art texture sheet (`resources/aurvandils_fylgja_tex.png`), emission map (`resources/aurvandils_fylgja_emission.png`), and runtime tangent-space normal map (`_BumpMap`).
   - Features hammered Viking bronze/dark-gold pauldrons etched with a symmetrical **Norse Odal (`ᛟ`) runic crest**, a glowing **astral amethyst forehead Dvergr gem**, glowing **astral white-violet eyes**, a crimson wool neck scarf, an ash-grey loincloth with a **Norse geometric diamond/rune hem border**, forged-gold hip plates and knee guards, and clenched combat fists with golden knuckle studs.
   - Dynamically retargeted and skinned at runtime onto Valheim's 20-bone `Player` humanoid `Armature` and `Animator` so all idle, summon (`gpower`), punch, barrage, and guard animations play seamlessly.
   - Uses a dedicated 3D studded barrage forearm/fist mesh (`resources/aurvandils_fylgja_barrage_fist.obj` — `321` triangles) for the spectral flurry projectiles during the 12-hit barrage.

2. **`ClassicProcedural`:**
   - Uses Valheim's base humanoid character mesh tinted spectral purple and attaches procedurally generated 3D armor and hair meshes (`StandEquipmentBuilder.cs`): dark forged-gold shoulder pads, waist belt, knee pads, golden Dvergr circlet (`HelmetDverger`), crimson neck scarf, matte light-grey loincloth, black combat boots and gloves, and a swept-up spiky black mane.

---

## Console Commands

Enable the in-game console by adding `-console` to Valheim's Steam Launch Options, press **`F5`** in-game, and run `devcommands` first:

| Command | Description |
| :--- | :--- |
| `fylgja` *(or `stand`)* | Equips **Aurvandil's Fylgja** (`GP_AurvandilsFylgja`) as your active Forsaken Power immediately. |
| `fylgja_model custom` *(or `fylgja model custom`)* | Switches to the **`CustomValheim3D`** 3D model, saves to config, and hot-swaps any active Fylgja live. |
| `fylgja_model classic` *(or `fylgja model classic`)* | Switches to the **`ClassicProcedural`** model, saves to config, and hot-swaps any active Fylgja live. |
| `fylgja_model` *(or `fylgja model`)* | Toggles between **`CustomValheim3D`** and **`ClassicProcedural`** live in-game. |
| `fylgja_opacity <0.2-1.0>` *(or `fylgja opacity <val>`)* | Sets Aurvandil's Fylgja's spectral opacity live (`0.20` = very ghostly, `0.72` = default, `1.00` = fully opaque) and saves to config. |
| `fylgja_stone [pin\|fix]` *(or `fylgja stone [pin\|fix]`)* | Prints the coordinates & live status of Aurvandil's Fylgja's Deep North Sacrificial Stone and the nearest **Lord Reto's Tomb** (`PlaceofMystery3`); pass `pin` to register the Deep North Boss pin or `fix` to re-ground/rescue it into an open clearing. |

---

## Configuration File (`grosales.StandsOfValheim.cfg`)

After launching Valheim once with the mod installed, BepInEx creates `Valheim/BepInEx/config/grosales.StandsOfValheim.cfg`:

```ini
[Combat]

## Base blunt damage for Aurvandil's Fylgja's single punch (Left Mouse Button) before Unarmed skill scaling (0.55 minimum factor), active buffs, and the 1.5x 3rd-hit combo finisher.
# Setting type: Single
# Default value: 120
# Acceptable value range: From 10 to 500
SinglePunchBaseDamage = 120

## Cooldown in seconds between 12-hit Astral Barrage (Middle Mouse Button) activations.
# Setting type: Single
# Default value: 6
# Acceptable value range: From 0.5 to 60
BarrageCooldown = 6

## Damage multiplier applied to the 35-blunt barrage base for every hit of the 12-hit Astral Barrage before Unarmed skill scaling (0.55 minimum factor) and active buffs (default 4.0 = 140 base blunt per hit).
# Setting type: Single
# Default value: 4
# Acceptable value range: From 0.5 to 20
BarrageDamageMultiplier = 4

[Visuals]

## Selects which 3D model to use for Aurvandil's Fylgja: 'CustomValheim3D' (the Valheim-styled custom 3D mesh, default) or 'ClassicProcedural' (the original procedural Valheim humanoid + armor builder).
# Setting type: FylgjaModelStyle
# Default value: CustomValheim3D
# Acceptable values: CustomValheim3D, ClassicProcedural
ModelStyle = CustomValheim3D

## Spectral opacity of Aurvandil's Fylgja (0.20 = very ghostly translucent, 0.72 = default astral spirit transparency, 1.00 = fully opaque).
# Setting type: Single
# Default value: 0.72
# Acceptable value range: From 0.2 to 1
ModelOpacity = 0.72
```

Changing `ModelStyle` or `ModelOpacity` via the config file, a BepInEx Configuration Manager UI, or the `F5` console automatically updates any currently summoned Fylgja.

---

## Installation

1. Ensure **`BepInExPack_Valheim`** is installed in your Valheim directory (or mod manager profile).
2. Download the prebuilt [`Releases/StandsOfValheim.dll`](Releases/StandsOfValheim.dll) and copy it into `Valheim/BepInEx/plugins/`.
3. For detailed step-by-step Windows instructions (including **r2modman / Gale** and manual Steam installation), see **[INSTALL.md](INSTALL.md)**.

---

## Building from Source

Requires the **.NET SDK** (targeting `net48`) and a local Valheim installation with **BepInEx**:

```bash
dotnet build -c Release -p:ValheimDir="/path/to/Valheim"
```

The compiled plugin is output to `bin/Release/net48/StandsOfValheim.dll` and can be copied directly into `Valheim/BepInEx/plugins/`.

---

## Repository Architecture

```text
StandsOfValheim/
├── Releases/
│   └── StandsOfValheim.dll                     # Prebuilt Release plugin DLL (all assets embedded)
├── resources/
│   ├── aurvandils_fylgja.obj                   # Valheim-styled chiseled low-poly 3D character mesh (2,562 tris)
│   ├── aurvandils_fylgja_barrage_fist.obj      # 3D studded barrage forearm/fist mesh (321 tris)
│   ├── aurvandils_fylgja_tex.png               # 1024x512 Valheim pixel-art albedo texture sheet
│   ├── aurvandils_fylgja_emission.png          # 1024x512 emission mask (eyes, forehead gem, runes, energy veins)
│   ├── bossstone_aurvandilsfylgja.png          # Sacrificial stone monolith runic texture
│   ├── icon_aurvandilsfylgja.png               # Forsaken Power HUD & sacrificial stone icon
│   └── ora_barrage.wav                         # 12-hit astral barrage spatial audio clip
├── src/
│   ├── Plugin.cs                               # BepInEx entry point, version metadata & Visuals / Combat configs
│   ├── SE_AurvandilsFylgja.cs                  # SE_Stats Forsaken Power (5m active, 10m cooldown, +100 carry weight)
│   ├── SacrificialStoneManager.cs              # Random Deep North monolith, legacy StartTemple cleanup & Lord Reto's Tomb Vegvisir
│   ├── StandManager.cs                         # Summon/dismiss lifecycle, hands-free check & attack/block dispatch
│   ├── StandFollower.cs                        # Floating levitation AI, suspended leg pose, single punch, 12-hit barrage & guard
│   ├── AurvandilsFylgja.cs                     # Root Fylgja builder, model style dispatcher, aura light, float motes & animator init
│   ├── CustomValheimModelBuilder.cs            # Embedded OBJ parser, 20-bone humanoid retargeter/skinner & spectral PBR materials
│   ├── StandEquipmentBuilder.cs                # Procedural 3D armor/hair meshes & depth-prepass spectral transparency helpers
│   ├── StandMaterialManager.cs                 # Safe fallback material & shader resolution
│   ├── StandBarrageVfx.cs                      # Pooled spectral barrage flurry fists & impact shockwave rings
│   ├── AttackPatches.cs                        # Harmony patches routing unarmed LMB / MMB to Fylgja attacks
│   ├── BlockPatches.cs                         # Harmony patches for 2x block armor & 6x timed parry while hands free
│   ├── AnimEventPatches.cs                     # Suppresses CharacterAnimEvent errors on non-networked visual clones
│   ├── IconManager.cs                          # Embedded & disk-override PNG texture/sprite loader
│   ├── SoundManager.cs                         # Embedded WAV parser & AudioClip builder for barrage audio
│   └── Commands.cs                             # 'fylgja', 'stand', 'fylgja_model', 'fylgja_opacity' & 'fylgja_stone' F5 console commands
├── INSTALL.md                                  # Detailed Windows installation & usage guide
├── README.md                                   # Project overview & technical documentation
├── LICENSE                                     # Apache-2.0 License
└── StandsOfValheim.csproj                      # MSBuild project file (net48)
```

---

## License

Licensed under the [Apache License 2.0](LICENSE).
