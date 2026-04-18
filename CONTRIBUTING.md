# Contributing to PoSHBlox

Thanks for being here. Cmdlet templates, codegen fixes, bug reports, UX
polish — everything helps.

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
├── Samples/         # Example .pblx files
├── Scripts/         # PowerShell helpers shipped with the app (module introspection)
├── scripts/         # Dev-only: Builtin regen manifest
├── Tools/           # Dev-only: audit / parse-check / integrity scripts
├── MainWindow.axaml # Primary window
└── Program.cs       # Entry point + CLI modes (--regen-manifest, --emit)
```

## How to Contribute

### The Easiest Way: Add Cmdlet Templates

You don't need to know C# to contribute cmdlet templates. Templates are
JSON. If you can write PowerShell, you can write a template.

#### Option A (recommended): Use **+ Import** in the palette

1. Click **`+ Import`** in the Node Palette
2. Pick a module name (`ActiveDirectory`, `SqlServer`, …)
3. Click **Scan** — PoSHBlox introspects cmdlets and parameters from your
   installed PowerShell host (pwsh 7+ preferred, 5.1 fallback)
4. Select cmdlets, set a category name, save
5. The resulting `.json` lands in `Templates/Custom/` and shows up in the
   palette on next launch

Share the generated file — that's the whole workflow.

#### Option B: Hand-author a `.json` in `Templates/Custom/`

Schema version 3. Minimal shape:

```json
{
  "version": 3,
  "category": "My Custom Tools",
  "templates": [
    {
      "name": "Invoke-MyTool",
      "cmdletName": "Invoke-MyTool",
      "description": "Runs my custom tool",
      "hasExecIn": true,
      "hasExecOut": true,
      "primaryPipelineParameter": "Target",
      "dataOutputs": [
        { "name": "Out", "isPrimary": true }
      ],
      "parameters": [
        {
          "name": "Target",
          "type": "String",
          "isMandatory": true,
          "isPipelineInput": true,
          "description": "Target host or path"
        }
      ]
    }
  ]
}
```

Restart PoSHBlox to pick it up.

#### Template Field Reference

**Top-level template fields:**

| Field | What it does |
|-------|-------------|
| `cmdletName` | Cmdlet to invoke. Empty = script-body node (see below). |
| `containerType` | `None`, `IfElse`, `ForEach`, `TryCatch`, `While`, `Function`, `Label`. |
| `hasExecIn` / `hasExecOut` | Triangle exec pins. Terminal nodes (sinks) typically set `hasExecOut: false`. |
| `dataOutputs` | List of `{ "name": "...", "type": "...", "isPrimary": true }`. Omit to get one primary `Out` of type `Any`. |
| `primaryPipelineParameter` | Name of the parameter whose input pin collapses `A \| B`. If omitted, the first param with `isPipelineInput: true` wins. |
| `knownParameterSets` / `defaultParameterSet` | Parameter-set metadata for multi-set cmdlets. Optional. |
| `scriptBody` | Raw PowerShell. Used when `cmdletName` is empty. |

**Parameter types** (`type` field, case-sensitive):

| Type | PowerShell equivalent | UI |
|------|----------------------|-----|
| `String` / `Path` / `Enum` | `[string]` (quoted) | TextBox / picker / ComboBox |
| `Int` | `[int]` | TextBox |
| `Bool` | `[bool]` or `[switch]` (use `"isSwitch": true`) | CheckBox |
| `StringArray` | `[string[]]` | TextBox (comma-separated → `@("a","b")`) |
| `Collection` | `[object[]]` | TextBox (comma-separated) |
| `ScriptBlock` | `[scriptblock]` | Multi-line TextBox (wrapped in `{ … }`) |
| `Credential` | `[PSCredential]` | TextBox (raw pass-through — use `$cred`) |
| `HashTable` | `[hashtable]` | TextBox (raw pass-through — use `@{...}`) |
| `Object` / `Any` | untyped | TextBox (raw pass-through) |

**Parameter flags:**

- `isMandatory` — adds red asterisk + blocks run
- `isSwitch` — presence-only; emits bare `-Name` when set
- `isPipelineInput` — `[Parameter(ValueFromPipeline)]`; a candidate for the pipeline-collapse pass
- `parameterSets` / `mandatoryInSets` — which sets this param belongs to (empty = common)
- `validValues` — drives the `Enum` ComboBox
- `defaultValue` — pre-fills the editor

**Script-body nodes:** Leave `cmdletName` empty, put code in `scriptBody`.
The node executes that literal instead of calling a named cmdlet.

#### Gotchas

- Strings in textboxes codegen as double-quoted literals: `"value"` with
  `$var`/`$(...)` expansion and `` ` ``/`"` escaping. If you need raw
  expressions (piping a variable into a param), wire the pin instead of
  typing into the textbox.
