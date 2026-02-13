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

    [ObservableProperty] private string _value = "";

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
