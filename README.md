# WadAssembly

A C# (.NET 8) tool with an Avalonia GUI for compiling community Doom projects: it takes
several `.wad` files contributed by different authors and produces **a single PWAD** with
the maps placed in their final slots, merged resources and deduplicated textures.

## Structure

```
src/
  CommunityWadCompiler.Core/   Library: WAD format, maps, texture merging, pipeline
  CommunityWadCompiler.App/    Avalonia UI (main window, view models)
tests/
  CommunityWadCompiler.Tests/  Roundtrip, map detection and merging tests
```

## Features

- **WAD reader/writer**: parses `IWAD`/`PWAD` (header, directory, lumps), whole-file
  loading and 4-byte-aligned writing.
- **Map detection**: `E1M1..E4M9`, `MAP01..`, and UDMF maps (TEXTMAP/ENDMAP).
- **Texture merging**: PNAMES + TEXTURE1/TEXTURE2 in classic Doom format, patches
  deduplicated by name with re-mapped indexes; textures deduplicated (first definition
  wins, matching the engine).
- **Resource merging**: generic lumps deduplicated by name (first occurrence wins);
  option to skip copying resources from the base/IWAD.
- **Slot assignment**: automatic (`MAP01...`) or manual, editable from the grid.
- **Animated/switches lump generation**: builds `ANIMATED` (Boom), `SWITCHES` (Boom) or
  `ANIMDEFS` (ZDoom) depending on the selected **target engine**; animated texture runs
  can be filtered through an **allowlist of prefixes**, with a 32-frame safety cap to
  avoid a crash in specs that limit the length of the lump.
- **Grid filters**: live search across the plan sheet with a per-field search dropdown
  (slot, WAD, level, status, author, music, notes...).
- **Row colors by status**: TODO/WIP/DONE/FIX tint the whole row background (toggleable).
- **Drag & drop**: drop `.wad` files anywhere on the window to add them as inputs.
- **Console log**: bottom panel with autoscroll, a search box and **Error / Warning /
  Info** filter chips.
- **Recent projects**: File → Open recent keeps the last 8 projects ready to reopen.
- **Project summary**: File → Project → Project summary... exports a Markdown report
  (slots, status, authors, notes) ready to post on a forum; copy to clipboard or save as
  `.md`.
- **Project management**: save/load projects as JSON, project settings (target engine,
  animated prefixes, intermission music, source ports...).
- **Music rename log**: renamed/reused music lumps are reported line by line in the log.
- **Keyboard shortcuts**: `Ctrl+S` save, `Ctrl+Shift+S` save as, `Ctrl+B` compile.
- **Localization**: Spanish / English UI, toggleable from Configuration → Language.
- **App icon**: custom icon on the executable and in the About dialog.

## Limitations (TODOs)

- ZDoom `TEXTURES` (text) lump merging not implemented yet; it is skipped with a notice.
- Boom-style list lumps (`ANIMATED`, `SWITCHES`, `ANIMDEFS`, `SNDINFO`, `UMAPINFO`,
  `MUSINFO`, ...) use "first source wins" instead of real concatenation/merging.
- Strife detection in TEXTUREx is basic.

## Build

Requires: .NET 8 SDK.

```
dotnet build CommunityWadCompiler.sln
dotnet run --project src/CommunityWadCompiler.App
```

## Quick start

1. **Add WADs...**: load the authors' `.wad` files.
2. Reorder with Move up / Move down; edit each map's **final slot** in the grid (rows
   left blank are assigned automatically).
3. (Optional) pick a **base WAD** (e.g. DOOM2.WAD) to seed TEXTURE1/PNAMES.
4. Set the **output** and press **Compile**.

The result opens in any modern port:
`yoursourceport -iwad doom2.wad -file megawad.wad`.