# HmiResize — resize WinCC Unified HMI screens to a new display resolution (TIA Portal Openness)

🇫🇷 [Version française](README.fr.md)

**HmiResize** automatically rescales every screen of a **Siemens SIMATIC WinCC Unified** HMI
(Unified Basic Panel, Unified Comfort Panel, PC Runtime) to a new display resolution,
directly in the project currently open in **TIA Portal**, using the **TIA Portal Openness** API.

Typical use case: an **MTP700 (800x480)** panel replaced by an **MTP1200 (1280x800)**.
After the device change in TIA Portal, screens keep their old size and the new display is
not filled. Instead of resizing hundreds of objects by hand, HmiResize scales screens,
screen items, fonts, control columns and even the pop-up coordinates written in scripts,
in a single operation that can be undone with one Ctrl+Z.

![HmiResize user interface](docs/screenshot.png)

## Features

- Attaches to the **TIA Portal instance already running** (no XML export/import).
- Resolutions by panel name (`MTP400`, `MTP700`, `MTP1000`, `MTP1200`, `MTP1500`,
  `MTP1900`, `MTP2200`) or in pixels (`1024x600`).
- **Stretch** mode (fill the whole display) or **uniform** mode (keep proportions).
- **Single transaction**: one Ctrl+Z in TIA Portal reverts everything.
- **Dry run** (`--dry-run`) to preview the changes.
- Report of the items that need a manual check.
- Compile the HMI and save the project from the tool.
- Simple **Windows GUI** (`HmiResizeGui.exe`) or **command line** (`HmiResize.exe`).

## Requirements

- Windows, **TIA Portal V21** with WinCC Unified (can be rebuilt for other versions, see below).
- The Windows user must be a member of the **Siemens TIA Openness** group (elevated prompt):
  ```
  net localgroup "Siemens TIA Openness" "%USERNAME%" /add
  ```
  then sign out of Windows and sign in again.
- No SDK needed: the C# compiler shipped with Windows (.NET Framework 4.8) is enough.

## Build

Run `build.bat` (or `build.bat 21` for TIA Portal V21). It produces:

- `HmiResize.exe`: the command-line engine;
- `HmiResizeGui.exe`: the GUI, which needs `HmiResize.exe` in the same folder.

The Openness assemblies are not copied: they are loaded from the TIA Portal installation
(path read from the Siemens registry keys).

## Usage

Open the project in TIA Portal first. On first run, TIA Portal asks to allow Openness
access: answer **Yes to all**.

### GUI

1. Start `HmiResizeGui.exe`.
2. Choose the source and target resolutions.
3. **Dry run**: shows what would change, without modifying anything.
4. **Resize screens**: performs the resize (asks for confirmation).
5. Check the result in TIA Portal, then **Compile HMI** and **Save project**.

Each button runs `HmiResize.exe` with the selected options; the report is shown live and
the command line is echoed, so it can be reused from a script.

### Command line

```
HmiResize.exe --list                                 list HMIs and screen sizes
HmiResize.exe --from MTP700 --to MTP1200 --dry-run   dry run, nothing is modified
HmiResize.exe --from MTP700 --to MTP1200             resize
HmiResize.exe --compile                              compile the HMI and show errors
HmiResize.exe --save-only                            save the project
```

| Option | Description |
|---|---|
| `--to <res>` | Target resolution (required): `1280x800` or a panel name |
| `--from <res>` | Source resolution. Default `auto` = most common screen size. **Set it explicitly** if TIA Portal already enlarged the screens during the device change |
| `--mode stretch` | Scale X and Y independently to fill the display (default) |
| `--mode uniform` | Same factor for X and Y: proportions kept, an empty band may remain |
| `--font auto` / `none` / `1.5` | Fonts × min(X factor, Y factor) (default), unchanged, or fixed factor |
| `--lines` | Also scale line and border widths |
| `--no-parts` | Do not touch control sub-elements (columns, toolbars, inner fonts) |
| `--no-scripts` | Do not rescale numeric coordinates in `OpenScreenInPopup` calls |
| `--device <name>` | HMI to process when the project contains several |
| `--screens "Screen1;Overview*"` | Only process these screens (wildcards `*` `?`) |
| `--exclude "Popup*"` | Screens to skip |
| `--save` | Save the project at the end |
| `--log report.txt` | Also write the report to a file |
| `--pid <n>` | TIA Portal process to use when several are running |
| `--compile` | Compile the HMI software, show errors/warnings, then exit |
| `--save-only` | Save the open project, then exit |
| `--dump "Screen1;Screen2"` | Diagnostics: print all items, properties and scripts of these screens |

## What gets scaled

- **Screens**: full-size screens get the target size. A screen shown in a screen window
  (header, footer, content area loaded with `ChangeScreen`) gets exactly the new size of
  that window, to the pixel. Other smaller screens (pop-ups) are scaled proportionally.
- **Box items** (buttons, I/O fields, texts, rectangles, controls, screen windows,
  faceplate containers…): position and size. Edges are scaled rather than widths, so
  adjacent items stay adjacent despite rounding.
- **Lines**: both end points. **Polylines and polygons**: origin and every point.
- **Circles, ellipses, arcs, segments**: center and radii.
- **Fonts** (`Font.Size`) and **padding**.
- **Control sub-elements**: column widths (alarm control, tables…), row height, toolbar
  elements and inner fonts.
- **Scripts**: in `OpenScreenInPopup(..., left, top, ...)` calls, numeric coordinates are
  scaled (`50, 50` → `80, 83`) and the line is tagged `/*HmiResize*/`. Coordinates passed as
  variables are reported.

## Manual checks (listed in the report)

- **Dynamized** position or size properties (tag or script).
- **Faceplate contents** (library types): only the container is resized.
- **Scripts** moving or resizing items (`.Left`, `.Top`, `.Width`, `.Height`, or
  `OpenScreenInPopup` with variable coordinates).
- Stretched **images** (graphic views).
- **System diagnosis control**: only its frame is resized, because reading some of its
  sub-elements crashes TIA Portal V21.
- **Polylines / polygons**: not yet tested on a real project.

⚠️ Running the tool again on screens already resized enlarges them again.
When in doubt, run a simulation first. Always work on a copy of your project.

## Tested on

TIA Portal V21, WinCC Unified Basic, MTP700 → MTP1200: 34 screens, 655 items,
34 pop-up calls rescaled, no error; the HMI compiles with 0 errors and 0 warnings.

## Other TIA Portal versions

`build.bat 20` builds against TIA Portal V20, and so on. The WinCC Unified API changes
between versions, so the code may need adjustments.

## Files

- `src/HmiResize.cs`: engine (C# 5 / .NET Framework 4.8)
- `src/HmiResizeGui.cs`: Windows Forms GUI
- `build.bat`: builds both programs

## Disclaimer

Independent project, not affiliated with Siemens AG. SIMATIC, WinCC and TIA Portal are
trademarks of Siemens AG. Use at your own risk and back up your project before any change.

## License

[MIT](LICENSE)
