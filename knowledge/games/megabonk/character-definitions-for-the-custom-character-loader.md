---
kind: game
title: "Character definitions for Megabonk's custom-character loader"
game: "Megabonk"
game_version: "not launched here; public notes say Steam app 3405340, build 21750826, shown as 1.0.69"
platform: windows
engine: unity-il2cpp
route: loader-api
tools: ["um megabonk", "UmMegabonkStudio (source, not compiled here)", "BepInEx 6.0.0-be.788 on the owner's PC", "Shadowth117 CustomCharacterCreator_Shadowth117Fix 1.4.1"]
anti_cheat: "no anti-cheat named in the public loader notes; runs can upload to leaderboards, so turn Upload score to Leaderboards off; this work does not patch Steam"
status: in-progress
agents: ["Cursor (Grok 4.7)"]
humans: []
date: 2026-11-08
links: ["https://github.com/PeterMoras/MEGABONK_CustomCharacterLoader", "https://github.com/Shadowth117/MEGABONK_CustomCharacterLoader", "https://github.com/PeterMoras/MEGABONK_CustomCharacterMaker", "https://thunderstore.io/c/megabonk/p/Shadowth117/CustomCharacterCreator_Shadowth117Fix/", "https://aaronorelup.com/ledger/modding-megabonk/notes/", "https://github.com/lukeod/megabonk_research"]
tags: ["characters", "bepinex", "il2cpp", "assetbundle"]
---

# Character definitions for Megabonk's custom-character loader

> `um megabonk` writes loader packs and, as of 2026-11-08, runtime clone drafts. The owner's PC ran Megabonk 1.0.69 with BepInEx 6.0.0-be.788 and CustomCharacterLoader 1.4.1 (`Loading Custom Creations`, no Error lines) and has no custom json installed. Unity 6000.6.8f1 is the only editor there, so a 2023.2 bundle cannot be built. The studio plugin clones a vanilla character instead. That plugin was not compiled or launched in this session.

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
8. **Unity 6000 is installed and the bundle build still cannot be done.** **Cause:** Megabonk's player is 2023.2.22f1. An AssetBundle from another editor version does not load, and 2023.2.22f1 is not installed. **Fix:** use the runtime clone plugin. It does not need the editor.
9. **F7 does nothing and the log never says `Studio UI OnGUI ran`.** **Cause:** OnGUI was not called. The `2cdd99a` run did log that OnGUI ran, then died inside a text box (gotcha 10). **Fix:** if the line is absent, the behaviour is not receiving OnGUI. If the line is present and the panel then errors, see gotcha 10.

## Assets
Example icons are 128×128 PNGs drawn by `examples/megabonk-characters/tools/make_icons.py`. No game textures.

## Runtime clone (2026-11-08)
The owner's log confirmed the loader starts on build 21750826 / Unity 2023.2.22f1 / BepInEx 6.0.0-be.788 / .NET 6.0.7. `Loading [CustomCharacterLoader 1.4.0]` is the assembly version inside the 1.4.1 package. Chainloader startup completed, then `Loading Custom Creations`. One Il2CppInterop `Class::Init` fallback warning, no Error entries. Leaderboard uploads were Off. The loader wrote skin-preference data into the save; the backed-up originals were kept.

A shipped character mod clones `CharacterData` with `Instantiate`, stores it at a free `eCharacter`, copies one `SkinData`, and assigns an existing weapon from `DataManager.weapons`. Tint copies the material and multiplies `_Color`. The older field note names `_AlbedoColor` for the toon shader, so the studio sets whichever of those two properties `HasProperty` reports. Hats are copied from `HatData.orientations` for the source character. None of that was executed here.

`examples/megabonk-characters/plugin/build.ps1` compiles `UmMegabonkStudio.dll` against the profile's `BepInEx\core` and `BepInEx\interop` (Il2Cppmscorlib, Assembly-CSharp, UnityEngine.CoreModule, IMGUIModule, InputLegacyModule, BepInEx.Core, BepInEx.Unity.IL2CPP, 0Harmony, Il2CppInterop.Runtime). The first local build with SDK 10.0.403 failed before any of those game type names were reached: CS0012 asked for `Il2Cppmscorlib`, and the same missing reference surfaced as CS0234 on `Il2CppSystem.Collections` and CS0246 on `UnlockableBase` and `StatModifier`. The reference was added. The next build still reported CS0246. A metadata-only read of this install's Assembly-CSharp put them in namespaces: `Assets.Scripts.Saves___Serialization.Progression.Achievements.UnlockableBase` and `Assets.Scripts.Inventory__Items__Pickups.Stats.StatModifier`. The source uses those names. Build 08dd4c2 then failed because CharacterData has no description field. This install's metadata lists localizedDescription on UnlockableBase, and GetDescription() returns string. The two writes were removed. The GetDescription prefix still returns the draft text. It has not been compiled here. The DLL was still not produced in this session. The references stay on that PC. The project target stays `net6.0`. A `dotnet.exe` whose `dotnet --list-sdks` is empty is the runtime host. The supported compiler is the .NET 10 SDK (`winget install Microsoft.DotNet.SDK.10`, LTS until 15 November 2028). The 10.0.100 SDK builds down-level targets through `netcoreapp1.0`, and the first restore pulls `Microsoft.NETCore.App.Ref` for `net6.0`. `CheckEolTargetFramework` is false so NETSDK1138 does not stop that build. The .NET 6 SDK is out of support (13 November 2024) and is not the install path. BepInEx still loads the DLL on its own .NET 6.0.7 host. F7 opens an IMGUI editor over `characters.json`. The oracle that OnGUI actually runs is the log line `Studio UI OnGUI ran`. Until that line exists, the editor is source only. `um megabonk studio` edits the same file and is covered by `tests/test_megabonk.py`. The DLL was not produced in the session that wrote this.

