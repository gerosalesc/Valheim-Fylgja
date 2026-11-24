# Installation & Usage Guide (Windows)

This guide explains how to install, configure, and use **Valheim Fylgja — Aurvandil's Fylgja** (`StandsOfValheim.dll`) for Valheim on Windows.

> **Single-File Plugin:** All 3D models, textures, icons, sacrificial stone textures, and barrage audio are embedded directly inside `StandsOfValheim.dll`. You only need that single `.dll` file to play.

---

## Step 1: Locate Your Valheim Folder

1. Open **Steam** and go to your **Library**.
2. Right-click **Valheim** → **Manage** → **Browse local files**.
3. Keep the opened folder ready (usually `C:\Program Files (x86)\Steam\steamapps\common\Valheim`).

---

## Step 2: Install BepInEx (If Not Already Installed)

Valheim requires **BepInEx** to load mods. Choose one of the two options below:

### Option A: Using a Mod Manager (Easiest — r2modman or Gale)

1. Download and install **r2modman** or **Gale Mod Manager** from Thunderstore.
2. Select **Valheim** and create or select a profile.
3. Search for and install **`BepInExPack_Valheim`** (by *denikson*).
4. Open your profile's folder via **Settings** → **Browse profile folder**, then navigate to `BepInEx\plugins\` and continue to **Step 3**.
   - *When using a mod manager, always launch the game using the manager's **Start modded** button.*

### Option B: Manual Installation with Steam

1. Download **`BepInExPack_Valheim`** from Thunderstore:  
   https://thunderstore.io/c/valheim/p/denikson/BepInExPack_Valheim/ (click **Manual Download**).
2. Extract the downloaded `.zip` archive.
3. Open the extracted `BepInExPack_Valheim` folder and copy **all files and folders inside it** (`BepInEx`, `doorstop_config.ini`, `winhttp.dll`, etc.) directly into your **Valheim** root directory from Step 1.
4. Launch Valheim once to the main menu, then close it so BepInEx initializes `BepInEx\plugins\` and `BepInEx\config\`.

---

## Step 3: Install `StandsOfValheim.dll`

1. Download **`StandsOfValheim.dll`** directly from [`Releases/StandsOfValheim.dll`](Releases/StandsOfValheim.dll) in this repository (click the file → **Download raw file**), or compile it from source—see [Building from Source](#building-from-source-optional) below.
2. Copy `StandsOfValheim.dll` into your plugins folder:
   - **Manual Steam install:** `C:\Program Files (x86)\Steam\steamapps\common\Valheim\BepInEx\plugins\StandsOfValheim.dll`
   - **Mod manager install:** `<Your Profile Folder>\BepInEx\plugins\StandsOfValheim.dll`

> **Multiplayer / Co-op Note:**  
> - All players joining the same world should have `StandsOfValheim.dll` installed in `BepInEx\plugins\`.  
> - If the host server also uses other server-synced mods (such as `AzuExtendedPlayerInventory`), all connecting clients must have those same server-required mods installed at matching versions as well, or Valheim will display an `"Incompatible version"` prompt on connect.

---

## Step 4: Unlocking & Using Aurvandil's Fylgja

### Unlocking in Survival
1. Journey to the **Ashlands** and locate **Lord Reto's Tomb** (the third Mysterious Location, `PlaceofMystery3`, discovered by following the Dyrnwyn Vegvisir chain).
2. Enter Lord Reto's cavern and fight through to the deepest back sanctuary (past Lord Reto) to find the glowing 3D carved granite **Astral Vegvisir** (**`Vegvisir: Aurvandil's Fylgja`**).
3. Interact (**`E`**) with the Vegvisir to pan your world map north and permanently pin the exact random **Deep North** location of **Aurvandil's Fylgja's Sacrificial Stone** *(the monolith is no longer placed at the Start Temple; any legacy Start Temple copy on existing worlds is automatically cleaned up)*.
4. Sail to the pinned location in the **Deep North**, hang a **Fallen Valkyrie Trophy** (`TrophyFallenValkyrie`) on the monolith's hook, and interact with the glowing stone to claim **Aurvandil's Fylgja** as your active Forsaken Power.

### Quick Testing via Console Command
1. Ensure the Valheim console is enabled in Steam (*Right-click Valheim in Steam → Properties → General → Launch Options → `-console`*).
2. In-game, press **`F5`** to open the console and enter:
   ```text
   devcommands
   fylgja
   ```
   *(You can also type `stand` as an alias.)* This equips **Aurvandil's Fylgja** as your active Forsaken Power immediately.
