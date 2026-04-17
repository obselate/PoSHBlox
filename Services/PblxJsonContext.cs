using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace PoSHBlox.Services;

// Source-generated JSON metadata for every type the app (de)serializes.
// Wiring callers through TypeInfoResolver = PblxJsonContext.Default avoids
// runtime reflection — required for trim/AOT safety and faster cold starts.
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(PblxDocument))]
[JsonSerializable(typeof(TemplateCatalogDto))]
[JsonSerializable(typeof(ClipboardSerializer.Payload))]
[JsonSerializable(typeof(List<DiscoveredCmdlet>))]
[JsonSerializable(typeof(RegenManifest))]
[JsonSerializable(typeof(AppSettingsData))]
public partial class PblxJsonContext : JsonSerializerContext
{
}
