"""Runtime character drafts for the Megabonk Studio plugin.

A draft is a vanilla character to clone (the appearance), plus a passive and a weapon that
already exist in the game, plus extra stat modifiers. The plugin clones CharacterData at
DataManager.Load. No Unity editor and no AssetBundle are involved. This module only checks
and edits the json. It does not talk to the game.
"""
from __future__ import annotations

import copy
import json
import re
from pathlib import Path

from um.megabonk import (
    CHARACTERS,
    MAX_ID,
    MIN_ID,
    MODIFY,
    PASSIVES,
    STATS,
    WEAPONS,
    MegabonkError,
    _id_problem,
    _num,
)

FORMAT = "um-megabonk-studio"
FORMAT_VERSION = 1
TINT = re.compile(r"^#[0-9A-Fa-f]{6}$")
_BY_CHAR = {name.lower(): name for name in CHARACTERS.values()}
_BY_PASSIVE = {name.lower(): name for name in PASSIVES}
_BY_WEAPON = {name.lower(): name for name in WEAPONS.values()}
_BY_STAT = {name.lower(): name for name in STATS}
_BY_MODIFY = {name.lower(): name for name in MODIFY}


def starter_file() -> dict:
    """Three selectable clones. Stats match the yaml examples and were not playtested."""
    return {
        "format": FORMAT,
        "format_version": FORMAT_VERSION,
        "characters": [
            {
                "id": 1842201001,
                "name": "Lumen",
                "description": "Cloned from Fox. Extra modifiers were not playtested.",
                "author": "local",
                "appearance": "Fox",
                "tint": "#FFD24A",
                "passive": "Float",
                "weapon": "FireStaff",
                "portrait": "",
                "stats": [
                    {"stat": "PickupRange", "modify": "Addition", "amount": 6},
                    {"stat": "MoveSpeedMultiplier", "modify": "Addition", "amount": 0.04},
                    {"stat": "MaxHealth", "modify": "Addition", "amount": -10},
                ],
            },
            {
                "id": 1842201002,
                "name": "Bulwark",
                "description": "Cloned from Megachad. Extra modifiers were not playtested.",
                "author": "local",
                "appearance": "Megachad",
                "tint": "",
                "passive": "Reinforced",
                "weapon": "Axe",
                "portrait": "",
                "stats": [
                    {"stat": "Armor", "modify": "Addition", "amount": 8},
                    {"stat": "MaxHealth", "modify": "Addition", "amount": 25},
                    {"stat": "MoveSpeedMultiplier", "modify": "Addition", "amount": -0.08},
                ],
            },
            {
                "id": 1842201003,
                "name": "Flicker",
                "description": "Cloned from Ninja. Extra modifiers were not playtested.",
                "author": "local",
                "appearance": "Ninja",
                "tint": "#E0C0FF",
                "passive": "CritHappens",
                "weapon": "Revolver",
                "portrait": "",
                "stats": [
                    {"stat": "CritChance", "modify": "Addition", "amount": 0.15},
                    {"stat": "MaxHealth", "modify": "Addition", "amount": -20},
                    {"stat": "Evasion", "modify": "Addition", "amount": 0.06},
                ],
            },
        ],
    }


def _canon(table: dict[str, str], value, label: str) -> str:
    if not isinstance(value, str) or not value.strip():
        raise MegabonkError(f"{label} is empty")
    found = table.get(value.strip().lower())
    if not found:
        raise MegabonkError(f"{label} {value!r} is not a published name")
    return found


def _optional(table: dict[str, str], value, label: str) -> str:
    if value is None or (isinstance(value, str) and not value.strip()):
        return ""
    return _canon(table, value, label)


