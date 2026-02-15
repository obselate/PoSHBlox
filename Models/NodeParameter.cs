using System;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;

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

    /// <summary>Maps ParamType to PowerShell type accelerator for function signatures.</summary>
    public string PowerShellTypeName => Type switch
    {
        ParamType.String or ParamType.Path => "string",
        ParamType.Int => "int",
        ParamType.Bool => "switch",
        ParamType.StringArray => "string[]",
        ParamType.ScriptBlock => "scriptblock",
        _ => "object",
    };

    [ObservableProperty] private string _value = "";

    // ── Type-based UI helpers ──────────────────────────────────

    public bool IsBool => Type == ParamType.Bool;
    public bool IsEnum => Type == ParamType.Enum && ValidValues.Length > 0;
    public bool IsTextInput => !IsBool && !IsEnum;

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

            ParamType.StringArray =>
                $"-{Name} @({string.Join(", ", val.Split(',', StringSplitOptions.TrimEntries).Select(s => $"\"{s}\""))})",

            ParamType.ScriptBlock =>
                $"-{Name} {{ {val} }}",

            ParamType.Enum =>
                $"-{Name} \"{val}\"",

            _ => $"-{Name} \"{val}\""
        };
    }
}
