using System.IO;
using System.Runtime.InteropServices;

namespace ScreenCap.Recording;

/// <summary>
/// Streaming animated-GIF (GIF89a) encoder for screen recordings.
/// Each frame only encodes the rectangle that changed since the previous frame, with unchanged
/// pixels inside it marked transparent, and gets its own 255-color palette.
/// </summary>
internal sealed class GifEncoder : IFrameEncoder
{
    private const byte TransparentIndex = 255;

    private readonly Stream _out;
    private readonly bool _ownsStream;
    private readonly int _width, _height;
    private byte[]? _previous;

    // A frame is written only once the next one arrives, because its delay depends on that timestamp.
    private byte[]? _pendingImage;
    private bool _pendingTransparent;
    private TimeSpan _pendingTime;

    public GifEncoder(Stream output, int width, int height, bool ownsStream = false)
    {
        _out = output;
        _ownsStream = ownsStream;
        _width = width;
        _height = height;

        WriteAscii("GIF89a");
        WriteUInt16(width);
        WriteUInt16(height);
        _out.WriteByte(0x70); // no global color table, 8-bit color resolution
        _out.WriteByte(0);    // background color index
        _out.WriteByte(0);    // pixel aspect ratio

        // NETSCAPE2.0 extension: loop forever
        _out.Write([0x21, 0xFF, 0x0B]);
        WriteAscii("NETSCAPE2.0");
        _out.Write([0x03, 0x01, 0x00, 0x00, 0x00]);
    }

    public void AddFrame(byte[] bgra, TimeSpan timestamp)
    {
        int x0 = 0, y0 = 0, x1 = _width - 1, y1 = _height - 1;
        bool transparent = _previous != null;
        if (_previous != null && !ChangedBounds(_previous, bgra, out x0, out y0, out x1, out y1))
            return; // identical frame: the pending frame simply stays on screen longer

        var image = EncodeImage(bgra, x0, y0, x1 - x0 + 1, y1 - y0 + 1, transparent);
        FlushPending(timestamp);
        _pendingImage = image;
        _pendingTransparent = transparent;
        _pendingTime = timestamp;
        _previous = bgra;
    }

    public void Finish(TimeSpan endTime)
    {
        FlushPending(endTime);
        _out.WriteByte(0x3B); // trailer
        _out.Flush();
    }

    public void Dispose()
    {
        if (_ownsStream)
            _out.Dispose();
    }

    private void FlushPending(TimeSpan now)
    {
        if (_pendingImage == null)
            return;

        // Delay is in 1/100 s; browsers treat values below 2 as 10, so clamp.
        int delay = Math.Clamp((int)Math.Round((now - _pendingTime).TotalMilliseconds / 10), 2, ushort.MaxValue);
        _out.Write([0x21, 0xF9, 0x04]);
        _out.WriteByte((byte)((1 << 2) | (_pendingTransparent ? 1 : 0))); // disposal: leave in place
        WriteUInt16(delay);
        _out.WriteByte(TransparentIndex);
        _out.WriteByte(0);

        _out.Write(_pendingImage);
        _pendingImage = null;
    }

    private bool ChangedBounds(byte[] previous, byte[] current, out int x0, out int y0, out int x1, out int y1)
    {
        var prev = MemoryMarshal.Cast<byte, uint>(previous);
        var cur = MemoryMarshal.Cast<byte, uint>(current);
        x0 = _width;
        y0 = _height;
        x1 = -1;
        y1 = -1;
        for (int y = 0; y < _height; y++)
        {
            int row = y * _width;
            var a = prev.Slice(row, _width);
            var b = cur.Slice(row, _width);
            if (a.SequenceEqual(b))
                continue;

            int first = 0;
            while (a[first] == b[first])
                first++;
            int last = _width - 1;
            while (a[last] == b[last])
                last--;

            if (y0 == _height)
                y0 = y;
            y1 = y;
            x0 = Math.Min(x0, first);
            x1 = Math.Max(x1, last);
        }
        return y1 >= 0;
    }

