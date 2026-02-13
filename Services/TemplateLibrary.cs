using System.Collections.Generic;
using PoSHBlox.Models;

namespace PoSHBlox.Services;

/// <summary>
/// Central registry of all built-in node templates.
/// To add a new cmdlet or node type, just add an entry to GetAll().
/// </summary>
public static class TemplateLibrary
{
    public static List<NodeTemplate> GetAll() =>
    [
        // ── File / Folder ──────────────────────────────────────
        new()
        {
            Name = "Get-ChildItem", CmdletName = "Get-ChildItem",
            Category = "File / Folder",
            Description = "List files and folders",
            InputCount = 0, OutputCount = 1,
            InputNames = [], OutputNames = ["Files"],
            Parameters =
            [
                new() { Name = "Path", Type = ParamType.Path, IsMandatory = true, DefaultValue = "C:\\", Description = "Directory to list" },
                new() { Name = "Filter", Type = ParamType.String, Description = "Wildcard filter (e.g. *.txt)" },
                new() { Name = "Recurse", Type = ParamType.Bool, Description = "Include subdirectories" },
                new() { Name = "Depth", Type = ParamType.Int, Description = "Recursion depth limit" },
                new() { Name = "Force", Type = ParamType.Bool, Description = "Include hidden/system items" },
            ]
        },
        new()
        {
            Name = "Copy-Item", CmdletName = "Copy-Item",
            Category = "File / Folder",
            Description = "Copy files or folders",
            Parameters =
            [
                new() { Name = "Path", Type = ParamType.Path, Description = "Source path (or use pipeline)" },
                new() { Name = "Destination", Type = ParamType.Path, IsMandatory = true, Description = "Destination path" },
                new() { Name = "Recurse", Type = ParamType.Bool, Description = "Copy subdirectories" },
                new() { Name = "Force", Type = ParamType.Bool, Description = "Overwrite existing" },
                new() { Name = "PassThru", Type = ParamType.Bool, DefaultValue = "true", Description = "Output copied items for chaining" },
                new() { Name = "WhatIf", Type = ParamType.Bool, DefaultValue = "true", Description = "Preview without executing" },
            ]
        },
        new()
        {
            Name = "Move-Item", CmdletName = "Move-Item",
            Category = "File / Folder",
            Description = "Move files or folders",
            Parameters =
            [
                new() { Name = "Path", Type = ParamType.Path, Description = "Source path (or use pipeline)" },
                new() { Name = "Destination", Type = ParamType.Path, IsMandatory = true, Description = "Destination path" },
                new() { Name = "Force", Type = ParamType.Bool, Description = "Overwrite existing" },
                new() { Name = "PassThru", Type = ParamType.Bool, DefaultValue = "true", Description = "Output moved items for chaining" },
                new() { Name = "WhatIf", Type = ParamType.Bool, DefaultValue = "true", Description = "Preview without executing" },
            ]
        },
        new()
        {
            Name = "Remove-Item", CmdletName = "Remove-Item",
            Category = "File / Folder",
            Description = "Delete files or folders",
            InputCount = 1, OutputCount = 0,
            OutputNames = [],
            Parameters =
            [
                new() { Name = "Path", Type = ParamType.Path, Description = "Path to remove (or use pipeline)" },
                new() { Name = "Recurse", Type = ParamType.Bool, Description = "Remove subdirectories" },
                new() { Name = "Force", Type = ParamType.Bool, Description = "Remove hidden/read-only items" },
                new() { Name = "WhatIf", Type = ParamType.Bool, DefaultValue = "true", Description = "Preview without executing" },
            ]
        },
        new()
        {
            Name = "Get-Content", CmdletName = "Get-Content",
            Category = "File / Folder",
            Description = "Read file contents",
            InputCount = 0, OutputCount = 1,
            InputNames = [], OutputNames = ["Lines"],
            Parameters =
            [
                new() { Name = "Path", Type = ParamType.Path, IsMandatory = true, Description = "File to read" },
                new() { Name = "TotalCount", Type = ParamType.Int, Description = "Read only first N lines" },
                new() { Name = "Tail", Type = ParamType.Int, Description = "Read only last N lines" },
                new() { Name = "Encoding", Type = ParamType.Enum, Description = "File encoding", ValidValues = ["UTF8", "ASCII", "Unicode", "Default"] },
            ]
        },
        new()
        {
            Name = "Set-Content", CmdletName = "Set-Content",
            Category = "File / Folder",
            Description = "Write to a file",
            OutputCount = 0, OutputNames = [],
            Parameters =
            [
                new() { Name = "Path", Type = ParamType.Path, IsMandatory = true, Description = "File to write" },
                new() { Name = "Encoding", Type = ParamType.Enum, Description = "File encoding", ValidValues = ["UTF8", "ASCII", "Unicode", "Default"] },
                new() { Name = "Force", Type = ParamType.Bool, Description = "Create path if needed" },
            ]
        },
        new()
        {
            Name = "Get-Acl", CmdletName = "Get-Acl",
            Category = "File / Folder",
            Description = "Get file/folder permissions",
            Parameters =
            [
                new() { Name = "Path", Type = ParamType.Path, Description = "Path (or use pipeline)" },
            ]
        },

        // ── Process / Service ──────────────────────────────────
        new()
        {
            Name = "Get-Process", CmdletName = "Get-Process",
            Category = "Process / Service",
            Description = "List running processes",
            InputCount = 0, OutputCount = 1,
            InputNames = [], OutputNames = ["Processes"],
            Parameters =
            [
                new() { Name = "Name", Type = ParamType.StringArray, Description = "Filter by process name(s) (wildcards ok)" },
                new() { Name = "Id", Type = ParamType.Int, Description = "Filter by process ID" },
                new() { Name = "ComputerName", Type = ParamType.String, Description = "Remote computer" },
            ]
        },
        new()
        {
            Name = "Stop-Process", CmdletName = "Stop-Process",
            Category = "Process / Service",
            Description = "Kill processes",
            OutputCount = 0, OutputNames = [],
            Parameters =
            [
                new() { Name = "Name", Type = ParamType.String, Description = "Process name to stop" },
                new() { Name = "Id", Type = ParamType.Int, Description = "Process ID to stop" },
                new() { Name = "Force", Type = ParamType.Bool, Description = "Force termination" },
                new() { Name = "WhatIf", Type = ParamType.Bool, DefaultValue = "true", Description = "Preview without executing" },
            ]
        },
        new()
        {
            Name = "Get-Service", CmdletName = "Get-Service",
            Category = "Process / Service",
            Description = "List services",
            InputCount = 0, OutputCount = 1,
            InputNames = [], OutputNames = ["Services"],
            Parameters =
            [
                new() { Name = "Name", Type = ParamType.StringArray, Description = "Service name(s) (wildcards ok)" },
                new() { Name = "DisplayName", Type = ParamType.StringArray, Description = "Filter by display name(s)" },
                new() { Name = "ComputerName", Type = ParamType.String, Description = "Remote computer" },
                new() { Name = "DependentServices", Type = ParamType.Bool, Description = "Include dependent services" },
                new() { Name = "RequiredServices", Type = ParamType.Bool, Description = "Include required services" },
            ]
        },
        new()
        {
            Name = "Start-Service", CmdletName = "Start-Service",
            Category = "Process / Service",
            Description = "Start a service",
            Parameters =
            [
                new() { Name = "Name", Type = ParamType.String, IsMandatory = true, Description = "Service name" },
                new() { Name = "PassThru", Type = ParamType.Bool, DefaultValue = "true", Description = "Output service object for chaining" },
                new() { Name = "WhatIf", Type = ParamType.Bool, DefaultValue = "true", Description = "Preview without executing" },
            ]
        },
        new()
        {
            Name = "Stop-Service", CmdletName = "Stop-Service",
            Category = "Process / Service",
            Description = "Stop a service",
            Parameters =
            [
                new() { Name = "Name", Type = ParamType.String, IsMandatory = true, Description = "Service name" },
                new() { Name = "Force", Type = ParamType.Bool, Description = "Force stop dependent services" },
                new() { Name = "PassThru", Type = ParamType.Bool, DefaultValue = "true", Description = "Output service object for chaining" },
                new() { Name = "WhatIf", Type = ParamType.Bool, DefaultValue = "true", Description = "Preview without executing" },
            ]
        },
        new()
        {
            Name = "Restart-Service", CmdletName = "Restart-Service",
            Category = "Process / Service",
            Description = "Restart a service",
            Parameters =
            [
                new() { Name = "Name", Type = ParamType.String, IsMandatory = true, Description = "Service name" },
                new() { Name = "Force", Type = ParamType.Bool, Description = "Force restart dependent services" },
                new() { Name = "PassThru", Type = ParamType.Bool, DefaultValue = "true", Description = "Output service object for chaining" },
                new() { Name = "WhatIf", Type = ParamType.Bool, DefaultValue = "true", Description = "Preview without executing" },
            ]
        },

        // ── Registry ───────────────────────────────────────────
        new()
        {
            Name = "Get-ItemProperty", CmdletName = "Get-ItemProperty",
            Category = "Registry",
            Description = "Read registry values",
            InputCount = 0, OutputCount = 1,
            InputNames = [], OutputNames = ["Values"],
            Parameters =
            [
                new() { Name = "Path", Type = ParamType.Path, IsMandatory = true, DefaultValue = "HKLM:\\SOFTWARE\\", Description = "Registry key path" },
                new() { Name = "Name", Type = ParamType.String, Description = "Specific value name" },
            ]
        },
        new()
        {
            Name = "Set-ItemProperty", CmdletName = "Set-ItemProperty",
            Category = "Registry",
            Description = "Write a registry value",
            InputCount = 1, OutputCount = 0,
            OutputNames = [],
            Parameters =
            [
                new() { Name = "Path", Type = ParamType.Path, IsMandatory = true, DefaultValue = "HKLM:\\SOFTWARE\\", Description = "Registry key path" },
                new() { Name = "Name", Type = ParamType.String, IsMandatory = true, Description = "Value name" },
                new() { Name = "Value", Type = ParamType.String, IsMandatory = true, Description = "Value data" },
                new() { Name = "Type", Type = ParamType.Enum, Description = "Registry value type", ValidValues = ["String", "ExpandString", "DWord", "QWord", "Binary", "MultiString"] },
                new() { Name = "Force", Type = ParamType.Bool, Description = "Create property if it doesn't exist" },
                new() { Name = "WhatIf", Type = ParamType.Bool, DefaultValue = "true", Description = "Preview without executing" },
            ]
        },
        new()
        {
            Name = "New-Item (Reg Key)", CmdletName = "New-Item",
            Category = "Registry",
            Description = "Create a registry key",
            InputCount = 0, OutputCount = 1,
            InputNames = [], OutputNames = ["Key"],
            Parameters =
            [
                new() { Name = "Path", Type = ParamType.Path, IsMandatory = true, DefaultValue = "HKLM:\\SOFTWARE\\MyApp", Description = "Registry key to create" },
                new() { Name = "Force", Type = ParamType.Bool, Description = "Overwrite if exists" },
                new() { Name = "WhatIf", Type = ParamType.Bool, DefaultValue = "true", Description = "Preview without executing" },
            ]
        },
        new()
        {
            Name = "Remove-ItemProperty", CmdletName = "Remove-ItemProperty",
            Category = "Registry",
            Description = "Delete a registry value",
            InputCount = 1, OutputCount = 0,
            OutputNames = [],
            Parameters =
            [
                new() { Name = "Path", Type = ParamType.Path, IsMandatory = true, Description = "Registry key path" },
                new() { Name = "Name", Type = ParamType.String, IsMandatory = true, Description = "Value name to delete" },
                new() { Name = "Force", Type = ParamType.Bool, Description = "Remove read-only or protected values" },
                new() { Name = "WhatIf", Type = ParamType.Bool, DefaultValue = "true", Description = "Preview without executing" },
            ]
        },

        // ── Network / Remote ───────────────────────────────────
        new()
        {
            Name = "Test-Connection", CmdletName = "Test-Connection",
            Category = "Network / Remote",
            Description = "Ping a host",
            InputCount = 0, OutputCount = 1,
            InputNames = [], OutputNames = ["Results"],
            Parameters =
            [
                new() { Name = "ComputerName", Type = ParamType.StringArray, IsMandatory = true, Description = "Host(s) to ping, comma-separated" },
                new() { Name = "Count", Type = ParamType.Int, DefaultValue = "4", Description = "Number of pings" },
                new() { Name = "Quiet", Type = ParamType.Bool, Description = "Return only True/False" },
            ]
        },
        new()
        {
            Name = "Invoke-Command", CmdletName = "Invoke-Command",
            Category = "Network / Remote",
            Description = "Run script on remote machines",
            InputCount = 2, OutputCount = 1,
            InputNames = ["Credential", "ScriptBlock"], OutputNames = ["Results"],
            Parameters =
            [
                new() { Name = "ComputerName", Type = ParamType.StringArray, IsMandatory = true, Description = "Remote computer(s), comma-separated" },
            ]
        },
        new()
        {
            Name = "Test-NetConnection", CmdletName = "Test-NetConnection",
            Category = "Network / Remote",
            Description = "Test TCP port connectivity",
            InputCount = 0, OutputCount = 1,
            InputNames = [], OutputNames = ["Result"],
            Parameters =
            [
                new() { Name = "ComputerName", Type = ParamType.String, IsMandatory = true, Description = "Host to test" },
                new() { Name = "Port", Type = ParamType.Int, Description = "TCP port to test" },
                new() { Name = "InformationLevel", Type = ParamType.Enum, Description = "Detail level", ValidValues = ["Quiet", "Detailed"] },
            ]
        },
        new()
        {
            Name = "Get-NetAdapter", CmdletName = "Get-NetAdapter",
            Category = "Network / Remote",
            Description = "List network adapters",
            InputCount = 0, OutputCount = 1,
            InputNames = [], OutputNames = ["Adapters"],
            Parameters =
            [
                new() { Name = "Name", Type = ParamType.String, Description = "Adapter name filter" },
                new() { Name = "Physical", Type = ParamType.Bool, Description = "Physical adapters only" },
            ]
        },
        new()
        {
            Name = "Get-NetIPAddress", CmdletName = "Get-NetIPAddress",
            Category = "Network / Remote",
            Description = "Get IP addresses",
            InputCount = 0, OutputCount = 1,
            InputNames = [], OutputNames = ["IPs"],
            Parameters =
            [
                new() { Name = "AddressFamily", Type = ParamType.Enum, DefaultValue = "IPv4", Description = "Address family", ValidValues = ["IPv4", "IPv6"] },
                new() { Name = "InterfaceAlias", Type = ParamType.String, Description = "Interface name filter" },
            ]
        },

        // ── String / Data ──────────────────────────────────────
        new()
        {
            Name = "Where-Object",
            Category = "String / Data",
            Description = "Filter pipeline objects",
            ScriptBody = "$input | Where-Object {\n    $_.Name -like \"*pattern*\"\n}",
        },
        new()
        {
            Name = "Select-Object", CmdletName = "Select-Object",
            Category = "String / Data",
            Description = "Pick specific properties",
            Parameters =
            [
                new() { Name = "Property", Type = ParamType.StringArray, IsMandatory = true, DefaultValue = "Name, Status", Description = "Property names, comma-separated" },
                new() { Name = "First", Type = ParamType.Int, Description = "Take first N objects" },
                new() { Name = "Last", Type = ParamType.Int, Description = "Take last N objects" },
                new() { Name = "Unique", Type = ParamType.Bool, Description = "Remove duplicates" },
            ]
        },
        new()
        {
            Name = "Sort-Object", CmdletName = "Sort-Object",
            Category = "String / Data",
            Description = "Sort pipeline objects",
            Parameters =
            [
                new() { Name = "Property", Type = ParamType.StringArray, IsMandatory = true, DefaultValue = "Name", Description = "Property(s) to sort by" },
                new() { Name = "Descending", Type = ParamType.Bool, Description = "Sort descending" },
                new() { Name = "Unique", Type = ParamType.Bool, Description = "Remove duplicates" },
            ]
        },
        new()
        {
            Name = "Group-Object", CmdletName = "Group-Object",
            Category = "String / Data",
            Description = "Group by property",
            Parameters =
            [
                new() { Name = "Property", Type = ParamType.StringArray, IsMandatory = true, DefaultValue = "Status", Description = "Property(s) to group by" },
                new() { Name = "NoElement", Type = ParamType.Bool, Description = "Omit group members (count only)" },
            ]
        },
        new()
        {
            Name = "ForEach-Object",
            Category = "String / Data",
            Description = "Transform each object",
            ScriptBody = "$input | ForEach-Object {\n    $_\n}",
        },
        new()
        {
            Name = "Export-Csv", CmdletName = "Export-Csv",
            Category = "String / Data",
            Description = "Export to CSV file",
            OutputCount = 0, OutputNames = [],
            Parameters =
            [
                new() { Name = "Path", Type = ParamType.Path, IsMandatory = true, DefaultValue = ".\\output.csv", Description = "Output file path" },
                new() { Name = "NoTypeInformation", Type = ParamType.Bool, DefaultValue = "true", Description = "Omit type header" },
                new() { Name = "Delimiter", Type = ParamType.String, Description = "Field delimiter (default comma)" },
                new() { Name = "Append", Type = ParamType.Bool, Description = "Append to existing file" },
                new() { Name = "Encoding", Type = ParamType.Enum, Description = "File encoding", ValidValues = ["UTF8", "ASCII", "Unicode", "Default"] },
            ]
        },
        new()
        {
            Name = "ConvertTo-Json", CmdletName = "ConvertTo-Json",
            Category = "String / Data",
            Description = "Convert objects to JSON",
            Parameters =
            [
                new() { Name = "Depth", Type = ParamType.Int, DefaultValue = "3", Description = "Serialization depth" },
                new() { Name = "Compress", Type = ParamType.Bool, Description = "Minified output" },
            ]
        },
        new()
        {
            Name = "Select-String", CmdletName = "Select-String",
            Category = "String / Data",
            Description = "Regex match in text",
            Parameters =
            [
                new() { Name = "Pattern", Type = ParamType.String, IsMandatory = true, Description = "Regex pattern to match" },
                new() { Name = "Path", Type = ParamType.Path, Description = "File(s) to search (or use pipeline)" },
                new() { Name = "CaseSensitive", Type = ParamType.Bool, Description = "Case-sensitive matching" },
                new() { Name = "SimpleMatch", Type = ParamType.Bool, Description = "Literal string match (not regex)" },
            ]
        },
        new()
        {
            Name = "Measure-Object", CmdletName = "Measure-Object",
            Category = "String / Data",
            Description = "Count, sum, average",
            Parameters =
            [
                new() { Name = "Property", Type = ParamType.StringArray, Description = "Property(s) to measure" },
                new() { Name = "Sum", Type = ParamType.Bool, Description = "Calculate sum" },
                new() { Name = "Average", Type = ParamType.Bool, Description = "Calculate average" },
                new() { Name = "Maximum", Type = ParamType.Bool, Description = "Find maximum" },
                new() { Name = "Minimum", Type = ParamType.Bool, Description = "Find minimum" },
            ]
        },

        // ── Output ─────────────────────────────────────────────
        new()
        {
            Name = "Write-Host",
            CmdletName = "Write-Host",
            Category = "Output",
            Description = "Write colored text to console (not pipeline)",
            OutputCount = 0, OutputNames = [],
            Parameters = new List<ParameterDef>
            {
                new ParameterDef { Name = "Object", Type = ParamType.String, Description = "Text or object to display (leave blank to display pipeline input)" },
                new ParameterDef { Name = "ForegroundColor", Type = ParamType.Enum, Description = "Text color", ValidValues = ["Black", "DarkBlue", "DarkGreen", "DarkCyan", "DarkRed", "DarkMagenta", "DarkYellow", "Gray", "DarkGray", "Blue", "Green", "Cyan", "Red", "Magenta", "Yellow", "White"] },
                new ParameterDef { Name = "BackgroundColor", Type = ParamType.Enum, Description = "Background color", ValidValues = ["Black", "DarkBlue", "DarkGreen", "DarkCyan", "DarkRed", "DarkMagenta", "DarkYellow", "Gray", "DarkGray", "Blue", "Green", "Cyan", "Red", "Magenta", "Yellow", "White"] },
                new ParameterDef { Name = "NoNewline", Type = ParamType.Bool, Description = "Don't append newline" },
            },
        },
        new()
        {
            Name = "Write-Output",
            CmdletName = "Write-Output",
            Category = "Output",
            Description = "Write objects to the pipeline (default behavior)",
            Parameters = new List<ParameterDef>
            {
                new ParameterDef { Name = "InputObject", Type = ParamType.String, Description = "Object to output" },
            },
        },
        new()
        {
            Name = "Write-Warning",
            CmdletName = "Write-Warning",
            Category = "Output",
            Description = "Write a warning message",
            OutputCount = 0, OutputNames = [],
            Parameters = new List<ParameterDef>
            {
                new ParameterDef { Name = "Message", Type = ParamType.String, IsMandatory = true, Description = "Warning text" },
            },
        },
        new()
        {
            Name = "Write-Error",
            CmdletName = "Write-Error",
            Category = "Output",
            Description = "Write a non-terminating error",
            OutputCount = 0, OutputNames = [],
            Parameters = new List<ParameterDef>
            {
                new ParameterDef { Name = "Message", Type = ParamType.String, IsMandatory = true, Description = "Error text" },
            },
        },
        new()
        {
            Name = "Write-Verbose",
            CmdletName = "Write-Verbose",
            Category = "Output",
            Description = "Write verbose output (requires -Verbose or $VerbosePreference)",
            OutputCount = 0, OutputNames = [],
            Parameters = new List<ParameterDef>
            {
                new ParameterDef { Name = "Message", Type = ParamType.String, IsMandatory = true, Description = "Verbose text" },
            },
        },
        new()
        {
            Name = "Out-Host",
            CmdletName = "Out-Host",
            Category = "Output",
            Description = "Send output to the console host",
            OutputCount = 0, OutputNames = [],
        },
        new()
        {
            Name = "Out-Null",
            CmdletName = "Out-Null",
            Category = "Output",
            Description = "Suppress output (discard pipeline objects)",
            OutputCount = 0, OutputNames = [],
        },
        new()
        {
            Name = "Format-Table",
            CmdletName = "Format-Table",
            Category = "Output",
            Description = "Format output as a table",
            Parameters = new List<ParameterDef>
            {
                new ParameterDef { Name = "Property", Type = ParamType.StringArray, Description = "Properties to display (comma-separated)" },
                new ParameterDef { Name = "AutoSize", Type = ParamType.Bool, Description = "Auto-size columns" },
                new ParameterDef { Name = "Wrap", Type = ParamType.Bool, Description = "Wrap long text" },
            },
        },
        new()
        {
            Name = "Format-List",
            CmdletName = "Format-List",
            Category = "Output",
            Description = "Format output as property list",
            Parameters = new List<ParameterDef>
            {
                new ParameterDef { Name = "Property", Type = ParamType.StringArray, Description = "Properties to display (comma-separated or *)" },
            },
        },

        // ── Control Flow (Containers) ──────────────────────────
        new()
        {
            Name = "If / Else",
            Category = "Control Flow",
            Description = "Conditional branch - drop nodes into Then/Else zones",
            ContainerType = ContainerType.IfElse,
        },
        new()
        {
            Name = "ForEach Loop",
            Category = "Control Flow",
            Description = "Loop over a collection - drop nodes into Body zone",
            ContainerType = ContainerType.ForEach,
        },
        new()
        {
            Name = "Try / Catch",
            Category = "Control Flow",
            Description = "Error handling - drop nodes into Try/Catch zones",
            ContainerType = ContainerType.TryCatch,
        },
        new()
        {
            Name = "While Loop",
            Category = "Control Flow",
            Description = "Loop while condition is true - drop nodes into Body zone",
            ContainerType = ContainerType.While,
        },

        // ── Function (Container) ───────────────────────────────
        new()
        {
            Name = "Function",
            Category = "Function",
            Description = "Named function - chain nodes inside as a pipeline with clear input/output",
            ContainerType = ContainerType.Function,
        },

        // ── Custom ─────────────────────────────────────────────
        new()
        {
            Name = "Custom Script",
            Category = "Custom",
            Description = "Empty script block - write anything",
            ScriptBody = "# Your PowerShell 5.1 code here\n$input | ForEach-Object {\n    $_\n}",
        },
        new()
        {
            Name = "Variable Source",
            Category = "Custom",
            Description = "Define a variable or data source",
            ScriptBody = "@(\n    \"Server01\",\n    \"Server02\",\n    \"Server03\"\n)",
            InputCount = 0, OutputCount = 1,
            InputNames = [], OutputNames = ["Data"],
        },
    ];
}
