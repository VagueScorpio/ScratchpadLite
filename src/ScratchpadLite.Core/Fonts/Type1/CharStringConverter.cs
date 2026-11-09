namespace ScratchpadLite.Core.Fonts.Type1;

/// <summary>
/// Turns a Type 1 charstring into an outline, and an outline into a Type 2
/// charstring for the CFF table (Section 6.6.4, step 3).
/// </summary>
/// <remarks>
/// Done in two passes rather than translated command by command. The Type 1
/// language has features Type 2 has no equivalent for - subroutine calls that
/// return values, flex curves assembled from seven separate moves, accented
/// characters that reference two other glyphs, and division - so interpreting
/// it into a plain outline first and emitting from that is both shorter and
/// far easier to be sure of.
/// <para>
/// Hints are dropped, which Section 6.6.4 allows. A converted font can look
/// slightly soft on screen at small sizes as a result; printing is unaffected.
/// </para>
/// </remarks>
public static class CharStringConverter
{
    /// <summary>How deep subroutine calls may nest before it is a loop.</summary>
    private const int MaxCallDepth = 30;

    /// <summary>A guard against a charstring that never reaches endchar.</summary>
    private const int MaxOperations = 200_000;

    public enum Op
    {
        Move,
        Line,
        Curve,
    }

    /// <summary>One drawing step, in absolute font units.</summary>
    public readonly record struct Step(Op Kind, double X1, double Y1, double X2, double Y2, double X3, double Y3);

    /// <summary>A converted glyph.</summary>
    public sealed record Outline
    {
        public double Width { get; init; }

        public double LeftSideBearing { get; init; }

        public List<Step> Steps { get; init; } = [];

        /// <summary>True when the glyph has no outline - a space, say.</summary>
        public bool IsBlank => Steps.Count == 0;
    }

    /// <summary>
    /// Interprets one glyph's charstring.
    /// </summary>
    /// <remarks>
    /// Never throws for a malformed charstring: a glyph that cannot be
    /// interpreted comes back as whatever was drawn before the trouble, which
    /// is better than losing the whole font for one bad glyph.
    /// </remarks>
    public static Outline Convert(Type1Parser.Font font, string glyphName)
    {
        ArgumentNullException.ThrowIfNull(font);

        if (!font.CharStrings.TryGetValue(glyphName, out var charString))
        {
            return new Outline();
        }

        var state = new State(font);
        state.Run(charString, 0);
        state.ClosePath();

        return new Outline
        {
            Width = state.Width,
            LeftSideBearing = state.LeftSideBearing,
            Steps = state.Steps,
        };
    }

    /// <summary>The interpreter's working state.</summary>
    private sealed class State(Type1Parser.Font font)
    {
        private readonly List<double> _stack = new(48);
        private readonly List<double> _psStack = new(8);
        private readonly List<double> _flexPoints = new(16);

        private double _x;
        private double _y;
        private bool _inFlex;
        private bool _open;
        private int _operations;
        private double _seacDepth;

        internal double Width { get; private set; }

        internal double LeftSideBearing { get; private set; }

        internal List<Step> Steps { get; } = [];

