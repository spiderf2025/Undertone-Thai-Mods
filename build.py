#!/usr/bin/env python3
# -*- coding: utf-8 -*-
import argparse
import hashlib
import subprocess
import zipfile
from pathlib import Path

ROOT = Path(__file__).resolve().parent
SRC_DIR = ROOT / "src"
ASSETS_DIR = ROOT / "assets"
PAYLOAD_DIR = ROOT / "payload"
BUILD_DIR = ROOT / "build"

DEFAULT_REFS = Path(r"C:\Program Files (x86)\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.8")
DEFAULT_SDK_GLOB = Path(r"C:\Program Files\dotnet\sdk")


def find_csc(sdk_override: str | None) -> Path:
    if sdk_override:
        p = Path(sdk_override) / "Roslyn" / "bincore" / "csc.dll"
        if not p.is_file():
            raise FileNotFoundError(f"csc.dll not found at {p}")
        return p
    matches = sorted(DEFAULT_SDK_GLOB.glob("8.*/Roslyn/bincore/csc.dll"))
    if not matches:
        matches = sorted(DEFAULT_SDK_GLOB.glob("*/Roslyn/bincore/csc.dll"))
    if not matches:
        raise FileNotFoundError("Could not find Roslyn csc.dll under C:\\Program Files\\dotnet\\sdk")
    return matches[-1]


def build_payload_zip(out_zip: Path) -> int:
    count = 0
    with zipfile.ZipFile(out_zip, "w", compression=zipfile.ZIP_DEFLATED, compresslevel=6, allowZip64=True) as zf:
        for folder in ("asar_patch", "patch_meta"):
            base = PAYLOAD_DIR / folder
            for f in sorted(base.rglob("*")):
                if f.is_file():
                    rel = f.relative_to(PAYLOAD_DIR).as_posix()
                    zf.write(f, rel)
                    count += 1
    return count


def main():
    parser = argparse.ArgumentParser(description="Build Undertone_ThaiMod.exe from C# source files.")
    parser.add_argument("--sdk", default=None, help="Path to .NET SDK directory containing Roslyn/bincore/csc.dll")
    parser.add_argument("--references", default=str(DEFAULT_REFS), help="Path to .NET Framework 4.8 reference assemblies")
    parser.add_argument("--verify-only", action="store_true", help="Verify source files and payload without compiling")
    args = parser.parse_args()

    required = [
        SRC_DIR / "Installer.cs",
        SRC_DIR / "ModernUI.cs",
        SRC_DIR / "DynamicPatcher.cs",
        ASSETS_DIR / "app.ico",
        ASSETS_DIR / "app.manifest",
        ASSETS_DIR / "logo.png",
    ]
    for p in required:
        if not p.is_file():
            raise FileNotFoundError(f"Missing required source/asset file: {p}")

    BUILD_DIR.mkdir(parents=True, exist_ok=True)
    payload_zip = BUILD_DIR / "payload.zip"
    n_files = build_payload_zip(payload_zip)
    print(f"Packed {n_files} files into {payload_zip} ({payload_zip.stat().st_size:,} bytes)")

    if args.verify_only:
        print("Verify-only complete.")
        return

    csc_dll = find_csc(args.sdk)
    refs = Path(args.references)
    out_exe = BUILD_DIR / "Undertone_ThaiMod.exe"
    cmd = [
        "dotnet",
        str(csc_dll),
        "/nologo",
        "/target:winexe",
        "/platform:anycpu",
        "/optimize+",
        "/nostdlib+",
        f"/out:{out_exe}",
        f"/win32manifest:{ASSETS_DIR / 'app.manifest'}",
        f"/win32icon:{ASSETS_DIR / 'app.ico'}",
    ] + [
        f"/reference:{refs / dll}"
        for dll in (
            "mscorlib.dll",
            "System.dll",
            "System.Core.dll",
            "System.Drawing.dll",
            "System.Windows.Forms.dll",
            "System.Web.Extensions.dll",
            "System.IO.Compression.dll",
        )
    ] + [
        f"/resource:{payload_zip},payload.zip",
        f"/resource:{ASSETS_DIR / 'logo.png'},logo.png",
        f"/resource:{ASSETS_DIR / 'app.ico'},app.ico",
        str(SRC_DIR / "Installer.cs"),
        str(SRC_DIR / "ModernUI.cs"),
        str(SRC_DIR / "DynamicPatcher.cs"),
    ]
    subprocess.run(cmd, check=True, cwd=ROOT)
    with out_exe.open("rb") as f:
        sha = hashlib.file_digest(f, "sha256").hexdigest().upper()
    print(f"Built {out_exe} ({out_exe.stat().st_size / (1024 * 1024):.2f} MB, SHA256: {sha})")


if __name__ == "__main__":
    main()
