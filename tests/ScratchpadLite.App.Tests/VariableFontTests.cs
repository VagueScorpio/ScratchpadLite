using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Xunit;

namespace ScratchpadLite.App.Tests;

/// <summary>
/// What WPF really does with a variable font, which Section 13's fifth known
/// limitation asked to have measured.
/// </summary>
/// <remarks>
/// <para>The blueprint's guess was that a variable font "may show only its
/// default style". On .NET 10 and Windows 11 that is not what happens, and
/// the difference matters enough to the font picker to be held down by a
/// test: every style the font declares is offered and each one is drawn
/// differently. What is not possible is an axis value the font does not
/// name - asking for a weight in between snaps to the nearest named
/// instance instead of interpolating.</para>
///
/// <para>The question is "does it look different", so these render and
/// compare pixels. Comparing advance widths would have given the wrong
/// answer: Bahnschrift's weights happen to share their widths, which is how
/// the font is drawn rather than anything WPF did.</para>
///
/// <para>Bahnschrift ships with Windows 10 and 11 and declares five weights
/// in one file. If it is ever missing, these tests step aside rather than
/// failing for a reason that has nothing to do with this code.</para>
/// </remarks>
[Collection(nameof(AppHost))]
public class VariableFontTests
{
    private const string Variable = "Bahnschrift";

    private static bool Available() =>
        AppHost.Run(() => new FontFamily(Variable).FamilyTypefaces.Count > 1);

    private static byte[] Draw(string family, FontWeight weight) =>
        AppHost.Run(() =>
        {
            var text = new FormattedText(
                "Hamburgefonstiv",
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                new Typeface(new FontFamily(family), FontStyles.Normal, weight, FontStretches.Normal),
                32,
                Brushes.Black,
                1.0);

            var visual = new DrawingVisual();

            using (var dc = visual.RenderOpen())
            {
                dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, 400, 60));
                dc.DrawText(text, new Point(2, 2));
            }

            var bitmap = new RenderTargetBitmap(400, 60, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(visual);

            var pixels = new byte[400 * 60 * 4];
            bitmap.CopyPixels(pixels, 400 * 4, 0);

            return pixels;
        });

    [StaFact]
    public void EveryStyleAVariableFontDeclaresIsDrawnDifferently()
    {
        if (!Available())
        {
            return;
        }

        var light = Draw(Variable, FontWeights.Light);
        var regular = Draw(Variable, FontWeights.Regular);
        var bold = Draw(Variable, FontWeights.Bold);

        Assert.False(light.SequenceEqual(regular), "Light and Regular came out identical");
        Assert.False(bold.SequenceEqual(regular), "Bold and Regular came out identical");
    }

    [StaFact]
    public void AnAxisValueTheFontDoesNotNameSnapsToOneItDoes()
    {
        if (!Available())
        {
            return;
        }

        // Bahnschrift names 300, 350, 400, 600 and 700. 800 is not one of
        // them, and comes out as 700 rather than as something heavier.
        var asked = Draw(Variable, FontWeight.FromOpenTypeWeight(800));
        var nearest = Draw(Variable, FontWeight.FromOpenTypeWeight(700));

        Assert.True(asked.SequenceEqual(nearest), "an undeclared weight should fall back to a declared one");
    }

    [StaFact]
    public void AFontWithNoLightFaceFallsBackRatherThanThinningTheLetters()
    {
        // The control. Arial has no Light, so Light and Regular are the same
        // picture - which is what "only the default style" would have looked
        // like for the variable font, and does not.
        var light = Draw("Arial", FontWeights.Light);
        var regular = Draw("Arial", FontWeights.Regular);

        Assert.True(light.SequenceEqual(regular), "Arial has no Light face to show");
    }
}
