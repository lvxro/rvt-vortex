using ModelContextProtocol.Protocol;
using Newtonsoft.Json.Linq;

namespace RevitCortex.Server.Tools;

/// <summary>
/// Turns a plugin result that carries a picture into MCP content: one text block with
/// the result's other fields, then one image block. The plugin sends the picture as
/// base64 in <c>imageBase64</c> with its type in <c>mimeType</c>; left inside the text
/// the model would read a megabyte of letters instead of seeing an image.
///
/// Anything else (a failure, a result without a picture, base64 that does not decode)
/// comes back as a single text block, unchanged.
/// </summary>
public static class ToolImageResult
{
    public const string ImageField = "imageBase64";
    public const string MimeTypeField = "mimeType";

    public static IEnumerable<ContentBlock> ToContent(JToken result)
    {
        if (result is JObject payload
            && payload[ImageField] is JValue { Type: JTokenType.String } encoded
            && TryDecode((string?)encoded, out var image))
        {
            var details = (JObject)payload.DeepClone();
            details.Remove(ImageField);

            var mimeType = payload[MimeTypeField]?.Type == JTokenType.String
                ? payload[MimeTypeField]!.Value<string>()
                : null;
            if (string.IsNullOrWhiteSpace(mimeType)) mimeType = "image/png";

            return new ContentBlock[]
            {
                new TextContentBlock { Text = details.ToString() },
                ImageContentBlock.FromBytes(image, mimeType!)
            };
        }

        return new ContentBlock[] { new TextContentBlock { Text = result.ToString() } };
    }

    private static bool TryDecode(string? encoded, out byte[] image)
    {
        image = Array.Empty<byte>();
        if (string.IsNullOrEmpty(encoded)) return false;

        try
        {
            image = Convert.FromBase64String(encoded);
            return image.Length > 0;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
