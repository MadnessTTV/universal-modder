"""Offline tests for `um megabonk`. No game, no Unity, no AssetBundle that the game could load."""
from __future__ import annotations

import json
from pathlib import Path

import pytest
from PIL import Image

from um.megabonk_studio import (
    add_character,
    apply_model_draft,
    interpret_model_message,
    load_studio,
    push_snapshot,
    save_studio,
    set_character,
    starter_file,
    undo_snapshot,
    validate_studio,
)
from um.megabonk import (
    CATEGORIES,
    CHARACTERS,
    MIN_BUNDLE_BYTES,
    MODIFY,
    PASSIVES,
    REQUIRED_WEAPON_STATS,
    STATS,
    WEAPONS,
    MegabonkError,
    catalog_text,
    doctor,
    import_icon,
    install_pack,
    new_character,
    pack_character,
    prerequisites_text,
    uninstall,
    validate_definition,
    validate_loader_json,
    load_yaml,
)

ROOT = Path(__file__).resolve().parents[1]
EXAMPLES = ROOT / "examples" / "megabonk-characters" / "characters"


def _unityfs(path: Path) -> None:
    path.write_bytes(b"UnityFS" + b"\0" * MIN_BUNDLE_BYTES)


def _game(tmp_path: Path, loader: bool = True) -> Path:
    game = tmp_path / "Megabonk"
    data = game / "Megabonk_Data"
    (data / "il2cpp_data" / "Metadata").mkdir(parents=True)
    (data / "il2cpp_data" / "Metadata" / "global-metadata.dat").write_bytes(b"metadata")
    (data / "globalgamemanagers").write_bytes(b"\0" * 32 + b"2023.2.22f1\0")
    (data / "app.info").write_text("Ved\nMegabonk\n", encoding="utf-8")
    (game / "Megabonk.exe").write_bytes(b"MZ")
    (game / "GameAssembly.dll").write_bytes(b"MZ")
    (game / "BepInEx" / "plugins").mkdir(parents=True)
    (game / "BepInEx" / "core").mkdir()
    if loader:
        plug = game / "BepInEx" / "plugins" / "CustomCharacterLoader"
        plug.mkdir()
        (plug / "CustomCharacterLoader.dll").write_bytes(b"MZ")
    save = tmp_path / "LocalLow" / "Ved" / "Megabonk"
    save.mkdir(parents=True)
    (save / "progression.json").write_text("KEEP", encoding="utf-8")
    return game


def _pack(tmp_path: Path, yaml_name: str) -> Path:
    out = tmp_path / "packs" / yaml_name
    pack_character(EXAMPLES / f"{yaml_name}.yaml", out)
    doc = json.loads((out / f"{yaml_name}.custom.json").read_text(encoding="utf-8"))
    _unityfs(out / doc["character"]["assetBundleName"])
    return out


def test_catalog_matches_the_published_counts():
    assert len(STATS) == 57
    assert STATS["ExtraJumps"] == 46
    assert STATS["ProjectileBounces"] == 45
    assert list(STATS)[0] == "MaxHealth"
    assert len(PASSIVES) == 21
    assert PASSIVES["WallClimb"] == 8
    assert PASSIVES["None"] == 9
    assert PASSIVES["Float"] == 10
    assert len(CATEGORIES) == 7
    assert CHARACTERS[14] == "Amog"
    assert WEAPONS[30] == "Scythe"
    text = catalog_text()
    assert "ExtraJumps" in text and "CritHappens" in text
    pre = prerequisites_text()
    assert "6.0.738" in pre and "1.4.1" in pre and "UnityFS" in pre
    assert "not re-run" in pre.lower() or "Not re-run" in pre


def test_three_example_characters_are_distinct_and_pack():
    ids, effects, stat_lines, damages = set(), set(), set(), set()
    icons = []
    for path in sorted(EXAMPLES.glob("*.yaml")):
        data = load_yaml(path)
        errors, _warnings = validate_definition(data, path.parent)
        assert errors == []
        ids.add(data["id"])
        effects.add(data["passive"]["effect"])
        stat_lines.add(tuple((row["stat"], row["amount"]) for row in data["stats"]))
        damages.add(data["weapon"]["damage"])
        assert data["id"] >= 1000
        assert not any(row["stat"] == "PickupRange" and row["amount"] >= 45 for row in data["stats"])
        icons.append(path.parent / data["appearance"]["icon"])
    assert len(ids) == len(effects) == len(stat_lines) == len(damages) == 3
    blobs = [icon.read_bytes() for icon in icons]
    assert len(set(blobs)) == 3
    for icon in icons:
        with Image.open(icon) as im:
            assert im.format == "PNG" and im.size == (128, 128)


