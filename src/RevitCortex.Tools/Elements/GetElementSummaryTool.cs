using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Results;
using RevitCortex.Core.Session;
using RevitCortex.Core.Tools;
using RevitCortex.Tools.Utilities;

namespace RevitCortex.Tools.Elements;

/// <summary>
/// One compact card per element ID: category, name, comments, bounding box in mm,
/// number of solids and volume. It answers "what are these IDs, how big, and where"
/// in one call, which used to take a script.
///
/// Solids are counted the way get_element_solid_geometry counts them (Fine detail,
/// volume above 1e-6 ft3, instance geometry unwrapped one level), so the two tools
/// agree on the number.
/// </summary>
[ToolSafety(true, false)]
public class GetElementSummaryTool : ICortexTool
{
    public string Name => "get_element_summary";
    public string Category => "Elements";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "One compact card per element ID: category, name, comments, bounding box in mm, number of solids and volume in m3.";

    /// <summary>Most elements described in one call; more IDs than this are reported as cut.</summary>
    public const int MaxElements = 500;

    /// <summary>
    /// Reading solids is the slow part. Past this many seconds the remaining elements
    /// are returned without solids, and the result says how many.
    /// </summary>
    private const int SolidsBudgetSeconds = 60;

    private const double MmPerFoot = 304.8;
    private const double Ft3ToM3 = 0.0283168;
    private const double MinVolumeFt3 = 1e-6;

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var (doc, error) = ToolHelpers.RequireDocument(session);
        if (doc == null) return error!;

        List<long> requested;
        bool includeSolids;
        try
        {
            requested = (input["elementIds"]?.ToObject<List<long>>() ?? new List<long>()).Distinct().ToList();
            includeSolids = input["includeSolids"]?.Value<bool?>() ?? true;
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                $"Could not read the parameters: {ex.Message}",
                suggestion: "elementIds is an array of numbers, e.g. {\"elementIds\": [606873, 606874]}");
        }

        if (requested.Count == 0)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "elementIds is required",
                suggestion: "Example: {\"elementIds\": [606873, 606874]}");

        try
        {
            var elements = new JArray();
            var notFound = new JArray();
            var solidsSkipped = 0;
            var stopwatch = Stopwatch.StartNew();

            foreach (var id in requested.Take(MaxElements))
            {
                var element = doc.GetElement(ToolHelpers.ToElementId(id));
                if (element == null)
                {
                    notFound.Add(id);
                    continue;
                }

                var readSolids = includeSolids && stopwatch.Elapsed.TotalSeconds <= SolidsBudgetSeconds;
                if (includeSolids && !readSolids) solidsSkipped++;

                elements.Add(Describe(id, element, readSolids));
            }

            var result = new JObject
            {
                ["requestedCount"] = requested.Count,
                ["returnedCount"] = elements.Count,
                ["truncated"] = requested.Count > MaxElements
            };
            if (notFound.Count > 0) result["notFound"] = notFound;
            if (requested.Count > MaxElements)
                result["note"] = $"Only the first {MaxElements} IDs were read. Call again with the rest.";
            if (solidsSkipped > 0)
            {
                result["solidsSkipped"] = solidsSkipped;
                result["solidsNote"] = $"Reading solids took more than {SolidsBudgetSeconds} s, so {solidsSkipped} element(s) " +
                                       "came back without solidCount and volumeM3. Call again with only those IDs.";
            }
            result["elements"] = elements;

            return CortexResult<object>.Ok(result);
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown,
                $"Failed to summarize the elements: {ex.Message}");
        }
    }

    /// <summary>A field that has no value is left out, to keep a long list short.</summary>
    private static JObject Describe(long id, Element element, bool readSolids)
    {
        var card = new JObject { ["id"] = id };

        var name = Safe(() => element.Name);
        if (!string.IsNullOrEmpty(name)) card["name"] = name;

        var category = Safe(() => element.Category?.Name);
        if (!string.IsNullOrEmpty(category)) card["category"] = category;

        var categoryCode = Safe(() => CategoryCode(element));
        if (!string.IsNullOrEmpty(categoryCode)) card["categoryCode"] = categoryCode;

        var comments = Safe(() => element.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS)?.AsString());
        if (!string.IsNullOrEmpty(comments)) card["comments"] = comments;

        var box = SafeBox(element);
        if (box != null) card["boundingBox"] = box;

        if (readSolids)
        {
            try
            {
                CountSolids(element, out var solidCount, out var volumeFt3);
                card["solidCount"] = solidCount;
                card["volumeM3"] = Math.Round(volumeFt3 * Ft3ToM3, 6);
            }
            catch (Exception ex)
            {
                card["solidsError"] = ex.Message;
            }
        }

        return card;
    }

    private static string? CategoryCode(Element element)
    {
        var category = element.Category;
        if (category == null) return null;

        // Built-in categories have negative IDs that are the BuiltInCategory values.
        var value = ToolHelpers.GetElementIdValue(category.Id);
        if (value >= 0 || value < int.MinValue) return null;

        return Enum.GetName(typeof(BuiltInCategory), (BuiltInCategory)(int)value);
    }

    /// <summary>The element's bounding box in model coordinates, in mm.</summary>
    private static JObject? SafeBox(Element element)
    {
        try
        {
            var box = element.get_BoundingBox(null);
            if (box == null) return null;

            return new JObject
            {
                ["min"] = PointMm(box.Min),
                ["max"] = PointMm(box.Max),
                ["sizeX"] = Mm(box.Max.X - box.Min.X),
                ["sizeY"] = Mm(box.Max.Y - box.Min.Y),
                ["sizeZ"] = Mm(box.Max.Z - box.Min.Z)
            };
        }
        catch
        {
            return null;
        }
    }

    private static void CountSolids(Element element, out int count, out double volumeFt3)
    {
        count = 0;
        volumeFt3 = 0;

        var geometry = element.get_Geometry(new Options { DetailLevel = ViewDetailLevel.Fine });
        if (geometry == null) return;

        foreach (var obj in geometry)
        {
            if (obj is Solid solid)
            {
                Add(solid, ref count, ref volumeFt3);
            }
            else if (obj is GeometryInstance instance)
            {
                foreach (var nested in instance.GetInstanceGeometry())
                {
                    if (nested is Solid nestedSolid)
                        Add(nestedSolid, ref count, ref volumeFt3);
                }
            }
        }
    }

    private static void Add(Solid solid, ref int count, ref double volumeFt3)
    {
        if (solid.Faces.Size == 0 || solid.Volume <= MinVolumeFt3) return;
        count++;
        volumeFt3 += solid.Volume;
    }

    private static JObject PointMm(XYZ point) => new JObject
    {
        ["x"] = Mm(point.X),
        ["y"] = Mm(point.Y),
        ["z"] = Mm(point.Z)
    };

    private static double Mm(double feet) => Math.Round(feet * MmPerFoot, 1);

    private static string? Safe(Func<string?> read)
    {
        try { return read(); }
        catch { return null; }
    }
}
