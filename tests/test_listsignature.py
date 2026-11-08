"""Compile and run ListSignature against its own assembly. No game DLL."""
from __future__ import annotations

import os
import shutil
import subprocess
from pathlib import Path

import pytest

ROOT = Path(__file__).resolve().parents[1]
PROJECT = ROOT / "examples" / "megabonk-characters" / "plugin" / "tools" / "ListSignature"


def _dotnet() -> str | None:
    found = shutil.which("dotnet")
    if found:
        return found
    home = Path.home() / ".dotnet" / "dotnet"
    if home.is_file():
        return str(home)
    return None


@pytest.mark.skipif(_dotnet() is None, reason="dotnet SDK is not installed")
def test_listsignature_decodes_a_constructor_and_parameters():
    dotnet = _dotnet()
    env = os.environ.copy()
    env["DOTNET_ROOT"] = str(Path(dotnet).parent)
    env["DOTNET_CLI_HOME"] = str(Path.home() / ".dotnet")
    env["PATH"] = str(Path(dotnet).parent) + os.pathsep + env.get("PATH", "")
    built = subprocess.run(
        [dotnet, "build", str(PROJECT), "-v", "q", "--nologo"],
        cwd=ROOT,
        env=env,
        text=True,
        capture_output=True,
        check=False,
    )
    assert built.returncode == 0, built.stdout + built.stderr
    dll = next(PROJECT.glob("bin/**/ListSignature.dll"))
    dumped = subprocess.run(
        [dotnet, "run", "--project", str(PROJECT), "--no-build", "--", str(dll), "SignatureFixture"],
        cwd=ROOT,
        env=env,
        text=True,
        capture_output=True,
        check=False,
    )
    assert dumped.returncode == 0, dumped.stdout + dumped.stderr
    text = dumped.stdout
    assert "TYPE SignatureFixture" in text
    assert "CTOR public instance .ctor (Int32 count, String name) -> Void" in text
    assert "METHOD public static Sample (ref Int32 value, String[] names) -> Void" in text
    assert "signature UNKNOWN" not in text