3. To inspect or pin the Deep North Sacrificial Stone and locate the nearest Lord Reto's Tomb in your world, run:
   ```text
   fylgja_stone pin
   ```

---

## Step 5: Controls & Combat Mechanics

1. **Summon (`F` Key):** Press **`F`** (your Forsaken Power key) to summon **Aurvandil's Fylgja**:
   - **Active Duration:** `5 minutes` (`300s`)
   - **Cooldown:** `10 minutes` (`600s`)
   - **Passive Bonus (While Active):** **`+100` Max Carry Weight** (no other passive stat bonuses)
2. **Hands-Free Requirement:**  
   Keep your **hands free** (no weapon, shield, or tool equipped in either hand; press **`R`** to sheathe). While holding any weapon, shield, or tool, your normal player attacks and blocks work as usual.
3. **Unarmed Fylgja Actions (With Hands Free):**
   - **Left Mouse Button (Single Punch):** Fast single Fylgja punch (`5` stamina, `120` base blunt damage configurable via `SinglePunchBaseDamage`, dynamically scaled by `Unarmed` skill with a `0.55` minimum factor and active buffs). Uses a 3-step combo chain: hits 1–2 deal `1.0x` damage / `1.0x` stagger / `18` knockback; the **3rd-hit combo finisher** deals **`1.5x` damage, `1.5x` stagger, `30` knockback**, and triggers an impact shockwave ring.
   - **Middle Mouse Button (12-Hit Astral Barrage):** Rapid **12-hit punch barrage** (`16` stamina, **`6.0s` cooldown**). Each hit dynamically deals **`4x`** the `35`-blunt barrage base (`140` base blunt per hit, scaled by your `Unarmed` skill with a `0.55` minimum factor and active buffs; configurable via `BarrageDamageMultiplier`), plays both the looping `ora_barrage.wav` audio and Valheim's normal punch swing and impact sounds on **every hit**, trains your `Unarmed` skill on hits, applies **`0.75x` stagger per hit** with a **`1.5x` stagger finisher** on hit 12, and keeps knockback at **half of the Demolisher** (`SledgeDemolisher`).
   - **Hold Right Mouse Button (Fylgja Shield Guard & 6x Parry):** Moves Aurvandil's Fylgja directly in front of you in a guard stance.
     - **Standard Guard:** Blocks incoming hits with **`2x`** your player body armor (scaled up to `+50%` with your **Blocking** skill).
     - **Timed Parry (`< 0.25s` before impact):** Parries incoming attacks with **`6x`** your player body armor (scaled with your **Blocking** skill), staggering the attacker and knocking them back.
4. **Friendly Fire (PvP) Protection:**  
   Aurvandil's Fylgja will **never** hit, stagger, or knock back other players unless **both** you and the target player have **PvP (Friendly Fire — crossed swords in inventory)** active. Friendly tamed animals are also protected while your PvP is off.

---

## Step 6: Customizing 3D Model Style & Spectral Transparency

By default, **Valheim Fylgja** uses the edited Valheim-styled custom 3D model (**`CustomValheim3D`**) with spectral transparency (**`ModelOpacity = 0.72`**) and a floating levitation effect. You can switch between 3D models or adjust opacity at any time:

### Option 1: Live In-Game via `F5` Console (with `devcommands`)
Open the **`F5`** console and run:
```text
fylgja_model custom
```
to use the Valheim-styled custom 3D model (**`CustomValheim3D`**), or:
```text
fylgja_model classic
```
to use the classic procedural humanoid + armor builder (**`ClassicProcedural`**). Running `fylgja_model` (or `fylgja model`) with no argument toggles between both styles, saves the setting to your config file, and hot-swaps your currently summoned Fylgja on the spot.

To adjust spectral transparency live (`0.20` = very ghostly, `0.72` = default, `1.00` = fully opaque), run:
```text
fylgja_opacity 0.65
```

### Option 2: Via Configuration File
Open `Valheim\BepInEx\config\grosales.StandsOfValheim.cfg` in any text editor (created automatically after launching the game once with the mod installed):

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

---

## Building from Source (Optional)

If you want to compile `StandsOfValheim.dll` yourself on Windows (requires **.NET SDK** and **Valheim + BepInEx** installed):

```powershell
dotnet build -c Release -p:ValheimDir="C:\Program Files (x86)\Steam\steamapps\common\Valheim"
```

Then copy `bin\Release\net48\StandsOfValheim.dll` into `Valheim\BepInEx\plugins\`.
