using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ScreenCap.Scrolling;

internal enum StitchOutcome
{
    /// <summary>The content scrolled and the new rows were appended.</summary>
    Appended,

    /// <summary>The frame is the same as the previous one (nothing scrolled, e.g. the end was reached).</summary>
    NoChange,

    /// <summary>No reliable overlap with the previous frame was found.</summary>
    Lost,
}

/// <summary>
/// Joins successive captures of a scrolling region into one tall image. Each frame is matched against
/// the previous one by comparing rows; only the rows that scrolled into view are appended.
/// Rows that stay put at the top and bottom (sticky headers/footers) are detected on the first scroll
/// and kept once instead of being repeated.
/// Rows are compared in vertical strips and count as equal when most strips match, so parts of the
/// region that don't scroll with the content (a scrollbar, a side panel, rounded window corners) are tolerated.
/// </summary>
internal sealed class ScrollStitcher
{
    /// <summary>Fraction of compared rows that must match for an offset to be accepted.</summary>
    private const double MinMatch = 0.95;

    /// <summary>Number of vertical strips per row, and how many must match for the rows to count as equal.</summary>
    private const int Strips = 16;
    private const int StripsToMatch = 12;

    private readonly int _width, _height, _stride;
    private readonly MemoryStream _body = new();
    private byte[] _previous;
    private RowInfo[] _previousRows;
    private byte[]? _header;
    private int _top = -1, _bottom = -1; // sticky band heights, fixed after the first scroll

    public ScrollStitcher(int width, int height, byte[] firstFrame)
    {
        _width = width;
        _height = height;
        _stride = width * 4;
        _previous = firstFrame;
        _previousRows = Analyze(firstFrame);
    }

    /// <summary>Height in pixels of the stitched image so far.</summary>
    public int Height => _top < 0 ? _height : _top + (int)(_body.Length / _stride) + _bottom;

    /// <summary>Scroll distance of the last appended frame, in pixels.</summary>
    public int LastOffset { get; private set; }

    /// <param name="frame">Top-down BGRA pixels of the region, same size as the first frame.</param>
    /// <param name="expectedOffset">Likely scroll distance, used to break ties in repetitive content.</param>
    public StitchOutcome Add(byte[] frame, int expectedOffset = 0)
    {
        var rows = Analyze(frame);
        int top = _top, bottom = _bottom;
        if (top < 0)
        {
            // First scroll: rows identical at the same position are sticky (or just blank margins).
            top = CountEqual(rows, 0, 1);
            if (top == _height)
                return StitchOutcome.NoChange;
            bottom = CountEqual(rows, _height - 1, -1);
            top = Math.Min(top, _height / 3);
            bottom = Math.Min(bottom, _height / 3);
        }

        int band = _height - top - bottom;
        if (band < 16)
            return StitchOutcome.Lost;
        if (Enumerable.Range(top, band).All(y => Same(rows[y], _previousRows[y])))
            return StitchOutcome.NoChange;

        int offset = FindOffset(rows, top, band, expectedOffset);
        if (offset <= 0)
            return StitchOutcome.Lost;

        if (_top < 0)
        {
            // Lock the bands and start the image: header, then the first frame's scrolling part.
            _top = top;
            _bottom = bottom;
            _header = _previous.AsSpan(0, top * _stride).ToArray();
            _body.Write(_previous, top * _stride, band * _stride);
        }
        // The last `offset` rows of the band are new content.
        _body.Write(frame, (top + band - offset) * _stride, offset * _stride);

        _previous = frame;
        _previousRows = rows;
        LastOffset = offset;
        return StitchOutcome.Appended;
    }

    /// <summary>
    /// The smallest scroll distance whose overlap matches best. Blank (single-color) rows are ignored
    /// because they match anything.
    /// </summary>
    private int FindOffset(RowInfo[] rows, int top, int band, int expected)
    {
        int minOverlap = Math.Max(8, band / 6);
        int best = -1;
        double bestScore = -1;
        for (int dy = 1; dy <= band - minOverlap; dy++)
        {
            int matches = 0, compared = 0;
            for (int i = 0; i < band - dy; i++)
            {
                var before = _previousRows[top + dy + i];
                var now = rows[top + i];
                if (before.Blank && now.Blank)
                    continue;
                compared++;
                if (Same(before, now))
                    matches++;
            }
            if (compared < 4)
                continue;
            double score = (double)matches / compared;
            bool better = score > bestScore + 1e-9
                || (Math.Abs(score - bestScore) <= 1e-9 && expected > 0 && Math.Abs(dy - expected) < Math.Abs(best - expected));
            if (better)
            {
                bestScore = score;
                best = dy;
            }
        }
        return bestScore >= MinMatch ? best : -1;
    }

    private int CountEqual(RowInfo[] rows, int start, int step)
    {
        int count = 0;
        for (int y = start; y >= 0 && y < _height && Same(rows[y], _previousRows[y]); y += step)
            count++;
        return count;
    }

    private static bool Same(RowInfo a, RowInfo b)
    {
        int equal = 0;
        for (int i = 0; i < Strips; i++)
        {
            if (a.Hashes[i] == b.Hashes[i])
                equal++;
        }
        return equal >= StripsToMatch;
    }

    /// <summary>The stitched image: header, scrolled content, then the latest frame's footer.</summary>
    public BitmapSource ToBitmap()
    {
        byte[] pixels;
        if (_top < 0)
            pixels = _previous; // never scrolled: just the first frame
        else
        {
            var all = new MemoryStream();
            all.Write(_header!);
            _body.Position = 0;
            _body.CopyTo(all);
            all.Write(_previous, (_height - _bottom) * _stride, _bottom * _stride);
            pixels = all.ToArray();
        }
        var bitmap = BitmapSource.Create(_width, pixels.Length / _stride, 96, 96, PixelFormats.Bgr32, null, pixels, _stride);
        bitmap.Freeze();
        return bitmap;
    }

    /// <param name="Hashes">One hash per vertical strip of the row.</param>
    private readonly record struct RowInfo(int[] Hashes, bool Blank);

    private RowInfo[] Analyze(byte[] frame)
    {
        var rows = new RowInfo[_height];
        for (int y = 0; y < _height; y++)
        {
            var bytes = frame.AsSpan(y * _stride, _stride);
            var pixels = MemoryMarshal.Cast<byte, uint>(bytes);
            uint first = pixels[0] & 0xFFFFFF;
            bool blank = true;
            foreach (uint p in pixels)
            {
                if ((p & 0xFFFFFF) != first)
                {
                    blank = false;
                    break;
                }
            }
            var hashes = new int[Strips];
            for (int i = 0; i < Strips; i++)
            {
                int from = _width * i / Strips, to = _width * (i + 1) / Strips;
                var hash = new HashCode();
                hash.AddBytes(bytes[(from * 4)..(to * 4)]);
                hashes[i] = hash.ToHashCode();
            }
            rows[y] = new RowInfo(hashes, blank);
        }
        return rows;
    }
}
