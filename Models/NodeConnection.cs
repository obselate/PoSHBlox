using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace PoSHBlox.Models;

public partial class NodeConnection : ObservableObject
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N")[..8];
    public NodePort Source { get; set; } = null!;
    public NodePort Target { get; set; } = null!;

    [ObservableProperty] private double _sourceX;
    [ObservableProperty] private double _sourceY;
    [ObservableProperty] private double _targetX;
    [ObservableProperty] private double _targetY;
}
