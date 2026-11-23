UNDERTONE THAI MOD - SOURCE CODE FOR NEXUS MODS REVIEW
Prepared: 22 November 2026 (Asia/Bangkok)

PURPOSE AND IDENTITY
This package contains the original C# source files, assets, and expanded embedded
payload used by the current Undertone_ThaiMod.exe. These are the build sources,
not decompiled replacements. See RELEASE_IDENTITIES.json for SHA-256 hashes.
The user reports that version 1.3 was accepted, while the current upload was
quarantined. A screenshot reports 8/67 detections with MSILHeracles labels.
This is a request for independent review, not proof that a detection is false.
The screenshot ZIP and current local distribution have different hashes; the
identities file explicitly separates them.

CONTENTS
src/Installer.cs       Installer entry point, Steam discovery, ASAR patch writer.
src/ModernUI.cs        Windows Forms user interface.
src/DynamicPatcher.cs  Updates relevant JavaScript/HTML in the game's ASAR.
assets/               Original icon, UI logo and application manifest.
payload/asar_patch/    Thai data tables, translated images and font files.
payload/patch_meta/    Readable JavaScript/HTML hooks and localization mappings.
payload_zip_metadata.json  Original archive metadata for reconstructing payload.
build.py              Self-contained offline compilation script.
SHA256SUMS.txt         Checksums for all review input files.
comparison/           Source differences against the supplied version 1.3 EXE.

The archive contains no installer EXE and no nested archive. Its embedded payload
has been expanded to make all scripts and data directly available for review.
The build script recreates payload.zip locally and verifies its SHA-256 against
the exact embedded resource before compiling. No game installation is needed
for compilation, and no source checkout or package download is performed by it.

BUILD REQUIREMENTS
Windows; Python 3.11; .NET SDK 8.0; .NET Framework 4.8 Developer/Targeting Pack.
The original local build used Python and zlib versions in BUILD_ENVIRONMENT.json
and Roslyn from SDK 8.0.428. Install prerequisites separately before building.

BUILD STEPS (PowerShell, from the extracted source directory)
  python build.py --verify-only
  python build.py

If prerequisites are installed in nonstandard locations:
  python build.py --sdk "C:\Program Files\dotnet\sdk\8.0.428" --references "C:\Program Files (x86)\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.8"

Output: build/Undertone_ThaiMod.exe
The script does not run the resulting installer or modify any game files.
The original compiler invocation was not deterministic. PE timestamps and MVIDs
may differ between builds; source, IL and embedded resource comparison should
be used in addition to hashes. Existing assembly version/date attributes are
preserved exactly from the release sources even where they retain older dates.

BEHAVIOR RELEVANT TO SECURITY REVIEW
The installer reads Steam installation registry keys/library metadata, checks
whether the game process is running, and replaces relevant game ASAR contents
using a temporary rebuilt ASAR. Please inspect the actual backup/replacement
logic in Installer.cs; this document does not claim a full-game rollback exists.
The manifest requests asInvoker, not administrator elevation.
The Donate button opens the declared Google Sites donation URL on user click.
The embedded game main-process hook invokes PowerShell to enumerate installed
Windows font names for the font picker. Its arguments include -ExecutionPolicy
Bypass; the exact script is readable in payload/patch_meta/main_region.js.
Base64 in that hook transports the local font-list JSON back to the game.
The game preload hook exposes that font-list IPC call to the game renderer.
Some injected strings are generated as JavaScript Unicode escapes for Thai.
The build includes no obfuscator or executable packer and performs no signing.

COMPARISON WITH VERSION 1.3
Independent decompilation of the two supplied EXEs found the installer engine
and UI code unchanged. The code differences are in DynamicPatcher: compatibility
with the current game's readable JS, subtitle-name fallback, and exit handling.
Both embedded payloads contain the same 27 filenames. Only th_subtitles.json
and patch_meta/main_region.js differ; the latter adds game-exit handlers.
See comparison/source_diff.txt and comparison/payload_changes.json.

VERIFICATION LIMITS
The sources, reconstructed resource and compiled IL can be compared locally.
No claim is made that this source package itself has passed VirusTotal or that
all detections have been resolved. No file was submitted to any external service
while preparing this package.
