# MODLOG — Megabonk character definitions

## 2026-11-07

- Repo is the universal-modder toolkit, not the game. `um scan` on this machine finds no Megabonk install. No `GameAssembly.dll`, no interop assemblies, no Unity editor.
- Knowledge base already has `knowledge/games/megabonk/custom-characters-and-weapons-with-bepinex-il2cpp-in-megabon.md` (Gemini, 2026-10-12): BepInEx 6 IL2CPP, `DataManager.characterData`, character grid from `CharacterMenu`, weapon `baseStats` must contain the standard keys. That note's plugin was verified by its author in 1.0.69. This session did not re-decompile the game and does not repeat that plugin.
- Public loader source (PeterMoras, MIT) and the Shadowth117 fork (Thunderstore 1.4.1, depends on BepInExPack_IL2CPP 6.0.738) show the file the game actually consumes: `BepInEx/plugins/CustomCharacters/*.custom.json` plus a sibling AssetBundle. `LoadCustomCreations` strips `.custom.json` and calls `AssetBundle.LoadFromStream`. There is no try/catch around a missing file.
- Maker `CustomCharacterSO.toJSON` writes `themesongPath`. Loader `JCharacter.FromJSON` reads `themeSongPath`. Packs emit both.
- Maker sample stats include PickupRange 45. Packs do not copy that unless the yaml says so.
- YAML 1.1 reads bare `None` and `Null` as null. The dumper quotes those names. The empty passive is `EPassive.None` (9). The category is `EStatCategory.Null` (6).
- Route: loader-api, authoring side only. A new passive effect or a new projectile class needs a Harmony plugin against `BepInEx/interop`, which is not in this environment, so it was not written.
- Tests: `uv run --with pytest pytest -q tests/test_megabonk.py`. Install/uninstall covered on a fake game tree. UnityFS header checked. No bundle was loaded by Unity or by Megabonk.

## 2026-11-08

- The user's PC confirmed 1.0.69, Unity 2023.2.22f1, BepInEx 6.0.0-be.788, .NET 6.0.7. CustomCharacterLoader 1.4.1 logs `Loading Custom Creations` with no Error lines. No custom json or bundle is installed. Leaderboard uploads are Off. The loader itself wrote skin data into the save; the backup was kept.
- Unity Hub's editor is 6000.6.8f1. That cannot build a 2023.2.22f1 AssetBundle.
- A current character mod (Guz) clones `CharacterData` with `Object.Instantiate`, assigns a free `eCharacter`, copies one skin, and points the weapon at an existing `DataManager.weapons` entry. Tint multiplies `_Color` on a copied material. The field note also names `_AlbedoColor` for the toon shader. The studio plugin does both properties when `HasProperty` is true.
- Plugin source is `plugin/`. It is not compiled in this environment. `build.ps1` compiles it against the profile's interop and does not upload those DLLs. F7 IMGUI is in the source. It is unverified until `Studio UI OnGUI ran` shows up in `LogOutput.log`.
- The game PC has `dotnet.exe` and an empty `dotnet --list-sdks`. That is the runtime. The build script stops there and prints `winget install Microsoft.DotNet.SDK.10`. TargetFramework stays `net6.0`. The .NET 6 SDK is not requested.
- SDK 10.0.403 was installed and the net6.0 restore from nuget.org succeeded. The build then failed with CS0012 for `Il2Cppmscorlib` plus CS0234/CS0246 on the same unresolved base. `interop\Il2Cppmscorlib.dll` is now a reference. The plugin DLL is still not built here.
- `um megabonk studio` edits the same `characters.json`. That path is tested here.