        internal void Run(byte[] code, int depth)
        {
            if (depth > MaxCallDepth)
            {
                return;
            }

            var i = 0;

            while (i < code.Length)
            {
                if (++_operations > MaxOperations)
                {
                    return;
                }

                var b = code[i++];

                // 32 and above is a number, in one of three encodings.
                if (b >= 32)
                {
                    if (b <= 246)
                    {
                        _stack.Add(b - 139);
                    }
                    else if (b <= 250)
                    {
                        if (i >= code.Length)
                        {
                            return;
                        }

                        _stack.Add(((b - 247) * 256) + code[i++] + 108);
                    }
                    else if (b <= 254)
                    {
                        if (i >= code.Length)
                        {
                            return;
                        }

                        _stack.Add(-((b - 251) * 256) - code[i++] - 108);
                    }
                    else
                    {
                        if (i + 4 > code.Length)
                        {
                            return;
                        }

                        // A plain 32-bit integer in Type 1 - not the 16.16
                        // fixed-point value the same byte means in Type 2.
                        _stack.Add(
                            (code[i] << 24) | (code[i + 1] << 16) | (code[i + 2] << 8) | code[i + 3]);
                        i += 4;
                    }

                    continue;
                }

                switch (b)
                {
                    case 13:                                        // hsbw
                        if (_stack.Count >= 2)
                        {
                            LeftSideBearing = _stack[0];
                            Width = _stack[1];
                            _x = _stack[0];
                        }

                        _stack.Clear();
                        break;

                    case 9:                                         // closepath
                        ClosePath();
                        _stack.Clear();
                        break;

                    case 1 or 3:                                    // hstem, vstem
                        _stack.Clear();
                        break;

                    case 21:                                        // rmoveto
                        if (_stack.Count >= 2)
                        {
                            MoveBy(_stack[^2], _stack[^1]);
                        }

                        _stack.Clear();
                        break;

                    case 22:                                        // hmoveto
                        if (_stack.Count >= 1)
                        {
                            MoveBy(_stack[^1], 0);
                        }

                        _stack.Clear();
                        break;

                    case 4:                                         // vmoveto
                        if (_stack.Count >= 1)
                        {
                            MoveBy(0, _stack[^1]);
                        }

                        _stack.Clear();
                        break;

                    case 5:                                         // rlineto
                        if (_stack.Count >= 2)
                        {
                            LineBy(_stack[0], _stack[1]);
                        }

                        _stack.Clear();
                        break;

                    case 6:                                         // hlineto
                        if (_stack.Count >= 1)
                        {
                            LineBy(_stack[0], 0);
                        }

                        _stack.Clear();
                        break;

                    case 7:                                         // vlineto
                        if (_stack.Count >= 1)
                        {
                            LineBy(0, _stack[0]);
                        }

                        _stack.Clear();
                        break;

                    case 8:                                         // rrcurveto
                        if (_stack.Count >= 6)
                        {
                            CurveBy(_stack[0], _stack[1], _stack[2], _stack[3], _stack[4], _stack[5]);
                        }

                        _stack.Clear();
                        break;

                    case 30:                                        // vhcurveto
                        if (_stack.Count >= 4)
                        {
                            CurveBy(0, _stack[0], _stack[1], _stack[2], _stack[3], 0);
                        }

                        _stack.Clear();
                        break;

                    case 31:                                        // hvcurveto
                        if (_stack.Count >= 4)
                        {
                            CurveBy(_stack[0], 0, _stack[1], _stack[2], 0, _stack[3]);
                        }

                        _stack.Clear();
                        break;

                    case 10:                                        // callsubr
                        if (_stack.Count >= 1)
                        {
                            var index = (int)_stack[^1];
                            _stack.RemoveAt(_stack.Count - 1);

                            if (index >= 0 && index < font.Subrs.Count)
                            {
                                Run(font.Subrs[index], depth + 1);
                            }
                        }

                        break;

                    case 11:                                        // return
                        return;

                    case 14:                                        // endchar
                        _stack.Clear();
                        return;

                    case 12:
                        if (i >= code.Length)
                        {
                            return;
                        }

                        if (Escaped(code[i++], depth))
                        {
                            return;
                        }

                        break;

                    default:
                        // An operator from a later specification, or damage.
                        // Clearing the stack keeps the rest of the glyph
                        // readable instead of compounding the error.
                        _stack.Clear();
                        break;
                }
            }
        }

        /// <summary>The two-byte operators. Returns true to stop the charstring.</summary>
        private bool Escaped(byte b, int depth)
        {
            switch (b)
            {
                case 0:                                             // dotsection
                case 1:                                             // vstem3
                case 2:                                             // hstem3
                    _stack.Clear();
                    return false;

                case 6:                                             // seac
                    Seac(depth);
                    return true;

                case 7:                                             // sbw
                    if (_stack.Count >= 4)
                    {
                        LeftSideBearing = _stack[0];
                        Width = _stack[2];
                        _x = _stack[0];
                        _y = _stack[1];
                    }

                    _stack.Clear();
                    return false;

                case 12:                                            // div
                    if (_stack.Count >= 2)
                    {
                        var divisor = _stack[^1];
                        var dividend = _stack[^2];

                        _stack.RemoveRange(_stack.Count - 2, 2);
                        _stack.Add(divisor == 0 ? 0 : dividend / divisor);
                    }

                    return false;

                case 16:                                            // callothersubr
                    OtherSubr();
                    return false;

                case 17:                                            // pop
                    // Takes the value callothersubr left behind. An empty
                    // PostScript stack means the font did something unusual;
                    // a zero keeps the charstring readable.
                    if (_psStack.Count > 0)
                    {
                        _stack.Add(_psStack[^1]);
                        _psStack.RemoveAt(_psStack.Count - 1);
                    }
                    else
                    {
                        _stack.Add(0);
                    }

                    return false;

                case 33:                                            // setcurrentpoint
                    if (_stack.Count >= 2)
                    {
                        _x = _stack[0];
                        _y = _stack[1];
                    }

                    _stack.Clear();
                    return false;

                default:
                    _stack.Clear();
                    return false;
            }
        }

