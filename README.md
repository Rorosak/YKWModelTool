# YKWModelTool — Universal Yo-kai Watch Model & Animation Exporter

Standalone Windows GUI + CLI tool for extracting and converting **Yo-kai Watch** (Nintendo 3DS) character models and animations (`.xc`, `.xv`, `.mtn2`, `.mtn3`, `.mtninf`, `.mtninf2`, `.xi`, `RES.bin`) into **GLB**, **FBX**, **Blender (`.blend`)**, **Animated & Static COLLADA (`.dae`)**, **Valve SMD (`.smd`)**, and **PNG textures**.

> **Disclaimer:** This repository contains **tool source code, compiled binaries, and required runtime libraries only**. No Nintendo / Level-5 game assets or copyrighted models/textures are included. Supply your own legally dumped game files.

---

## Features

- **Universal Level-5 Scanner**: Automatically pairs character model archives (`*_p00.xc`, etc.) with all sibling animation packs (`*_p10.xc`, `*_p20.xc`, `*_p80.xc`, `*_p84.xc`, or loose `.mtn2`/`.mtn3` + `.mtninf`/`.mtninf2` + `RES.bin`).
- **Headless Native Export**: Directly exports textures (`.png`), static COLLADA (`model.dae`), and Valve `.smd` files using Metanoia's parsers and a hidden OpenGL context—without requiring manual GUI steps inside Metanoia.
- **Full Animation Support**: Exports both full animation reels (`out_00`) and all trimmed sub-animations from `.mtninf` / `.mtninf2`, preserving Shift-JIS Japanese animation names alongside English action hints in the GUI.
- **Multi-Animation Blender Assembly**: Uses Blender's Python API (`bpy`) in background mode (streaming progress asynchronously) to merge all skeletal animations into NLA tracks, pack textures, and bake single multi-animation `.glb`, `.fbx`, and `.blend` files.

---

## Tools & Credits

YKWModelTool is built on top of and integrates the following open-source tools and libraries:

