using System.Collections.Generic;
using RevitCortex.Tools.Views;
using Xunit;

namespace RevitCortex.Tests.Views;

/// <summary>
/// The Revit-free half of get_view_image: reading an exported image's size from its
/// header, normalising the inputs, and the plan for an image too large to return.
/// </summary>
public class ViewImageSupportTests
{
    private static byte[] Png(int width, int height)
    {
        var bytes = new List<byte> { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
        bytes.AddRange(new byte[] { 0, 0, 0, 13, (byte)'I', (byte)'H', (byte)'D', (byte)'R' });
        bytes.AddRange(BigEndian32(width));
        bytes.AddRange(BigEndian32(height));
        bytes.AddRange(new byte[] { 8, 6, 0, 0, 0 });
        return bytes.ToArray();
    }

    private static byte[] BigEndian32(int value)
    {
        return new[] { (byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value };
    }

    /// <summary>A JPEG header: an APP0 segment, a quantisation table, then the frame.</summary>
    private static byte[] Jpeg(int width, int height, byte frameMarker = 0xC0)
    {
        var bytes = new List<byte> { 0xFF, 0xD8 };
        bytes.AddRange(new byte[] { 0xFF, 0xE0, 0x00, 0x10 });
        bytes.AddRange(new byte[14]);
        bytes.AddRange(new byte[] { 0xFF, 0xDB, 0x00, 0x05, 1, 2, 3 });
        bytes.AddRange(new byte[] { 0xFF, frameMarker, 0x00, 0x0B, 8 });
        bytes.AddRange(new[] { (byte)(height >> 8), (byte)height, (byte)(width >> 8), (byte)width });
        bytes.AddRange(new byte[] { 1, 1, 0x11, 0 });
        return bytes.ToArray();
    }

    [Fact]
    public void ReadsPngSize()
    {
        Assert.True(ViewImageSupport.TryReadImageInfo(Png(1568, 1109), out var mime, out var width, out var height));
        Assert.Equal("image/png", mime);
        Assert.Equal(1568, width);
        Assert.Equal(1109, height);
    }

    [Theory]
    [InlineData((byte)0xC0)] // baseline
    [InlineData((byte)0xC2)] // progressive
    public void ReadsJpegSize_PastTheSegmentsBeforeTheFrame(byte frameMarker)
    {
        Assert.True(ViewImageSupport.TryReadImageInfo(Jpeg(1097, 776, frameMarker), out var mime, out var width, out var height));
        Assert.Equal("image/jpeg", mime);
        Assert.Equal(1097, width);
        Assert.Equal(776, height);
    }

    [Fact]
    public void JpegHuffmanTable_IsNotMistakenForAFrame()
    {
        // 0xC4 sits inside the start-of-frame range but is a Huffman table.
        var bytes = new List<byte> { 0xFF, 0xD8, 0xFF, 0xC4, 0x00, 0x09, 1, 2, 3, 4, 5, 6, 7 };
        bytes.AddRange(new byte[] { 0xFF, 0xC0, 0x00, 0x0B, 8, 0x01, 0x00, 0x02, 0x00, 1, 1, 0x11, 0 });

        Assert.True(ViewImageSupport.TryReadImageInfo(bytes.ToArray(), out _, out var width, out var height));
        Assert.Equal(512, width);
        Assert.Equal(256, height);
    }

    [Fact]
    public void RejectsWhatIsNotAnImage()
    {
        Assert.False(ViewImageSupport.TryReadImageInfo(null, out _, out _, out _));
        Assert.False(ViewImageSupport.TryReadImageInfo(new byte[0], out _, out _, out _));
        Assert.False(ViewImageSupport.TryReadImageInfo(new byte[] { (byte)'B', (byte)'M', 0, 0, 0, 0, 0, 0 }, out _, out _, out _));
    }

    [Fact]
    public void RejectsTruncatedHeaders()
    {
        var png = Png(800, 600);
        Assert.False(ViewImageSupport.TryReadImageInfo(png[..20], out _, out _, out _));

        var jpeg = Jpeg(800, 600);
        Assert.False(ViewImageSupport.TryReadImageInfo(jpeg[..^6], out _, out _, out _));

        // A scan with no frame before it.
        Assert.False(ViewImageSupport.TryReadImageInfo(new byte[] { 0xFF, 0xD8, 0xFF, 0xDA, 0x00, 0x02, 0, 0 }, out _, out _, out _));
    }

    [Theory]
    [InlineData(null, 1568)]
    [InlineData(100, 256)]
    [InlineData(2000, 2000)]
    [InlineData(99999, 4096)]
    public void PixelSize_DefaultsAndClamps(int? requested, int expected)
    {
        Assert.Equal(expected, ViewImageSupport.ClampPixelSize(requested));
    }

    [Theory]
    [InlineData(null, "png")]
    [InlineData("", "png")]
    [InlineData("PNG", "png")]
    [InlineData(" jpg ", "jpeg")]
    [InlineData("JPEG", "jpeg")]
    public void Format_Normalises(string? input, string expected)
    {
        Assert.True(ViewImageSupport.TryNormalizeFormat(input, out var format));
        Assert.Equal(expected, format);
    }

    [Fact]
    public void Format_RejectsOtherTypes()
    {
        Assert.False(ViewImageSupport.TryNormalizeFormat("tiff", out _));
    }

    [Theory]
    [InlineData(null, "full")]
    [InlineData("Full", "full")]
    [InlineData("VISIBLE", "visible")]
    public void Region_Normalises(string? input, string expected)
    {
        Assert.True(ViewImageSupport.TryNormalizeRegion(input, out var region));
        Assert.Equal(expected, region);
    }

    [Fact]
    public void Region_RejectsOtherValues()
    {
        Assert.False(ViewImageSupport.TryNormalizeRegion("crop", out _));
    }

    [Fact]
    public void OversizedPng_IsRetriedAsJpegAtTheSameSize()
    {
        Assert.True(ViewImageSupport.TryNextAttempt("png", 1568, out var format, out var pixelSize));
        Assert.Equal("jpeg", format);
        Assert.Equal(1568, pixelSize);
    }

    [Fact]
    public void OversizedJpeg_IsRetriedSmaller_DownToTheMinimum()
    {
        Assert.True(ViewImageSupport.TryNextAttempt("jpeg", 1568, out var format, out var pixelSize));
        Assert.Equal("jpeg", format);
        Assert.Equal(1098, pixelSize);

        Assert.True(ViewImageSupport.TryNextAttempt("jpeg", 300, out _, out pixelSize));
        Assert.Equal(ViewImageSupport.MinPixelSize, pixelSize);

        Assert.False(ViewImageSupport.TryNextAttempt("jpeg", ViewImageSupport.MinPixelSize, out _, out _));
    }

    [Fact]
    public void ShrinkPlan_EndsWithinTheAttemptBudget_AndNeverGrows()
    {
        var format = "png";
        var pixelSize = ViewImageSupport.MaxPixelSize;
        var steps = 0;
        while (ViewImageSupport.TryNextAttempt(format, pixelSize, out var nextFormat, out var nextPixelSize))
        {
            Assert.True(nextPixelSize <= pixelSize);
            Assert.True(nextFormat != format || nextPixelSize < pixelSize, "each step must change something");
            format = nextFormat;
            pixelSize = nextPixelSize;
            Assert.True(++steps < 50, "the plan must terminate");
        }

        Assert.Equal(ViewImageSupport.MinPixelSize, pixelSize);
    }

    [Fact]
    public void ImageBudget_LeavesRoomForBase64UnderOneMegabyte()
    {
        var base64Length = (ViewImageSupport.MaxImageBytes + 2) / 3 * 4;
        Assert.True(base64Length < 1_000_000);
    }
}