        /// <summary>
        /// The OtherSubrs, which are where Type 1's awkward features live
        /// (Section 6.6.4, step 3).
        /// </summary>
        /// <remarks>
        /// Flex is the interesting one. A flex curve is written as seven
        /// <c>rmoveto</c>s between OtherSubr 1 and OtherSubr 0, so an
        /// interpreter that did not know about it would draw a glyph with seven
        /// stray moves in the middle of a contour. The first of the seven is a
        /// reference point that is not on the curve and is thrown away; the
        /// remaining six are the control points of two curves.
        /// </remarks>
        private void OtherSubr()
        {
            if (_stack.Count < 2)
            {
                _stack.Clear();
                return;
            }

            var index = (int)_stack[^1];
            var argumentCount = (int)_stack[^2];

            _stack.RemoveRange(_stack.Count - 2, 2);

            var arguments = new List<double>(Math.Max(0, argumentCount));

            for (var i = 0; i < argumentCount && _stack.Count > 0; i++)
            {
                arguments.Insert(0, _stack[^1]);
                _stack.RemoveAt(_stack.Count - 1);
            }

            switch (index)
            {
                case 0:                                             // end flex
                    EndFlex();
                    break;

                case 1:                                             // start flex
                    _inFlex = true;
                    _flexPoints.Clear();
                    break;

                case 2:                                             // collecting
                    break;

                case 3:
                    // Hint replacement. The convention is
                    // "subr# 1 3 callothersubr pop callsubr", so the subroutine
                    // number has to come back out of the pop. The subroutine it
                    // then calls holds only hints, which are dropped anyway.
                    _psStack.Add(arguments.Count > 0 ? arguments[0] : 3);
                    break;

                default:
                    // An OtherSubr this does not know: the specification says
                    // to leave the arguments for the pops that follow.
                    for (var i = arguments.Count - 1; i >= 0; i--)
                    {
                        _psStack.Add(arguments[i]);
                    }

                    break;
            }
        }

        private void EndFlex()
        {
            _inFlex = false;

            // Seven collected points: a reference point then six control
            // points. Anything else means the font's flex was malformed.
            if (_flexPoints.Count >= 14)
            {
                var p = _flexPoints;
                var at = p.Count - 12;

                AddCurve(p[at], p[at + 1], p[at + 2], p[at + 3], p[at + 4], p[at + 5]);
                AddCurve(p[at + 6], p[at + 7], p[at + 8], p[at + 9], p[at + 10], p[at + 11]);

                _x = p[at + 10];
                _y = p[at + 11];
            }

            _flexPoints.Clear();

            // OtherSubr 0 leaves the end point for two pops followed by
            // setcurrentpoint.
            _psStack.Add(_y);
            _psStack.Add(_x);
        }

        /// <summary>
        /// An accented character, drawn as two other glyphs
        /// (Section 6.6.4, step 3).
        /// </summary>
        /// <remarks>
        /// The base and accent are named by StandardEncoding code, not by name,
        /// which is why <see cref="AdobeEncodings"/> has to exist. The accent is
        /// shifted by the difference between the two glyphs' side bearings,
        /// which is the part that is easy to get wrong - using adx alone leaves
        /// accents sitting visibly off-centre.
        /// </remarks>
        private void Seac(int depth)
        {
            if (_stack.Count < 5 || _seacDepth > 0)
            {
                _stack.Clear();
                return;
            }

            var asb = _stack[0];
            var adx = _stack[1];
            var ady = _stack[2];
            var bchar = (int)_stack[3];
            var achar = (int)_stack[4];

            _stack.Clear();

            var baseName = AdobeEncodings.StandardName(bchar);
            var accentName = AdobeEncodings.StandardName(achar);

            if (baseName is null || accentName is null)
            {
                return;
            }

            _seacDepth++;

            var baseGlyph = Convert(font, baseName);
            var accentGlyph = Convert(font, accentName);

            Steps.Clear();
            Steps.AddRange(baseGlyph.Steps);

            var shiftX = LeftSideBearing - asb + adx;
            var shiftY = ady;

            foreach (var step in accentGlyph.Steps)
            {
                Steps.Add(new Step(
                    step.Kind,
                    step.X1 + shiftX, step.Y1 + shiftY,
                    step.X2 + shiftX, step.Y2 + shiftY,
                    step.X3 + shiftX, step.Y3 + shiftY));
            }

            _seacDepth--;
            _ = depth;
        }

        internal void ClosePath() => _open = false;

        private void MoveBy(double dx, double dy)
        {
            if (_inFlex)
            {
                // Inside flex these are not moves: they are the control points.
                _x += dx;
                _y += dy;
                _flexPoints.Add(_x);
                _flexPoints.Add(_y);
                return;
            }

            _x += dx;
            _y += dy;

            Steps.Add(new Step(Op.Move, 0, 0, 0, 0, _x, _y));
            _open = true;
        }

        private void LineBy(double dx, double dy)
        {
            if (!_open)
            {
                MoveBy(0, 0);
            }

            _x += dx;
            _y += dy;

            Steps.Add(new Step(Op.Line, 0, 0, 0, 0, _x, _y));
        }

        private void CurveBy(
            double dx1, double dy1, double dx2, double dy2, double dx3, double dy3)
        {
            if (!_open)
            {
                MoveBy(0, 0);
            }

            var c1X = _x + dx1;
            var c1Y = _y + dy1;
            var c2X = c1X + dx2;
            var c2Y = c1Y + dy2;

            _x = c2X + dx3;
            _y = c2Y + dy3;

            AddCurve(c1X, c1Y, c2X, c2Y, _x, _y);
        }

        private void AddCurve(double x1, double y1, double x2, double y2, double x3, double y3)
        {
            if (!_open)
            {
                Steps.Add(new Step(Op.Move, 0, 0, 0, 0, _x, _y));
                _open = true;
            }

            Steps.Add(new Step(Op.Curve, x1, y1, x2, y2, x3, y3));
        }
    }
}
