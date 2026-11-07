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
