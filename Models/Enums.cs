namespace PoSHBlox.Models;

public enum PortDirection { Input, Output }

public enum PortType { String, Object, Array, Pipeline }

public enum ParamType
{
    String,
    Int,
    Bool,
    StringArray,
    ScriptBlock,
    Path,
    Credential,
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
