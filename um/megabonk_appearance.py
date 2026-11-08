"""Appearance edits that do not change combat fields.

A look is the vanilla body name, one whole-body tint, and whether that tint
is painted. Stats, passive, weapon, and id stay put until a separate new
character is requested.
"""
from __future__ import annotations

import copy
import re

from um.megabonk import CHARACTERS, MegabonkError
from um.megabonk_studio import allocate_id, validate_studio

TINT = re.compile(r"^#[0-9A-Fa-f]{6}$")
HEX_DIGITS = re.compile(r"^[0-9A-Fa-f]{6}$")


def normalize_hex(text: str) -> str | None:
    raw = (text or "").strip()
    if HEX_DIGITS.match(raw):
        raw = "#" + raw
    if not TINT.match(raw):
        return None
    return "#" + raw[1:].upper()


def snapshot(row: dict) -> dict:
    return {
        "appearance": row["appearance"],
        "tint": row.get("tint") or "",
        "paint": bool(row.get("paint", False)),
    }


def apply_look(row: dict, draft: dict) -> dict:
    """Return a copy. Combat fields and id match the source row."""
    _check_draft(draft)
    out = copy.deepcopy(row)
    out["appearance"] = draft["appearance"]
    out["tint"] = draft.get("tint") or ""
    out["paint"] = bool(draft.get("paint", False))
    return out


def apply_as_new(studio: dict, source_id: int, draft: dict) -> dict:
    """Return a new studio file. The source row is not modified."""
    _check_draft(draft)
    studio = copy.deepcopy(studio)
    source = None
    for row in studio["characters"]:
        if int(row["id"]) == int(source_id):
            source = row
            break
    if source is None:
        raise MegabonkError(f"no character with id {source_id}")
    made = copy.deepcopy(source)
    made["id"] = allocate_id(studio)
    made["appearance"] = draft["appearance"]
    made["tint"] = draft.get("tint") or ""
    made["paint"] = bool(draft.get("paint", False))
    studio["characters"].append(made)
    errors, _warnings = validate_studio(studio)
    if errors:
        raise MegabonkError("refusing to add the look:\n" + "\n".join(errors))
    return studio


def validate_preset(preset: dict, names: list[str] | None = None) -> str | None:
    if not isinstance(preset, dict):
        return "preset must be an object"
    allowed = {"name", "appearance", "tint", "paint"}
    extra = sorted(set(preset) - allowed)
    if extra:
        return "unknown preset fields: " + ", ".join(extra)
    name = preset.get("name")
    if not isinstance(name, str) or not name.strip() or len(name.strip()) > 40 or "\n" in name:
        return "preset name must be 1 to 40 characters on one line"
    if names is not None and name.strip() in names:
        return "a preset with that name is already saved"
    if preset.get("appearance") not in set(CHARACTERS.values()):
        return "appearance is not a published body"
    tint = preset.get("tint") or ""
    paint = preset.get("paint", False)
    if paint not in (True, False):
        return "paint must be true or false"
    if tint not in ("",) and not (isinstance(tint, str) and TINT.match(tint)):
        return "tint must be empty or #RRGGBB"
    if paint and not TINT.match(tint):
        return "paint requires tint #RRGGBB"
    return None


def _check_draft(draft: dict) -> None:
    if draft.get("appearance") not in set(CHARACTERS.values()):
        raise MegabonkError("appearance is not a published body")
    tint = draft.get("tint") or ""
    paint = bool(draft.get("paint", False))
    if tint not in ("",) and not TINT.match(tint):
        raise MegabonkError("tint must be empty or #RRGGBB")
    if paint and not TINT.match(tint):
        raise MegabonkError("paint requires tint #RRGGBB")