def test_pack_json_has_the_loader_shape(tmp_path):
    out = tmp_path / "lumen"
    result = pack_character(EXAMPLES / "lumen.yaml", out)
    doc = json.loads(result["json"].read_text(encoding="utf-8"))
    errors, _warnings = validate_loader_json(doc)
    assert errors == []
    assert doc["version"] == "1.4.0"
    assert doc["character"]["eCharacter"] == 1842201001
    assert doc["character"]["themeSongPath"] == doc["character"]["themesongPath"] == ""
    assert doc["character"]["statModifiers"][0] == {"stat": STATS["PickupRange"], "modifyType": 0, "modification": 6}
    assert doc["passive"]["passive"] == PASSIVES["Float"]
    assert doc["passive"]["passiveName"] == "Cinder Drift"
    stats = {row["Stat"]: row["Value"] for row in doc["weapon"]["stats"]}
    for name in REQUIRED_WEAPON_STATS:
        assert STATS[name] in stats
    assert stats[STATS["DamageMultiplier"]] == 6
    assert stats[STATS["Projectiles"]] == 3
    assert "clips" not in json.dumps(doc)
    assert "grounded" not in json.dumps(doc)
    assert not (out / "lumen").exists()
    assert "Unity 2023.2.22f1" in (out / "BUNDLE.txt").read_text(encoding="utf-8")
    flicker = json.loads((pack_character(EXAMPLES / "flicker.yaml", tmp_path / "flicker")["json"]).read_text(encoding="utf-8"))
    flicker_stats = {row["Stat"] for row in flicker["weapon"]["stats"]}
    assert STATS["Lifesteal"] in flicker_stats
    assert flicker["passive"]["passive"] == PASSIVES["CritHappens"]


def test_rejects_vanilla_ids_and_duplicate_weapon_stats(tmp_path):
    path = tmp_path / "hero.yaml"
    new_character(path, "Hero", "local", ident=1842201999)
    data = load_yaml(path)
    errors, _warnings = validate_definition(data, path.parent)
    assert errors == []
    data["id"] = 14
    errors, _ = validate_definition(data, path.parent)
    assert any("Amog" in e for e in errors)
    data["id"] = 30
    errors, _ = validate_definition(data, path.parent)
    assert any("Scythe" in e for e in errors)
    data["id"] = 1842201999
    data["weapon"]["extra_stats"] = [{"stat": "DamageMultiplier", "amount": 1}]
    errors, _ = validate_definition(data, path.parent)
    assert any("twice" in e for e in errors)
    data["weapon"]["extra_stats"] = []
    data["stats"] = [{"stat": "PickupRange", "modify": "Flat", "amount": 45}]
    _errors, warnings = validate_definition(data, path.parent)
    assert any("45" in w for w in warnings)
    data["passive"]["effect"] = None
    errors, _ = validate_definition(data, path.parent)
    assert any("quote" in e.lower() for e in errors)


def test_import_icon_copies_a_png_and_rejects_other_files(tmp_path):
    path = tmp_path / "hero.yaml"
    new_character(path, "Hero", "local", ident=1842201888)
    src = tmp_path / "face.png"
    Image.new("RGBA", (32, 16), (10, 20, 30, 255)).save(src)
    note = import_icon(path, src, "character", fit=128)
    assert "128x128" not in note
    data = load_yaml(path)
    stored = tmp_path / data["appearance"]["icon"]
    with Image.open(stored) as im:
        assert im.size == (128, 128) and im.format == "PNG"
    other = tmp_path / "face.jpg"
    Image.new("RGB", (8, 8), (1, 2, 3)).save(other, "JPEG")
    with pytest.raises(MegabonkError):
        import_icon(path, other, "weapon")


