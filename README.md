<p align="center">
  <img src="Assets/poshblox-banner.svg" alt="PoSHBlox" width="720" />
</p>

<p align="center">
  A visual node-graph editor for building PowerShell scripts. Drag, connect, run.
</p>

<p align="center">
  <img src="https://img.shields.io/badge/.NET-10.0-512BD4?style=flat-square" alt=".NET 10" />
  <img src="https://img.shields.io/badge/Avalonia-11.3-7B2BF9?style=flat-square" alt="Avalonia" />
  <img src="https://img.shields.io/badge/FluentAvalonia-2.5-0078D4?style=flat-square" alt="FluentAvalonia" />
  <img src="https://img.shields.io/badge/platform-Windows-333?style=flat-square" alt="Platform" />
</p>

---

## What is PoSHBlox?

PoSHBlox lets you visually compose PowerShell scripts by wiring together
nodes on a canvas. Each node is a cmdlet, control-flow block, or custom
script fragment; connections define the pipeline. Preview the generated
script, run it, or export a clean `.ps1`.

<p align="center">
  <img src="Assets/poshblox-gif.gif" alt="PoSHBlox UI" width="900" />
</p>

### Features

- 85+ built-in cmdlet templates across 12 categories
- Control-flow containers: If/Else, ForEach, Try/Catch, While, Functions, Labels (nestable)
- Live script preview and one-click Run (prefers pwsh 7+, falls back to Windows PowerShell 5.1)
- Pipeline-aware codegen: chains pipes, assigns variables at branch points, detects cycles
- Import cmdlets from installed modules to extend the palette
- Keyboard shortcuts, dark theme, pan/zoom canvas with Bezier wires

## Get it

### Download a prebuilt release (recommended)

Grab the latest `.zip` from **[Releases](../../releases)**. No .NET install
required — the `.exe` is self-contained. Extract, run `PoSHBlox.exe`.

Every push to every branch also publishes a rolling pre-release
(`Branch build: <branch-name>`) and a 30-day artifact on the
[Actions](../../actions/workflows/build.yml) run page.

### Build from source

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).

```bash
git clone https://github.com/obselate/PoSHBlox.git
cd PoSHBlox
dotnet run
```

## How it works

1. **Add nodes** from the palette (press `P`)
2. **Connect** output ports to input ports by dragging wires
3. **Configure** parameters in the right-side properties panel
4. **Preview** (`Alt+S`), **Run** (`F5`), or **Export** (`Ctrl+E`) as `.ps1`

Press `?` any time for the full keyboard cheatsheet.

## Regenerating the built-in catalogs

The fastest way to add cmdlets is **+ Import** in the palette — pick an
installed module and PoSHBlox introspects it into a Custom template set.

The shipped `Templates/Builtin/*.json` can be rebuilt from
`scripts/builtin-catalog.json` (the reference manifest of modules and
cmdlets per category). Curated per-parameter defaults are preserved
across runs.

```powershell
# Pipe to Out-Host so the WinExe flushes output to the shell.
.\bin\Debug\net10.0\PoSHBlox.exe --regen-manifest scripts\builtin-catalog.json 2>&1 | Out-Host

# Dry-run to preview:
.\bin\Debug\net10.0\PoSHBlox.exe --regen-manifest scripts\builtin-catalog.json --dry-run 2>&1 | Out-Host
```

## License

GNU AGPL v3 — see [LICENSE](LICENSE)
