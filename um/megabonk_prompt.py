"""Keyboard buffer and create/edit flow for the Megabonk studio.

The game draws this buffer itself. It does not call the stripped text-box API.
`examples/megabonk-characters/plugin/PromptBuffer.cs` and `StudioSession.cs` follow
these same transitions. Tests cover this module. They do not call OpenAI.
"""
from __future__ import annotations


def _clamp(value: int, limit: int) -> int:
    if value < 0:
        return 0
    if value > limit:
        return limit
    return value


def _line_bounds(text: str, index: int) -> tuple[int, int]:
    start = text.rfind("\n", 0, index) + 1
    end = text.find("\n", index)
    if end < 0:
        end = len(text)
    return start, end


def _vertical(text: str, caret: int, direction: int) -> int:
    start, end = _line_bounds(text, caret)
    column = caret - start
    if direction < 0:
        if start == 0:
            return 0
        previous_end = start - 1
        previous_start = text.rfind("\n", 0, previous_end) + 1
        return min(previous_start + column, previous_end)
    if end >= len(text):
        return len(text)
    next_start = end + 1
    next_end = text.find("\n", next_start)
    if next_end < 0:
        next_end = len(text)
    return min(next_start + column, next_end)


def _word_left(text: str, caret: int) -> int:
    i = caret
    if i > 0 and text[i - 1] == "\n":
        return i - 1
    while i > 0 and text[i - 1].isspace() and text[i - 1] != "\n":
        i -= 1
    while i > 0 and not text[i - 1].isspace():
        i -= 1
    return i


def _word_right(text: str, caret: int) -> int:
    i = caret
    n = len(text)
    if i < n and text[i] == "\n":
        return i + 1
    while i < n and not text[i].isspace():
        i += 1
    while i < n and text[i].isspace() and text[i] != "\n":
        i += 1
    return i


class PromptBuffer:
    def __init__(self, text: str = "", caret: int | None = None, mark: int | None = None):
        self.text = text or ""
        self.caret = len(self.text) if caret is None else _clamp(caret, len(self.text))
        self.mark = self.caret if mark is None else _clamp(mark, len(self.text))

    def selection(self) -> tuple[int, int]:
        return (self.caret, self.mark) if self.caret < self.mark else (self.mark, self.caret)

    def selected_text(self) -> str:
        start, end = self.selection()
        return self.text[start:end]

    def insert(self, value: str) -> None:
        value = value or ""
        start, end = self.selection()
        self.text = self.text[:start] + value + self.text[end:]
        self.caret = self.mark = start + len(value)

    def backspace(self) -> None:
        if self.caret != self.mark:
            self.insert("")
            return
        if self.caret <= 0:
            return
        self.text = self.text[: self.caret - 1] + self.text[self.caret :]
        self.caret = self.mark = self.caret - 1

    def delete(self) -> None:
        if self.caret != self.mark:
            self.insert("")
            return
        if self.caret >= len(self.text):
            return
        self.text = self.text[: self.caret] + self.text[self.caret + 1 :]

    def _place(self, index: int, extend: bool) -> None:
        self.caret = _clamp(index, len(self.text))
        if not extend:
            self.mark = self.caret

    def move(self, kind: str, extend: bool = False, ctrl: bool = False) -> None:
        if not extend and self.caret != self.mark and kind in ("left", "right") and not ctrl:
            start, end = self.selection()
            self.caret = self.mark = start if kind == "left" else end
            return
        if kind == "left":
            nxt = _word_left(self.text, self.caret) if ctrl else self.caret - 1
        elif kind == "right":
            nxt = _word_right(self.text, self.caret) if ctrl else self.caret + 1
        elif kind == "up":
            nxt = _vertical(self.text, self.caret, -1)
        elif kind == "down":
            nxt = _vertical(self.text, self.caret, 1)
        elif kind == "home":
            nxt = 0 if ctrl else _line_bounds(self.text, self.caret)[0]
        elif kind == "end":
            nxt = len(self.text) if ctrl else _line_bounds(self.text, self.caret)[1]
        else:
            return
        self._place(nxt, extend)

    def select_all(self) -> None:
        self.mark = 0
        self.caret = len(self.text)

    def set_caret(self, index: int, extend: bool = False) -> None:
        self._place(index, extend)


