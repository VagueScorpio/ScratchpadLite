using System.Windows.Controls;

namespace ScratchpadLite.App.Editor;

/// <summary>
/// Inserting images, the drag-handle resize adorner, and the image
/// properties dialog (Section 6.9). The original bytes and MIME type stay
/// on the image so saving writes the file back unchanged rather than
/// re-encoding it.
/// </summary>
/// <remarks>Phase 6.</remarks>
internal static class ImageSupport
{
    internal static void Insert(RichTextBox editor, string imagePath) =>
        throw new NotImplementedException("Phase 6");
}