- **[Metanoia](https://github.com/ploaj/Metanoia)** by **ploaj** — Core Level-5 `XPCK` (`.xc`/`.xv`), 3D mesh/skeleton, `.xi` texture, and `.mtn2`/`.mtn3`/`.mtninf`/`.mtninf2` animation parsers, plus `RenderTexture`, `DAEWriter`, and `ExportSMD`.
  - *Note:* `Metanoia\Metanoia.exe` includes a binary IL patch at file offset `0xD0F` fixing `FormatManager.GetAnimationExtensionFilter()` to return `"All files (*.*)|*.*"` instead of `"Supported Files |*"`.
- **[Blender](https://www.blender.org/)** — Executed in `--background` mode via generated `bpy` scripts to import COLLADA animations as NLA strips, pack textures, and export consolidated `.glb`, `.fbx`, and `.blend` files (tested with Blender 4.4).
- **[OpenTK](https://opentk.net/)** (`OpenTK.dll`, `OpenTK.GLControl.dll`) — Provides the headless OpenGL context required by Metanoia's texture/SMD exporters and 3D matrix/quaternion/vector math.
- **[Kuriimu2](https://github.com/FanTranslatorsInternational/Kuriimu2)** by **Fan Translators International** — Used during development to inspect and verify Level-5 `XPCK` (`.xc`) container archives and extracted subfiles.
- **[Newtonsoft.Json](https://www.newtonsoft.com/json)** & **[DockPanel Suite](https://github.com/dockpanelsuite/dockpanelsuite)** (`WeifenLuo.WinFormsUI.Docking.dll`) — Supporting dependencies required by `Metanoia.exe`.

---

## Requirements

- **Windows** with **.NET Framework 4.x** (pre-installed on Windows 10 / 11)
- **[Blender](https://www.blender.org/download/)** (auto-detected from `C:\Program Files\Blender Foundation` or `PATH`) — required only when exporting **GLB**, **FBX**, or **BLEND** files. Static/Animated `.dae`, `.smd`, `.png`, and raw `.xc` extraction work without Blender.

---

## Usage

### 1. GUI Mode
Double-click `YKWModelTool.exe` (or `Export_YKW_Model.bat` with no arguments):
1. Drag and drop a character folder or `.xc`/`.xv` file onto the window (or click **Folder...** / **.xc File...**).
2. Select a detected character model on the left and check/uncheck animations on the right (**Select All**, **Select None**, **Sub-Animations Only**, **Full Reels Only (out_00)**).
3. Choose output formats (`GLB`, `FBX`, `Blender`, `Animated COLLADA`, `Static COLLADA`, `Valve SMD`, `Textures`, `Extract Raw Archive Files`) and FPS (default `30`).
4. Click **Export Selected**.

### 2. Drag-and-Drop Batch Export
Drag one or more character folders (or `.xc` files) directly onto `Export_YKW_Model.bat` to export models and animations with default settings (`glb,fbx,blend,dae,static_dae,smd,png` at 30 FPS).

### 3. Command-Line Interface (CLI)
```bat
YKWModelTool.exe --cli "<input_folder_or_xc>" [options]
```

**Options:**
- `--out <dir>`: Output folder (defaults to the input folder).
- `--formats <list>`: Comma-separated formats from: `glb`, `fbx`, `blend`, `dae` (animated), `static_dae`, `smd`, `png`, `raw`, or `all`. Default: `glb,fbx,blend,dae,static_dae,smd,png`.
- `--fps <num>`: Frame rate for COLLADA / Blender animations (default: `30`).
- `--anims <all|sub|reel>`: Export all animations, sub-animations only, or full `out_00` reels only (default: `all`).
- `--blender <path>`: Custom path to `blender.exe` (auto-detected if omitted).
- `--help`: Show CLI usage help.

**Example:**
```bat
YKWModelTool.exe --cli "C:\Games\YKW3\x561000" --out "C:\Exports\x561000" --formats glb,fbx,png --fps 30
```

---

## Output Layout

- `<prefix>_animated.glb` — Single glTF binary with mesh, rig, packed textures, and all selected animations.
- `<prefix>_animated.fbx` — Single FBX with mesh, rig, embedded textures, and all selected animations.
- `<prefix>_animated.blend` — Blender project with all animations stored as actions/NLA tracks and packed textures.
- `model.dae` / `<prefix>_model.dae` — Static rigged COLLADA model.
- `model_animated.dae` — Rigged COLLADA model with the longest animation reel embedded.
- `<tex>.png` — Extracted character textures.
- `exports/smd/` — `<prefix>_model.smd` and per-pack `<pack>/<idx>_<name>.smd`.
- `exports/dae/` — Per-pack animated `<pack>/<idx>_<name>.dae` files with copied textures.
- `raw/` *(optional, `--formats raw`)* — Extracted raw subfiles from each `.xc`/`.xv` archive.

---

## Repository Structure

```text
├── src/
│   └── YKWModelTool.cs             # Complete GUI + CLI source code (C# 5 / .NET 4.x)
├── Metanoia/
│   ├── Metanoia.exe                # Patched Metanoia assembly (0xD0F filter fix)
│   ├── Metanoia.exe.config
│   ├── OpenTK.dll
│   ├── OpenTK.GLControl.dll
│   ├── Newtonsoft.Json.dll
│   ├── WeifenLuo.WinFormsUI.Docking.dll
│   ├── lib/
│   │   ├── DivaAlchemy.dll
│   │   └── HSDLib.dll
│   └── Rendering/
│       └── Shaders/
│           ├── Generic.frag
│           └── Generic.vert
├── YKWModelTool.exe                # Pre-built executable
├── Export_YKW_Model.bat            # Drag-and-drop launcher
├── build.bat                       # Rebuilds YKWModelTool.exe via csc.exe
├── .gitignore                      # Blocks all game archives and exported assets
├── LICENSE
└── README.md
```

---

## Building from Source

Run `build.bat`, or compile manually with the built-in .NET Framework 4.x C# compiler:

```bat
C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe /nologo /target:winexe /platform:anycpu /optimize+ ^
  /out:YKWModelTool.exe ^
  /reference:Metanoia\Metanoia.exe ^
  /reference:Metanoia\OpenTK.dll ^
  /reference:Metanoia\OpenTK.GLControl.dll ^
  /reference:System.Windows.Forms.dll ^
  /reference:System.Drawing.dll ^
  /reference:System.Xml.dll ^
  /reference:System.Core.dll ^
  src\YKWModelTool.cs
```
