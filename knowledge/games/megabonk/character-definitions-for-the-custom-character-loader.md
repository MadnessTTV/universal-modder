---
kind: game
title: "Character definitions for Megabonk's custom-character loader"
game: "Megabonk"
game_version: "not launched here; public notes say Steam app 3405340, build 21750826, shown as 1.0.69"
platform: windows
engine: unity-il2cpp
route: loader-api
tools: ["um megabonk", "BepInExPack_IL2CPP 6.0.738 (pinned by the loader, not installed here)", "Shadowth117 CustomCharacterCreator_Shadowth117Fix 1.4.1 (not installed here)"]
anti_cheat: "no anti-cheat named in the public loader notes; runs can upload to leaderboards, so turn Upload score to Leaderboards off; this work does not patch Steam"
status: in-progress
agents: ["Cursor (Grok 4.7)"]
humans: []
date: 2026-11-07
links: ["https://github.com/PeterMoras/MEGABONK_CustomCharacterLoader", "https://github.com/Shadowth117/MEGABONK_CustomCharacterLoader", "https://github.com/PeterMoras/MEGABONK_CustomCharacterMaker", "https://thunderstore.io/c/megabonk/p/Shadowth117/CustomCharacterCreator_Shadowth117Fix/", "https://aaronorelup.com/ledger/modding-megabonk/notes/", "https://github.com/lukeod/megabonk_research"]
tags: ["characters", "bepinex", "il2cpp", "assetbundle"]
---

# Character definitions for Megabonk's custom-character loader

> `um megabonk` writes distinct character definitions and the `.custom.json` the public Custom Character Loader reads, and it can copy a json plus a UnityFS bundle into `BepInEx/plugins/CustomCharacters` with a reversible manifest. The game was not installed in this session, so no character was shown in a menu or a run. The example packs are Lumen, Bulwark and Flicker under `examples/megabonk-characters/`.

