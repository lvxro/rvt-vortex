using System;
using System.Linq;
using ModelContextProtocol.Protocol;
using Newtonsoft.Json.Linq;
using RevitCortex.Server.Tools;
using Xunit;

namespace RevitCortex.Tests.Server;

/// <summary>
/// get_view_image returns the picture as an MCP image block. If the base64 stayed in
/// the text block the model would read a megabyte of letters and see nothing.
/// </summary>
public class ToolImageResultTests
{
    private static readonly byte[] Picture = { 0x89, 0x50, 0x4E, 0x47, 1, 2, 3, 4, 5 };

    [Fact]
    public void ResultWithImage_BecomesTextThenImage()
    {
        var payload = new JObject
        {
            ["viewId"] = 312,
            ["viewName"] = "Level 1",
            ["mimeType"] = "image/png",
            ["width"] = 1568,
            ["imageBase64"] = Convert.ToBase64String(Picture)
        };

        var content = ToolImageResult.ToContent(payload).ToList();

        Assert.Equal(2, content.Count);

        var text = Assert.IsType<TextContentBlock>(content[0]);
        var details = JObject.Parse(text.Text);
        Assert.Null(details["imageBase64"]);
        Assert.Equal(312, details["viewId"]!.Value<int>());
        Assert.Equal("Level 1", details["viewName"]!.Value<string>());
        Assert.Equal(1568, details["width"]!.Value<int>());

        var image = Assert.IsType<ImageContentBlock>(content[1]);
        Assert.Equal("image/png", image.MimeType);
        Assert.Equal(Picture, image.DecodedData.ToArray());
    }

    [Fact]
    public void ResultWithImage_DoesNotChangeThePayloadItWasGiven()
    {
        var payload = new JObject { ["imageBase64"] = Convert.ToBase64String(Picture) };

        ToolImageResult.ToContent(payload).ToList();

        Assert.NotNull(payload["imageBase64"]);
    }

    [Fact]
    public void MissingMimeType_DefaultsToPng()
    {
        var payload = new JObject { ["imageBase64"] = Convert.ToBase64String(Picture) };

        var image = Assert.IsType<ImageContentBlock>(ToolImageResult.ToContent(payload).Last());

        Assert.Equal("image/png", image.MimeType);
    }

    [Fact]
    public void Failure_StaysASingleTextBlock()
    {
        var payload = JObject.Parse("""
        { "success": false, "error": { "code": "InvalidInput", "message": "A view of type Schedule cannot be exported as an image" } }
        """);

        var content = ToolImageResult.ToContent(payload).ToList();

        var text = Assert.IsType<TextContentBlock>(Assert.Single(content));
        Assert.Contains("cannot be exported", text.Text);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not base64 !!")]
    public void UnusableImageData_StaysText(string encoded)
    {
        var payload = new JObject { ["viewId"] = 1, ["imageBase64"] = encoded };

        var content = ToolImageResult.ToContent(payload).ToList();

        Assert.IsType<TextContentBlock>(Assert.Single(content));
    }

    [Fact]
    public void NonObjectResult_StaysText()
    {
        var content = ToolImageResult.ToContent(JValue.CreateNull()).ToList();

        Assert.IsType<TextContentBlock>(Assert.Single(content));
    }
}
