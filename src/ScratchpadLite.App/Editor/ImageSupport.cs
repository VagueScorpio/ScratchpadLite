using System.IO;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ScratchpadLite.App.Bridge;
using ScratchpadLite.Core.Model;
using ScratchpadLite.Core.Platform;

namespace ScratchpadLite.App.Editor;

/// <summary>
/// Inserting images, the drag-handle resize adorner, and the image
/// properties dialog (Section 6.9). The original bytes and MIME type stay
/// on the image so saving writes the file back unchanged rather than
/// re-encoding it.
/// </summary>
/// <remarks>
/// A picture in a RichTextBox is an <see cref="Image"/> inside an
/// <see cref="InlineUIContainer"/>, and WPF gives it no way to be selected or
/// resized by the user at all - so the handles are an
/// <see cref="Adorner"/> this class puts on and takes off as the caret moves.
/// <para>
/// Every picture keeps its original file bytes on <c>Image.Tag</c>. That is
/// what makes a save write the same PNG back: re-encoding on each save would
/// degrade a photograph a little every time, and a screenshot saved five times
/// should still be the same screenshot.
/// </para>
/// </remarks>
internal static class ImageSupport
{
    /// <summary>What the Insert Image dialog offers, and what WPF can decode.</summary>
    internal const string OpenFilter =
        "Images|*.png;*.jpg;*.jpeg;*.gif;*.bmp;*.tif;*.tiff;*.webp;*.ico"
        + "|PNG|*.png"
        + "|JPEG|*.jpg;*.jpeg"
        + "|GIF|*.gif"
        + "|Bitmap|*.bmp"
        + "|TIFF|*.tif;*.tiff"
        + "|WebP|*.webp"
        + "|All files|*.*";

    /// <summary>
    /// A picture bigger than this is refused rather than loaded (Section 7.5).
    /// </summary>
    /// <remarks>
    /// 64 MB of compressed image decodes to far more in memory, and a document
    /// with several of them would be unusable long before the limit is reached.
    /// </remarks>
    internal const long MaxImageBytes = 64L * 1024 * 1024;

    // --------------------------------------------------------------- insert --

    /// <summary>Why a picture could not be inserted, in words meant for a person.</summary>
    internal sealed class ImageException(string message, Exception? inner = null)
        : Exception(message, inner);

