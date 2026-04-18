using System.Linq;

namespace PoSHBlox.Services;

/// <summary>
/// V2 → V3 migration: PowerShell <c>[switch]</c> parameters split out from
/// typed <c>[bool]</c>. Pre-v3 files carry Bool-typed params without an
/// <c>IsSwitch</c> flag — and the dominant case for cmdlet bool params was
/// switches, so flip <c>IsSwitch = true</c> for every Bool param, then drop
/// the corresponding data-input pins (switches are checkbox badges now, not
/// wireable ports). Connections that landed on those pins are pruned too.
/// </summary>
internal static class V2ToV3Migrator
{
    public static void Migrate(PblxDocument doc)
    {
        // Collect pin IDs we're about to strip so we can drop stale connections.
        var orphanedPinIds = new System.Collections.Generic.HashSet<string>();

        foreach (var node in doc.Nodes)
        {
            var switchParamNames = new System.Collections.Generic.HashSet<string>(
                System.StringComparer.OrdinalIgnoreCase);

            foreach (var p in node.Parameters)
            {
                if (p.Type == nameof(PoSHBlox.Models.ParamType.Bool) && !p.IsSwitch)
                {
                    p.IsSwitch = true;
                    switchParamNames.Add(p.Name);
                }
            }

            if (switchParamNames.Count == 0) continue;

            // Drop input pins paired with switch params — keep the DTO shape
            // aligned with what NodeFactory would emit post-split.
            var toRemove = node.Inputs
                .Where(port => port.Kind == nameof(PoSHBlox.Models.PortKind.Data)
                               && !string.IsNullOrEmpty(port.ParameterName)
                               && switchParamNames.Contains(port.ParameterName))
                .ToList();

            foreach (var port in toRemove)
            {
                orphanedPinIds.Add(port.Id);
                node.Inputs.Remove(port);
            }
        }

        if (orphanedPinIds.Count > 0)
        {
            doc.Connections = doc.Connections
                .Where(c => !orphanedPinIds.Contains(c.SourcePortId)
                         && !orphanedPinIds.Contains(c.TargetPortId))
                .ToList();
        }

        doc.Version = 3;
    }
}
