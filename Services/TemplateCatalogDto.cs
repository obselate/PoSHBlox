using System.Collections.Generic;

namespace PoSHBlox.Services;

public class TemplateCatalogDto
{
    public int Version { get; set; } = 2;
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