def test_install_refuses_a_missing_or_fake_bundle_and_leaves_the_game(tmp_path):
    game = _game(tmp_path)
    save = tmp_path / "LocalLow" / "Ved" / "Megabonk" / "progression.json"
    out = tmp_path / "bare"
    pack_character(EXAMPLES / "lumen.yaml", out)
    before = list((game / "BepInEx" / "plugins").rglob("*"))
    with pytest.raises(MegabonkError, match="missing AssetBundle"):
        install_pack(out, game)
    assert list((game / "BepInEx" / "plugins").rglob("*")) == before
    assert save.read_text(encoding="utf-8") == "KEEP"
    (out / "lumen").write_text("not a bundle", encoding="utf-8")
    with pytest.raises(MegabonkError, match="UnityFS"):
        install_pack(out, game)
    assert not (game / "BepInEx" / "plugins" / "CustomCharacters").exists()
    assert save.read_text(encoding="utf-8") == "KEEP"


def test_install_and_uninstall_are_limited_to_the_manifest(tmp_path):
    game = _game(tmp_path)
    save = tmp_path / "LocalLow" / "Ved" / "Megabonk" / "progression.json"
    chars = game / "BepInEx" / "plugins" / "CustomCharacters"
    chars.mkdir()
    existing = chars / "lumen.custom.json"
    existing.write_text("original-json", encoding="utf-8")
    (chars / "someone-else.custom.json").write_text('{"character":{"eCharacter":1842201777}}', encoding="utf-8")
    lumen = _pack(tmp_path, "lumen")
    bulwark = _pack(tmp_path, "bulwark")
    installed = install_pack(lumen, game)
    assert installed["in_game_verified"] is False
    assert existing.read_text(encoding="utf-8") != "original-json"
    install_pack(bulwark, game)
    foreign = chars / "foreign.custom.json"
    foreign.write_text(json.dumps({"character": {"eCharacter": 1842201003}}), encoding="utf-8")
    flicker = _pack(tmp_path, "flicker")
    with pytest.raises(MegabonkError, match="already used"):
        install_pack(flicker, game)
    assert foreign.read_text(encoding="utf-8").startswith("{")
    uninstall(game, ident=1842201001)
    assert existing.read_text(encoding="utf-8") == "original-json"
    assert (chars / "bulwark.custom.json").is_file()
    assert (chars / "someone-else.custom.json").is_file()
    assert not (chars / "lumen").exists()
    uninstall(game)
    assert not (chars / "bulwark.custom.json").exists()
    assert not (chars / "bulwark").exists()
    assert (chars / "someone-else.custom.json").is_file()
    assert (game / "BepInEx" / "plugins" / "CustomCharacterLoader" / "CustomCharacterLoader.dll").is_file()
    assert save.read_text(encoding="utf-8") == "KEEP"
    assert not (game / "BepInEx" / "um-megabonk" / "install.json").exists()


def test_reinstall_of_our_character_is_allowed(tmp_path):
    game = _game(tmp_path)
    lumen = _pack(tmp_path, "lumen")
    install_pack(lumen, game)
    again = install_pack(lumen, game)
    assert again["id"] == 1842201001


def test_uninstall_leaves_a_file_that_changed(tmp_path):
    game = _game(tmp_path)
    install_pack(_pack(tmp_path, "lumen"), game)
    target = game / "BepInEx" / "plugins" / "CustomCharacters" / "lumen.custom.json"
    target.write_text("edited by hand", encoding="utf-8")
    bundle = game / "BepInEx" / "plugins" / "CustomCharacters" / "lumen"
    with pytest.raises(MegabonkError, match="changed after install"):
        uninstall(game)
    assert target.read_text(encoding="utf-8") == "edited by hand"
    assert bundle.is_file()
    notes = uninstall(game, force=True)
    assert any("lumen.custom.json" in note for note in notes)
    assert not target.exists()


def test_install_refuses_anticheat_and_a_missing_loader_is_only_a_doctor_blocker(tmp_path):
    game = _game(tmp_path)
    (game / "EasyAntiCheat").mkdir()
    with pytest.raises(MegabonkError, match="anti-cheat"):
        install_pack(_pack(tmp_path, "lumen"), game)
    assert not (game / "BepInEx" / "plugins" / "CustomCharacters" / "lumen.custom.json").exists()
    bare = _game(tmp_path / "bare", loader=False)
    report = doctor(bare)
    assert any("loader" in b.lower() for b in report["blockers"])
    assert report["unity_version"] == "2023.2.22f1"
    assert report["looks_like_megabonk"] is True
    full = doctor(game)
    assert any("anti-cheat" in b for b in full["blockers"])


