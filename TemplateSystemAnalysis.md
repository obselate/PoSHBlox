# JSON Template System — Analysis & User Workflow

## Pro/Con Analysis

### Pros

| Pro | Impact |
|-----|--------|
| **Scalable** — adding a cmdlet = editing JSON, no recompilation | High — enables community contributions without C# knowledge |
| **User-importable modules** — unlocks PSGallery/custom module integration | High — the core goal of this refactor |
| **Separation of data from code** — templates are no longer compiled into the binary | Medium — cleaner architecture, smaller binary |
| **Hot-reload potential** — JSON can be watched via `FileSystemWatcher` for live palette updates | Medium — future enhancement |
| **Consistent with existing patterns** — `ProjectSerializer` already uses the same JSON serialization options | Low friction — no new dependencies or paradigms |
| **Backward compatible** — fallback to hardcoded `TemplateLibrary.GetAll()` means zero risk during transition | Safety net |

### Cons

| Con | Impact | Mitigation |
|-----|--------|------------|
| **Cross-platform broken for Import dialog** — `powershell.exe` is Windows-only | Medium — PoSHBlox targets PS 5.1 (Windows-only anyway), but Avalonia is cross-platform in principle | Detect OS and disable Import on non-Windows, or support `pwsh` as fallback |
| **No template validation** — malformed JSON silently skipped, user won't know why their import didn't appear | Medium — confusing UX | Add a validation step or error toast in a future iteration |
| **Single-file publish incompatible** — custom templates in `AppContext.BaseDirectory` get wiped | Low — not currently using single-file publish | Migrate to AppData if distribution model changes |
| **Import UX for huge modules is poor** — no progress bar, no timeout, could hang on Azure-scale modules | Medium — bad first impression | Add 30-second timeout + progress reporting |
| **Get-Help descriptions often empty** — imported cmdlets may have blank descriptions unless `Update-Help` has been run | Low — functional but looks unpolished | Fall back to auto-description or let users edit after import |
| **More files to ship** — 9 JSON files + 1 PS1 script in the release | Low — trivial size increase (~20KB total) | Already shipping 3 platform zips, this is nothing |
| **Complexity increase** — 12 new files, a new dialog, a PS introspection service | Medium — more surface area for bugs | Phased rollout ensures JSON loader works before Import dialog is built |

### Caveats & Pitfalls

