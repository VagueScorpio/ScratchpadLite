namespace ScratchpadLite.Core.Model;

/// <summary>
/// Unit conversions used across the formats (Section 5.1 / 6.4). The IR speaks
/// points; WPF speaks device-independent pixels; DOCX speaks half-points,
/// twips and EMU.
/// </summary>
public static class Units
{
    public const double DipsPerPoint = 96.0 / 72.0;
    public const double TwipsPerPoint = 20.0;
    public const double EmuPerPoint = 12700.0;

    public static double PointsToDips(double points) => points * DipsPerPoint;

    public static double DipsToPoints(double dips) => dips / DipsPerPoint;

    public static double HalfPointsToPoints(double halfPoints) => halfPoints / 2.0;

    public static double PointsToHalfPoints(double points) => points * 2.0;

    public static double TwipsToPoints(double twips) => twips / TwipsPerPoint;

    public static double PointsToTwips(double points) => points * TwipsPerPoint;

    public static double EmuToPoints(double emu) => emu / EmuPerPoint;

    public static double PointsToEmu(double points) => points * EmuPerPoint;
}
