"""Megabonk character definitions for the community custom-character loader.

    um megabonk prerequisites
    um megabonk catalog
    um megabonk new characters/lumen.yaml --name Lumen --author you
    um megabonk import-icon characters/lumen.yaml --slot character --png art/lumen.png
    um megabonk validate characters/lumen.yaml
    um megabonk pack characters/lumen.yaml -o packs/lumen
    um megabonk doctor "C:/Program Files (x86)/Steam/steamapps/common/Megabonk"
    um megabonk install packs/lumen --game "C:/Program Files (x86)/Steam/steamapps/common/Megabonk"
    um megabonk uninstall --game "C:/Program Files (x86)/Steam/steamapps/common/Megabonk"

Authors distinct character definitions (appearance paths, a renamed existing passive, stat
modifiers, a weapon) and writes the `.custom.json` the public Custom Character Loader reads.
It does not ship a playable model. The loader opens a sibling UnityFS AssetBundle and pulls
the prefab, icons, materials and weapon attack out of that bundle. Building the bundle needs
Unity 2023.2.22f1 and the public maker project. This machine has no Megabonk install, so
nothing here was loaded by the game.

Enum numbers are the ones published for the 1.0.69-era character tools (see the catalogs
below). A game update can renumber them. Re-read BepInEx/interop/Assembly-CSharp.dll from
your own install before trusting a pack on a newer build.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import re
import shutil
from pathlib import Path

from um.common import die

FORMAT = "um-megabonk-character"
FORMAT_VERSION = 1
LOADER_JSON_VERSION = "1.4.0"
MIN_ID = 1000
MAX_ID = 2147483647  # the public maker randomizes inside signed int range
UNITYFS = b"UnityFS"
MIN_BUNDLE_BYTES = 64
INSTALL_DIR = "BepInEx/plugins/CustomCharacters"
STATE_DIR = "BepInEx/um-megabonk"
MANIFEST_NAME = "install.json"
REQUIRED_CLIPS = ("Idle", "Run", "Jump", "Fall", "Grind")
ANIMATOR_BOOLS = ("grounded", "moving", "jumping", "grinding")
BUNDLE_NAME = re.compile(r"^[A-Za-z0-9][A-Za-z0-9_-]{0,40}$")
ASSET_PATH = re.compile(r"^Assets/[A-Za-z0-9_./ -]+$")

# EStat, index order published in lukeod/megabonk_research enums.md (validated there 2026-01-28
# against that author's decompile). 57 values, 0-56. ExtraJumps is 46.
STATS: dict[str, int] = {
    "MaxHealth": 0, "HealthRegen": 1, "Shield": 2, "Thorns": 3, "Armor": 4, "Evasion": 5,
    "Evolve": 6, "DamageReductionMultiplier": 7, "DamageCooldownMultiplier": 8, "SizeMultiplier": 9,
    "DurationMultiplier": 10, "ProjectileSpeedMultiplier": 11, "DamageMultiplier": 12, "Unused0": 13,
    "EffectDurationMultiplier": 14, "AttackSpeed": 15, "Projectiles": 16, "Lifesteal": 17,
    "CritChance": 18, "CritDamage": 19, "FireDamage": 20, "IceDamage": 21, "LightningDamage": 22,
    "EliteDamageMultiplier": 23, "KnockbackMultiplier": 24, "MoveSpeedMultiplier": 25, "JumpHeight": 26,
    "FallDamageReduction": 27, "Slam": 28, "PickupRange": 29, "Luck": 30, "GoldIncreaseMultiplier": 31,
    "XpIncreaseMultiplier": 32, "ChestIncreaseMultiplier": 33, "ChestPriceMultiplier": 34,
    "ShopPriceReduction": 35, "Holiness": 36, "Wickedness": 37, "Difficulty": 38, "EliteSpawnIncrease": 39,
    "PowerupBoostMultiplier": 40, "PowerupChance": 41, "BurnChance": 42, "FreezeChance": 43,
    "WeaponBurstCooldown": 44, "ProjectileBounces": 45, "ExtraJumps": 46, "Overheal": 47,
    "HealingMultiplier": 48, "SilverIncreaseMultiplier": 49, "EnemyAmountMultiplier": 50,
    "EnemySizeMultiplier": 51, "EnemySpeedMultiplier": 52, "EnemyHpMultiplier": 53,
    "EnemyDamageMultiplier": 54, "EnemyScalingMultiplier": 55, "PoisonDamageMultiplier": 56,
}
# The maker's CustomWeaponSO.GetStatList always writes these, in this order. The loader copies
# the json list into WeaponData.baseStats. A missing key is a reported crash on the first swing.
REQUIRED_WEAPON_STATS = (
    "AttackSpeed", "DamageMultiplier", "KnockbackMultiplier", "SizeMultiplier", "ProjectileBounces",
    "Projectiles", "DurationMultiplier", "ProjectileSpeedMultiplier", "CritChance", "CritDamage",
)
MODIFY: dict[str, int] = {"Addition": 0, "Multiplication": 1, "Flat": 2}
CATEGORIES: dict[str, int] = {
    "Offensive": 0, "Defensive": 1, "Movement": 2, "Utility": 3, "Difficulty": 4, "Coolness": 5, "Null": 6,
}
# Fox ratios shipped as the maker's CustomCharacterSO sample. Written here so a new character does
# not silently get an all-zero list. What the game does with the ratios was not re-measured.
DEFAULT_CATEGORY_RATIOS: dict[str, float] = {
    "Offensive": 0.0, "Defensive": 0.2765, "Movement": 0.3056, "Utility": 1.0,
    "Difficulty": 0.0, "Coolness": 0.625, "Null": 1.0,
}
# EPassive, sequential from 0, as published in the character maker's type list (21 members).
PASSIVES: dict[str, int] = {
    "Bullseye": 0, "RngBlessing": 1, "SpeedDemon": 2, "Reinforced": 3, "Flowstate": 4,
    "CritHappens": 5, "Warrior": 6, "Flex": 7, "WallClimb": 8, "None": 9, "Float": 10,
    "Enduring": 11, "Plague": 12, "Quantum": 13, "Shadowstep": 14, "Gamba": 15, "Vampire": 16,
    "Curse": 17, "Stonks": 18, "LockIn": 19, "Zap": 20,
}
# Vanilla ECharacter, sequential from 0. A custom id in this range collides with a real character.
CHARACTERS: dict[int, str] = {
    0: "Fox", 1: "Calcium", 2: "SirOofie", 3: "Cl4nk", 4: "Megachad", 5: "Ogre", 6: "Robinette",
    7: "Athena", 8: "Birdo", 9: "Bush", 10: "Bandit", 11: "Monke", 12: "Noelle", 13: "TonyMcZoom",
    14: "Amog", 15: "Spaceman", 16: "Ninja", 17: "Vlad", 18: "Dicehead", 19: "SirChadwell",
}
# Vanilla EWeapon. The loader registers the custom weapon under the same number as the character,
# so an id in this range also collides with a weapon.
WEAPONS: dict[int, str] = {
    0: "FireStaff", 1: "Bone", 2: "Sword", 3: "Revolver", 4: "Aura", 5: "Axe", 6: "Bow", 7: "Aegis",
    8: "Test", 9: "LightningStaff", 10: "Flamewalker", 11: "Rockets", 12: "Bananarang", 13: "Tornado",
    14: "Dexecutioner", 15: "Sniper", 16: "Frostwalker", 17: "SpaceNoodle", 18: "DragonsBreath",
    19: "Chunkers", 20: "Mine", 21: "PoisonFlask", 22: "BlackHole", 23: "Katana", 24: "BloodMagic",
    25: "BluetoothDagger", 26: "Dice", 27: "HeroSword", 28: "CorruptSword", 29: "Shotgun", 30: "Scythe",
}
WEAPON_FIELDS = {
    "attack_speed": "AttackSpeed", "damage": "DamageMultiplier", "knockback": "KnockbackMultiplier",
    "size": "SizeMultiplier", "bounces": "ProjectileBounces", "projectiles": "Projectiles",
    "duration": "DurationMultiplier", "projectile_speed": "ProjectileSpeedMultiplier",
    "crit_chance": "CritChance", "crit_damage": "CritDamage",
}
ANTICHEAT_GLOBS = (
    "*easyanticheat*", "*start_protected_game*", "*battleye*", "*gameguard*", "*eac_launcher*",
)


class MegabonkError(Exception):
    """A character file, pack or install step that cannot continue."""


def prerequisites_text() -> str:
    return """\