    /// <summary>Image descriptor + local color table + LZW data for a sub-rectangle.</summary>
    private byte[] EncodeImage(byte[] bgra, int left, int top, int w, int h, bool useTransparency)
    {
        var cur = MemoryMarshal.Cast<byte, uint>(bgra);
        var prev = _previous == null ? default : MemoryMarshal.Cast<byte, uint>(_previous);

        // Collect RGB colors (or -1 for "unchanged → transparent").
        var colors = new int[w * h];
        for (int y = 0; y < h; y++)
        {
            int src = (top + y) * _width + left;
            int dst = y * w;
            for (int x = 0; x < w; x++)
            {
                uint px = cur[src + x];
                colors[dst + x] = useTransparency && px == prev[src + x] ? -1 : (int)(px & 0xFFFFFF);
            }
        }

        var (palette, indices) = ColorQuantizer.Quantize(colors, TransparentIndex);

        using var ms = new MemoryStream();
        ms.WriteByte(0x2C);
        WriteUInt16(ms, left);
        WriteUInt16(ms, top);
        WriteUInt16(ms, w);
        WriteUInt16(ms, h);
        ms.WriteByte(0x87); // local color table, 256 entries
        ms.Write(palette);
        LzwEncoder.Encode(ms, indices);
        return ms.ToArray();
    }

    private void WriteAscii(string s)
    {
        foreach (char c in s)
            _out.WriteByte((byte)c);
    }

    private void WriteUInt16(int value) => WriteUInt16(_out, value);

    private static void WriteUInt16(Stream s, int value)
    {
        s.WriteByte((byte)(value & 0xFF));
        s.WriteByte((byte)((value >> 8) & 0xFF));
    }
}

/// <summary>Reduces a frame to at most 255 colors (index 255 is reserved for transparency).</summary>
internal static class ColorQuantizer
{
    private const int MaxColors = 255;

    /// <param name="colors">0xRRGGBB values, or -1 for transparent pixels.</param>
    /// <returns>A 256×RGB palette and one palette index per pixel.</returns>
    public static (byte[] Palette, byte[] Indices) Quantize(int[] colors, byte transparentIndex)
    {
        var palette = new byte[256 * 3];
        var indices = new byte[colors.Length];

        // Screen content often has few colors: map them exactly when possible.
        var exact = new Dictionary<int, byte>();
        bool fits = true;
        foreach (int c in colors)
        {
            if (c < 0 || exact.ContainsKey(c))
                continue;
            if (exact.Count == MaxColors)
            {
                fits = false;
                break;
            }
            exact[c] = (byte)exact.Count;
        }

        if (fits)
        {
            foreach (var (color, index) in exact)
                SetPaletteEntry(palette, index, color >> 16 & 0xFF, color >> 8 & 0xFF, color & 0xFF);
            for (int i = 0; i < colors.Length; i++)
                indices[i] = colors[i] < 0 ? transparentIndex : exact[colors[i]];
            return (palette, indices);
        }

        var binToIndex = MedianCut(colors, palette);
        for (int i = 0; i < colors.Length; i++)
            indices[i] = colors[i] < 0 ? transparentIndex : binToIndex[Bin(colors[i])];
        return (palette, indices);
    }

    private static int Bin(int c) => (c >> 9 & 0x7C00) | (c >> 6 & 0x03E0) | (c >> 3 & 0x001F); // RGB 5-5-5

    /// <summary>Median cut over a 15-bit color histogram; fills the palette and returns bin → palette index.</summary>
    private static byte[] MedianCut(int[] colors, byte[] palette)
    {
        var count = new int[32768];
        var sumR = new long[32768];
        var sumG = new long[32768];
        var sumB = new long[32768];
        foreach (int c in colors)
        {
            if (c < 0)
                continue;
            int bin = Bin(c);
            count[bin]++;
            sumR[bin] += c >> 16 & 0xFF;
            sumG[bin] += c >> 8 & 0xFF;
            sumB[bin] += c & 0xFF;
        }

        var used = new List<int>();
        for (int bin = 0; bin < count.Length; bin++)
        {
            if (count[bin] > 0)
                used.Add(bin);
        }

        static int Channel(int bin, int axis) => axis switch { 0 => bin >> 10 & 31, 1 => bin >> 5 & 31, _ => bin & 31 };

        static (int Axis, int Range) WidestAxis(List<int> box)
        {
            int bestAxis = 0, bestRange = -1;
            for (int axis = 0; axis < 3; axis++)
            {
                int min = 31, max = 0;
                foreach (int bin in box)
                {
                    int v = Channel(bin, axis);
                    min = Math.Min(min, v);
                    max = Math.Max(max, v);
                }
                if (max - min > bestRange)
                {
                    bestRange = max - min;
                    bestAxis = axis;
                }
            }
            return (bestAxis, bestRange);
        }

        var boxes = new List<List<int>> { used };
        while (boxes.Count < MaxColors)
        {
            // Split the box with the widest color range.
            int target = -1, targetAxis = 0, targetRange = 0;
            for (int i = 0; i < boxes.Count; i++)
            {
                if (boxes[i].Count < 2)
                    continue;
                var (axis, range) = WidestAxis(boxes[i]);
                if (range > targetRange)
                {
                    target = i;
                    targetAxis = axis;
                    targetRange = range;
                }
            }
            if (target < 0)
                break;

            var box = boxes[target];
            box.Sort((a, b) => Channel(a, targetAxis).CompareTo(Channel(b, targetAxis)));
            long total = box.Sum(b => (long)count[b]);
            long acc = 0;
            int split = 1;
            for (int i = 0; i < box.Count - 1; i++)
            {
                acc += count[box[i]];
                split = i + 1;
                if (acc * 2 >= total)
                    break;
            }
            boxes[target] = box.GetRange(0, split);
            boxes.Add(box.GetRange(split, box.Count - split));
        }

        var binToIndex = new byte[32768];
        for (int i = 0; i < boxes.Count; i++)
        {
            long n = 0, r = 0, g = 0, b = 0;
            foreach (int bin in boxes[i])
            {
                n += count[bin];
                r += sumR[bin];
                g += sumG[bin];
                b += sumB[bin];
                binToIndex[bin] = (byte)i;
            }
            SetPaletteEntry(palette, i, (int)(r / n), (int)(g / n), (int)(b / n));
        }
        return binToIndex;
    }

