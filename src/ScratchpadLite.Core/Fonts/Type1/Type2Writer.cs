namespace ScratchpadLite.Core.Fonts.Type1;

/// <summary>
/// Writes an outline as a Type 2 charstring for the CFF table
/// (Section 6.6.4, step 3).
/// </summary>
/// <remarks>
/// Only four operators are emitted - <c>rmoveto</c>, <c>rlineto</c>,
/// <c>rrcurveto</c> and <c>endchar</c> - which is the smallest set that can
/// describe any outline. Type 2 has a dozen more that exist to make files
/// smaller by exploiting runs of horizontal or vertical segments, and none of
/// them would change what is drawn. The file this goes into lives in a cache,
/// so the trade is all in favour of the version that is easy to be sure of.
/// <para>
/// The glyph's advance width is the optional leading number. It works because
/// <see cref="CffWriter"/> writes <c>nominalWidthX 0</c> and
/// <c>defaultWidthX 0</c> in the Private DICT, so the leading number is the
/// width rather than a difference from something.
/// </para>
/// </remarks>
public static class Type2Writer
{
    private const byte RMoveTo = 21;
    private const byte RLineTo = 5;
    private const byte RRCurveTo = 8;
    private const byte EndChar = 14;

    /// <summary>
    /// The largest number of coordinate arguments to put before one operator.
    /// </summary>
    /// <remarks>
    /// The Type 2 interpreter stack holds 48 numbers. rrcurveto takes six at a
    /// time and may be given several curves' worth at once, so this stays a
    /// safe multiple of six well inside the limit.
    /// </remarks>
    private const int MaxArguments = 42;

    public static byte[] Write(CharStringConverter.Outline outline)
    {
        ArgumentNullException.ThrowIfNull(outline);

        var code = new List<byte>(64);

        AddNumber(code, outline.Width);

        double x = 0;
        double y = 0;

        var pending = new List<double>(MaxArguments);
        var pendingOp = (byte)0;

        void Flush()
        {
            if (pendingOp == 0)
            {
                return;
            }

            foreach (var value in pending)
            {
                AddNumber(code, value);
            }

            code.Add(pendingOp);
            pending.Clear();
            pendingOp = 0;
        }

        void Queue(byte op, params double[] values)
        {
            // Runs of the same operator share one operator byte, which is the
            // one Type 2 economy worth taking because it needs no new logic.
            if (pendingOp != op || pending.Count + values.Length > MaxArguments)
            {
                Flush();
                pendingOp = op;
            }

            pending.AddRange(values);
        }

        foreach (var step in outline.Steps)
        {
            switch (step.Kind)
            {
                case CharStringConverter.Op.Move:
                    Flush();
                    Queue(RMoveTo, step.X3 - x, step.Y3 - y);
                    Flush();
                    x = step.X3;
                    y = step.Y3;
                    break;

                case CharStringConverter.Op.Line:
                    Queue(RLineTo, step.X3 - x, step.Y3 - y);
                    x = step.X3;
                    y = step.Y3;
                    break;

                case CharStringConverter.Op.Curve:
                    Queue(
                        RRCurveTo,
                        step.X1 - x, step.Y1 - y,
                        step.X2 - step.X1, step.Y2 - step.Y1,
                        step.X3 - step.X2, step.Y3 - step.Y2);
                    x = step.X3;
                    y = step.Y3;
                    break;
            }
        }

        Flush();
        code.Add(EndChar);

        return code.ToArray();
    }

    /// <summary>
    /// The one charstring every CFF font must have: .notdef, with a width and
    /// nothing drawn.
    /// </summary>
    public static byte[] NotDef(double width)
    {
        var code = new List<byte>(4);

        AddNumber(code, width);
        code.Add(EndChar);

        return code.ToArray();
    }

    /// <summary>
    /// A Type 2 number. Whole values use the integer encodings; anything with a
    /// fraction uses the 16.16 fixed-point form.
    /// </summary>
    private static void AddNumber(List<byte> code, double value)
    {
        var rounded = Math.Round(value);

        if (Math.Abs(value - rounded) < 0.001 && rounded is >= -32768 and <= 32767)
        {
            AddInteger(code, (int)rounded);
            return;
        }

        // 255 followed by a 16.16 fixed-point value. Type 1 fonts produce
        // fractional coordinates through div and through flex, so this is not
        // a theoretical case.
        var fixedPoint = (int)Math.Round(Math.Clamp(value, -32768, 32767.99) * 65536);

        code.Add(255);
        code.Add((byte)(fixedPoint >> 24));
        code.Add((byte)(fixedPoint >> 16));
        code.Add((byte)(fixedPoint >> 8));
        code.Add((byte)fixedPoint);
    }

    private static void AddInteger(List<byte> code, int value)
    {
        switch (value)
        {
            case >= -107 and <= 107:
                code.Add((byte)(value + 139));
                break;

            case >= 108 and <= 1131:
                var positive = value - 108;
                code.Add((byte)(247 + (positive >> 8)));
                code.Add((byte)(positive & 0xFF));
                break;

            case >= -1131 and <= -108:
                var negative = -value - 108;
                code.Add((byte)(251 + (negative >> 8)));
                code.Add((byte)(negative & 0xFF));
                break;

            default:
                code.Add(28);
                code.Add((byte)(value >> 8));
                code.Add((byte)value);
                break;
        }
    }
}
