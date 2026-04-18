# Contributing to PoSHBlox

Thanks for being here. Catalog extensions, codegen fixes, bug reports,
UX polish — everything helps.

## Getting Started

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- Git
- An editor (VS Code, Rider, Visual Studio — whatever you like)

### Build & Run

```bash
git clone https://github.com/obselate/PoSHBlox.git
cd PoSHBlox
dotnet run
```

If it doesn't build, open an issue.

## Project Structure

```
PoSHBlox/
├── Assets/          # Icons, fonts, banner
├── Controls/        # NodeGraphCanvas — the canvas control
├── Models/          # GraphNode, NodePort, NodeParameter, ContainerZone, enums
├── Rendering/       # Canvas renderer, pan/zoom, Bezier wires, theme
├── Services/        # Codegen, template loading, serializer, introspection
├── ViewModels/      # CommunityToolkit.Mvvm VMs
├── Views/           # ImportModuleWindow
├── Themes/          # AXAML theme/token files
├── Templates/
│   ├── Builtin/     # Shipped cmdlet catalogs (regenerate via CLI, don't hand-edit)
│   └── Custom/      # User/community catalogs — drop JSON here
├── Scripts/         # PowerShell helpers shipped with the app (module introspection)
├── scripts/         # Dev-only: Builtin regen manifest
├── Tools/           # Dev-only: audit / parse-check / integrity scripts
├── MainWindow.axaml # Primary window
└── Program.cs       # Entry point + CLI modes (--regen-manifest, --emit)
```

## How to Contribute

### Extend the shipped catalog

Adding modules that ship to every user is the highest-leverage
contribution that doesn't require C#. You edit one JSON file, run one
command, open a PR.

`scripts/builtin-catalog.json` is the reference manifest that drives
`Templates/Builtin/*.json`. Each `target` says: which output file,
which category label, which PowerShell modules (and optionally which
specific cmdlets) should populate it.

```json
{
  "targets": [
    {
      "outputFile": "Templates/Builtin/NetworkRemote.json",
      "category": "Network / Remote",
      "sources": [
        {
          "module": "Microsoft.PowerShell.Utility",
          "cmdlets": ["Invoke-WebRequest", "Invoke-RestMethod"]
        }
      ]
    }
  ]
}
```

To contribute: add a new target, or extend an existing one's `sources`
/ `cmdlets` list, then regenerate (see below). PR both the manifest
edit and the regenerated `Templates/Builtin/*.json`. The introspector
fills in parameter sets, editions, pipeline flags, and descriptions
from the live module — you don't need to understand the output schema.

> **Not a contribution path:** the **+ Import** button in the palette.
> That's a local feature — the machine-generated JSON lands in
> `Templates/Custom/` and works on your machine. For anything
> everyone should get, the manifest is the source of truth. Don't
> copy `Templates/Custom/*.json` into a PR.

### Found a Bug?

Open an issue. Include what you did, what you expected, what happened.
Screenshots and the generated script output help.

### Want to Add a Feature?

Open an issue first to discuss. Saves time if it doesn't fit the project
direction or someone's already working on it.

### Deeper Code Contributions

If you're comfortable with C# and want to work on the core:

- **Code generation** — `Services/ScriptGenerator.cs`. Walks exec wires from
  each exec-root, assigns variables per node, collapses pipeline-eligible
  chains (`A.ExecOut→B.ExecIn` + `A.PrimaryDataOutput→B.PrimaryPipelineTarget`,
  single consumer, no user-set OutputVariable) into `A | B`. ForEach's
  `Item` data pin compiles to `$_`.
- **Template loading** — `Services/TemplateLoader.cs`. Reads `Templates/Builtin/`
  and `Templates/Custom/`; malformed files are skipped with a debug trace.
  Catalogs at `version < 3` are migrated inline (every `Bool` param gets
  `IsSwitch = true`). Project-document migration (the `.pblx` format) is a
  separate pipeline — see `Services/V1Migrator.cs` / `V2ToV3Migrator.cs`,
  invoked from `ProjectSerializer`.
- **Module introspection** — `Services/PowerShellIntrospector.cs` +
  `Scripts/IntrospectModule.ps1`. Host resolution lives in
  `Services/PowerShellHost.cs`, shared with Run. `IntrospectionMerger`
  combines pwsh + powershell scans into one catalog.
