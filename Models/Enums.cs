namespace PoSHBlox.Models;

public enum PortDirection { Input, Output }

/// <summary>
/// Kind of pin: triangle exec pins drive execution order; data pins carry values.
/// </summary>
public enum PortKind { Exec, Data }

/// <summary>
/// Unified type system for both data pins and parameters.
/// <c>Any</c> matches anything (used for script-body outputs and untyped pipeline data).
/// </summary>
public enum ParamType
{
    Any,
    String,
    Int,
    Bool,
    Path,
    StringArray,
    Object,
    Collection,
    ScriptBlock,
    Credential,
    HashTable,
    Enum,
}

public enum ContainerType
{
    None,
    IfElse,
    ForEach,
    TryCatch,
    While,
    Function,
    Label,
}

/// <summary>
/// Distinguishes the structural role of a node. <c>Cmdlet</c> is the default —
/// full V2 shape (exec pins, per-param data inputs, data outputs). <c>Value</c>
/// is a compact literal/variable-reference producer: no exec pins, no param
/// pins, one typed data output whose codegen emits the literal expression
/// (<c>$true</c>, <c>$env:ComputerName</c>, …) directly inline at the
/// consumer. Containers use <c>Cmdlet</c> and are distinguished by
/// <see cref="ContainerType"/>.
/// </summary>
public enum NodeKind
{
    Cmdlet,
    Value,
}