def test_doctor_on_the_prepared_install_has_no_play_blockers_other_than_honesty(tmp_path):
    game = _game(tmp_path)
    install_pack(_pack(tmp_path, "bulwark"), game)
    report = doctor(game)
    assert report["blockers"] == []
    assert report["characters"][0]["unityfs"] is True
    assert report["characters"][0]["id"] == 1842201002
    assert any("not run" in note.lower() or "In-game" in note for note in report["notes"])


def test_studio_starter_clones_are_distinct_and_round_trip(tmp_path):
    data = starter_file()
    errors, warnings = validate_studio(data)
    assert errors == [] and warnings == []
    looks, passives, weapons = set(), set(), set()
    for row in data["characters"]:
        looks.add(row["appearance"])
        passives.add(row["passive"])
        weapons.add(row["weapon"])
        assert row["id"] >= 1000
        assert row["stats"]
    assert len(looks) == len(passives) == len(weapons) == 3
    path = tmp_path / "characters.json"
    save_studio(path, data)
    loaded = load_studio(path)
    assert [row["name"] for row in loaded["characters"]] == ["Lumen", "Bulwark", "Flicker"]
    with pytest.raises(MegabonkError, match="Amog"):
        add_character(loaded, "Spare", "Amog", ident=14)
    assert len(loaded["characters"]) == 3
    add_character(loaded, "Spare", "Amog")
    assert loaded["characters"][-1]["appearance"] == "Amog"
    assert loaded["characters"][-1]["id"] >= 1000


def test_studio_set_rejects_a_bad_tint_without_losing_the_draft(tmp_path):
    path = tmp_path / "characters.json"
    save_studio(path, starter_file())
    data = load_studio(path)
    with pytest.raises(MegabonkError, match="tint"):
        set_character(data, 1842201001, tint="gold")
    assert data["characters"][0]["tint"] == "#FFD24A"
    set_character(data, 1842201001, stats=["Armor:Addition:3"], appearance="Calcium")
    assert data["characters"][0]["appearance"] == "Calcium"
    assert data["characters"][0]["stats"][-1] == {"stat": "Armor", "modify": "Addition", "amount": 3}


def test_plugin_catalog_matches_the_python_enums():
    text = (ROOT / "examples" / "megabonk-characters" / "plugin" / "Catalog.cs").read_text(encoding="utf-8")

    def grab(array):
        start = text.index(f"string[] {array}")
        body = text[start:text.index("};", start)]
        return re.findall(r'"([A-Za-z0-9]+)"', body)

    import re
    assert grab("Stats") == list(STATS)
    assert grab("Passives") == list(PASSIVES)
    assert grab("Characters") == [CHARACTERS[i] for i in range(len(CHARACTERS))]
    assert grab("Weapons") == [WEAPONS[i] for i in range(len(WEAPONS))]
    assert grab("ModifyTypes") == list(MODIFY)
    plugin = "\n".join(p.read_text(encoding="utf-8") for p in (ROOT / "examples" / "megabonk-characters" / "plugin").glob("*.cs"))
    for member in ("characterData", "unsortedCharacterData", "skinData", "statModifiers", "eCharacter", "Instantiate"):
        assert member in plugin


def test_build_script_requires_the_net10_sdk_and_keeps_net6():
    script = (ROOT / "examples" / "megabonk-characters" / "plugin" / "build.ps1").read_text(encoding="utf-8")
    project = (ROOT / "examples" / "megabonk-characters" / "plugin" / "UmMegabonkStudio.csproj").read_text(encoding="utf-8")
    assert "Microsoft.DotNet.SDK.10" in script
    assert "dotnet --list-sdks" in script
    assert "Install the .NET 6 SDK" not in script
    assert "<TargetFramework>net6.0</TargetFramework>" in project
    assert "<CheckEolTargetFramework>false</CheckEolTargetFramework>" in project
    assert "interop\\Il2Cppmscorlib.dll" in script
    assert "interop\\UnityEngine.AudioModule.dll" in script
    assert "Il2Cppmscorlib.dll" in project
    registrar = (ROOT / "examples" / "megabonk-characters" / "plugin" / "Registrar.cs").read_text(encoding="utf-8")
    plugin = (ROOT / "examples" / "megabonk-characters" / "plugin" / "Plugin.cs").read_text(encoding="utf-8")
    assert "Assets.Scripts.Saves___Serialization.Progression.Achievements" in registrar
    assert "Assets.Scripts.Saves___Serialization.Progression.Achievements" in plugin
    assert "List<Assets.Scripts.Inventory__Items__Pickups.Stats.StatModifier>" in registrar
    assert ".description" not in registrar
    window = (ROOT / "examples" / "megabonk-characters" / "plugin" / "StudioWindow.cs").read_text(encoding="utf-8")
    assert "TextField" not in window and "TextArea" not in window
    assert "keyboardControl" in window
    assert "skinData.Add" in registrar
    assert "characterSkins" in registrar


