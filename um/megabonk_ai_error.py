"""Map an OpenAI HTTP error to a fixed status line.

The wording comes from the published error-code and rate-limit guides.
The provider's message, headers, and any other text are not copied through.
"""
from __future__ import annotations

import json
import re

# https://platform.openai.com/docs/guides/error-codes
# https://platform.openai.com/docs/guides/rate-limits
_RATE_CODES = {"rate_limit_exceeded", "slow_down"}
_QUOTA_CODES = {
    "insufficient_quota",
    "credit_balance_exhausted",
    "organization_spend_limit_exceeded",
    "project_spend_limit_exceeded",
    "organization_usage_limit_exceeded",
}
_OVERLOAD_CODES = {"server_is_overloaded"}
_RATE_TYPES = {"rate_limit_error"}
_QUOTA_TYPES = {"insufficient_quota"}
_OVERLOAD_TYPES = {"service_unavailable_error"}
_TOKEN = re.compile(r"^[a-z_]{1,64}$")
_SECONDS = re.compile(r"^[0-9]{1,5}$")
_LOCAL_WAIT = 15


def explain_provider_failure(status: int, body: str | None, retry_after: str | None) -> dict:
    status = int(status)
    if status == 401:
        return _result(
            "auth",
            "The model server did not accept the request (401). Nothing was saved. Your prompt is still here.",
            0,
        )
    kind, ident = _classify(body)
    if kind == "quota":
        return _result(
            "quota",
            f"The account limit {ident} was reached. Sending again will not fix it. "
            "Your prompt is still here. Nothing was saved.",
            0,
        )
    if kind == "rate":
        wait = _retry_seconds(retry_after) or _LOCAL_WAIT
        return _result(
            "rate",
            f"The model service is rate limiting requests ({ident}). Wait {wait} seconds, "
            "then send again if you want. Your prompt is still here. Nothing was saved.",
            wait,
        )
    if kind == "overload":
        wait = _retry_seconds(retry_after) or _LOCAL_WAIT
        return _result(
            "overload",
            f"The model is temporarily overloaded ({ident}). Wait {wait} seconds, "
            "then send again if you want. Your prompt is still here. Nothing was saved.",
            wait,
        )
    if status == 429:
        return _result(
            "status",
            "The model server returned 429. No recognized limit code was included, "
            "so this is not treated as a billing limit. Nothing was saved. Your prompt is still here.",
            0,
        )
    return _result(
        "status",
        f"The model server returned {status}. Nothing was saved. Your prompt is still here.",
        0,
    )


def _result(kind: str, text: str, pause: int) -> dict:
    return {"kind": kind, "text": text, "pause_seconds": pause}


def _classify(body: str | None) -> tuple[str, str | None]:
    code, typ = _tokens(body)
    if code in _QUOTA_CODES:
        return "quota", code
    if code in _RATE_CODES:
        return "rate", code
    if code in _OVERLOAD_CODES:
        return "overload", code
    if typ in _QUOTA_TYPES:
        return "quota", typ
    if typ in _RATE_TYPES:
        return "rate", typ
    if typ in _OVERLOAD_TYPES:
        return "overload", typ
    return "status", None


def _tokens(body: str | None) -> tuple[str | None, str | None]:
    if not body or len(body) > 4096:
        return None, None
    try:
        data = json.loads(body)
    except json.JSONDecodeError:
        return None, None
    if not isinstance(data, dict):
        return None, None
    error = data.get("error")
    if not isinstance(error, dict):
        return None, None
    return _allow(error.get("code")), _allow(error.get("type"))


def _allow(value) -> str | None:
    if not isinstance(value, str) or not _TOKEN.match(value):
        return None
    if value in _RATE_CODES or value in _QUOTA_CODES or value in _OVERLOAD_CODES:
        return value
    if value in _RATE_TYPES or value in _QUOTA_TYPES or value in _OVERLOAD_TYPES:
        return value
    return None


def _retry_seconds(header: str | None) -> int | None:
    if not isinstance(header, str) or not _SECONDS.fullmatch(header.strip()):
        return None
    seconds = int(header.strip())
    if seconds < 1 or seconds > 3600:
        return None
    return seconds
