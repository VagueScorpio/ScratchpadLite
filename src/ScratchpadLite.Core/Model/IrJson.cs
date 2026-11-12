using System.Text.Json;
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

/// <summary>
/// Reads and writes <see cref="IReadOnlySet{T}"/> of string.
/// </summary>
/// <remarks>
/// <para>
/// <c>System.Text.Json</c> writes an <c>IReadOnlySet&lt;string&gt;</c> happily
/// and refuses to read one back: "the collection type is abstract, an
/// interface, or is read only, and could not be instantiated and populated".
/// It has no way to know that a <see cref="HashSet{T}"/> would do.
/// </para>
/// <para>
/// This went unnoticed from Phase 4 to Phase 8 because nothing read the IR
/// back. The formats all parse their own files into IR and serialize IR out;
/// <see cref="SpDocument"/> itself was only ever written. Crash recovery
/// (Section 6.13.1) is the first thing in the application that writes IR JSON
/// and then reads it, and it found this on its first test.
/// </para>
/// </remarks>
public sealed class ReadOnlyStringSetConverter : JsonConverter<IReadOnlySet<string>>
{
    /// <summary>
    /// Handle a JSON null here rather than letting the serializer assign it.
    /// </summary>
    /// <remarks>
    /// Without this, <c>"fontsUsed": null</c> - which an older recovery file or
    /// a hand-edited one can easily contain - puts a null into a property whose
    /// type says it is never null, and the first thing to touch it throws
    /// somewhere else entirely. Taking the null here turns it into an empty
    /// set, which is what "no fonts" means.
    /// </remarks>
    public override bool HandleNull => true;

    public override IReadOnlySet<string> Read(
        ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return new HashSet<string>();
        }

        if (reader.TokenType != JsonTokenType.StartArray)
        {
            throw new JsonException("a set of font names has to be a JSON array");
        }

        var set = new HashSet<string>(StringComparer.Ordinal);

        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            if (reader.TokenType != JsonTokenType.String)
            {
                throw new JsonException("a font name has to be a JSON string");
            }

            if (reader.GetString() is { } name)
            {
                set.Add(name);
            }
        }

        return set;
    }

    public override void Write(
        Utf8JsonWriter writer, IReadOnlySet<string> value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(value);

        writer.WriteStartArray();

        // Sorted, so two writes of the same document give the same bytes. A
        // HashSet's order is not promised, and a recovery file that differs
        // from itself for no reason is a recovery file nobody can diff.
        foreach (var name in value.OrderBy(n => n, StringComparer.Ordinal))
        {
            writer.WriteStringValue(name);
        }

        writer.WriteEndArray();
    }
}
