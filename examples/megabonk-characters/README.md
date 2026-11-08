# Megabonk character packs

## Runtime studio (no Unity 2023 editor)

The installed editor is Unity 6000.6.8f1. AssetBundles for this game have to be built with 2023.2.22f1, which is not installed. The studio does not build a bundle. It clones a vanilla character at runtime, which is how a current Megabonk character mod adds someone selectable, and then changes the name, an existing passive, an existing weapon, extra stats, and a tint.

The plugin source is `plugin/`. It was not compiled in this session. The project stays `net6.0` so BepInEx's .NET 6.0.7 host can load the DLL. Compiling that target needs an SDK. A `dotnet.exe` with an empty `dotnet --list-sdks` is the runtime only.

Install the current long-term SDK, .NET 10, which Microsoft documents as able to build older targets including `net6.0`. Support runs until 15 November 2028. The .NET 6 SDK left support on 13 November 2024. Do not install it. .NET 8 and .NET 9 leave support on 11 November 2026.

```powershell
winget install Microsoft.DotNet.SDK.10
```

That command is the one on [Install .NET on Windows](https://learn.microsoft.com/en-us/dotnet/core/install/windows). Install it with administrative privileges. Open a new terminal afterward. The SDK adds itself beside the existing `dotnet.exe`. The first build restores `Microsoft.NETCore.App.Ref` for `net6.0` from nuget.org. That package is the compile-time reference surface. It is not copied into the plugin, and the system .NET 10 runtime does not replace BepInEx's host.

```powershell
powershell -File examples\megabonk-characters\plugin\build.ps1 -Install
```

That reads `%APPDATA%\Thunderstore Mod Manager\DataFolder\Megabonk\profiles\Default\BepInEx`, checks the interop DLLs, and builds `UmMegabonkStudio.dll`. `-Install` copies it to `BepInEx\plugins\UmMegabonkStudio\` and, only if you do not already have one, copies `studio/characters.json` (Lumen cloned from Fox, Bulwark from Megachad, Flicker from Ninja).

Restart with the game already closed. In `BepInEx\LogOutput.log` look for `Megabonk Studio 0.3.3`, `Registered 'Lumen'`, `skinData contains`, and `prefab` not `NULL`. After F7, the next line should be `Studio panel draw finished.` The installed `0.3.0` build stopped on the first open draw with `Method unstripping failed` and did not record a stack. `0.3.1` drops the layout calls that build did not use. `0.3.2` turns a recognized provider error into a short status. `0.3.3` paints a tint only when `paint` is true, as one color on copied materials. `studio/superman.character.json` is a separate character, not a replacement for `characters.json`. Append it with `um megabonk studio append` only when id 1842201004 is free. A 429 is a billing limit only when the response code is one of the published quota codes. The log does not include the response body. The editor stays closed until **F7**. The first screen has a **Create a character** button. That path types with the physical keyboard and applies a new character. **Edit** on a row is a different path and shows what will change before it saves. The panel does not use the game's text widget. While a box is focused, legacy `UnityEngine.Input` key queries are reported as up so typing does not also fire gameplay actions. Closing the panel, or pressing Esc in a box, gives those keys back. This 0.3.0 panel has not been watched in the game. The owner confirmed the previous installed build (commit `e865c46`) runs, and reported that Studio 0.2.1 was usable.

The `2cdd99a` build did run. Its text boxes threw `Method unstripping failed` in `TextEditor.SaveBackup` and then spilled GUI clip errors. `PlayerRenderer.SetCharacter` threw `KeyNotFoundException` for character ids 1842201002 and 1842201003. Later source removed the text boxes, registers `skinData` plus the save's `characterSkins` index, and references `UnityEngine.AudioModule`. Do not put a text box back.

A tint button stores `#RRGGBB`. The visible mesh keeps the cloned character's materials. A new 3D model and a new ability effect are not available. A passive is one of the existing 21. A weapon is one of the existing weapons.

The model section does not send a prompt until `ai.json` sits beside `characters.json` and both `base_url` and `model` are set. There is no default address and no built-in key. For the hosted OpenAI API the file is:

```json
{
  "base_url": "https://api.openai.com/v1",
  "model": "gpt-5.4-mini",
  "api_key_env": "MEGABONK_AI_KEY"
}
```

`api_key_env` may be omitted; it defaults to `MEGABONK_AI_KEY`. The key itself does not go in this file. Official model pages list `gpt-5.4` and `gpt-5.4-mini` as supporting `v1/chat/completions`. `gpt-5.4-mini` is the smaller of those two. Each Send is a billed call on the account that owns the key. This session did not create a key, call the API, or spend anything. The installed `0.2.0` build still posts when the variable is empty. `0.2.1` refuses that post. `um megabonk studio review-model reply.txt` checks a saved reply offline. Apply is a separate button. Undo restores the previous character list. The model cannot choose an id.

The game reads `MEGABONK_AI_KEY` from its own process environment. Thunderstore Mod Manager keeps the environment it had when it started, and the game inherits that copy. Quit the manager, then start it from a session that already has the variable. Do not use `setx` or System Properties for the key: those write it in plaintext under the user environment registry and still do not update a manager that is already running. Do not put the key on a command line.

If OnGUI never logs, edit the json and restart:

```bash
um megabonk studio validate studio/characters.json
um megabonk studio set studio/characters.json --id 1842201001 --tint #FFD24A --stat Armor:Addition:3
```

A different appearance applies on the next launch. Passive, weapon, tint and extra stats are written onto the live clone for the next run. The passive is one of the game's 21 effects. This does not invent a new one. Leaderboard uploads stay Off; the plugin does not touch that switch or the save folder.

The first local build failed with CS0012 because `interop\Il2Cppmscorlib.dll` was not a reference. `Assembly-CSharp` and the Unity modules extend `Il2CppSystem.Object` in that DLL, so the compiler also reported `Il2CppSystem.Collections`, `UnlockableBase`, and `StatModifier` as missing. Those two game type names are unchanged. They are the names already used by the public loader and the Guz mod. The csproj now references `Il2Cppmscorlib.dll`. This session still has not produced the DLL.

If the next build still names a missing type, print signatures only. This does not load the DLL as code:

```powershell
dotnet run --project examples\megabonk-characters\plugin\tools\ListSignature -- "%APPDATA%\Thunderstore Mod Manager\DataFolder\Megabonk\profiles\Default\BepInEx\interop\Assembly-CSharp.dll" UnlockableBase StatModifier
```

Paste that text. Do not send the DLL.

If the build fails, paste the first compiler error (the missing type or method). Do not send any file from `interop\` or `core\`.

## Loader packs (need Unity 2023.2.22f1)

Three distinct character definitions — **Lumen**, **Bulwark** and **Flicker** — plus `um megabonk`, which checks them and writes the `.custom.json` the community Custom Character Loader reads.

This folder does not contain a character the game can spawn. Megabonk is Unity 2023.2 IL2CPP. Characters are ScriptableObjects filled from an AssetBundle. The json names the prefab, icons, materials and weapon attack inside that bundle. Without the bundle, the loader opens a missing file during `DataManager.Load`. `um megabonk install` refuses to copy a json into the game unless a sibling file with a UnityFS header is present, and a header check is not a load test.

Nothing here was launched in Megabonk. This environment had no game install. `uv run --with pytest pytest -q tests/test_megabonk.py` is the check that did run.

## Credits

Character definitions, the packer and these icons were made in this repo (Cursor, Grok 4.7). The icons are original 128×128 PNGs from `tools/make_icons.py`. They are not ripped from the game.

The loader and the Unity maker are other people's projects:

- [PeterMoras/MEGABONK_CustomCharacterLoader](https://github.com/PeterMoras/MEGABONK_CustomCharacterLoader) (MIT), and the post-December-2025 fix [Shadowth117/MEGABONK_CustomCharacterLoader](https://github.com/Shadowth117/MEGABONK_CustomCharacterLoader) v1.4.1
- [PeterMoras/MEGABONK_CustomCharacterMaker](https://github.com/PeterMoras/MEGABONK_CustomCharacterMaker)
- BepInEx IL2CPP pack, Thunderstore dependency pin **6.0.738**

Enum numbers are the ones those public catalogs published for the 1.0.69-era tools. A newer game build can renumber them.

## What each character is

| | Lumen | Bulwark | Flicker |
|---|---|---|---|
| id | 1842201001 | 1842201002 | 1842201003 |
| passive label | Cinder Drift | Plate Oath | Lucky Spark |
| passive effect | Float (10) | Reinforced (3) | CritHappens (5) |
| weapon | Ember Toss, 3 projectiles, damage 6 | Ram, damage 22, slow | Needle, damage 9, crit on the weapon, plus lifesteal |
| stat modifiers | pickup +6, move speed +0.04, max health −10 | armor +8, max health +25, move speed −0.08 | crit chance +0.15, max health −20, evasion +0.06 |

`Addition` is modify type 0, "adds to the additive component" in the published enum note. These numbers were not playtested. A passive line selects one of the game's 21 `EPassive` values and shows your name and description. It does not invent a new effect.

Ids start at 1000, which is the maker's rule, and they stay clear of vanilla characters (Fox is 0, Amog is 14) and vanilla weapons (the loader registers the custom weapon under the same number; Scythe is 30).

## Customize

From the repo root:

```bash
um megabonk validate examples/megabonk-characters/characters/lumen.yaml
um megabonk catalog
um megabonk new my-hero.yaml --name MyHero --author you --id 1842202001
um megabonk import-icon my-hero.yaml --slot character --png art/hero.png --fit 128
um megabonk pack examples/megabonk-characters/characters/lumen.yaml -o packs/lumen
```

Edit the yaml. `stats` is a list of `{stat, modify, amount}`. `modify` is `Addition`, `Multiplication` or `Flat`. `passive.effect` is a name from `um megabonk catalog`. Leave `category_ratios` at the maker's published Fox sample unless you mean to change it. An empty `stats` list is valid. Do not copy the maker's sample +45 pickup range unless you want that modifier.

`import-icon` copies a PNG next to the yaml. `pack` copies local files into `unity-assets/` and rewrites those paths to `Assets/<bundle>/staged/...`. Paths that already start with `Assets/` are left for the Unity project. The five clip names and the animator bools stay in the yaml. They are not fields in the loader json. They have to be on the prefab.

## Install the loader yourself

`um megabonk` does not download BepInEx and does not copy it into the game.

1. Copy the game folder. A copy still shares saves, Steam Cloud and the leaderboard switch with the real install.
2. Back up saves before the first modded launch:

   ```bash
   um backup create "%USERPROFILE%\AppData\LocalLow\Ved\Megabonk" --name megabonk-saves
   ```

3. Install **BepInExPack_IL2CPP 6.0.738** into that copy (the character loader's Thunderstore dependency pin). Thunderstore also has 6.0.788 (updated 2026-10-12). That newer pack was not tested here with the character loader.
4. Install **Shadowth117 CustomCharacterCreator_Shadowth117Fix 1.4.1**. Public reports say the original 1.4.0 loader broke in the December 2025 update.
5. In game, set **Upload score to Leaderboards** to Off before any modded run. This tool does not change that switch and does not patch Steam.
6. Build the AssetBundle in **Unity 2023.2.22f1** with the public maker. Put your model, the attack prefab (one of the game's existing projectile scripts) and the staged icons on the paths the json names. Build Custom Assets. Copy the extensionless bundle next to the `.custom.json`. Leave the `.manifest` behind.
7. Check the folder, then install:

   ```bash
   um megabonk doctor "C:\path\to\Megabonk"
   um megabonk install packs/lumen --game "C:\path\to\Megabonk"
   ```

   Files go to `BepInEx/plugins/CustomCharacters/`. The manifest is `BepInEx/um-megabonk/install.json`. The installer does not read or write `%USERPROFILE%\AppData\LocalLow\Ved\Megabonk`.

8. Launch and look for `Loaded Custom Character:` in `BepInEx/LogOutput.log`. That log line is the loader's own message. It was not produced here.

## Uninstall

```bash
um megabonk uninstall --game "C:\path\to\Megabonk"
um megabonk uninstall --game "C:\path\to\Megabonk" --id 1842201001
```

This deletes only files the manifest recorded, and it restores any file it had replaced. If a file changed after install, it is left in place until you pass `--force`. BepInEx stays. Steam's "verify integrity" does not remove BepInEx either.

If the loader ran, it may have added a skin-preference entry to the save. Restoring the save snapshot is a separate `um backup restore`, and only after you mean to roll saves back.

## Exact files a local install needs

```bash
um megabonk prerequisites
```

Minimum the doctor looks for:

- `GameAssembly.dll`
- `*_Data/il2cpp_data/Metadata/global-metadata.dat`
- `BepInEx/plugins/` from your BepInEx 6.0.738 install
- a DLL under `BepInEx/plugins/` whose name contains `CustomCharacter`
- for each character, `NAME.custom.json` and a sibling `NAME` whose first bytes are `UnityFS`

No anti-cheat directory was part of the public notes used here. If `doctor` sees EasyAntiCheat, BattlEye or GameGuard in the folder, install stops.