def apply_key(buf: PromptBuffer, key: str, character: str = "", shift: bool = False, ctrl: bool = False, clipboard: str = "") -> str | None:
    """Apply one physical key. Returns text that should replace the clipboard, or None.

    `key` is backspace, delete, left, right, up, down, home, end, return, char, a, c, x, or v.
    """
    if ctrl and key in ("a", "c", "x", "v"):
        if key == "a":
            buf.select_all()
            return None
        if key == "c":
            return buf.selected_text()
        if key == "x":
            taken = buf.selected_text()
            if taken:
                buf.insert("")
            return taken
        buf.insert(clipboard or "")
        return None
    if key == "backspace":
        buf.backspace()
    elif key == "delete":
        buf.delete()
    elif key in ("left", "right", "up", "down", "home", "end"):
        buf.move(key, extend=shift, ctrl=ctrl)
    elif key == "return":
        buf.insert("\n")
    elif key == "char" and character:
        buf.insert(character)
    return None


class StudioSession:
    """Which screen is up, and whether Apply creates or edits.

    Create never carries an edit id. Edit and refine never Apply as new.
    """

    def __init__(self):
        self.intent = "home"
        self.screen = "home"
        self.edit_id = None
        self.created_id = None

    def begin_create(self) -> None:
        self.intent = "create"
        self.screen = "create_prompt"
        self.edit_id = None

    def begin_edit(self, ident: int) -> None:
        self.intent = "edit"
        self.screen = "edit_home"
        self.edit_id = int(ident)

    def begin_refine(self) -> None:
        if self.created_id is None:
            raise ValueError("refine needs a character that was just created")
        self.intent = "refine"
        self.screen = "refine_prompt"
        self.edit_id = self.created_id

    def note_reply(self, kind: str) -> None:
        if self.intent == "create":
            self.screen = "create_questions" if kind == "questions" else "create_preview"
            return
        if self.intent in ("edit", "refine"):
            self.screen = "edit_questions" if kind == "questions" else "edit_preview"
            return
        raise ValueError("no prompt is in progress")

    def apply_target(self):
        if self.intent == "create" and self.screen == "create_preview":
            return None
        if self.intent in ("edit", "refine") and self.screen == "edit_preview":
            return self.edit_id
        raise ValueError("this screen does not apply")

    def note_created(self, ident: int) -> None:
        self.created_id = int(ident)
        self.screen = "refine_offer"
        self.intent = "home"
        self.edit_id = None


def describe_changes(before: dict, draft: dict) -> list[str]:
    labels = (
        ("name", "Name"),
        ("description", "Description"),
        ("author", "Author"),
        ("appearance", "Look"),
        ("passive", "Passive"),
        ("weapon", "Weapon"),
        ("tint", "Tint"),
    )
    lines = []
    for key, label in labels:
        old = before.get(key) or ""
        new = draft.get(key) or ""
        if old != new:
            lines.append(f"{label}: {old or '(empty)'} -> {new or '(empty)'}")
    old_stats = _stat_text(before.get("stats"))
    new_stats = _stat_text(draft.get("stats"))
    if old_stats != new_stats:
        lines.append(f"Stats: {old_stats} -> {new_stats}")
    if not lines:
        lines.append("No field differs from the current character.")
    return lines


def _stat_text(stats) -> str:
    if not stats:
        return "(none)"
    parts = []
    for row in stats:
        parts.append(f"{row.get('stat')} {row.get('modify')} {row.get('amount')}")
    return ", ".join(parts)
