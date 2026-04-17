using System;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using PoSHBlox.Services;

namespace PoSHBlox.Models;

public partial class NodeParameter : ObservableObject
{
    public string Name { get; set; } = "";
    public ParamType Type { get; set; } = ParamType.String;
    public bool IsMandatory { get; set; }
    public string DefaultValue { get; set; } = "";
    public string Description { get; set; } = "";
    public string[] ValidValues { get; set; } = [];

    /// <summary>true = function argument, not a node config param</summary>
    public bool IsArgument { get; set; }

    /// <summary>true = [Parameter(ValueFromPipeline)]</summary>
    public bool IsPipelineInput { get; set; }

    /// <summary>
    /// True = panel-only config knob; no data-input pin is generated for this
    /// parameter. Used for container meta-settings whose value must be a
    /// compile-time string (e.g. ForEach's IterationVariable name) — wiring a
    /// runtime expression into them doesn't make sense.
    /// </summary>
    public bool IsConfigOnly { get; set; }

    /// <summary>
    /// Parameter sets this param belongs to. Empty = visible in every set
    /// (common params, legacy templates). Non-empty = visible only when
    /// the node's <see cref="GraphNode.ActiveParameterSet"/> is in this list.
    /// </summary>
    public string[] ParameterSets { get; set; } = [];

    /// <summary>Sets in which this param is mandatory. Falls back to <see cref="IsMandatory"/> when empty.</summary>
    public string[] MandatoryInSets { get; set; } = [];

    /// <summary>
    /// Editions the parameter was introspected for. Empty = universal / legacy
    /// catalog without host metadata — no warning fires. Populated from the
    /// merged catalog via <see cref="ParameterDef.SupportedEditions"/>.
    /// </summary>
    public string[] SupportedEditions { get; set; } = [];

    /// <summary>
    /// False only when <see cref="SupportedEditions"/> is non-empty and the active
    /// host's edition isn't in it. UI binds to this to surface a "not introspected
    /// for &lt;edition&gt;" hint.
    /// </summary>
    public bool IsSupportedByActiveHost
    {
        get
        {
            if (SupportedEditions.Length == 0) return true;
            var active = PowerShellHostRegistry.Active?.Edition;
            return active != null && SupportedEditions.Contains(active, StringComparer.OrdinalIgnoreCase);
        }
    }

    /// <summary>Inverse of <see cref="IsSupportedByActiveHost"/> for IsVisible bindings.</summary>
    public bool IsMissingForActiveHost => !IsSupportedByActiveHost;

    /// <summary>
    /// Human label for the warning line — "Not introspected for pwsh". Empty when
    /// the param is supported by the active host.
    /// </summary>
    public string MissingHostLabel =>
        IsMissingForActiveHost
            ? $"Not introspected for {PowerShellHostRegistry.Active?.Edition ?? "active host"}"
            : "";

    /// <summary>
    /// Fired by the owning view model when <see cref="PowerShellHostRegistry.ActiveHostChanged"/>
    /// fires so bindings to <see cref="IsSupportedByActiveHost"/> / <see cref="IsMissingForActiveHost"/>
    /// / <see cref="MissingHostLabel"/> refresh. Avoids a static event subscription
    /// on the model (which would leak handlers for every spawned param).
    /// </summary>
    public void NotifyHostCompatChanged()
    {
        OnPropertyChanged(nameof(IsSupportedByActiveHost));
        OnPropertyChanged(nameof(IsMissingForActiveHost));
        OnPropertyChanged(nameof(MissingHostLabel));
    }

    /// <summary>
    /// Maintained by the view model: true when the param belongs to the node's
    /// currently-active parameter set (or when the param doesn't declare any
    /// sets — common params are always in scope).
    /// </summary>
    [ObservableProperty] private bool _isInActiveSet = true;

    /// <summary>
    /// Maintained by the view model: true when the param is mandatory given
    /// the node's current parameter set. Falls back to <see cref="IsMandatory"/>
    /// when the param declares no per-set mandatory flags (legacy / non-set
    /// cmdlets). The properties panel's red asterisk and the validator's
    /// missing-mandatory check both read this.
    /// </summary>
    [ObservableProperty] private bool _isEffectivelyMandatory;