    private static void SetPaletteEntry(byte[] palette, int index, int r, int g, int b)
    {
        palette[index * 3] = (byte)r;
        palette[index * 3 + 1] = (byte)g;
        palette[index * 3 + 2] = (byte)b;
    }
}

/// <summary>GIF variable-length LZW encoder (8-bit minimum code size).</summary>
internal static class LzwEncoder
{
    private const int MinCodeSize = 8;
    private const int ClearCode = 1 << MinCodeSize;
    private const int EndCode = ClearCode + 1;
    private const int MaxCode = 4096;

    public static void Encode(Stream output, byte[] indices)
    {
        output.WriteByte(MinCodeSize);
        var writer = new SubBlockWriter(output);
        var dictionary = new Dictionary<int, int>(MaxCode);
        int codeSize = MinCodeSize + 1;
        int next = EndCode + 1;

        writer.Write(ClearCode, codeSize);
        int prefix = indices[0];
        for (int i = 1; i < indices.Length; i++)
        {
            int k = indices[i];
            int key = prefix << 8 | k;
            if (dictionary.TryGetValue(key, out int code))
            {
                prefix = code;
                continue;
            }

            writer.Write(prefix, codeSize);
            if (next < MaxCode)
            {
                dictionary[key] = next++;
                // The decoder adds its entries one code later, so it widens when next exceeds the limit.
                if (next > 1 << codeSize && codeSize < 12)
                    codeSize++;
            }
            else
            {
                writer.Write(ClearCode, codeSize);
                dictionary.Clear();
                codeSize = MinCodeSize + 1;
                next = EndCode + 1;
            }
            prefix = k;
        }

        writer.Write(prefix, codeSize);
        // The decoder adds one more entry after reading the last code, which may widen the end code.
        if (next >= 1 << codeSize && codeSize < 12)
            codeSize++;
        writer.Write(EndCode, codeSize);
        writer.Flush();
        output.WriteByte(0); // block terminator
    }

    /// <summary>Packs codes LSB-first into GIF data sub-blocks of up to 255 bytes.</summary>
    private sealed class SubBlockWriter(Stream output)
    {
        private readonly byte[] _block = new byte[255];
        private int _length;
        private int _bits;
        private int _bitCount;

        public void Write(int code, int size)
        {
            _bits |= code << _bitCount;
            _bitCount += size;
            while (_bitCount >= 8)
            {
                Put((byte)_bits);
                _bits >>= 8;
                _bitCount -= 8;
            }
        }

        public void Flush()
        {
            if (_bitCount > 0)
            {
                Put((byte)_bits);
                _bits = 0;
                _bitCount = 0;
            }
            if (_length > 0)
            {
                output.WriteByte((byte)_length);
                output.Write(_block, 0, _length);
                _length = 0;
            }
        }

        private void Put(byte b)
        {
            _block[_length++] = b;
            if (_length == _block.Length)
            {
                output.WriteByte((byte)_length);
                output.Write(_block, 0, _length);
                _length = 0;
            }
        }
    }
}
