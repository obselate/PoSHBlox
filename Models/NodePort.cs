using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace PoSHBlox.Models;

public partial class NodePort : ObservableObject
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N")[..8];
    public string Name { get; set; } = "";
    public PortDirection Direction { get; set; }
    public PortType Type { get; set; } = PortType.Pipeline;
    public GraphNode? Owner { get; set; }

    [ObservableProperty] private double _anchorX;
    [ObservableProperty] private double _anchorY;
}