    /// <summary>Composite visibility gate used by the properties-panel ItemTemplate.</summary>
    public bool ShouldRenderInPanel => !IsArgument && IsInActiveSet;

    partial void OnIsInActiveSetChanged(bool value) => OnPropertyChanged(nameof(ShouldRenderInPanel));

    /// <summary>Which node owns this parameter. Set by NodeFactory; null for loose ParameterDefs.</summary>
    public GraphNode? Owner { get; set; }

    /// <summary>
    /// The data-input pin paired with this parameter (Owner.Inputs with matching ParameterName).
    /// Null if the parameter has no paired pin (function arguments, legacy nodes).
    /// </summary>
    public NodePort? InputPort =>
        Owner?.Inputs.FirstOrDefault(p => p.ParameterName == Name);

    /// <summary>
    /// True when <see cref="InputPort"/> has at least one incoming wire. Set by
    /// the view model on every connection change — the parameter doesn't observe
    /// the graph itself.
    /// </summary>
    [ObservableProperty] private bool _isWired;

    /// <summary>
    /// Label used by the properties panel when <see cref="IsWired"/> is true —
    /// typically <c>"← SourceNode.PinName"</c>. Empty otherwise.
    /// </summary>
    [ObservableProperty] private string _wiredFromLabel = "";

    /// <summary>Convenience negation of <see cref="IsWired"/> for IsVisible bindings.</summary>
    public bool IsUnwired => !IsWired;

    /// <summary>
    /// True when the properties panel should show the full Description text.
    /// Toggled by clicking the description line. Default false = 2-line preview.
    /// </summary>
    [ObservableProperty] private bool _isDescriptionExpanded;

    partial void OnIsWiredChanged(bool value) => OnPropertyChanged(nameof(IsUnwired));

    /// <summary>Maps ParamType to PowerShell type accelerator for function signatures.</summary>
    public string PowerShellTypeName => Type switch
    {
        ParamType.String or ParamType.Path => "string",
        ParamType.Int => "int",
        ParamType.Bool => "switch",
        ParamType.StringArray => "string[]",
        ParamType.ScriptBlock => "scriptblock",
        ParamType.Credential => "PSCredential",
        ParamType.HashTable => "hashtable",
        ParamType.Collection => "object[]",
        _ => "object",
    };

    [ObservableProperty] private string _value = "";

    // ── Type-based UI helpers ──────────────────────────────────

    public bool IsBool => Type == ParamType.Bool;
    public bool IsEnum => Type == ParamType.Enum && ValidValues.Length > 0;
    public bool IsTextInput => !IsBool && !IsEnum;

    /// <summary>
    /// True when the properties-panel value editor should offer a Browse button
    /// that launches a file picker. Matches catalog entries the user typically
    /// fills with a filesystem path: <c>ParamType.Path</c> directly, or the
    /// common path-named <c>string[]</c> params (<c>Import-Csv</c>'s <c>Path</c>
    /// is <c>StringArray</c> because the cmdlet binds <c>string[]</c>). Registry
    /// <c>Path</c> params share the same <c>ParamType.Path</c> classification
    /// and also get the button — harmless false positive; user can cancel.
    /// </summary>
    public bool IsPathLike => Type == ParamType.Path
        || (Type == ParamType.StringArray && IsPathLikeName(Name));

    /// <summary>
    /// Parameter accepts multiple paths at once (<c>string[]</c>). Tells the
    /// picker to allow multi-select and the caller to join with commas, which
    /// <see cref="ToPowerShellArg"/> already expands to a PS array literal.
    /// </summary>
    public bool AllowsMultiplePaths => IsPathLike && Type == ParamType.StringArray;

    private static bool IsPathLikeName(string name) => name switch
    {
        "Path" or "LiteralPath" or "FilePath" or "OutFile" or "File" => true,
        _ => false,
    };

    /// <summary>
    /// Bool-typed wrapper for CheckBox binding.
    /// Reads EffectiveValue; writes to Value as "true"/"false".
    /// </summary>
    public bool BoolValue
    {
        get => EffectiveValue.Equals("true", StringComparison.OrdinalIgnoreCase);
        set
        {
            Value = value ? "true" : "false";
            OnPropertyChanged();
        }
    }

    private const string EnumDefaultLabel = "(Default)";