def validate_studio(data: dict) -> tuple[list[str], list[str]]:
    errors: list[str] = []
    warnings: list[str] = []
    if not isinstance(data, dict):
        return ["studio file must be an object"], []
    if data.get("format") != FORMAT or data.get("format_version") != FORMAT_VERSION:
        errors.append(f"expected format {FORMAT} version {FORMAT_VERSION}")
        return errors, warnings
    rows = data.get("characters")
    if not isinstance(rows, list):
        return ["characters must be a list"], warnings
    if not rows:
        warnings.append("no characters yet; the plugin will register nothing")
    seen: set[int] = set()
    appearances: set[str] = set()
    for i, row in enumerate(rows):
        where = f"characters[{i}]"
        if not isinstance(row, dict):
            errors.append(f"{where} must be an object")
            continue
        problem = _id_problem(row.get("id"))
        if problem:
            errors.append(f"{where}: {problem}")
        else:
            ident = int(row["id"])
            if ident in seen:
                errors.append(f"{where}: duplicate id {ident}")
            seen.add(ident)
        for key in ("name", "description", "author"):
            if not isinstance(row.get(key), str) or not row[key].strip():
                errors.append(f"{where}.{key} must be a non-empty string")
        try:
            appearance = _canon(_BY_CHAR, row.get("appearance"), f"{where}.appearance")
            if appearance in appearances:
                warnings.append(f"{appearance} is cloned by more than one character; they will share that model")
            appearances.add(appearance)
        except MegabonkError as e:
            errors.append(str(e))
        tint = row.get("tint", "")
        if tint not in ("", None) and (not isinstance(tint, str) or not TINT.match(tint)):
            errors.append(f"{where}.tint must be empty or #RRGGBB")
        try:
            _optional(_BY_PASSIVE, row.get("passive", ""), f"{where}.passive")
            _optional(_BY_WEAPON, row.get("weapon", ""), f"{where}.weapon")
        except MegabonkError as e:
            errors.append(str(e))
        portrait = row.get("portrait", "")
        if portrait not in ("", None) and (not isinstance(portrait, str) or portrait.startswith(("/", "\\")) or ".." in portrait):
            errors.append(f"{where}.portrait must be empty or a relative file name")
        stats = row.get("stats", [])
        if not isinstance(stats, list):
            errors.append(f"{where}.stats must be a list")
            continue
        used: set[str] = set()
        for j, stat in enumerate(stats):
            label = f"{where}.stats[{j}]"
            if not isinstance(stat, dict):
                errors.append(f"{label} must be an object")
                continue
            try:
                name = _canon(_BY_STAT, stat.get("stat"), f"{label}.stat")
                _canon(_BY_MODIFY, stat.get("modify"), f"{label}.modify")
                _num(stat.get("amount"), f"{label}.amount")
            except MegabonkError as e:
                errors.append(str(e))
                continue
            if name in used:
                errors.append(f"{label}: {name} is listed twice")
            used.add(name)
            if name == "PickupRange":
                try:
                    if float(stat.get("amount")) >= 45:
                        warnings.append(f"{where}: PickupRange of 45 or more matches the maker sample bonus")
                except (TypeError, ValueError):
                    pass
    return errors, warnings


def load_studio(path: Path) -> dict:
    try:
        data = json.loads(path.read_text(encoding="utf-8"))
    except json.JSONDecodeError as e:
        raise MegabonkError(f"invalid json: {e}") from e
    errors, _warnings = validate_studio(data)
    if errors:
        raise MegabonkError("studio file is not valid:\n" + "\n".join(f"- {e}" for e in errors))
    return data


def save_studio(path: Path, data: dict) -> None:
    errors, _warnings = validate_studio(data)
    if errors:
        raise MegabonkError("refusing to write an invalid studio file:\n" + "\n".join(f"- {e}" for e in errors))
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(data, indent=2) + "\n", encoding="utf-8")


def _row(data: dict, ident: int) -> dict:
    for row in data["characters"]:
        if int(row["id"]) == ident:
            return row
    raise MegabonkError(f"no character with id {ident}")


def add_character(data: dict, name: str, appearance: str, author: str = "local", ident: int | None = None,
                  passive: str = "", weapon: str = "", description: str = "") -> dict:
    appearance = _canon(_BY_CHAR, appearance, "appearance")
    passive = _optional(_BY_PASSIVE, passive, "passive")
    weapon = _optional(_BY_WEAPON, weapon, "weapon")
    if ident is None:
        used = {int(row["id"]) for row in data.get("characters", [])}
        ident = max([MIN_ID, *used]) + 1 if used else 1842201100
        if ident < MIN_ID or ident > MAX_ID:
            raise MegabonkError(f"could not allocate an id (next would be {ident})")
    row = {
        "id": ident,
        "name": name.strip(),
        "description": description.strip() or f"Cloned from {appearance}.",
        "author": author.strip() or "local",
        "appearance": appearance,
        "tint": "",
        "passive": passive,
        "weapon": weapon,
        "portrait": "",
        "stats": [],
    }
    data.setdefault("characters", []).append(row)
    errors, _warnings = validate_studio(data)
    if errors:
        data["characters"].pop()
        raise MegabonkError("\n".join(errors))
    return row


def set_character(data: dict, ident: int, **changes) -> dict:
    original = _row(data, ident)
    row = copy.deepcopy(original)
    index = data["characters"].index(original)
    data["characters"][index] = row
    try:
        return _apply_changes(data, row, changes)
    except MegabonkError:
        data["characters"][index] = original
        raise


