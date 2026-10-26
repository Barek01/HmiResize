# CLAUDE.md

Guidance for Claude Code (and any agent) working on this repository.

## What this is

HmiResize rescales every screen of a Siemens **WinCC Unified** HMI to a new panel
resolution (e.g. MTP700 800x480 → MTP1200 1280x800), directly in the project open in
**TIA Portal**, through the **TIA Portal Openness** API. WinCC Comfort/Advanced (non-Unified)
is not supported.

- `src/HmiResize.cs`: the engine, a console app (`HmiResize.exe`). All Openness work lives here.
- `src/HmiResizeGui.cs`: a Windows Forms GUI (`HmiResizeGui.exe`) that only builds a command
  line, runs `HmiResize.exe` and streams its output. No Openness reference.
- `build.bat [version]`: builds both (default TIA Portal V21).
- `README.md` (English) / `README.fr.md` (French): user documentation; keep both in sync.

## Build

```
build.bat        (or: build.bat 21)
```

- Uses `%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe`: **C# 5 only** (no string
  interpolation, `?.`, `nameof`, expression-bodied members, `out var`...). No .NET SDK on the dev PC.
- Openness references come from `Portal V<ver>\PublicAPI\V<ver>\net48` (Base, WinCCUnified) and
  `Portal V<ver>\Bin\PublicAPI` (Contract, ClientAdapter.Interfaces). They are **not** copied next
  to the exe; `InitAssemblyResolver` loads them at runtime (Siemens registry key first, then the
  default install paths). Keep `Run()` behind `[MethodImpl(NoInlining)]` so no Siemens type is
  JIT-compiled before the resolver is registered.
- `*.exe` are git-ignored; binaries are published as GitHub Release assets only.

## Running / testing

Requires TIA Portal running with a project open, and the Windows user in the local group
**Siemens TIA Openness**. Every rebuilt exe has a new hash, so TIA Portal shows its Openness
access dialog again: a human must answer "Yes to all", otherwise attach fails with
"Security error ... The operation has timed out".

Safe, read-only commands to use while developing:

```
HmiResize.exe --list                               HMIs and screen sizes
HmiResize.exe --from MTP700 --to MTP1200 --dry-run report only, modifies nothing
HmiResize.exe --dump "Screen1;Screen2"             all items, attributes and scripts of screens
HmiResize.exe --compile                            compile the HMI, print errors/warnings
```

A real run (`--from X --to Y`) modifies the open project inside one `ExclusiveAccess` +
`Transaction`: one Ctrl+Z in TIA Portal reverts it. **Running it twice scales twice.**
Only test real runs on a copy of a project, and never save (`--save`, `--save-only`) unless asked.

## How the engine works

1. Attach to the TIA Portal process, find `HmiSoftware` (Unified) through
   `DeviceItem.GetService<SoftwareContainer>()`, collect screens recursively through screen groups.
2. Factors: `sx = toW/fromW`, `sy = toH/fromH` (`uniform` mode uses the min for both);
   `sk = min(sx, sy)` for radii/lines; font factor `auto` = `sk`.
3. Screen size: full-size screens → target size; screens shown in a screen window (matched by
   the window's `Screen` attribute, then by identical size) → exact new window size; others scaled.
4. Items are handled generically through `IEngineeringObject.GetAttribute/SetAttributes`:
   box items scale **edges** (left and right edge), not width, so adjacent items stay adjacent;
   lines use X1/Y1/X2/Y2; polylines/polygons scale `Points`; centric shapes scale center and radii.
5. Sub-objects ("parts": fonts, padding, columns, row height, toolbars) are discovered by
   **.NET reflection on the API types**, then read with typed property access.
6. `OpenScreenInPopup(..., left, top, ...)` calls in script dynamizations and event handlers get
   their numeric 5th/6th arguments rescaled; the line is tagged `/*HmiResize*/`.
7. Anything that cannot be adapted safely is reported with `Warn(...)` instead of being guessed.

## Openness pitfalls (TIA Portal V21)

- **Never blanket-read every attribute of Unified controls.** Reading
  `HmiSystemDiagnosisControl.ScriptDiagnosisView` crashes TIA Portal entirely. Types and properties
  to avoid are listed in `NoPartsTypes` / `NoReadProps`; add to them rather than removing the
  safeguard. Prefer reflecting on the API offline before reading a new attribute live.
- `HmiSoftware` has no `GetService`; the `ICompilable` service is found by walking up from
  `SoftwareContainer.OwnedBy` through `Parent`.
- Attribute values must be written back with their exact .NET type (`Int32`, `UInt32`, `Byte`,
  `Single`...): use `FromD`.
- Enumerating a composition while TIA Portal dies raises `EngineeringObjectDisposedException`;
  check that TIA Portal is still running before blaming the code.
- Polylines/polygons are implemented but have not been tested on a real project yet.

## Conventions

- User-facing text (GUI, console messages, help) is in **English**. Code comments are in French;
  keep that style in existing files.
- Match the existing code style: one file per program, small helper methods, no external packages.
- New command-line options: add them to `Options.Parse`, `PrintHelp`, the GUI (if relevant) and
  both READMEs.
- **This repository is public: never commit customer project names, HMI/device/screen names from
  real projects, logs, dumps, screenshots showing anything other than the tool, or personal data.**
  Commits use the GitHub noreply address.

## Releases

1. Build with `build.bat`, check the exes contain no local paths or personal strings.
2. Zip `HmiResize.exe`, `HmiResizeGui.exe`, `README.md`, `README.fr.md`, `LICENSE` as
   `HmiResize-vX.Y.Z-win-x64.zip`.
3. `gh release create vX.Y.Z <zip> HmiResize.exe HmiResizeGui.exe --notes-file ...`