    /// <summary>
    /// Items for the Enum ComboBox. Non-mandatory enums get a "(Default)" entry
    /// that maps back to empty string, allowing the user to clear their selection.
    /// </summary>
    public string[] EnumOptions => !IsMandatory && ValidValues.Length > 0
        ? new[] { EnumDefaultLabel }.Concat(ValidValues).ToArray()
        : ValidValues;

    /// <summary>
    /// Enum-typed wrapper for ComboBox binding.
    /// Maps "(Default)" ↔ "" so selecting it clears Value and omits the parameter.
    /// </summary>
    public string SelectedEnumValue
    {
        get => string.IsNullOrEmpty(Value) ? (IsMandatory ? "" : EnumDefaultLabel) : Value;
        set
        {
            Value = value == EnumDefaultLabel ? "" : value;
            OnPropertyChanged();
        }
    }

    partial void OnValueChanged(string value)
    {
        OnPropertyChanged(nameof(EffectiveValue));
        if (IsBool) OnPropertyChanged(nameof(BoolValue));
        if (IsEnum) OnPropertyChanged(nameof(SelectedEnumValue));
    }

    /// <summary>
    /// Resolve the effective value: user-entered value if present, otherwise default.
    /// Returns empty string if neither is set.
    /// </summary>
    public string EffectiveValue
    {
        get
        {
            var val = string.IsNullOrWhiteSpace(Value) ? DefaultValue : Value;
            return val?.Trim() ?? "";
        }
    }

    /// <summary>
    /// Format this parameter as a PowerShell command-line argument.
    /// Returns empty string if the value is blank (parameter will be omitted).
    /// </summary>
    public string ToPowerShellArg()
    {
        var val = EffectiveValue;
        if (string.IsNullOrWhiteSpace(val)) return "";

        return Type switch
        {
            ParamType.String or ParamType.Path =>
                $"-{Name} \"{val.Replace("\"", "`\"")}\"",

            ParamType.Int =>
                $"-{Name} {val}",

            ParamType.Bool =>
                val.Equals("true", StringComparison.OrdinalIgnoreCase) ? $"-{Name}" : "",

            // Collection and StringArray both receive a comma-separated string
            // from the UI and expand to a PowerShell array. Without this, a value
            // like "A,B,C" would emit as `-Property "A,B,C"`, which cmdlets like
            // Select-Object treat as a single literal property name.
            ParamType.StringArray or ParamType.Collection =>
                $"-{Name} @({string.Join(", ", val.Split(',', StringSplitOptions.TrimEntries).Select(s => $"\"{s}\""))})",

            ParamType.ScriptBlock =>
                $"-{Name} {{ {val} }}",

            ParamType.Enum =>
                $"-{Name} \"{val}\"",

            _ => $"-{Name} \"{val}\""
        };
    }

    /// <summary>
    /// Format this parameter as a splat-hashtable entry (<c>Name = value</c>).
    /// Returns empty string if the value is blank or the parameter should be
    /// omitted (false-valued switches). The leading indentation is the caller's
    /// responsibility; this returns the key-value text alone.
    /// </summary>
    public string ToSplatEntry()
    {
        var val = EffectiveValue;
        if (string.IsNullOrWhiteSpace(val)) return "";

        return Type switch
        {
            ParamType.String or ParamType.Path =>
                $"{Name} = \"{val.Replace("\"", "`\"")}\"",

            ParamType.Int =>
                $"{Name} = {val}",

            // Switches in splat form get explicit $true (no omit-when-false
            // semantic like the inline form — if the value is 'false' we
            // skipped above via the IsNullOrWhiteSpace guard catching empty,
            // but an explicit "false" still emits; splat can legitimately
            // carry false).
            ParamType.Bool =>
                $"{Name} = ${val.ToLowerInvariant()}",

            ParamType.StringArray or ParamType.Collection =>
                $"{Name} = @({string.Join(", ", val.Split(',', StringSplitOptions.TrimEntries).Select(s => $"\"{s}\""))})",

            ParamType.ScriptBlock =>
                $"{Name} = {{ {val} }}",

            ParamType.Enum =>
                $"{Name} = \"{val}\"",

            _ => $"{Name} = \"{val}\""
        };
    }
}