- **Project serialization** — `Services/ProjectSerializer.cs` /
  `ProjectDto.cs`. The `.pblx` format.
- **Rendering** — `Rendering/NodeGraphRenderer.cs`. Custom `DrawingContext`
  rendering for nodes, wires, containers.
- **ViewModels** use CommunityToolkit.Mvvm (`[ObservableProperty]`, `[RelayCommand]`).

### Regenerating the built-in catalogs

`Templates/Builtin/*.json` is generated from `scripts/builtin-catalog.json`
(the reference manifest). To rebuild after editing that manifest:

```powershell
# Pipe to Out-Host so the WinExe flushes output to the shell.
.\bin\Debug\net10.0\PoSHBlox.exe --regen-manifest scripts\builtin-catalog.json 2>&1 | Out-Host

# Dry-run first:
.\bin\Debug\net10.0\PoSHBlox.exe --regen-manifest scripts\builtin-catalog.json --dry-run 2>&1 | Out-Host
```

Curated per-parameter `defaultValue`s in the existing output file are
preserved across runs (matched by cmdlet + parameter name). Descriptions
are refreshed from live introspection, so hand-edits to descriptions
will be overwritten — edit `scripts/builtin-catalog.json` or the
introspection source instead.

### Dev-only tooling (`Tools/`)

- `Audit-Templates.ps1` — schema sanity over Builtin + Custom (invalid
  `type` / `containerType` / `kind` values, duplicate parameter names,
  multiple `isPrimary` outputs, `primaryPipelineParameter` that doesn't
  resolve, `defaultParameterSet` not in `knownParameterSets`, Bool
  defaults that aren't bool-like).
- `Parse-Check-Samples.ps1` — point it at a folder of `.pblx` files
  (`-Path <dir>`); shells out to `--emit` for each and runs
  `[Parser]::ParseInput` on the generated PowerShell.
- `Check-Samples.ps1` — graph-integrity sweep over a folder of `.pblx`
  files: dangling port IDs, source/target node-ID vs port-owner
  mismatches, orphan `ParentNodeId`, `ParentZoneName` not on the
  parent's zones, duplicate node/port IDs, `ActiveParameterSet` not in
  `KnownParameterSets`.

## Pull Request Process

1. Fork and branch from `main`
2. Name your branch descriptively: `add-sqlserver-templates`, `fix-cycle-detection-edge-case`
3. Make changes
4. Test — at minimum, build successfully. For codegen changes, run
   `Tools/Parse-Check-Samples.ps1 -Path <folder>` on a few `.pblx`
   graphs to confirm the emitted PowerShell still parses
5. Open a PR with a clear description of what changed and why

One feature or fix per PR.

## Code Style

- Follow existing conventions
- MVVM: ViewModels use CommunityToolkit.Mvvm (`[ObservableProperty]`, `[RelayCommand]`)
- Avalonia conventions in XAML
- No UI logic in models
- When in doubt, match the surrounding code

## Areas Where Help Is Needed

- Shipped-catalog expansion — add popular modules (Az, AWS, Exchange, VMware) as new targets in `scripts/builtin-catalog.json`
- Codegen edge cases — nested pipelines, advanced parameter sets
- UX polish — auto-arrange, minimap, undo/redo
- Import dialog — module picker from `Get-Module -ListAvailable`, progress for large modules, cross-platform support
- Catalog validation — richer errors from `Tools/Audit-Templates.ps1` (and ideally surface them in-app instead of silent skips)
- Tutorial content, wiki pages
- Unit tests around codegen and catalog loading
- Bug reports — just using the tool and telling us what breaks is genuinely valuable

## AI-Assisted Contributions

Using AI tools (Copilot, Claude, ChatGPT, whatever) to help write code,
manifest entries, or docs is fine. We're not going to gatekeep how you
produce your work.

**The catch:** you own what you submit. AI-generated code must be
reviewed, understood, and tested by you. "The AI wrote it" isn't a
defense for broken code or a manifest entry that points at a module
that doesn't exist. AI is a tool, not an author.

## Questions?

Open an issue or start a discussion. There are no stupid questions — this
project is literally built to help people learn.