def _apply_changes(data: dict, row: dict, changes: dict) -> dict:
    for key in ("name", "description", "author"):
        if changes.get(key):
            row[key] = str(changes[key]).strip()
    if changes.get("appearance"):
        row["appearance"] = _canon(_BY_CHAR, changes["appearance"], "appearance")
    if "passive" in changes and changes["passive"] is not None:
        row["passive"] = _optional(_BY_PASSIVE, changes["passive"], "passive")
    if "weapon" in changes and changes["weapon"] is not None:
        row["weapon"] = _optional(_BY_WEAPON, changes["weapon"], "weapon")
    if changes.get("tint") is not None:
        tint = changes["tint"]
        if tint and not TINT.match(tint):
            raise MegabonkError("tint must be empty or #RRGGBB")
        row["tint"] = tint
    if changes.get("clear_stats"):
        row["stats"] = []
    for spec in changes.get("stats") or []:
        stat, modify, amount = spec.split(":", 2)
        entry = {"stat": _canon(_BY_STAT, stat, "stat"), "modify": _canon(_BY_MODIFY, modify, "modify"), "amount": _num(float(amount), "amount")}
        row["stats"] = [s for s in row["stats"] if s["stat"].lower() != entry["stat"].lower()]
        row["stats"].append(entry)
    errors, _warnings = validate_studio(data)
    if errors:
        raise MegabonkError("\n".join(errors))
    return row


CHARACTER_KEYS = {
    "name", "description", "author", "appearance", "tint", "passive", "weapon", "portrait", "stats",
}
STAT_KEYS = {"stat", "modify", "amount"}
MESSAGE_KEYS = {"type", "questions", "character"}


def _extract_typed_object(text: str) -> dict:
    raw = text.strip()
    if raw.startswith("```"):
        lines = raw.splitlines()
        if lines and lines[0].startswith("```"):
            lines = lines[1:]
        if lines and lines[-1].startswith("```"):
            lines = lines[:-1]
        raw = "\n".join(lines).strip()
    decoder = json.JSONDecoder()
    for i, ch in enumerate(raw):
        if ch != "{":
            continue
        try:
            obj, _end = decoder.raw_decode(raw[i:])
        except json.JSONDecodeError:
            continue
        if isinstance(obj, dict) and "type" in obj:
            return obj
    raise MegabonkError("model reply has no JSON object with a type field")


def _draft_errors(row: dict) -> list[str]:
    errors = []
    if not isinstance(row, dict):
        return ["character must be an object"]
    extra = sorted(set(row) - CHARACTER_KEYS)
    if extra:
        errors.append("unknown character fields: " + ", ".join(extra))
    if "id" in row:
        errors.append("the model must not choose an id")
    for key in ("name", "description", "author"):
        if not isinstance(row.get(key), str) or not row[key].strip():
            errors.append(f"{key} must be a non-empty string")
    try:
        _canon(_BY_CHAR, row.get("appearance"), "appearance")
    except MegabonkError as e:
        errors.append(str(e))
    tint = row.get("tint", "")
    if tint not in ("", None) and (not isinstance(tint, str) or not TINT.match(tint)):
        errors.append("tint must be empty or #RRGGBB")
    try:
        _optional(_BY_PASSIVE, row.get("passive", ""), "passive")
        _optional(_BY_WEAPON, row.get("weapon", ""), "weapon")
    except MegabonkError as e:
        errors.append(str(e))
    portrait = row.get("portrait", "")
    if portrait not in ("", None) and (
        not isinstance(portrait, str) or portrait.startswith(("/", "\\")) or ".." in portrait or "://" in portrait
    ):
        errors.append("portrait must be empty or a relative file name")
    stats = row.get("stats", [])
    if stats is None:
        stats = []
    if not isinstance(stats, list):
        errors.append("stats must be a list")
        return errors
    used = set()
    for i, stat in enumerate(stats):
        if not isinstance(stat, dict):
            errors.append(f"stats[{i}] must be an object")
            continue
        extra_s = sorted(set(stat) - STAT_KEYS)
        if extra_s:
            errors.append(f"stats[{i}] unknown fields: " + ", ".join(extra_s))
        try:
            name = _canon(_BY_STAT, stat.get("stat"), f"stats[{i}].stat")
            _canon(_BY_MODIFY, stat.get("modify"), f"stats[{i}].modify")
            _num(stat.get("amount"), f"stats[{i}].amount")
        except MegabonkError as e:
            errors.append(str(e))
            continue
        if name in used:
            errors.append(f"{name} is listed twice")
        used.add(name)
    return errors


