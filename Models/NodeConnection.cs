using PoSHBlox.Services;

namespace PoSHBlox.Models;

public class NodeConnection
{
    public string Id { get; init; } = IdMint.ShortGuid();
    public NodePort Source { get; set; } = null!;
    public NodePort Target { get; set; } = null!;
}