def _draft_message(**extra):
    character = {
        "name": "Cinder",
        "description": "A spark.",
        "author": "local",
        "appearance": "fox",
        "tint": "#ffd24a",
        "passive": "float",
        "weapon": "axe",
        "stats": [{"stat": "CritChance", "modify": "Addition", "amount": 0.15}],
    }
    character.update(extra)
    return json.dumps({"type": "draft", "character": character})


def test_model_reply_accepts_questions_and_a_catalog_draft():
    asked = interpret_model_message('```json\n{"type":"questions","questions":["Which look?"]}\n```')
    assert asked == {"ok": True, "kind": "questions", "questions": ["Which look?"]}
    parsed = interpret_model_message(_draft_message())
    assert parsed["ok"] is True and parsed["kind"] == "draft"
    assert parsed["character"]["appearance"] == "Fox"
    assert parsed["character"]["passive"] == "Float"
    assert parsed["character"]["weapon"] == "Axe"
    assert parsed["character"]["tint"] == "#ffd24a"
    assert "id" not in parsed["character"]
    original = starter_file()
    before = json.dumps(original)
    applied = apply_model_draft(original, parsed["character"])
    assert json.dumps(original) == before
    assert applied["characters"][-1]["id"] == 1842201004
    assert applied["characters"][-1]["name"] == "Cinder"
    onto = apply_model_draft(original, parsed["character"], onto_id=1842201001)
    assert onto["characters"][0]["id"] == 1842201001
    assert onto["characters"][0]["name"] == "Cinder"
    assert onto["characters"][0]["appearance"] == "Fox"
    history = push_snapshot([], before)
    restored, history = undo_snapshot(history)
    assert json.loads(restored)["characters"][0]["name"] == "Lumen"
    assert history == []
    with pytest.raises(MegabonkError, match="nothing to undo"):
        undo_snapshot([])


def test_model_reply_rejects_unknown_fields_effects_and_ids():
    assert interpret_model_message(_draft_message(id=1842201001))["ok"] is False
    assert interpret_model_message(_draft_message(script="System.Diagnostics.Process.Start"))["ok"] is False
    bad_stat = _draft_message(stats=[{"stat": "Fly", "modify": "Addition", "amount": 1}])
    assert interpret_model_message(bad_stat)["ok"] is False
    assert interpret_model_message(_draft_message(passive="Teleport"))["ok"] is False
    assert interpret_model_message(_draft_message(tint="gold"))["ok"] is False
    assert interpret_model_message(_draft_message(portrait="https://example.invalid/a.png"))["ok"] is False
    both = json.dumps({"type": "questions", "questions": ["Look?"], "character": {"name": "X"}})
    assert interpret_model_message(both)["ok"] is False
    assert interpret_model_message('{"type":"draft"}')["ok"] is False
    assert interpret_model_message("not json")["ok"] is False
    original = starter_file()
    with pytest.raises(MegabonkError, match="no character"):
        apply_model_draft(original, interpret_model_message(_draft_message())["character"], onto_id=999999)


def test_model_client_has_no_default_endpoint_and_the_panel_stays_button_only():
    plugin = ROOT / "examples" / "megabonk-characters" / "plugin"
    client = (plugin / "AiClient.cs").read_text(encoding="utf-8")
    window = (plugin / "StudioWindow.cs").read_text(encoding="utf-8")
    assert "chat/completions" in client
    assert "MEGABONK_AI_KEY" in client
    assert "127.0.0.1" not in client
    assert "api.openai.com" not in client
    assert "sk-" not in client
    assert "TextField" not in window and "TextArea" not in window
    assert "whiteTexture" in window
    assert "open list" in window
    assert "base_url" in window


def test_doctor_on_an_empty_folder_lists_the_missing_files(tmp_path):
    report = doctor(tmp_path / "nope")
    assert report["blockers"]
    empty = tmp_path / "empty"
    empty.mkdir()
    report = doctor(empty)
    joined = " ".join(report["blockers"]).lower()
    assert "gameassembly" in joined
    assert "global-metadata" in joined
    assert "bepinex" in joined
