namespace PoSHBlox.Models;

public enum PortDirection { Input, Output }

/// <summary>
/// Kind of pin: triangle exec pins drive execution order; data pins carry values.
/// </summary>
public enum PortKind { Exec, Data }

/// <summary>Legacy pipeline-flow port typing — V1 only. Removed in Step 9.</summary>
public enum PortType { String, Object, Array, Pipeline }

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