- **Default values in JSON**: `NodeTemplate` initializes `InputNames = ["In"]` and `OutputNames = ["Out"]` in C#. If a JSON file omits these, the C# defaults persist — meaning a cmdlet that should have zero inputs silently gets an "In" port. All JSON templates must explicitly set these fields.
- **Module import requirements**: Some PS modules need admin elevation or Windows features (e.g., `ActiveDirectory` needs RSAT, `Hyper-V` needs the feature enabled). Scan will fail with a PowerShell error.
- **Binary vs script modules**: Binary modules (C# cmdlets) introspect cleanly. Script modules with advanced functions have less structured metadata.
- **Two sources of truth during transition**: While `TemplateLibrary.cs` fallback exists alongside JSON, risk of editing one and not the other. Remove fallback quickly.
- **Write permissions**: If app is installed in `Program Files`, writing to `Templates/Custom/` fails (UAC). Works fine for portable/unzipped installs (current distribution model).

---

## User Workflows for Adding New Nodes

### Workflow 1: Import an Entire PowerShell Module (In-App)

> **Use when**: You have a PS module installed and want to pull in its cmdlets.

1. Click **`+ Import`** in the Node Palette header
2. Type the module name (e.g., `ActiveDirectory`, `SqlServer`, `Az.Compute`)
3. Click **Scan** — the app runs PowerShell to discover all cmdlets in that module
4. Review the list of discovered cmdlets:
   - Each shows the cmdlet name, description, and parameter count
   - All are checked by default — uncheck any you don't need
5. Set the **Category name** (defaults to the module name, e.g., "ActiveDirectory")
6. Click **Save**
7. The JSON file is saved to `Templates/Custom/{ModuleName}.json`
8. The palette refreshes automatically with the new category and cmdlets

```
Example:
  Module: ActiveDirectory
  Discovered: Get-ADUser, Get-ADGroup, New-ADUser, Set-ADUser, ...
  Category: "Active Directory"
  Saved to: Templates/Custom/ActiveDirectory.json
```

### Workflow 2: Hand-Write a JSON Template File

> **Use when**: You want to add a custom cmdlet, a function from a script module, or a wrapper around a tool that isn't a PS module.

1. Create a new `.json` file in `Templates/Custom/` (next to the exe)
2. Use this structure:

```json
{
  "version": 1,
  "category": "My Custom Tools",
  "templates": [
    {
      "name": "Invoke-MyTool",
      "cmdletName": "Invoke-MyTool",
      "description": "Run my custom tool with parameters",
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
        },
        {
          "name": "Verbose",
          "type": "Bool",
          "isMandatory": false,
          "defaultValue": "",
          "description": "Enable verbose output",
          "validValues": []
        }
      ]
    }
  ]
}
```

3. Restart PoSHBlox (or if hot-reload is supported, it appears instantly)
4. The new category "My Custom Tools" appears in the palette

### Workflow 3: Edit an Existing Imported Template

> **Use when**: An imported cmdlet has a blank description, wrong defaults, or you want to tweak parameters.

1. Open the JSON file in `Templates/Custom/` with any text editor
2. Find the template by `name`
3. Edit `description`, `defaultValue`, `validValues`, or any other field
4. Save the file
5. Restart PoSHBlox to pick up changes

### Workflow 4: Share Templates with Others

> **Use when**: You've built a useful template set and want to share it.

1. Copy the `.json` file from your `Templates/Custom/` folder
2. Share it (GitHub, email, Discord, etc.)
3. The recipient drops it into their `Templates/Custom/` folder
4. Done — the templates appear in their palette on next launch

### Workflow 5: Add a Script-Body Node (No Cmdlet)

> **Use when**: You want a node that runs arbitrary PowerShell, not a named cmdlet.

```json
{
  "name": "Parse Log File",
  "cmdletName": "",
  "description": "Extract errors from a log file",
  "scriptBody": "$input | ForEach-Object {\n    if ($_ -match 'ERROR') { $_ }\n}",
  "containerType": "None",
  "inputCount": 1,
  "outputCount": 1,
  "inputNames": ["LogLines"],
  "outputNames": ["Errors"],
  "parameters": []
}
```

When `cmdletName` is empty, PoSHBlox uses `scriptBody` as the node's code. The user can edit it in the properties panel.

---

## Parameter Type Reference

When writing templates, use these `type` values:

| Type | JSON Value | PowerShell Equivalent | UI Control |
|------|------------|----------------------|------------|
| Text input | `"String"` | `[string]` | TextBox |
| Number input | `"Int"` | `[int]` | TextBox (numeric) |
| Toggle | `"Bool"` | `[switch]` | CheckBox |
| Comma-separated list | `"StringArray"` | `[string[]]` | TextBox |
| Code block | `"ScriptBlock"` | `[scriptblock]` | Multi-line TextBox |
| File/folder path | `"Path"` | `[string]` (path) | TextBox |
| Username/password | `"Credential"` | `[PSCredential]` | TextBox |
| Dropdown | `"Enum"` | `[ValidateSet()]` | ComboBox (uses `validValues`) |

## Port Configuration

| Scenario | inputCount | outputCount | Notes |
|----------|-----------|-------------|-------|
| Source node (no pipeline input) | `0` | `1` | e.g., `Get-Process`, `Get-ChildItem` |
| Pipeline filter (in → out) | `1` | `1` | e.g., `Where-Object`, `Sort-Object` — default |
| Sink node (no output) | `1` | `0` | e.g., `Remove-Item`, `Write-Host`, `Out-Null` |
| Multi-input node | `2+` | `1` | e.g., `Invoke-Command` (Credential + ScriptBlock) |
