using System;

namespace PoSHBlox.Services;

/// <summary>
/// Central factory for the 8-char short IDs used on nodes, ports, and
/// connections. Keeps the ID strategy auditable from one place — if we ever
/// need to lengthen them or switch to a sortable scheme, every mint site
/// migrates together.
/// </summary>
public static class IdMint
{
    public static string ShortGuid() => Guid.NewGuid().ToString("N")[..8];
}
