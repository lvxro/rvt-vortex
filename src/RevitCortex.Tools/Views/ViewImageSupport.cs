using System;

namespace RevitCortex.Tools.Views;

/// <summary>
/// The parts of get_view_image that need no Revit: input normalisation, reading
/// an image's size from its header, and the plan for shrinking an image that is
/// too large to return. Kept free of Revit types so the unit tests run without
/// a Revit host.
/// </summary>
public static class ViewImageSupport
{
    /// <summary>Longest side, in pixels, when the caller does not ask for one.</summary>
    public const int DefaultPixelSize = 1568;

    public const int MinPixelSize = 256;
    public const int MaxPixelSize = 4096;

    /// <summary>
    /// Largest image the tool returns. MCP clients cap a tool result at about
    /// 1 MB; base64 adds a third, so 700 kB of image stays under that cap.
    /// </summary>
    public const int MaxImageBytes = 700_000;

    /// <summary>Exports tried before giving up on fitting <see cref="MaxImageBytes"/>.</summary>
    public const int MaxAttempts = 5;

    public const string FormatPng = "png";
    public const string FormatJpeg = "jpeg";
    public const string RegionFull = "full";
    public const string RegionVisible = "visible";

    public static int ClampPixelSize(int? requested)
    {
        if (requested == null) return DefaultPixelSize;
        return Math.Max(MinPixelSize, Math.Min(MaxPixelSize, requested.Value));
    }

    /// <summary>Accepts png, jpeg and jpg in any case; empty means png.</summary>
    public static bool TryNormalizeFormat(string? value, out string format)
    {
        format = FormatPng;
        if (string.IsNullOrWhiteSpace(value)) return true;

        switch (value!.Trim().ToLowerInvariant())
        {
            case "png":
                return true;
            case "jpeg":
            case "jpg":
                format = FormatJpeg;
                return true;
            default:
                return false;
        }
    }

    /// <summary>Accepts full and visible in any case; empty means full.</summary>
    public static bool TryNormalizeRegion(string? value, out string region)
    {
        region = RegionFull;
        if (string.IsNullOrWhiteSpace(value)) return true;

        switch (value!.Trim().ToLowerInvariant())
        {
            case "full":
                return true;
            case "visible":
                region = RegionVisible;
                return true;
            default:
                return false;
        }
    }

    /// <summary>
    /// The next export to try after one came out larger than <see cref="MaxImageBytes"/>:
    /// first the same size as JPEG, then smaller JPEGs. False when there is nothing
    /// smaller left to try.
    /// </summary>
    public static bool TryNextAttempt(string format, int pixelSize, out string nextFormat, out int nextPixelSize)
    {
        nextFormat = FormatJpeg;
        nextPixelSize = pixelSize;

        if (format != FormatJpeg) return true;
        if (pixelSize <= MinPixelSize) return false;

        nextPixelSize = Math.Max(MinPixelSize, (int)Math.Round(pixelSize * 0.7));
        return true;
    }

    /// <summary>
    /// Reads the type and pixel size of a PNG or JPEG from its header.
    /// False for anything else, including a truncated file.
    /// </summary>
    public static bool TryReadImageInfo(byte[]? bytes, out string mimeType, out int width, out int height)
    {
        mimeType = string.Empty;
        width = 0;
        height = 0;
        if (bytes == null) return false;

        if (IsPng(bytes))
        {
            // Signature (8) + IHDR length (4) + "IHDR" (4), then width and height, big-endian.
            if (bytes.Length < 24) return false;
            width = ReadInt32BigEndian(bytes, 16);
            height = ReadInt32BigEndian(bytes, 20);
            if (width <= 0 || height <= 0) return false;
            mimeType = "image/png";
            return true;
        }

        if (bytes.Length >= 4 && bytes[0] == 0xFF && bytes[1] == 0xD8)
        {
            if (!TryReadJpegSize(bytes, out width, out height)) return false;
            mimeType = "image/jpeg";
            return true;
        }

        return false;
    }

    private static bool IsPng(byte[] bytes)
    {
        return bytes.Length >= 8
            && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47
            && bytes[4] == 0x0D && bytes[5] == 0x0A && bytes[6] == 0x1A && bytes[7] == 0x0A;
    }

    private static int ReadInt32BigEndian(byte[] bytes, int offset)
    {
        return (bytes[offset] << 24) | (bytes[offset + 1] << 16) | (bytes[offset + 2] << 8) | bytes[offset + 3];
    }

    /// <summary>
    /// Walks the JPEG segments up to the first start-of-frame, which carries the size.
    /// </summary>
    private static bool TryReadJpegSize(byte[] bytes, out int width, out int height)
    {
        width = 0;
        height = 0;

        var pos = 2;
        while (pos + 3 < bytes.Length)
        {
            if (bytes[pos] != 0xFF)
            {
                pos++;
                continue;
            }

            var marker = bytes[pos + 1];

            // 0xFF may be repeated as padding before a marker.
            if (marker == 0xFF)
            {
                pos++;
                continue;
            }

            // Markers with no length field.
            if (marker == 0x01 || marker == 0xD8 || (marker >= 0xD0 && marker <= 0xD7))
            {
                pos += 2;
                continue;
            }

            // End of image, or start of scan: the frame header always comes before the scan.
            if (marker == 0xD9 || marker == 0xDA) return false;

            var segmentLength = (bytes[pos + 2] << 8) | bytes[pos + 3];
            if (segmentLength < 2) return false;

            var isStartOfFrame = marker >= 0xC0 && marker <= 0xCF
                && marker != 0xC4 && marker != 0xC8 && marker != 0xCC;
            if (isStartOfFrame)
            {
                // marker (2) + length (2) + precision (1), then height and width.
                if (pos + 8 >= bytes.Length) return false;
                height = (bytes[pos + 5] << 8) | bytes[pos + 6];
                width = (bytes[pos + 7] << 8) | bytes[pos + 8];
                return width > 0 && height > 0;
            }

            pos += 2 + segmentLength;
        }

        return false;
    }
}