- Malformed JSON is skipped silently at load — check the debug trace if a
  file doesn't show up. `Tools/Audit-Templates.ps1` will flag most issues.
- The introspection-based Import dialog populates parameter sets, editions,
  and pipeline flags correctly. Hand-authored templates only get what you
  write.

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
  `V1Migrator` and `V2ToV3Migrator` upgrade older catalogs on read.
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

Curated per-parameter defaults and descriptions are preserved across runs.

### Dev-only tooling (`Tools/`)

- `Audit-Templates.ps1` — schema sanity over Builtin + Custom (flags missing
  fields, type drift, empty categories).
- `Parse-Check-Samples.ps1` — runs `--emit` over every `Samples/*.pblx` and
  verifies the output parses as valid PowerShell.
- `Check-Samples.ps1` — graph-integrity sweep (dangling port IDs, orphan
  `ParentNodeId`, duplicate IDs, active-set consistency).

## Pull Request Process

1. Fork and branch from `main`
2. Name your branch descriptively: `add-sqlserver-templates`, `fix-cycle-detection-edge-case`
3. Make changes
4. Test — at minimum, build successfully, and for codegen changes run
   `Tools/Parse-Check-Samples.ps1` to confirm samples still parse
5. Open a PR with a clear description of what changed and why

One feature or fix per PR.

## Code Style

- Follow existing conventions
- MVVM: ViewModels use CommunityToolkit.Mvvm (`[ObservableProperty]`, `[RelayCommand]`)
- Avalonia conventions in XAML
- No UI logic in models
- When in doubt, match the surrounding code

## Areas Where Help Is Needed

- More cmdlet templates — especially Az, AWS, Exchange, VMware
- Codegen edge cases — nested pipelines, advanced parameter sets
- UX polish — auto-arrange, minimap, undo/redo
- Import dialog — module picker from `Get-Module -ListAvailable`, progress for large modules, cross-platform support
- Template validation — useful error messages instead of silent skips
- Tutorial content, wiki pages
- Unit tests around codegen / template loading
- Bug reports — just using the tool and telling us what breaks is genuinely valuable

## Sharing Templates

Built a useful pack?

1. Copy your `.json` from `Templates/Custom/`
2. Share however you like — GitHub, Discord, email
3. Recipient drops it in their `Templates/Custom/`
4. Shows up in their palette on next launch

No compilation, no PR required (though we'd love to ship good community
packs in `Templates/Builtin/`).

## AI-Assisted Contributions

Using AI tools (Copilot, Claude, ChatGPT, whatever) to help write code or
templates is fine. We're not going to gatekeep how you produce your work.

**The catch:** you own what you submit. AI-generated code must be
reviewed, understood, and tested by you. "The AI wrote it" isn't a
defense for broken code or nonsensical parameter definitions. AI is a
tool, not an author.

## Questions?

Open an issue or start a discussion. There are no stupid questions — this
project is literally built to help people learn.