Megabonk custom characters — what has to be true before one appears in the game

Checked against public sources on 2026-11-07. Not re-run in Megabonk from this environment
(no game files were available).

Game
  Steam app 3405340, Windows x64. Field notes and a public playtest describe build 21750826
  shown in game as 1.0.69, Unity 2023.2.22f1, IL2CPP. Confirm the version on your install
  (Megabonk_Data/globalgamemanagers, or the in-game version). A newer build can break the loader.

Files this tool reads from the install (it never copies them into the repo)
  Megabonk.exe
  GameAssembly.dll
  Megabonk_Data/il2cpp_data/Metadata/global-metadata.dat
  Megabonk_Data/app.info                         (company Ved, product Megabonk, on the builds above)
  BepInEx/plugins/                               (created by the loader pack, not by this tool)
  BepInEx/plugins/**/CustomCharacter*.dll        (the character loader)
  BepInEx/interop/Assembly-CSharp.dll            (only after one modded boot; for checking enums)

Loader, installed by you into a copy of the game
  BepInExPack_IL2CPP 6.0.738.
    The character loader's Thunderstore dependency string pins 6.0.738.
    A newer pack, 6.0.788, was current on Thunderstore on 2026-10-12. It was not tested here
    with the character loader. Prefer the pinned 6.0.738 unless you are checking a newer pair.
  Shadowth117-CustomCharacterCreator_Shadowth117Fix 1.4.1
    (https://github.com/Shadowth117/MEGABONK_CustomCharacterLoader).
    The original MegabonkModders CustomCharacterCreator 1.4.0 is the loader that public reports
    say broke in the December 2025 game update. Do not install that build for a current game.
  This command does not download or install either package.

Appearance
  The loader opens the file next to NAME.custom.json (the same path with .custom.json removed)
  as a Unity AssetBundle, then loads the prefab, icons, materials and weapon attack by the
  Assets/... paths inside the json. A json without that bundle does not show a character, and
  a missing bundle throws inside DataManager.Load. This tool refuses to copy a json into the
  game unless the sibling file starts with the UnityFS header.
  Building that bundle needs Unity 2023.2.22f1 (built-in render pipeline) and the public maker
  https://github.com/PeterMoras/MEGABONK_CustomCharacterMaker — drop its files into Assets/ and
  add com.unity.nuget.newtonsoft-json. The maker repo vendors game scripts. This repo does not
  copy them. Use your own model. The published character budget is five clips (Idle, Run, Jump,
  Fall, Grind) on an Animator at the prefab root, driven by bools grounded, moving, jumping,
  grinding. Weapons need an attack prefab that uses one of the game's existing projectile
  scripts. A passive can only be one of the 21 EPassive values, under your own name and icon.

Saves and leaderboards
  Saves live in %USERPROFILE%\\AppData\\LocalLow\\Ved\\Megabonk and are shared by a copied
  game folder. Steam Cloud and the leaderboard upload switch live with that profile too.
  Before the first modded launch:
    um backup create "%USERPROFILE%\\AppData\\LocalLow\\Ved\\Megabonk" --name megabonk-saves
  In game: Settings, Game, Upload score to Leaderboards, Off.
  The community loader's save patch can add a skin-preference entry. This installer does not
  read or write the save folder, the registry, or Steam.

Uninstall
  um megabonk uninstall --game <install>
  removes only files this tool's manifest records, and restores any file it replaced.
  It does not remove BepInEx. Verifying the game in Steam does not remove BepInEx either.
"""


def catalog_text() -> str:
    lines = ["EStat (character and weapon numbers)", ""]
    for name, idx in STATS.items():
        lines.append(f"  {idx:2d}  {name}")
    lines += ["", "EStatModifyType", ""]
    lines += [f"  {idx}  {name}" for name, idx in MODIFY.items()]
    lines += ["", "EStatCategory", ""]
    lines += [f"  {idx}  {name}" for name, idx in CATEGORIES.items()]
    lines += ["", "EPassive (the effect a custom passive can select)", ""]
    lines += [f"  {idx:2d}  {name}" for name, idx in PASSIVES.items()]
    lines += ["", "Vanilla ECharacter (do not reuse these ids)", ""]
    lines += [f"  {idx:2d}  {name}" for idx, name in CHARACTERS.items()]
    lines += ["", "Vanilla EWeapon (the loader also registers the custom weapon under the character id)", ""]
    lines += [f"  {idx:2d}  {name}" for idx, name in WEAPONS.items()]
    lines.append("")
    lines.append("Numbers are from the public 1.0.69-era catalogs. They were not re-read from an interop DLL here.")
    return "\n".join(lines)


def _yaml():
    try:
        import yaml
    except ImportError as e:
        raise MegabonkError("this command needs pyyaml (bin/um installs it via uv)") from e
    return yaml


def _sha256(path: Path) -> str:
    h = hashlib.sha256()
    with open(path, "rb") as f:
        for chunk in iter(lambda: f.read(1 << 20), b""):
            h.update(chunk)
    return h.hexdigest()


def _num(value, label: str) -> int | float:
    if isinstance(value, bool) or not isinstance(value, (int, float)):
        raise MegabonkError(f"{label} must be a number, got {value!r}")
    if isinstance(value, float) and value.is_integer():
        return int(value)
    return value


def _req_str(data: dict, key: str, label: str) -> str:
    value = data.get(key)
    if not isinstance(value, str) or not value.strip():
        raise MegabonkError(f"{label} needs a non-empty {key}")
    return value.strip()


def _enum(table: dict[str, int], value, label: str) -> tuple[str, int]:
    if isinstance(value, bool):
        raise MegabonkError(f"{label} is not a known name")
    if isinstance(value, int) or (isinstance(value, float) and value.is_integer()):
        idx = int(value)
        for name, number in table.items():
            if number == idx:
                return name, number
        raise MegabonkError(f"{label} number {idx} is outside the published {label} list")
    if isinstance(value, str):
        for name, number in table.items():
            if name.lower() == value.strip().lower():
                return name, number
    if value is None and any(name.lower() in {"none", "null"} for name in table):
        raise MegabonkError(f"{label} was read as null. Quote YAML names such as 'None' and 'Null'.")
    raise MegabonkError(f"{label} {value!r} is not in the published list ({', '.join(table)})")


def _id_problem(value) -> str | None:
    if isinstance(value, bool) or not isinstance(value, (int, float)) or (isinstance(value, float) and not value.is_integer()):
        return f"id must be an integer from {MIN_ID} to {MAX_ID}"
    idx = int(value)
    if idx < MIN_ID or idx > MAX_ID:
        extra = ""
        if idx in CHARACTERS:
            extra = f" ({CHARACTERS[idx]} is a vanilla character)"
        elif idx in WEAPONS:
            extra = f" ({WEAPONS[idx]} is a vanilla weapon, and the loader registers the custom weapon under this id)"
        elif 0 <= idx < MIN_ID:
            extra = " (the public maker treats ids under 1000 as vanilla and randomizes them)"
        return f"id {idx} is not allowed{extra}; use {MIN_ID}..{MAX_ID}"
    return None


def _asset_ok(path: str) -> bool:
    return bool(ASSET_PATH.match(path)) and ".." not in path.split("/")


def _mapping(value, label: str) -> dict:
    if not isinstance(value, dict):
        raise MegabonkError(f"{label} must be a mapping")
    return value


def _list(value, label: str) -> list:
    if not isinstance(value, list):
        raise MegabonkError(f"{label} must be a list")
    return value


def load_yaml(path: Path) -> dict:
    data = _yaml().safe_load(path.read_text(encoding="utf-8"))
    if not isinstance(data, dict):
        raise MegabonkError(f"{path} is not a character mapping")
    return data


def dump_yaml(path: Path, data: dict) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    yaml = _yaml()

    class _Dumper(yaml.SafeDumper):
        pass

    def _represent_str(dumper, value):
        # YAML 1.1 reads a bare None as null. The empty passive is the name None.
        # YAML 1.1 also reads a bare Null as null, and Null is a real stat category.
        style = "'" if value.lower() in {"none", "null", "yes", "no", "true", "false", "on", "off"} else None
        return dumper.represent_scalar("tag:yaml.org,2002:str", value, style=style)

    _Dumper.add_representer(str, _represent_str)
    path.write_text(
        yaml.dump(data, Dumper=_Dumper, sort_keys=False, allow_unicode=True, width=100, default_flow_style=False),
        encoding="utf-8",
    )


def new_character(path: Path, name: str, author: str, ident: int | None = None) -> dict:
    """Write a starter definition. Icon paths are Unity paths until import-icon replaces them."""
    if path.exists():
        raise MegabonkError(f"refusing to overwrite {path}")
    ident = ident if ident is not None else 1842201000
    problem = _id_problem(ident)
    if problem:
        raise MegabonkError(problem)
    slug = re.sub(r"[^a-z0-9]+", "", name.lower()) or "hero"
    if not BUNDLE_NAME.match(slug):
        raise MegabonkError(f"could not make an asset bundle name from {name!r}")
    data = {
        "format": FORMAT,
        "format_version": FORMAT_VERSION,
        "id": ident,
        "author": author,
        "asset_bundle": slug,
        "name": name,
        "description": f"{name} is a new Megabonk character. Replace this text.",
        "collider": {"height": 3.85, "width": 0.7},
        "coolness": 5,
        "difficulty": 1,
        "appearance": {
            "prefab": f"Assets/{slug}/{slug}.prefab",
            "icon": f"Assets/{slug}/icons/character.png",
            "theme_song": "",
            "footsteps": [],
            "clips": list(REQUIRED_CLIPS),
            "animator_bools": list(ANIMATOR_BOOLS),
        },
        "passive": {
            "effect": "None",
            "name": f"{name} passive",
            "description": "Select an EPassive effect. None has no built-in behaviour; the name still shows.",
            "icon": f"Assets/{slug}/icons/passive.png",
        },
        "weapon": {
            "name": f"{name} weapon",
            "description": "Numbers below are passed through to the loader. They were not playtested here.",
            "icon": f"Assets/{slug}/icons/weapon.png",
            "attack_prefab": f"Assets/{slug}/attack.prefab",
            "attack_speed": 1,
            "damage": 10,
            "knockback": 1,
            "size": 1,
            "bounces": 0,
            "projectiles": 1,
            "duration": 1,
            "projectile_speed": 1,
            "crit_chance": 0,
            "crit_damage": 0,
            "extra_stats": [],
            "upgrades": [{"stat": "DamageMultiplier", "modify": "Flat", "amount": 2}],
        },
        "stats": [],
        "category_ratios": dict(DEFAULT_CATEGORY_RATIOS),
        "skins": [{
            "name": "Default",
            "description": f"{name}'s default look.",
            "icon": f"Assets/{slug}/icons/skin.png",
            "materials": [f"Assets/{slug}/body.mat"],
        }],
    }
    dump_yaml(path, data)
    return data


def _modifier(raw, label: str) -> dict:
    raw = _mapping(raw, label)
    stat, stat_i = _enum(STATS, raw.get("stat"), f"{label} stat")
    modify, modify_i = _enum(MODIFY, raw.get("modify", raw.get("modifyType")), f"{label} modify")
    amount = _num(raw.get("amount", raw.get("modification")), f"{label} amount")
    return {"stat": stat_i, "modifyType": modify_i, "modification": amount, "_stat": stat, "_modify": modify}


def _category_ratios(raw) -> list[dict]:
    if raw is None:
        raw = DEFAULT_CATEGORY_RATIOS
    if isinstance(raw, dict):
        items = raw.items()
    elif isinstance(raw, list):
        items = []
        for entry in raw:
            entry = _mapping(entry, "category ratio")
            items.append((entry.get("category", entry.get("stat")), entry.get("value")))
    else:
        raise MegabonkError("category_ratios must be a mapping of category to number, or a list")
    found = {}
    for name, value in items:
        canon, _idx = _enum(CATEGORIES, name, "category")
        found[canon] = _num(value, f"category {canon}")
    missing = [c for c in CATEGORIES if c not in found]
    if missing:
        raise MegabonkError("category_ratios is missing " + ", ".join(missing))
    return [{"category": CATEGORIES[c], "value": found[c]} for c in CATEGORIES]


def validate_definition(data: dict, base: Path | None = None) -> tuple[list[str], list[str]]:
    """Return (errors, warnings). Errors mean the loader json would be wrong or rejected."""
    errors: list[str] = []
    warnings: list[str] = []

    def bad(msg: str) -> None:
        errors.append(msg)

    if data.get("format") != FORMAT or data.get("format_version") != FORMAT_VERSION:
        bad(f"expected format {FORMAT} version {FORMAT_VERSION}")
        return errors, warnings
    problem = _id_problem(data.get("id"))
    if problem:
        bad(problem)
    for key in ("author", "name", "description"):
        if not isinstance(data.get(key), str) or not data[key].strip():
            bad(f"{key} must be a non-empty string")
    bundle = data.get("asset_bundle")
    if not isinstance(bundle, str) or not BUNDLE_NAME.match(bundle):
        bad("asset_bundle must match [A-Za-z0-9][A-Za-z0-9_-]{0,40} (it becomes the file name)")
    try:
        col = _mapping(data.get("collider"), "collider")
        _num(col.get("height"), "collider.height")
        _num(col.get("width"), "collider.width")
        _num(data.get("coolness"), "coolness")
        _num(data.get("difficulty"), "difficulty")
    except MegabonkError as e:
        bad(str(e))
    appearance = data.get("appearance")
    if not isinstance(appearance, dict):
        bad("appearance must be a mapping")
        appearance = {}
    for key in ("prefab", "icon"):
        if not isinstance(appearance.get(key), str) or not appearance[key].strip():
            bad(f"appearance.{key} is required")
    clips = appearance.get("clips", [])
    if clips and (not isinstance(clips, list) or [c for c in REQUIRED_CLIPS if c not in clips]):
        warnings.append(
            "appearance.clips should include Idle, Run, Jump, Fall and Grind. They live on the prefab, "
            "not in the loader json. The loader was not re-tested here."
        )
    elif not clips:
        warnings.append("appearance.clips is empty. Published characters use Idle, Run, Jump, Fall and Grind on the prefab.")
    try:
        passive = _mapping(data.get("passive"), "passive")
        _enum(PASSIVES, passive.get("effect"), "passive.effect")
        if not isinstance(passive.get("name"), str) or not passive["name"].strip():
            bad("passive.name is required (this is the label; effect selects the game passive)")
        if not isinstance(passive.get("description"), str):
            bad("passive.description must be a string")
        if not isinstance(passive.get("icon"), str) or not passive["icon"].strip():
            bad("passive.icon is required")
    except MegabonkError as e:
        bad(str(e))
    try:
        weapon = _mapping(data.get("weapon"), "weapon")
        for key in ("name", "description", "icon", "attack_prefab"):
            if not isinstance(weapon.get(key), str) or not str(weapon[key]).strip():
                bad(f"weapon.{key} is required")
        for key in WEAPON_FIELDS:
            _num(weapon.get(key), f"weapon.{key}")
        seen = set(REQUIRED_WEAPON_STATS)
        for extra in _list(weapon.get("extra_stats", []), "weapon.extra_stats"):
            stat, _i = _enum(STATS, _mapping(extra, "extra stat").get("stat"), "extra stat")
            _num(_mapping(extra, "extra stat").get("amount", extra.get("value")), stat)
            if stat in seen:
                bad(f"weapon stat {stat} is listed twice (the loader adds base stats as a dictionary)")
            seen.add(stat)
        upgrades = _list(weapon.get("upgrades", []), "weapon.upgrades")
        if not upgrades:
            warnings.append("weapon.upgrades is empty, so the weapon has no level-up choices in the json")
        for entry in upgrades:
            _modifier(entry, "upgrade")
    except MegabonkError as e:
        bad(str(e))
    try:
        for entry in _list(data.get("stats", []), "stats"):
            _modifier(entry, "stat")
        _category_ratios(data.get("category_ratios"))
    except MegabonkError as e:
        bad(str(e))
    skins = data.get("skins")
    if not isinstance(skins, list) or not skins:
        bad("skins must contain at least one skin")
        skins = []
    names = set()
    for skin in skins:
        try:
            skin = _mapping(skin, "skin")
        except MegabonkError as e:
            bad(str(e))
            continue
        if not isinstance(skin.get("name"), str) or not skin["name"].strip():
            bad("each skin needs a name")
        elif skin["name"] in names:
            bad(f"duplicate skin name {skin['name']}")
        names.add(skin.get("name"))
        if not isinstance(skin.get("icon"), str) or not skin["icon"].strip():
            bad(f"skin {skin.get('name')} needs an icon")
        mats = skin.get("materials")
        if not isinstance(mats, list) or not mats or not all(isinstance(m, str) and m.strip() for m in mats):
            bad(f"skin {skin.get('name')} needs at least one material path")
        elif len(mats) > 1:
            warnings.append(
                f"skin {skin.get('name')} has {len(mats)} materials. A public playtest reported that a second "
                "material is drawn over the body. Not re-tested here."
            )
    asset_paths = [value for _label, value in _local_paths(data) if value.startswith("Assets/")]
    if asset_paths:
        warnings.append(
            f"{len(asset_paths)} path(s) are Assets/ references, not files in this pack. "
            "Unity 2023.2.22f1 has to build them into the AssetBundle. This tool did not."
        )
    if base is not None:
        warnings.extend(_missing_local_files(data, base))
    # The maker's sample stat block adds +45 pickup range. Flag it so it is a choice, not an accident.
    for entry in data.get("stats") or []:
        if isinstance(entry, dict) and str(entry.get("stat", "")).lower() == "pickuprange":
            try:
                if float(entry.get("amount", entry.get("modification", 0))) >= 45:
                    warnings.append(
                        "a PickupRange modifier of 45 or more matches the maker's sample character, "
                        "which public notes say ships by accident when stats are left at the sample"
                    )
            except (TypeError, ValueError):
                pass
    return errors, warnings


def _local_paths(data: dict) -> list[tuple[str, str]]:
    found = []
    appearance = data.get("appearance") or {}
    found.append(("appearance.icon", appearance.get("icon", "")))
    if appearance.get("theme_song"):
        found.append(("appearance.theme_song", appearance["theme_song"]))
    for i, step in enumerate(appearance.get("footsteps") or []):
        found.append((f"appearance.footsteps[{i}]", step))
    found.append(("appearance.prefab", appearance.get("prefab", "")))
    passive = data.get("passive") or {}
    found.append(("passive.icon", passive.get("icon", "")))
    weapon = data.get("weapon") or {}
    found.append(("weapon.icon", weapon.get("icon", "")))
    found.append(("weapon.attack_prefab", weapon.get("attack_prefab", "")))
    for i, skin in enumerate(data.get("skins") or []):
        if not isinstance(skin, dict):
            continue
        found.append((f"skins[{i}].icon", skin.get("icon", "")))
        for j, mat in enumerate(skin.get("materials") or []):
            found.append((f"skins[{i}].materials[{j}]", mat))
    return [(label, value) for label, value in found if isinstance(value, str)]


def _missing_local_files(data: dict, base: Path) -> list[str]:
    warnings = []
    for label, value in _local_paths(data):
        if value.startswith("Assets/"):
            if not _asset_ok(value):
                warnings.append(f"{label} path {value!r} is not a safe Assets/ path")
            continue
        if not value:
            continue
        path = (base / value).resolve()
        if not path.is_file():
            warnings.append(f"{label} file is not next to the definition: {value}")
    return warnings


def _stage(value: str, base: Path, unity_root: Path, bundle: str, slot: str) -> str:
    """Return the Assets/ path the loader will request, copying a local file into the pack."""
    if not isinstance(value, str):
        raise MegabonkError(f"{slot} path is missing")
    value = value.strip()
    if not value:
        return ""
    if value.startswith("Assets/"):
        if not _asset_ok(value):
            raise MegabonkError(f"{slot} path is not a safe Assets/ path: {value}")
        return value
    src = (base / value).resolve()
    if not src.is_file():
        raise MegabonkError(f"{slot} file does not exist: {value}")
    if ".." in Path(value).parts:
        raise MegabonkError(f"{slot} path must stay next to the definition")
    suffix = src.suffix.lower() or ".asset"
    dest_rel = Path(bundle) / "staged" / f"{slot}{suffix}"
    dest = unity_root / dest_rel
    dest.parent.mkdir(parents=True, exist_ok=True)
    shutil.copy2(src, dest)
    return "Assets/" + dest_rel.as_posix()


def to_loader_json(data: dict, base: Path, unity_root: Path) -> dict:
    errors, _warnings = validate_definition(data, base)
    if errors:
        raise MegabonkError("character definition is not valid:\n" + "\n".join(f"- {e}" for e in errors))
    bundle = data["asset_bundle"].lower()
    appearance = data["appearance"]
    passive = data["passive"]
    weapon = data["weapon"]
    _, passive_i = _enum(PASSIVES, passive["effect"], "passive.effect")
    theme = _stage(appearance.get("theme_song") or "", base, unity_root, bundle, "theme")
    steps = [
        _stage(step, base, unity_root, bundle, f"footstep{i}")
        for i, step in enumerate(appearance.get("footsteps") or [])
    ]
    stats = []
    for i, raw in enumerate(weapon.get("extra_stats") or []):
        stat, idx = _enum(STATS, raw["stat"], "extra stat")
        stats.append({"Stat": idx, "Value": _num(raw.get("amount", raw.get("value")), stat)})
    base_stats = [{"Stat": STATS[name], "Value": _num(weapon[field], f"weapon.{field}")} for field, name in WEAPON_FIELDS.items()]
    # required stats first, extras after, matching the maker's GetStatList order
    ordered = base_stats + stats
    upgrades = []
    for raw in weapon.get("upgrades") or []:
        mod = _modifier(raw, "upgrade")
        upgrades.append({"stat": mod["stat"], "modifyType": mod["modifyType"], "modification": mod["modification"]})
    char_stats = []
    for raw in data.get("stats") or []:
        mod = _modifier(raw, "stat")
        char_stats.append({"stat": mod["stat"], "modifyType": mod["modifyType"], "modification": mod["modification"]})
    skins = []
    for i, skin in enumerate(data["skins"]):
        skins.append({
            "skinName": skin["name"].strip(),
            "description": skin.get("description") or "",
            "iconPath": _stage(skin["icon"], base, unity_root, bundle, f"skin{i}"),
            "materialPaths": [_stage(m, base, unity_root, bundle, f"skin{i}mat{j}") for j, m in enumerate(skin["materials"])],
        })
    character = {
        "author": data["author"].strip(),
        "eCharacter": int(data["id"]),
        "assetBundleName": bundle,
        "characterName": data["name"].strip(),
        "characterDescription": data["description"].strip(),
        "colliderHeight": _num(data["collider"]["height"], "collider.height"),
        "colliderWidth": _num(data["collider"]["width"], "collider.width"),
        "coolness": _num(data["coolness"], "coolness"),
        "difficulty": _num(data["difficulty"], "difficulty"),
        # The maker writes themesongPath. The loader reads themeSongPath. Emit both.
        "themesongPath": theme,
        "themeSongPath": theme,
        "audioFootstepsPaths": steps,
        "prefabPath": _stage(appearance["prefab"], base, unity_root, bundle, "prefab"),
        "iconPath": _stage(appearance["icon"], base, unity_root, bundle, "character-icon"),
        "statModifiers": char_stats,
        "categoryRatios": _category_ratios(data.get("category_ratios")),
    }
    return {
        "version": LOADER_JSON_VERSION,
        "character": character,
        "passive": {
            "passiveName": passive["name"].strip(),
            "passiveDescription": passive.get("description") or "",
            "passive": passive_i,
            "iconPath": _stage(passive["icon"], base, unity_root, bundle, "passive-icon"),
        },
        "weapon": {
            "weaponName": weapon["name"].strip(),
            "weaponDescription": weapon["description"].strip(),
            "UpgradeOptions": upgrades,
            "stats": ordered,
            "iconPath": _stage(weapon["icon"], base, unity_root, bundle, "weapon-icon"),
            "weaponAttackPath": _stage(weapon["attack_prefab"], base, unity_root, bundle, "attack"),
        },
        "skins": skins,
    }


def validate_loader_json(doc: dict) -> tuple[list[str], list[str]]:
    errors, warnings = [], []
    if not isinstance(doc, dict):
        return ["loader json must be an object"], []
    if doc.get("version") != LOADER_JSON_VERSION:
        warnings.append(f"version is {doc.get('version')!r}; the maker writes {LOADER_JSON_VERSION}")
    char = doc.get("character")
    if not isinstance(char, dict):
        return ["missing character object"], warnings
    problem = _id_problem(char.get("eCharacter"))
    if problem:
        errors.append(problem)
    for key in ("author", "characterName", "characterDescription", "prefabPath", "iconPath", "assetBundleName"):
        if not isinstance(char.get(key), str) or not char[key].strip():
            errors.append(f"character.{key} is required")
    if isinstance(char.get("assetBundleName"), str) and not BUNDLE_NAME.match(char["assetBundleName"]):
        errors.append("character.assetBundleName is not a safe file name")
    if "statModifiers" not in char or not isinstance(char["statModifiers"], list):
        errors.append("character.statModifiers must be a list (empty is allowed)")
    if not isinstance(char.get("categoryRatios"), list) or len(char["categoryRatios"]) != len(CATEGORIES):
        errors.append(f"character.categoryRatios must have all {len(CATEGORIES)} categories")
    if char.get("themeSongPath") != char.get("themesongPath"):
        warnings.append("themeSongPath and themesongPath differ; the loader reads themeSongPath and the maker writes themesongPath")
    passive = doc.get("passive")
    if not isinstance(passive, dict):
        errors.append("missing passive object")
    else:
        try:
            _enum(PASSIVES, passive.get("passive"), "passive")
        except MegabonkError as e:
            errors.append(str(e))
        if not isinstance(passive.get("iconPath"), str):
            errors.append("passive.iconPath is required")
    weapon = doc.get("weapon")
    if not isinstance(weapon, dict):
        errors.append("missing weapon object")
    else:
        stats = weapon.get("stats")
        if not isinstance(stats, list):
            errors.append("weapon.stats must be a list")
        else:
            have = set()
            for entry in stats:
                if not isinstance(entry, dict) or "Stat" not in entry or "Value" not in entry:
                    errors.append("each weapon stat needs Stat and Value")
                    continue
                try:
                    name, _idx = _enum(STATS, entry["Stat"], "weapon stat")
                except MegabonkError as e:
                    errors.append(str(e))
                    continue
                if name in have:
                    errors.append(f"duplicate weapon stat {name}")
                have.add(name)
            missing = [name for name in REQUIRED_WEAPON_STATS if name not in have]
            if missing:
                errors.append("weapon.stats is missing " + ", ".join(missing))
        if not isinstance(weapon.get("UpgradeOptions"), list):
            errors.append("weapon.UpgradeOptions must be a list")
        if not isinstance(weapon.get("weaponAttackPath"), str) or not weapon["weaponAttackPath"]:
            errors.append("weapon.weaponAttackPath is required")
    skins = doc.get("skins")
    if not isinstance(skins, list) or not skins:
        errors.append("skins must be a non-empty list")
    else:
        for skin in skins:
            if not isinstance(skin, dict) or not skin.get("materialPaths") or not skin.get("iconPath"):
                errors.append("each skin needs iconPath and materialPaths")
    return errors, warnings


def pack_character(src: Path, out: Path) -> dict:
    """Write NAME.custom.json and staged files. Does not write an AssetBundle."""
    data = load_yaml(src)
    out.mkdir(parents=True, exist_ok=True)
    for old in out.glob("*.custom.json"):
        old.unlink()
    unity = out / "unity-assets"
    if unity.exists():
        shutil.rmtree(unity)
    doc = to_loader_json(data, src.parent, unity)
    bundle = doc["character"]["assetBundleName"]
    json_path = out / f"{bundle}.custom.json"
    json_path.write_text(json.dumps(doc, indent=2) + "\n", encoding="utf-8")
    errors, warnings = validate_loader_json(doc)
    if errors:
        raise MegabonkError("packed json failed its own check:\n" + "\n".join(errors))
    meta = {
        "format": "um-megabonk-pack",
        "format_version": 1,
        "loader_json": json_path.name,
        "bundle_file": bundle,
        "bundle_present": False,
        "in_game_verified": False,
        "id": doc["character"]["eCharacter"],
        "name": doc["character"]["characterName"],
        "warnings": warnings,
        "note": "The sibling bundle file is not produced here. Build it in Unity 2023.2.22f1 and place it next to the json before install.",
    }
    (out / "pack.json").write_text(json.dumps(meta, indent=2) + "\n", encoding="utf-8")
    (out / "BUNDLE.txt").write_text(
        f"Expected AssetBundle file name: {bundle}\n"
        "Same folder as this file, no extension, UnityFS header, built with Unity 2023.2.22f1.\n"
        "Copy unity-assets/<name>/ into the maker project's Assets/ if you staged local files,\n"
        "and keep every Assets/... path in the json pointing at an asset the build includes.\n",
        encoding="utf-8",
    )
    return {"json": json_path, "bundle_name": bundle, "warnings": warnings, "doc": doc}


def import_icon(src: Path, png: Path, slot: str, skin: str | None = None, fit: int | None = None) -> str:
    """Copy a PNG next to the definition and point the chosen slot at it."""
    try:
        from PIL import Image
    except ImportError as e:
        raise MegabonkError("import-icon needs pillow") from e
    if slot not in ("character", "weapon", "passive", "skin"):
        raise MegabonkError("slot must be character, weapon, passive or skin")
    try:
        with Image.open(png) as im:
            im.load()
            if im.format != "PNG":
                raise MegabonkError(f"{png} is {im.format}, not a PNG")
            image = im.convert("RGBA")
    except MegabonkError:
        raise
    except Exception as e:
        raise MegabonkError(f"could not read {png} as a PNG: {e}") from e
    if fit is not None:
        if fit not in (128, 256):
            raise MegabonkError("--fit must be 128 or 256")
        image = image.resize((fit, fit), Image.Resampling.NEAREST)
    elif image.size[0] not in (128, 256) or image.size[0] != image.size[1]:
        # still imported; the caller prints the warning via the returned note
        pass
    data = load_yaml(src)
    slug = re.sub(r"[^a-z0-9]+", "", str(data.get("asset_bundle", "hero")).lower()) or "hero"
    label = slot if slot != "skin" else "skin-" + re.sub(r"[^a-z0-9]+", "", (skin or "default").lower())
    rel = Path("icons") / f"{slug}-{label}.png"
    dest = src.parent / rel
    dest.parent.mkdir(parents=True, exist_ok=True)
    image.save(dest, "PNG")
    rel_s = rel.as_posix()
    if slot == "character":
        data.setdefault("appearance", {})["icon"] = rel_s
    elif slot == "weapon":
        data.setdefault("weapon", {})["icon"] = rel_s
    elif slot == "passive":
        data.setdefault("passive", {})["icon"] = rel_s
    else:
        skins = data.get("skins") or []
        match = None
        for entry in skins:
            if isinstance(entry, dict) and (skin is None or entry.get("name") == skin):
                match = entry
                break
        if match is None:
            raise MegabonkError(f"no skin named {skin!r}" if skin else "the character has no skins")
        match["icon"] = rel_s
    dump_yaml(src, data)
    note = ""
    if image.size[0] not in (128, 256) or image.size[0] != image.size[1]:
        note = f" (imported at {image.size[0]}x{image.size[1]}; published icons are 128 or 256 square)"
    return rel_s + note


def _inside(child: Path, parent: Path) -> bool:
    try:
        child.resolve().relative_to(parent.resolve())
        return True
    except ValueError:
        return False


def _game_index(game: Path):
    from um.scan import Index
    return Index(game)


def doctor(game: Path) -> dict:
    """Read-only report of an install folder. Does not create or change files."""
    report = {
        "game": str(game),
        "looks_like_unity_il2cpp": False,
        "looks_like_megabonk": False,
        "unity_version": None,
        "app_info": None,
        "loader_dlls": [],
        "characters": [],
        "blockers": [],
        "notes": [
            "In-game load was not run from this environment.",
            "A UnityFS header check is not a successful AssetBundle load.",
        ],
    }
    if not game.is_dir():
        report["blockers"].append(f"not a folder: {game}")
        return report
    ix = _game_index(game)
    report["looks_like_unity_il2cpp"] = ix.has("gameassembly.dll") and ix.has("*_data/il2cpp_data/metadata/global-metadata.dat")
    if not ix.has("gameassembly.dll"):
        report["blockers"].append("missing GameAssembly.dll (IL2CPP player)")
    if not ix.has("*_data/il2cpp_data/metadata/global-metadata.dat"):
        report["blockers"].append("missing *_Data/il2cpp_data/Metadata/global-metadata.dat")
    data_dirs = {p.split("/")[0] for p in ix.find("*_data/app.info", "*_data/globalgamemanagers")}
    from um.scan import unity_version
    for data_dir in sorted(data_dirs):
        version = unity_version(ix, data_dir)
        if version and not report["unity_version"]:
            report["unity_version"] = version
        info = ix.path(f"{data_dir}/app.info")
        if info.is_file() and report["app_info"] is None:
            report["app_info"] = info.read_text(encoding="utf-8", errors="replace").strip()
    if report["unity_version"] and report["unity_version"] != "2023.2.22f1":
        report["notes"].append(
            f"Unity version string is {report['unity_version']}. Public character mods name 2023.2.22f1. "
            "A bundle built for a different editor does not load."
        )
    elif not report["unity_version"]:
        report["notes"].append("Unity version string was not found in globalgamemanagers.")
    blob = (report["app_info"] or "").lower()
    exes = ix.find("*.exe")
    report["looks_like_megabonk"] = "megabonk" in blob or any("megabonk" in name for name in exes)
    if report["looks_like_unity_il2cpp"] and not report["looks_like_megabonk"]:
        report["notes"].append("This folder looks like Unity IL2CPP, and app.info/exe did not say Megabonk.")
    if any(ix.has(pat) or ix.has_dir(pat) for pat in ANTICHEAT_GLOBS):
        report["blockers"].append("anti-cheat files are in this folder; this tool will not install into it")
    if not ix.has_dir("bepinex/plugins"):
        report["blockers"].append("BepInEx/plugins is missing. Install BepInExPack_IL2CPP 6.0.738 yourself first.")
    report["loader_dlls"] = ix.find("bepinex/plugins/**/*customcharacter*.dll") + ix.find("bepinex/plugins/*customcharacter*.dll")
    # find() uses fnmatch, which does not treat ** specially. Fall back to a suffix scan.
    if not report["loader_dlls"]:
        report["loader_dlls"] = [
            rel for rel in ix.files
            if rel.startswith("bepinex/plugins/") and rel.endswith(".dll") and "customcharacter" in rel
        ]
    if not report["loader_dlls"]:
        report["blockers"].append(
            "no CustomCharacter loader DLL under BepInEx/plugins. "
            "Public reports say to use Shadowth117 CustomCharacterCreator_Shadowth117Fix 1.4.1, not 1.4.0."
        )
    chars = game / "BepInEx" / "plugins" / "CustomCharacters"
    if chars.is_dir():
        for json_path in sorted(chars.glob("*.json")):
            bundle = _bundle_for(json_path)
            entry = {"json": json_path.name, "bundle": bundle.name, "bundle_exists": bundle.is_file(), "unityfs": False, "id": None}
            if bundle.is_file():
                entry["unityfs"] = _unityfs(bundle)
            try:
                doc = json.loads(json_path.read_text(encoding="utf-8"))
                if isinstance(doc, dict) and isinstance(doc.get("character"), dict):
                    entry["id"] = doc["character"].get("eCharacter")
            except (OSError, json.JSONDecodeError):
                entry["error"] = "json could not be parsed"
            report["characters"].append(entry)
            if not entry["bundle_exists"]:
                report["blockers"].append(f"{json_path.name} has no sibling bundle {bundle.name}; the loader opens that path during DataManager.Load")
            elif not entry["unityfs"]:
                report["blockers"].append(f"{bundle.name} does not start with a UnityFS header")
    if not ix.has("bepinex/interop/assembly-csharp.dll"):
        report["notes"].append("BepInEx/interop/Assembly-CSharp.dll is absent (normal before the first modded boot).")
    return report


def format_doctor(report: dict) -> str:
    lines = [
        f"game: {report['game']}",
        f"unity il2cpp: {report['looks_like_unity_il2cpp']}",
        f"name looks like Megabonk: {report['looks_like_megabonk']}",
        f"unity version: {report['unity_version'] or 'unknown'}",
        f"loader dlls: {', '.join(report['loader_dlls']) or 'none'}",
    ]
    if report["characters"]:
        lines.append("character jsons:")
        for entry in report["characters"]:
            lines.append(
                f"  {entry['json']} id={entry.get('id')} bundle={entry['bundle']} "
                f"exists={entry['bundle_exists']} unityfs={entry['unityfs']}"
            )
    if report["blockers"]:
        lines.append("blockers:")
        lines.extend(f"  - {b}" for b in report["blockers"])
    if report["notes"]:
        lines.append("notes:")
        lines.extend(f"  - {n}" for n in report["notes"])
    return "\n".join(lines)


def _bundle_for(json_path: Path) -> Path:
    name = json_path.name
    if name.endswith(".custom.json"):
        return json_path.with_name(name[: -len(".custom.json")])
    return json_path.with_suffix("")


def _unityfs(path: Path) -> bool:
    try:
        with open(path, "rb") as f:
            head = f.read(len(UNITYFS))
        return head == UNITYFS and path.stat().st_size >= MIN_BUNDLE_BYTES
    except OSError:
        return False


def _manifest_path(game: Path) -> Path:
    return game / STATE_DIR / MANIFEST_NAME


def _load_manifest(game: Path) -> dict:
    path = _manifest_path(game)
    if not path.is_file():
        return {"version": 1, "characters": {}}
    doc = json.loads(path.read_text(encoding="utf-8"))
    if not isinstance(doc, dict) or not isinstance(doc.get("characters"), dict):
        raise MegabonkError(f"install manifest is not usable: {path}")
    return doc


def _save_manifest(game: Path, doc: dict) -> None:
    path = _manifest_path(game)
    path.parent.mkdir(parents=True, exist_ok=True)
    tmp = path.with_suffix(".json.tmp")
    tmp.write_text(json.dumps(doc, indent=2) + "\n", encoding="utf-8")
    tmp.replace(path)


def _assert_installable(game: Path) -> None:
    if not game.is_dir():
        raise MegabonkError(f"game folder does not exist: {game}")
    ix = _game_index(game)
    if any(ix.has(pat) or ix.has_dir(pat) for pat in ANTICHEAT_GLOBS):
        raise MegabonkError("anti-cheat files are in this folder; refusing to install")
    plugins = game / "BepInEx" / "plugins"
    if not plugins.is_dir():
        raise MegabonkError(
            "BepInEx/plugins is missing. Install BepInExPack_IL2CPP 6.0.738 into this game yourself, "
            "then rerun install. This tool does not install the loader."
        )
    if not _inside(plugins, game):
        raise MegabonkError("BepInEx/plugins resolves outside the game folder")


def _installed_ids(game: Path, manifest: dict) -> dict[int, str]:
    found = {}
    for key, entry in manifest.get("characters", {}).items():
        if isinstance(entry, dict) and isinstance(entry.get("id"), int):
            found[entry["id"]] = key
    chars = game / "BepInEx" / "plugins" / "CustomCharacters"
    if chars.is_dir():
        for json_path in chars.glob("*.json"):
            try:
                doc = json.loads(json_path.read_text(encoding="utf-8"))
                ident = doc["character"]["eCharacter"]
            except (OSError, json.JSONDecodeError, KeyError, TypeError):
                continue
            if isinstance(ident, int):
                found.setdefault(ident, json_path.name)
    return found


def install_pack(pack: Path, game: Path) -> dict:
    """Copy one character json and its UnityFS bundle into the game. Refuses a missing bundle."""
    _assert_installable(game)
    if not pack.is_dir():
        raise MegabonkError(f"pack folder does not exist: {pack}")
    jsons = [p for p in pack.glob("*.custom.json") if p.is_file()]
    if len(jsons) != 1:
        raise MegabonkError(f"pack must contain exactly one .custom.json (found {len(jsons)})")
    json_path = jsons[0]
    try:
        doc = json.loads(json_path.read_text(encoding="utf-8"))
    except json.JSONDecodeError as e:
        raise MegabonkError(f"invalid json: {e}") from e
    errors, warnings = validate_loader_json(doc)
    if errors:
        raise MegabonkError("refusing to install an invalid character json:\n" + "\n".join(f"- {e}" for e in errors))
    ident = int(doc["character"]["eCharacter"])
    bundle_path = _bundle_for(json_path)
    if not bundle_path.is_file():
        raise MegabonkError(
            f"missing AssetBundle {bundle_path.name} next to {json_path.name}. "
            "The loader opens that file during DataManager.Load. Build it with Unity 2023.2.22f1 "
            "(see `um megabonk prerequisites`). Nothing was copied into the game."
        )
    if not _unityfs(bundle_path):
        raise MegabonkError(
            f"{bundle_path.name} does not start with a UnityFS header of at least {MIN_BUNDLE_BYTES} bytes. "
            "A text placeholder is not an AssetBundle. Nothing was copied into the game."
        )
    manifest = _load_manifest(game)
    known = _installed_ids(game, manifest)
    owner = known.get(ident)
    if owner and owner != str(ident):
        raise MegabonkError(f"character id {ident} is already used by {owner}. Nothing was copied.")
    dest_dir = game / "BepInEx" / "plugins" / "CustomCharacters"
    dest_dir.mkdir(parents=True, exist_ok=True)
    if not _inside(dest_dir, game):
        raise MegabonkError("CustomCharacters resolves outside the game folder")
    planned = [(json_path, dest_dir / json_path.name), (bundle_path, dest_dir / bundle_path.name)]
    for _src, dest in planned:
        if dest.is_symlink() or (dest.exists() and not dest.is_file()):
            raise MegabonkError(f"refusing to replace {dest}")
        if not _inside(dest, game):
            raise MegabonkError(f"destination escapes the game folder: {dest}")
    backup_root = game / STATE_DIR / "backup" / str(ident)
    records = []
    rollbacks: list[tuple[Path, bytes | None]] = []
    try:
        for src, dest in planned:
            rel = dest.relative_to(game).as_posix()
            previous = None
            for entry in manifest["characters"].values():
                for rec in entry.get("files", []):
                    if rec.get("rel") == rel:
                        previous = rec
            action = "added"
            backup_rel = None
            if previous:
                action = previous.get("action", "added")
                backup_rel = previous.get("backup")
            elif dest.is_file():
                action = "replaced"
                backup_root.mkdir(parents=True, exist_ok=True)
                backup = backup_root / dest.name
                shutil.copy2(dest, backup)
                backup_rel = backup.relative_to(game).as_posix()
            prior = dest.read_bytes() if dest.is_file() else None
            shutil.copy2(src, dest)
            rollbacks.append((dest, prior))
            records.append({"rel": rel, "sha256": _sha256(dest), "action": action, "backup": backup_rel})
        manifest["characters"][str(ident)] = {
            "id": ident,
            "name": doc["character"].get("characterName"),
            "files": records,
        }
        _save_manifest(game, manifest)
    except Exception:
        for dest, prior in reversed(rollbacks):
            if prior is None:
                dest.unlink(missing_ok=True)
            else:
                dest.write_bytes(prior)
        raise
    return {
        "id": ident,
        "name": doc["character"].get("characterName"),
        "files": [r["rel"] for r in records],
        "warnings": warnings,
        "in_game_verified": False,
    }


INSTALL_NOTICE = """\
Installed files are recorded in BepInEx/um-megabonk/install.json.
This did not change saves, the registry, or the leaderboard switch.
Before launching:
  um backup create "%USERPROFILE%\\AppData\\LocalLow\\Ved\\Megabonk" --name megabonk-saves
  In game, set Upload score to Leaderboards to Off.
The character is not verified in Megabonk by this install. Confirm it in character select,
and check BepInEx/LogOutput.log for "Loaded Custom Character:".
Uninstall with: um megabonk uninstall --game <this folder>
"""


def uninstall(game: Path, ident: int | None = None, force: bool = False) -> list[str]:
    """Remove files this tool installed, and restore files it replaced. Does not remove BepInEx."""
    if not game.is_dir():
        raise MegabonkError(f"game folder does not exist: {game}")
    manifest = _load_manifest(game)
    keys = [str(ident)] if ident is not None else list(manifest.get("characters", {}))
    if ident is not None and str(ident) not in manifest["characters"]:
        raise MegabonkError(f"id {ident} is not in the install manifest")
    removed = []
    problems = []
    for key in keys:
        entry = manifest["characters"].get(key)
        if not entry:
            continue
        blocked = []
        for rec in entry.get("files", []):
            dest = game / rec["rel"]
            if not _inside(dest, game):
                blocked.append(f"skipping path outside the game folder: {rec['rel']}")
            elif dest.is_symlink():
                blocked.append(f"skipping symlink: {rec['rel']}")
            elif dest.is_file() and _sha256(dest) != rec.get("sha256") and not force:
                blocked.append(f"{rec['rel']} changed after install; left in place (rerun with --force to remove it)")
            elif rec.get("action") == "replaced" and rec.get("backup"):
                backup = game / rec["backup"]
                if not (backup.is_file() and _inside(backup, game)):
                    blocked.append(f"backup missing for {rec['rel']}; left the current file in place")
        if blocked and not force:
            problems.extend(blocked)
            continue
        entry_notes = []
        for rec in entry.get("files", []):
            dest = game / rec["rel"]
            if not dest.is_file() or dest.is_symlink() or not _inside(dest, game):
                entry_notes.append(f"already absent {rec['rel']}")
                continue
            if rec.get("action") == "replaced" and rec.get("backup"):
                backup = game / rec["backup"]
                if backup.is_file():
                    shutil.copy2(backup, dest)
                    entry_notes.append(f"restored {rec['rel']}")
                    continue
            dest.unlink()
            entry_notes.append(f"removed {rec['rel']}")
        removed.extend(entry_notes)
        del manifest["characters"][key]
    if problems and not force:
        _save_manifest(game, manifest)
        raise MegabonkError("uninstall stopped:\n" + "\n".join(problems))
    if manifest["characters"]:
        _save_manifest(game, manifest)
    else:
        path = _manifest_path(game)
        if path.is_file():
            path.unlink()
    return removed


def _print_validation(errors: list[str], warnings: list[str]) -> int:
    for w in warnings:
        print(f"warning: {w}")
    for e in errors:
        print(f"error: {e}")
    if errors:
        return 1
    print(f"ok ({len(warnings)} warning{'s' if len(warnings) != 1 else ''})")
    return 0


def main(a):
    try:
        if a.cmd == "prerequisites":
            print(prerequisites_text())
        elif a.cmd == "catalog":
            print(catalog_text())
        elif a.cmd == "new":
            data = new_character(Path(a.file), a.name, a.author, a.id)
            print(f"wrote {a.file} ({data['name']}, id {data['id']})")
        elif a.cmd == "validate":
            path = Path(a.path)
            if path.is_dir() or path.name.endswith(".json"):
                doc = json.loads(path.read_text(encoding="utf-8")) if path.is_file() else None
                if path.is_dir():
                    jsons = list(path.glob("*.custom.json"))
                    if len(jsons) != 1:
                        die(f"expected one .custom.json in {path}")
                    doc = json.loads(jsons[0].read_text(encoding="utf-8"))
                errors, warnings = validate_loader_json(doc)
            else:
                errors, warnings = validate_definition(load_yaml(path), path.parent)
            raise SystemExit(_print_validation(errors, warnings))
        elif a.cmd == "pack":
            result = pack_character(Path(a.src), Path(a.out))
            print(f"wrote {result['json']}")
            print(f"bundle file still required: {result['bundle_name']} (UnityFS, not created here)")
            for w in result["warnings"]:
                print(f"warning: {w}")
        elif a.cmd == "import-icon":
            note = import_icon(Path(a.file), Path(a.png), a.slot, a.skin, a.fit)
            print(f"icon set to {note}")
        elif a.cmd == "doctor":
            report = doctor(Path(a.game))
            print(json.dumps(report, indent=2) if a.as_json else format_doctor(report))
            raise SystemExit(1 if report["blockers"] else 0)
        elif a.cmd == "install":
            result = install_pack(Path(a.pack), Path(a.game))
            print(f"installed {result['name']} (id {result['id']})")
            for rel in result["files"]:
                print(f"  {rel}")
            for w in result["warnings"]:
                print(f"warning: {w}")
            print(INSTALL_NOTICE)
        elif a.cmd == "uninstall":
            removed = uninstall(Path(a.game), a.id, a.force)
            if not removed:
                print("nothing to remove")
            for line in removed:
                print(line)
            print("BepInEx was left in place. Saves were not read or written.")
        else:
            die("unknown megabonk command")
    except MegabonkError as e:
        die(str(e))


def register(sub):
    p = sub.add_parser(
        "megabonk",
        help="author Megabonk character packs for the custom-character loader",
        description=__doc__,
        formatter_class=argparse.RawDescriptionHelpFormatter,
    )
    cs = p.add_subparsers(dest="cmd", metavar="<cmd>")

    q = cs.add_parser("prerequisites", help="print the game files, loader versions and limits")
    q.set_defaults(func=main)
    q = cs.add_parser("catalog", help="print published stat, passive, character and weapon numbers")
    q.set_defaults(func=main)

    q = cs.add_parser("new", help="write a starter character definition")
    q.add_argument("file")
    q.add_argument("--name", default="Hero")
    q.add_argument("--author", default="local")
    q.add_argument("--id", type=int)
    q.set_defaults(func=main)

    q = cs.add_parser("validate", help="check a definition yaml or a packed .custom.json")
    q.add_argument("path")
    q.set_defaults(func=main)

    q = cs.add_parser("pack", help="write a loader .custom.json (no AssetBundle)")
    q.add_argument("src")
    q.add_argument("-o", "--out", required=True)
    q.set_defaults(func=main)

    q = cs.add_parser("import-icon", help="copy a PNG into the character and point a slot at it")
    q.add_argument("file")
    q.add_argument("--png", required=True)
    q.add_argument("--slot", required=True, choices=("character", "weapon", "passive", "skin"))
    q.add_argument("--skin", help="skin name when --slot skin")
    q.add_argument("--fit", type=int, help="nearest-neighbour resize to 128 or 256")
    q.set_defaults(func=main)

    q = cs.add_parser("doctor", help="read an install folder and report loader, bundles and blockers")
    q.add_argument("game")
    q.add_argument("--json", dest="as_json", action="store_true")
    q.set_defaults(func=main)

    q = cs.add_parser("install", help="copy a pack's json and UnityFS bundle into the game")
    q.add_argument("pack")
    q.add_argument("--game", required=True)
    q.set_defaults(func=main)

    q = cs.add_parser("uninstall", help="remove characters this tool installed and restore replaced files")
    q.add_argument("--game", required=True)
    q.add_argument("--id", type=int)
    q.add_argument("--force", action="store_true")
    q.set_defaults(func=main)