def _normalize_draft(row: dict) -> dict:
    stats = []
    for stat in row.get("stats") or []:
        stats.append({
            "stat": _canon(_BY_STAT, stat["stat"], "stat"),
            "modify": _canon(_BY_MODIFY, stat["modify"], "modify"),
            "amount": _num(stat["amount"], "amount"),
        })
    return {
        "name": row["name"].strip(),
        "description": row["description"].strip(),
        "author": row["author"].strip(),
        "appearance": _canon(_BY_CHAR, row["appearance"], "appearance"),
        "tint": row.get("tint") or "",
        "passive": _optional(_BY_PASSIVE, row.get("passive", ""), "passive"),
        "weapon": _optional(_BY_WEAPON, row.get("weapon", ""), "weapon"),
        "portrait": row.get("portrait") or "",
        "stats": stats,
    }


def interpret_model_message(text: str, existing_ids: set[int] | None = None) -> dict:
    """Read one model reply. Does not contact a server and does not change a character file."""
    del existing_ids  # ids are assigned on apply; a model-supplied id is rejected below
    try:
        obj = _extract_typed_object(text)
    except MegabonkError as e:
        return {"ok": False, "error": str(e)}
    extra = sorted(set(obj) - MESSAGE_KEYS)
    if extra:
        return {"ok": False, "error": "unknown message fields: " + ", ".join(extra)}
    kind = obj.get("type")
    if kind == "questions":
        if "character" in obj:
            return {"ok": False, "error": "a question reply must not include a character"}
        questions = obj.get("questions")
        if not isinstance(questions, list) or not questions or len(questions) > 8:
            return {"ok": False, "error": "questions must be a list of 1 to 8 strings"}
        cleaned = []
        for i, q in enumerate(questions):
            if not isinstance(q, str) or not q.strip():
                return {"ok": False, "error": f"questions[{i}] must be a non-empty string"}
            cleaned.append(q.strip())
        return {"ok": True, "kind": "questions", "questions": cleaned}
    if kind == "draft":
        if "questions" in obj:
            return {"ok": False, "error": "a draft reply must not include questions"}
        errors = _draft_errors(obj.get("character"))
        if errors:
            return {"ok": False, "error": "; ".join(errors)}
        return {"ok": True, "kind": "draft", "character": _normalize_draft(obj["character"])}
    return {"ok": False, "error": "type must be questions or draft"}


def allocate_id(studio: dict) -> int:
    used = {int(row["id"]) for row in studio.get("characters") or []}
    ident = max([MIN_ID, *used]) + 1 if used else 1842201100
    if ident < MIN_ID or ident > MAX_ID:
        raise MegabonkError(f"could not allocate an id (next would be {ident})")
    return ident


def apply_model_draft(studio: dict, draft: dict, onto_id: int | None = None) -> dict:
    """Return a new studio file. The model draft must already have passed interpret_model_message."""
    studio = copy.deepcopy(studio)
    if onto_id is None:
        row = dict(draft)
        row["id"] = allocate_id(studio)
        row["stats"] = copy.deepcopy(draft.get("stats") or [])
        studio.setdefault("characters", []).append(row)
    else:
        target = None
        for row in studio["characters"]:
            if int(row["id"]) == onto_id:
                target = row
                break
        if target is None:
            raise MegabonkError(f"no character with id {onto_id}")
        kept = target["id"]
        target.clear()
        target.update(copy.deepcopy(draft))
        target["id"] = kept
    errors, _warnings = validate_studio(studio)
    if errors:
        raise MegabonkError("refusing to apply:\n" + "\n".join(errors))
    return studio


def push_snapshot(history: list, snapshot: str, limit: int = 20) -> list:
    return (list(history) + [snapshot])[-limit:]


def undo_snapshot(history: list) -> tuple[str, list]:
    if not history:
        raise MegabonkError("nothing to undo")
    return history[-1], list(history[:-1])


def format_studio(data: dict) -> str:
    lines = [f"{data.get('format')} v{data.get('format_version')}", ""]
    for row in data.get("characters") or []:
        stats = ", ".join(f"{s['stat']} {s['modify']} {s['amount']}" for s in row.get("stats") or []) or "(no extra stats)"
        lines.append(
            f"{row.get('name')} id={row.get('id')} look={row.get('appearance')} "
            f"passive={row.get('passive') or '(cloned)'} weapon={row.get('weapon') or '(cloned)'} tint={row.get('tint') or '(none)'}"
        )
        lines.append(f"  {stats}")
    if len(lines) == 2:
        lines.append("(no characters)")
    return "\n".join(lines)
