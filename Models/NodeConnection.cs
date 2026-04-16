using System;

namespace PoSHBlox.Models;

public class NodeConnection
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N")[..8];
    public NodePort Source { get; set; } = null!;
    public NodePort Target { get; set; } = null!;
}
