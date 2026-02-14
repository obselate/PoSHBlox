# Contributing to PoSHBlox

Thanks for being here. Whether you're adding a cmdlet template, improving the code generation engine, fixing bugs, or just telling us what's broken — every contribution matters.

## Getting Started

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- Git
- An editor (VS Code, Rider, Visual Studio — whatever you're comfortable with)

### Build & Run

```bash
git clone https://github.com/obselate/PoSHBlox.git
cd PoSHBlox
dotnet run
```

That's it. If it doesn't build, open an issue.

## Project Structure

```
PoSHBlox/
├── Assets/              # Icons, fonts, banner, screenshots
├── Controls/            # Custom Avalonia UI controls (NodeGraphCanvas)
├── Models/              # Data models: GraphNode, NodeConnection, NodePort, enums
├── Rendering/           # Canvas renderer, pan/zoom, Bezier wires, theme
├── Scripts/             # PowerShell scripts (module introspection)
├── Services/            # Code gen, template loading, project serialization, PS introspection
├── Templates/
│   ├── Builtin/         # Shipped cmdlet templates (JSON) — DO NOT hand-edit
│   └── Custom/          # User/community templates (JSON) — this is where you add stuff
├── TestProjects/        # Sample .pblx project files
├── Themes/              # AXAML theme/token files
├── ViewModels/          # MVVM ViewModels (CommunityToolkit.Mvvm)
├── Views/               # Additional windows (Import Module dialog)
├── MainWindow.axaml     # Primary window layout
└── Program.cs           # Entry point
```

## How to Contribute

### The Easiest Way: Add Cmdlet Templates

**You don't need to know C# to contribute cmdlet templates.** Templates are JSON files. If you can write PowerShell, you can write a template.

#### Option A: Drop a JSON file in `Templates/Custom/`

Create a `.json` file in `Templates/Custom/` with this structure:

```json
{
  "version": 1,
  "category": "My Custom Tools",
  "templates": [
    {
      "name": "Invoke-MyTool",
      "cmdletName": "Invoke-MyTool",
      "description": "Runs my custom tool",
      "scriptBody": "",
      "containerType": "None",
      "inputCount": 1,
      "outputCount": 1,
      "inputNames": ["In"],
      "outputNames": ["Out"],
      "parameters": [
        {
          "name": "Target",
          "type": "String",
          "isMandatory": true,
          "defaultValue": "",
          "description": "Target host or path",
          "validValues": []
        }
      ]
    }
  ]
}
```

Restart PoSHBlox and the new category appears in the palette.

#### Option B: Use the Import Module dialog

1. Click **`+ Import`** in the Node Palette
2. Type a module name (e.g., `ActiveDirectory`, `SqlServer`)
3. Click **Scan** — PoSHBlox runs PowerShell to discover cmdlets and parameters
4. Select which cmdlets to import, set a category name, and save
5. The JSON lands in `Templates/Custom/` automatically

#### Template Field Reference

**Parameter types** (`type` field):

| Type | PowerShell Equivalent | UI Control |
|------|----------------------|------------|
| `String` | `[string]` | TextBox |
| `Int` | `[int]` | TextBox (numeric) |
| `Bool` | `[switch]` | CheckBox |
| `StringArray` | `[string[]]` | TextBox (comma-separated) |
| `ScriptBlock` | `[scriptblock]` | Multi-line TextBox |
| `Path` | `[string]` (path) | TextBox |
| `Credential` | `[PSCredential]` | TextBox |
| `Enum` | `[ValidateSet()]` | ComboBox (uses `validValues`) |

**Port configuration** (`inputCount` / `outputCount`):

| Scenario | inputCount | outputCount | Example |
|----------|-----------|-------------|---------|
| Source node (no pipeline input) | `0` | `1` | `Get-Process`, `Get-ChildItem` |
| Pipeline filter (in → out) | `1` | `1` | `Where-Object`, `Sort-Object` |
| Sink node (no output) | `1` | `0` | `Remove-Item`, `Write-Host` |

**Container types** (`containerType` field): `None`, `IfElse`, `ForEach`, `TryCatch`, `While`, `Function`

**Script-body nodes** (no cmdlet): Leave `cmdletName` empty and put PowerShell code in `scriptBody`. The node will execute that code instead of a named cmdlet.

#### Important Gotchas

- Always explicitly set `inputNames` and `outputNames`, even if empty (`[]`). If you omit them, C# defaults kick in and your node silently gets an "In" port it shouldn't have.
- Check your JSON is valid before committing. Malformed files are silently skipped.
- Use `Get-Help <CmdletName> -Full` to verify parameter names and types when writing templates by hand.

### Found a Bug?

Open an issue. Include what you did, what you expected, and what actually happened. Screenshots or the generated script output help a lot.

### Want to Add a Feature?

Open an issue first to discuss it. This saves everyone time — especially you — if the feature doesn't align with the project direction or someone's already working on it.

### Deeper Code Contributions

If you're comfortable with C# and want to work on the core:

- **Code generation** lives in `Services/ScriptGenerator.cs` — it walks the node graph, resolves pipeline chains vs. variable assignments, detects cycles, and outputs PowerShell. Read through it and trace a simple graph before making changes.
- **Template loading** is in `Services/TemplateLoader.cs` — reads JSON from `Templates/Builtin/` and `Templates/Custom/`. Malformed files are skipped with a debug trace.
- **Module introspection** is `Services/PowerShellIntrospector.cs` + `Scripts/IntrospectModule.ps1` — launches PowerShell 5.1 to discover cmdlets in installed modules.
- **Rendering** is in `Rendering/NodeGraphRenderer.cs` — custom DrawingContext rendering for nodes, wires, and the canvas.
- **ViewModels** use CommunityToolkit.Mvvm with `[ObservableProperty]` and `[RelayCommand]`.

## Pull Request Process

1. Fork the repo and create a branch from `main`
2. Name your branch descriptively: `add-sqlserver-templates`, `fix-cycle-detection-edge-case`, etc.
3. Make your changes
4. Test — at minimum, build successfully and verify generated scripts are valid PowerShell
5. Open a PR with a clear description of what you changed and why

Keep PRs focused. One feature or fix per PR.

## Code Style

- Follow existing conventions in the codebase
- MVVM: ViewModels use CommunityToolkit.Mvvm (`[ObservableProperty]`, `[RelayCommand]`)
- XAML views use Avalonia UI conventions
- Keep models clean — no UI logic in model classes
- When in doubt, look at how the existing code does it and match that

## Areas Where Help Is Needed

- **More cmdlet templates** — especially for popular modules (Az, AWS, Exchange, VMware, etc.)
- **Code generation edge cases** — nested pipelines, splatting, advanced parameter sets
- **UX improvements** — auto-arrange, minimap, undo/redo, search in palette
- **Import dialog improvements** — progress bar for large modules, `pwsh` support for cross-platform
- **Template validation** — catch malformed JSON with useful error messages instead of silent skips
- **Documentation** — usage guides, tutorial content, wiki pages
- **Testing** — unit tests for code generation, integration tests for template loading
- **Bug reports** — just using the tool and telling us what breaks is genuinely valuable

## Sharing Templates

Built a useful template pack? Share it:

1. Copy your `.json` file from `Templates/Custom/`
2. Share it however you want — GitHub, Discord, email
3. The recipient drops it in their `Templates/Custom/` folder
4. It shows up in their palette on next launch

That's the whole workflow. No compilation, no PRs required (though we'd love to include good community templates in `Templates/Builtin/` for everyone).

## AI-Assisted Contributions

Using AI tools (Copilot, Claude, ChatGPT, whatever) to help write code, templates, or documentation is perfectly fine. We're not going to gatekeep how you produce your work.

**The catch:** You are responsible for what you submit. AI-generated code must be reviewed, understood, and tested by you before it goes into a PR. If you can't explain what your code does or why it works, it's not ready to submit. "The AI wrote it" is not a defense for broken code, bad templates, or nonsensical parameter definitions.

In short: AI is a tool, not an author. Use it, but own the output.

## Questions?

Open an issue or start a discussion. There are no stupid questions — this project is literally built to help people learn.