    internal static void Insert(RichTextBox editor, string imagePath)
    {
        ArgumentNullException.ThrowIfNull(editor);
        ArgumentException.ThrowIfNullOrEmpty(imagePath);

        byte[] bytes;

        try
        {
            var info = new FileInfo(imagePath);

            if (!info.Exists)
            {
                throw new ImageException($"{Path.GetFileName(imagePath)} is not there.");
            }

            if (info.Length > MaxImageBytes)
            {
                throw new ImageException(
                    $"{Path.GetFileName(imagePath)} is {info.Length / (1024 * 1024)} MB, which is too "
                    + $"large to put in a document. The limit is {MaxImageBytes / (1024 * 1024)} MB.");
            }

            bytes = File.ReadAllBytes(imagePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new ImageException($"{Path.GetFileName(imagePath)} could not be read.", ex);
        }

        Insert(editor, bytes, MimeFor(imagePath), Path.GetFileNameWithoutExtension(imagePath));
    }

    /// <summary>
    /// Inserts a picture from bytes, which is also the path a paste takes.
    /// </summary>
    internal static void Insert(RichTextBox editor, byte[] bytes, string mime, string alt = "")
    {
        ArgumentNullException.ThrowIfNull(editor);
        ArgumentNullException.ThrowIfNull(bytes);

        var bitmap = Decode(bytes);

        // Points, from the bitmap's own resolution rather than its pixel count,
        // so a 300-dpi photograph does not arrive four times too wide.
        var widthPt = Units.DipsToPoints(bitmap.Width);
        var heightPt = Units.DipsToPoints(bitmap.Height);

        // Nothing wider than the text column: a picture that pushes the page
        // sideways is worse than one that is smaller than its original.
        var availablePt = AvailableWidthPt(editor);

        if (widthPt > availablePt && widthPt > 0)
        {
            heightPt *= availablePt / widthPt;
            widthPt = availablePt;
        }

        var image = new Image
        {
            Source = bitmap,
            Width = Units.PointsToDips(widthPt),
            Height = Units.PointsToDips(heightPt),
            Stretch = Stretch.Fill,
            Tag = Store(editor, bytes, mime, alt),
        };

        if (!string.IsNullOrEmpty(alt))
        {
            image.ToolTip = alt;
        }

        editor.BeginChange();

        try
        {
            // Replacing the selection first, so inserting over selected text
            // behaves the way typing over it does.
            if (!editor.Selection.IsEmpty)
            {
                editor.Selection.Text = string.Empty;
            }

            InlineUIContainer container;

            if (editor.CaretPosition.Paragraph is not null)
            {
                // The two-argument constructor inserts at the position, which
                // is the only way to land in the middle of a run: adding to
                // Paragraph.Inlines would put the picture after the whole run
                // the caret happens to be inside.
                container = new InlineUIContainer(image, editor.CaretPosition);
            }
            else
            {
                container = new InlineUIContainer(image);
                editor.Document.Blocks.Add(new System.Windows.Documents.Paragraph(container));
            }

            editor.CaretPosition = container.ContentEnd;
        }
        finally
        {
            editor.EndChange();
        }
    }

    /// <summary>
    /// A bitmap from bytes, or a readable complaint.
    /// </summary>
    private static BitmapSource Decode(byte[] bytes)
    {
        try
        {
            var bitmap = new BitmapImage();

            bitmap.BeginInit();
            bitmap.StreamSource = new MemoryStream(bytes, writable: false);
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.EndInit();
            bitmap.Freeze();

            return bitmap;
        }
        catch (Exception ex) when (ex is NotSupportedException or ArgumentException
                                       or IOException or FileFormatException)
        {
            // WebP needs the Windows codec, which is present on Windows 11 and
            // on an updated Windows 10 but not on every PC, so this is a real
            // case rather than a theoretical one.
            throw new ImageException(
                "Windows cannot read this picture. It may be damaged, or it may be a format "
                + "this PC has no codec for - WebP needs the Windows Image Extension from the "
                + "Microsoft Store on some versions of Windows 10.",
                ex);
        }
    }

    /// <summary>
    /// The width available for a picture, in points.
    /// </summary>
    private static double AvailableWidthPt(RichTextBox editor)
    {
        var width = editor.Document.PageWidth;

        if (double.IsNaN(width) || width <= 0)
        {
            width = editor.ViewportWidth > 0 ? editor.ViewportWidth : 612;
        }

        var padding = editor.Document.PagePadding;
        var usable = width - padding.Left - padding.Right;

        return Units.DipsToPoints(Math.Max(72, usable));
    }

    // -------------------------------------------------------- the byte store --

    /// <summary>
    /// Keeps one copy of each distinct picture's bytes per document
    /// (Section 6.9).
    /// </summary>
    /// <remarks>
    /// The same logo pasted into forty places should be forty references to one
    /// array, not forty arrays. The key is the SHA-1 of the bytes, which the
    /// blueprint asks for; it is a content key here and not a security
    /// decision, and a collision would only mean two pictures sharing one
    /// buffer they are byte-for-byte identical to anyway.
    /// <para>
    /// Keyed on the FlowDocument rather than held in one static table, so
    /// closing a document releases its pictures - and a
    /// <see cref="System.Runtime.CompilerServices.ConditionalWeakTable{TKey,TValue}"/>
    /// so that happens without anyone remembering to clear it.
    /// </para>
    /// </remarks>
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<
        FlowDocument,
        Dictionary<string, byte[]>> Stores = new();

    private static SpProps.ImagePayload Store(RichTextBox editor, byte[] bytes, string mime, string alt)
    {
        var store = Stores.GetOrCreateValue(editor.Document);
        var key = Convert.ToHexString(SHA1.HashData(bytes));

        if (store.TryGetValue(key, out var existing))
        {
            return new SpProps.ImagePayload(existing, mime, alt);
        }

        store[key] = bytes;

        return new SpProps.ImagePayload(bytes, mime, alt);
    }

    /// <summary>How many distinct pictures a document is holding, for the tests.</summary>
    internal static int DistinctImageCount(FlowDocument document) =>
        Stores.TryGetValue(document, out var store) ? store.Count : 0;

    // --------------------------------------------------------- properties ----

    /// <summary>The picture at the caret, or null.</summary>
    internal static Image? ImageAt(RichTextBox editor)
    {
        if (editor is null)
        {
            return null;
        }

        // Either side of the caret, because clicking a picture usually leaves
        // the caret just after it.
        return Containing(editor.Selection.Start) ?? Containing(editor.Selection.End);
    }

    private static Image? Containing(TextPointer? pointer)
    {
        for (DependencyObject? node = pointer?.Parent; node is not null; node = Parent(node))
        {
            if (node is InlineUIContainer { Child: Image image })
            {
                return image;
            }
        }

        // A caret sitting immediately after a picture has the paragraph as its
        // parent, so the inline before it is worth a look.
        if (pointer?.GetAdjacentElement(LogicalDirection.Backward) is InlineUIContainer
            {
                Child: Image before,
            })
        {
            return before;
        }

        return pointer?.GetAdjacentElement(LogicalDirection.Forward) is InlineUIContainer
            {
                Child: Image after,
            }
            ? after
            : null;
    }

    /// <summary>
    /// Applies what the Image Properties dialog asked for.
    /// </summary>
    internal static void SetProperties(
        RichTextBox editor,
        Image image,
        double widthPt,
        double heightPt,
        string alt)
    {
        ArgumentNullException.ThrowIfNull(editor);
        ArgumentNullException.ThrowIfNull(image);

        editor.BeginChange();

        try
        {
            image.Width = Units.PointsToDips(Math.Max(1, widthPt));
            image.Height = Units.PointsToDips(Math.Max(1, heightPt));

            if (image.Tag is SpProps.ImagePayload payload)
            {
                image.Tag = payload with { Alt = alt };
            }

            image.ToolTip = string.IsNullOrEmpty(alt) ? null : alt;
        }
        finally
        {
            editor.EndChange();
        }
    }

    /// <summary>The natural size of a picture in points, for the Reset button.</summary>
    internal static (double WidthPt, double HeightPt) NaturalSize(Image image)
    {
        ArgumentNullException.ThrowIfNull(image);

        return image.Source is BitmapSource bitmap
            ? (Units.DipsToPoints(bitmap.Width), Units.DipsToPoints(bitmap.Height))
            : (Units.DipsToPoints(image.Width), Units.DipsToPoints(image.Height));
    }

    // ------------------------------------------------------------ the handles --

    /// <summary>
    /// Shows or hides the resize handles as the caret moves.
    /// </summary>
    /// <remarks>
    /// Called from the editor's selection-changed handler. One adorner at a
    /// time: the layer is cleared of ours before a new one goes on, so a
    /// document does not collect handles around every picture visited.
    /// </remarks>
    internal static void UpdateHandles(RichTextBox editor)
    {
        ArgumentNullException.ThrowIfNull(editor);

        var image = ImageAt(editor);

        if (ReferenceEquals(_adorned, image))
        {
            return;
        }

        Remove();

        if (image is null || AdornerLayer.GetAdornerLayer(image) is not { } layer)
        {
            return;
        }

        _adorner = new ResizeAdorner(image, editor);
        _adorned = image;
        layer.Add(_adorner);
    }

    /// <summary>Takes the handles off, for a document being closed or replaced.</summary>
    internal static void Remove()
    {
        if (_adorner is not null && _adorned is not null
            && AdornerLayer.GetAdornerLayer(_adorned) is { } layer)
        {
            layer.Remove(_adorner);
        }

        _adorner = null;
        _adorned = null;
    }

    private static ResizeAdorner? _adorner;
    private static Image? _adorned;

    /// <summary>
    /// Four corner handles that resize the picture they are drawn around.
    /// </summary>
    /// <remarks>
    /// The aspect ratio is kept unless Shift is held, which is the opposite of
    /// the convention in drawing programs and the same as the convention in
    /// word processors: a picture in a document is nearly always meant to keep
    /// its shape, and distorting one is the rarer intention.
    /// </remarks>
    private sealed class ResizeAdorner : Adorner
    {
        private const double HandleSize = 8;

        private readonly VisualCollection _handles;
        private readonly Thumb[] _thumbs = new Thumb[4];
        private readonly RichTextBox _editor;
        private readonly Image _image;

        private double _startWidth;
        private double _startHeight;

        internal ResizeAdorner(Image image, RichTextBox editor)
            : base(image)
        {
            _image = image;
            _editor = editor;
            _handles = new VisualCollection(this);

            for (var i = 0; i < _thumbs.Length; i++)
            {
                var thumb = new Thumb
                {
                    Width = HandleSize,
                    Height = HandleSize,
                    Background = Brushes.White,
                    BorderBrush = Brushes.SteelBlue,
                    BorderThickness = new Thickness(1),
                    Cursor = i is 0 or 3 ? Cursors.SizeNWSE : Cursors.SizeNESW,
                };

                var corner = i;

                thumb.DragStarted += (_, _) =>
                {
                    _startWidth = Current(_image.Width, ActualWidthOf(_image));
                    _startHeight = Current(_image.Height, ActualHeightOf(_image));
                };

                thumb.DragDelta += (_, e) => Drag(corner, e.HorizontalChange, e.VerticalChange);
                thumb.DragCompleted += (_, _) => Done();

                _thumbs[i] = thumb;
                _handles.Add(thumb);
            }
        }

        protected override int VisualChildrenCount => _handles.Count;

        protected override Visual GetVisualChild(int index) => _handles[index];

        protected override Size ArrangeOverride(Size finalSize)
        {
            var width = AdornedElement.RenderSize.Width;
            var height = AdornedElement.RenderSize.Height;
            var half = HandleSize / 2;

            // Top-left, top-right, bottom-left, bottom-right.
            _thumbs[0].Arrange(new Rect(-half, -half, HandleSize, HandleSize));
            _thumbs[1].Arrange(new Rect(width - half, -half, HandleSize, HandleSize));
            _thumbs[2].Arrange(new Rect(-half, height - half, HandleSize, HandleSize));
            _thumbs[3].Arrange(new Rect(width - half, height - half, HandleSize, HandleSize));

            return finalSize;
        }

        private void Drag(int corner, double dx, double dy)
        {
            // Dragging a left or top handle grows the picture in the opposite
            // direction to the mouse.
            var horizontal = corner is 1 or 3 ? dx : -dx;
            var vertical = corner is 2 or 3 ? dy : -dy;

            var width = Math.Max(HandleSize, _startWidth + horizontal);
            var height = Math.Max(HandleSize, _startHeight + vertical);

            if ((Keyboard.Modifiers & ModifierKeys.Shift) == 0 && _startWidth > 0 && _startHeight > 0)
            {
                // Keep the shape, following whichever direction was dragged
                // furthest so the picture tracks the mouse.
                var ratio = _startHeight / _startWidth;

                if (Math.Abs(horizontal) >= Math.Abs(vertical))
                {
                    height = width * ratio;
                }
                else
                {
                    width = height / ratio;
                }
            }

            _image.Width = width;
            _image.Height = height;
            InvalidateArrange();
        }

        /// <summary>
        /// Puts one undo entry on the stack for the whole drag.
        /// </summary>
        /// <remarks>
        /// The drag itself sets Width and Height directly, outside any change
        /// block: a RichTextBox records nothing for a property set on a
        /// UIElement it merely hosts, so there is nothing to group. Re-applying
        /// the final size inside a change block is what makes one Ctrl+Z put
        /// the picture back.
        /// </remarks>
        private void Done()
        {
            var width = _image.Width;
            var height = _image.Height;

            _editor.BeginChange();

            try
            {
                _image.Width = width;
                _image.Height = height;
            }
            finally
            {
                _editor.EndChange();
            }
        }

        private static double Current(double set, double actual) =>
            double.IsNaN(set) || set <= 0 ? actual : set;

        private static double ActualWidthOf(Image image) =>
            image.ActualWidth > 0 ? image.ActualWidth
            : image.Source is BitmapSource bitmap ? bitmap.Width : HandleSize;

        private static double ActualHeightOf(Image image) =>
            image.ActualHeight > 0 ? image.ActualHeight
            : image.Source is BitmapSource bitmap ? bitmap.Height : HandleSize;
    }

    // ---------------------------------------------------------------- bits ----

    /// <summary>The MIME type for a file name, as the formats record it.</summary>
    internal static string MimeFor(string path) =>
        Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".bmp" => "image/bmp",
            ".tif" or ".tiff" => "image/tiff",
            ".webp" => "image/webp",
            ".ico" => "image/x-icon",
            _ => "image/png",
        };

    private static DependencyObject? Parent(DependencyObject node) => node switch
    {
        FrameworkContentElement element => element.Parent,
        FrameworkElement element => element.Parent,
        _ => null,
    };
}
