using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using ScratchpadLite.Core.Platform;

namespace ScratchpadLite.App.Editor;

/// <summary>
/// Decides what survives a paste (Section 6.8).
/// </summary>
/// <remarks>
/// Clipboard content comes from other programs, so it is untrusted
/// (Section 7.5). The rules here are deliberately conservative:
/// <list type="bullet">
///   <item>
///     RTF is preferred over XAML. Both carry formatting, but XAML from an
///     unknown source is a serialized object graph, and WPF will happily
///     instantiate what is in it. RTF is only ever text and formatting.
///   </item>
///   <item>
///     Ctrl+Shift+V pastes plain text, which is the escape hatch when a paste
///     brings formatting nobody wanted.
///   </item>
/// </list>
/// </remarks>
internal static class PasteFilter
{
    /// <summary>
    /// Hooks the paste handler onto one editor. Called once per
    /// <see cref="EditorView"/>.
    /// </summary>
    internal static void Attach(RichTextBox rtb) =>
        DataObject.AddPastingHandler(rtb, OnPasting);

    internal static void Detach(RichTextBox rtb) =>
        DataObject.RemovePastingHandler(rtb, OnPasting);

    private static void OnPasting(object sender, DataObjectPastingEventArgs e)
    {
        // XAML on the clipboard is a serialized object graph. Downgrading to
        // RTF keeps the formatting without handing WPF arbitrary types to
        // instantiate; if there is no RTF, plain text is the safe floor.
        if (e.FormatToApply == DataFormats.Xaml)
        {
            if (e.SourceDataObject.GetDataPresent(DataFormats.Rtf))
            {
                e.FormatToApply = DataFormats.Rtf;
                Log.Debug("paste: XAML offered, using RTF instead");
            }
            else if (e.SourceDataObject.GetDataPresent(DataFormats.UnicodeText))
            {
                e.FormatToApply = DataFormats.UnicodeText;
                Log.Debug("paste: XAML offered with no RTF, using plain text");
            }
            else
            {
                e.CancelCommand();
                Log.Debug("paste: XAML only, cancelled");
            }
        }
    }

    /// <summary>
    /// Ctrl+Shift+V. Inserts the clipboard text with no formatting at all, as
    /// one undo step.
    /// </summary>
    internal static void PastePlainText(RichTextBox rtb)
    {
        if (!Clipboard.ContainsText())
        {
            return;
        }

        string text;
        try
        {
            text = Clipboard.GetText();
        }
        catch (System.Runtime.InteropServices.ExternalException ex)
        {
            // Another program can be holding the clipboard open.
            Log.Warn($"the clipboard could not be read ({ex.GetType().Name})");
            return;
        }

        if (text.Length == 0)
        {
            return;
        }

        rtb.BeginChange();
        try
        {
            if (!rtb.Selection.IsEmpty)
            {
                rtb.Selection.Text = string.Empty;
            }

            // Inserting at the caret rather than assigning Selection.Text means
            // the new text picks up the formatting already in force there,
            // which is what "paste as plain text" is expected to do: no
            // formatting from the source, the formatting of the destination.
            var caret = rtb.CaretPosition;
            caret.InsertTextInRun(text);
            rtb.CaretPosition = caret.GetPositionAtOffset(text.Length) ?? caret;
        }
        finally
        {
            rtb.EndChange();
        }
    }
}