10. **The studio panel throws `Method unstripping failed` and then `pushing more GUIClips than popping`.** **Cause:** `GUILayout.TextField` calls stripped `TextEditor.SaveBackup`, and the exception skipped `EndScrollView`. **Fix:** buttons only, `Begin`/`End` in `finally`, and a stopped OnGUI makes no further GUI calls.
11. **`PlayerRenderer.SetCharacter` throws `KeyNotFoundException` for a studio id.** **Cause:** `DataManager.GetSkins` is a direct `skinData` lookup, and the save config also has `characterSkins`. A clone missing either key hits `Dictionary.get_Item`. **Fix:** add the cloned skin under that id and add skin index 0 when the save map has no entry. Do not catch the exception inside `SetCharacter`.
12. **Build fails CS0012 on `AudioClip`.** **Cause:** `CharacterData.audioFootsteps` is an `AudioClip` array, and that type is in `UnityEngine.AudioModule`, which the project had not referenced. **Fix:** reference `interop/UnityEngine.AudioModule.dll`. CoreModule does not re-export it.
13. **The studio panel is hard to read, or a click cycles one catalog entry.** **Cause:** the `e865c46` panel copied the game skin and stepped one name per click. The owner confirmed that build runs. **Fix:** Studio 0.2.0 draws an opaque rectangle (`Texture2D.whiteTexture` tinted, white labels, white buttons) and opens the full appearance, passive, weapon, and stat lists. Still buttons only. This 0.2.0 panel was not launched in the game.
14. **Build fails CS1729 on `new RectOffset(0, 0, 0, 0)`.** **Cause:** this install's `UnityEngine.RectOffset` exposes a parameterless constructor and `set_left` / `set_right` / `set_top` / `set_bottom`. It has no four-int constructor. **Fix:** `new RectOffset()` and then assign the four properties.
15. **Typing still uses one mouse click per letter.** **Cause:** the 0.2.x panel avoided the stripped text widget by drawing an alphabet of buttons. **Fix:** Studio 0.3.0 reads `Event.current` (`EventType.KeyDown`, `character`, `keyCode`) into its own buffer. It does not call the text widget. While that buffer is focused, prefixes on `UnityEngine.Input.GetKeyDown`, `GetKey`, `GetKeyUp`, `GetButton`, `GetButtonDown`, `GetButtonUp`, `anyKey`, `anyKeyDown`, and `inputString` report no key. A hidden panel does not prefix those calls. Paste uses `GUIUtility.systemCopyBuffer` and shows a status line if that property is missing. This path has not been run in the game. If letters still trigger gameplay while the caret is visible, the game is not using `UnityEngine.Input`. Probe `InputLegacyModule` for `GetKeyDown`, and look for `Unity.InputSystem.dll` only if that probe shows the legacy methods are unused. Do not send the DLL.
16. **F7 logs `Studio editor opened` and then OnGUI stops with `Method unstripping failed`.** **Cause:** Studio 0.3.0's first open draw called IMGUI methods the working 0.2.1 panel did not call. The log stored only the exception type and the short message, so the stripped method name was lost. **Fix:** 0.3.1 keeps labels, buttons, scroll view, `GUI.color`, and `Texture2D.whiteTexture`, and does not call `GUILayoutUtility.GetRect`, `GUI.backgroundColor`, `GUIStyle.padding`, `GUILayout.Width`, `GUILayout.Space`, or `GUIStyle.CalcSize`. A failure now logs `phase=` and each exception's stack and inner exception, then stops OnGUI once. This has not been run. If it still stops, paste the `phase=` line and the stack under it. Probe `UnityEngine.IMGUIModule` for the method named in that stack. Metadata listing a method does not mean its body survived stripping.
17. **A prompt was not answered by a model.** **Cause:** the owner chose the hosted OpenAI chat-completions API. No key was supplied. The game PC has no Ollama or LM Studio. The client sends only when `ai.json` beside `characters.json` has `base_url` and `model`, and only when `MEGABONK_AI_KEY` is set in the game process. The key is not written to `ai.json`, not logged, and not placed on a command line. A long-running Thunderstore Mod Manager keeps the environment from its own start, so the game does not see a variable added later. **Fix:** quit the manager and start it from a session that already has the variable. Do not persist the key with `setx`. The reply must be `{"type":"questions",...}` or `{"type":"draft","character":{...}}` with catalog names only and no `id`. It stays pending until Apply. `um megabonk studio review-model` checks a saved reply without contacting a server.

## Open questions
- Whether Flat modifiers set a stat or add to it, on the current build.
- Whether BepInEx IL2CPP 6.0.788 loads CustomCharacterCreator 1.4.1. The pin that was read is 6.0.738.
- Hats on custom characters. The 1.4.1 Thunderstore text says the hat system does not work for them. Not re-tested.
- A plugin that grants a vanilla `EWeapon` at run start. The json cannot point at one. That needs interop assemblies from the user's boot.
