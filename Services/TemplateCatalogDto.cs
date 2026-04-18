using System.Collections.Generic;

namespace PoSHBlox.Services;

public class TemplateCatalogDto
{
    /// <summary>
    /// Catalog schema version. Bumped to 3 when parameters gained the
    /// <see cref="ParameterDef.IsSwitch"/> flag — loader migrates v≤2 catalogs
    /// by treating every Bool param as a switch (the dominant case for cmdlet
    /// bools, and how they were serialized before the split).
    /// </summary>
    public int Version { get; set; } = 3;
    public string Category { get; set; } = "";

    /// <summary>
    /// Host ids that produced this catalog's metadata (e.g. <c>["pwsh-7.4.1"]</c>).
    /// Stamped by <see cref="TemplateRegenerator"/> on write; empty for hand-authored
    /// catalogs. Runtime uses this to warn when the active host differs from any
    /// host the catalog was introspected against.
    /// </summary>
    public List<string> IntrospectedHosts { get; set; } = [];

    public List<NodeTemplate> Templates { get; set; } = [];
}
