using System.Text.Json.Serialization;

namespace ScratchpadLite.Core.Model;

/// <summary>
/// Source-generated JSON for recovery files (Section 6.3). Polymorphic through
/// the <c>[JsonDerivedType]</c> attributes on <see cref="Block"/> and
/// <see cref="Inline"/>; image bytes serialize as base64.
/// </summary>
/// <remarks>Phase 4.</remarks>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(SpDocument))]
public partial class IrJson : JsonSerializerContext;
