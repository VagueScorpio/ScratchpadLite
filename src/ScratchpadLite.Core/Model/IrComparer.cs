using System.Diagnostics.CodeAnalysis;

namespace ScratchpadLite.Core.Model;

/// <summary>
/// Records give value equality, but their list properties compare by reference
/// (Section 6.3). Tests use this comparer to compare documents by content.
/// </summary>
/// <remarks>Phase 4.</remarks>
public sealed class IrComparer : IEqualityComparer<SpDocument>
{
    public static IrComparer Instance { get; } = new();

    public bool Equals(SpDocument? x, SpDocument? y) => throw new NotImplementedException("Phase 4");

    public int GetHashCode([DisallowNull] SpDocument obj) => throw new NotImplementedException("Phase 4");
}