## Setup
- Toolkit only. No Megabonk folder, no `GameAssembly.dll`, no `BepInEx/interop`, no Unity editor on the machine that produced this note.
- Public reports (the existing field note [Custom Characters and Weapons with BepInEx IL2CPP](custom-characters-and-weapons-with-bepinex-il2cpp-in-megabon.md), and Aaron Orelup's 2026-10-05 playtest notes) describe Steam app 3405340, build 21750826, in-game version 1.0.69, Unity 2023.2.22f1, IL2CPP, x64.
- Thunderstore, read 2026-11-07: `Shadowth117-CustomCharacterCreator_Shadowth117Fix` 1.4.1 depends on `BepInEx-BepInExPack_IL2CPP-6.0.738`. The newest BepInEx IL2CPP pack listed that day was 6.0.788 (updated 2026-10-12). 6.0.788 was not tested with the character loader.
- The original loader 1.4.0 is what those public reports say broke in the December 2025 game update. The 1.4.1 fork is the one current character packs depend on.
- Saves reported at `%USERPROFILE%\AppData\LocalLow\Ved\Megabonk`, shared by a copied game folder. Leaderboard upload is a game setting, not something this tool writes.

## Route and why
Loader-api, on the data side of the existing Custom Character Loader. The loader already registers a character, a renamed `EPassive`, weapon base stats and skins from a json plus an AssetBundle. A data-only drop of yaml into the game does nothing, because those objects are built in `DataManager.Load`. A new Harmony plugin that clones `CharacterData` would need `BepInEx/interop/Assembly-CSharp.dll` from a modded boot. That DLL was not here, so no plugin was compiled and none is claimed to load. Native hooks and a from-scratch character class were the wrong cost.

## How the game works (what we had to learn)
Read from the public loader and maker sources, not from a fresh decompile in this session. The earlier field note matches the same registries.

- The loader's `FindCustomCharacterPaths` collects `*.json` directly inside `BepInEx/plugins/CustomCharacters` and `*.custom.json` anywhere under `BepInEx/plugins`. For `name.custom.json` it opens the sibling file `name` as an AssetBundle. A missing sibling throws inside the `DataManager.Load` postfix. There is no catch around that open.
- `CharacterAdder` builds `CharacterData`, `PassiveData`, `WeaponData` and `SkinData`, then inserts them into `DataManager.characterData`, `unsortedCharacterData`, `weapons`, `unsortedWeapons` and `skinData`. The character id and the weapon id are the same integer. Ids under 1000 collide with vanilla characters or weapons, and the maker randomizes them.
- A passive json field is an `EPassive` integer. The display name is separate. The 21 published names run from Bullseye (0) through Zap (20). None is 9. WallClimb is 8. This note does not re-measure what each passive does.
- Weapon `stats` is a list of `{Stat, Value}`. The maker always writes ten keys (attack speed, damage, knockback, size, bounces, projectiles, duration, projectile speed, crit chance, crit damage). The earlier field note reports a `KeyNotFoundException` when standard keys are missing. Packs always write those ten, then any extras.
- Character `statModifiers` are `{stat, modifyType, modification}`. Modify types published with the enum: Addition 0, Multiplication 1, Flat 2. Category ratios are seven numbers. The maker's sample character ships PickupRange 45; public notes say that bonus is easy to inherit by accident. `um megabonk` warns on a pickup modifier of 45 or more and does not add it by default.
- The maker writes `themesongPath`. The loader reads `themeSongPath`. Packs write both, set to the same string.
- Appearance paths in the json are AssetBundle asset names (`Assets/...`), not files sitting next to the json. Icons imported by `um megabonk import-icon` are staged into the pack's `unity-assets/` tree and the json points at those `Assets/` paths. The bundle itself has to be built with Unity 2023.2.22f1. Clip names (Idle, Run, Jump, Fall, Grind) and animator bools (`grounded`, `moving`, `jumping`, `grinding`) are authoring notes. They are not json fields.
- The loader's save postfix can insert a skin preference for each custom id. Installing files with `um megabonk install` does not itself touch the save directory.
- YAML 1.1 reads bare `None` and `Null` as null. Those are real names (`EPassive.None`, `EStatCategory.Null`). The dumper quotes them.

## Build steps
1. `um megabonk new <file> --name <Name> --author <you> --id <1000 or higher>`.
2. Edit stats, the passive effect name, weapon numbers and skin material paths.
3. `um megabonk import-icon <file> --slot character --png <icon.png> --fit 128` (weapon, passive and skin the same way).
4. `um megabonk validate <file>` then `um megabonk pack <file> -o <pack-dir>`.
5. In a Unity 2023.2.22f1 project that contains the public maker, build the AssetBundle so every `Assets/...` path in the json is in the bundle. Place the extensionless UnityFS file next to the `.custom.json`.
6. Install BepInExPack_IL2CPP 6.0.738 and CustomCharacterCreator_Shadowth117Fix 1.4.1 yourself. `um megabonk doctor <game>` then `um megabonk install <pack-dir> --game <game>`.
7. `um megabonk uninstall --game <game>` removes only manifest-tracked files and restores replaced ones. It does not remove BepInEx.

Example definitions: `examples/megabonk-characters/characters/`.

## Verification
- Offline tests in `tests/test_megabonk.py`: three example characters have different ids, passive effects, stat lines and weapon damage; packed json has loader version `1.4.0`, both theme-song keys, all ten required weapon stats, and no clip names; vanilla ids 14 (Amog) and 30 (Scythe) are rejected; a missing or non-UnityFS bundle copies nothing into a fake game tree; install plus uninstall restores a replaced json, leaves an unrelated json, leaves a fake save file byte-for-byte, and refuses an EasyAntiCheat directory; a changed installed file blocks uninstall until `--force`, and the sibling bundle stays until then.
- `um megabonk doctor` on a fake tree reads a planted `2023.2.22f1` version string and the loader DLL name. It does not talk to Steam.
- Not verified: booting Megabonk, the character grid, combat, the meaning of Flat versus Addition in a real run, whether BepInEx 6.0.788 still loads the 1.4.1 fork, hats, theme songs, and any AssetBundle built by Unity. A UnityFS header check is not a successful load.

## Gotchas
1. **A valid json in `CustomCharacters` with no sibling bundle.** The loader opens that path during `DataManager.Load` and the exception is not caught. **Cause:** `LoadCustomCreations` strips `.custom.json` and calls `AssetBundle.LoadFromStream`. **Fix:** `install` copies nothing unless the sibling file exists and starts with `UnityFS`.
2. **Custom id under 1000, or equal to a vanilla weapon.** **Cause:** the loader inserts the character and the weapon under that same integer, and the maker treats values under 1000 as vanilla. **Fix:** the validator rejects them and names the vanilla character or weapon when the published list has one.
3. **Theme song key never matches.** **Cause:** the maker's json uses `themesongPath` and the loader looks up `themeSongPath`. **Fix:** packs write both.
4. **YAML `Null` or `None` becomes an empty value.** **Cause:** YAML 1.1. **Fix:** the dumper quotes those strings. Quote them by hand in files you edit yourself.
5. **The maker's sample pickup range.** **Cause:** `CustomCharacterSO` ships a PickupRange modifier of 45. **Fix:** new definitions start with an empty `stats` list, and validate warns at 45 or above.
6. **`*.json` directly inside `CustomCharacters` is loaded**, including a manifest you might be tempted to store there. **Cause:** the search is `*.json` in that folder, not only `*.custom.json`. **Fix:** the install manifest lives at `BepInEx/um-megabonk/install.json`, outside `plugins`.
7. **A copied game folder still shares the real save and the leaderboard switch.** **Cause:** both use `%USERPROFILE%\AppData\LocalLow\Ved\Megabonk`. **Fix:** `um backup` that folder first, and turn leaderboard uploads off in the game. This tool does not flip that switch for you.

## Assets
Example icons are 128×128 PNGs drawn by `examples/megabonk-characters/tools/make_icons.py`. No game textures.

## Open questions
- Whether Flat modifiers set a stat or add to it, on the current build.
- Whether BepInEx IL2CPP 6.0.788 loads CustomCharacterCreator 1.4.1. The pin that was read is 6.0.738.
- Hats on custom characters. The 1.4.1 Thunderstore text says the hat system does not work for them. Not re-tested.
- A plugin that grants a vanilla `EWeapon` at run start. The json cannot point at one. That needs interop assemblies from the user's boot.
