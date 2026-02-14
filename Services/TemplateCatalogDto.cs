using System.Collections.Generic;

namespace PoSHBlox.Services;

public class TemplateCatalogDto
{
    public int Version { get; set; } = 1;
    public string Category { get; set; } = "";
    public List<NodeTemplate> Templates { get; set; } = [];
}
