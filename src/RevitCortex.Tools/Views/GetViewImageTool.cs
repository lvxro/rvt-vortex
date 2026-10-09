using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Results;
using RevitCortex.Core.Session;
using RevitCortex.Core.Tools;
using RevitCortex.Tools.Utilities;

namespace RevitCortex.Tools.Views;

/// <summary>
/// Exports a view as an image and returns it base64-encoded, so the AI can look at
/// the model without a screenshot. The MCP server turns <c>imageBase64</c> into an
/// MCP image block (see ToolImageResult in RevitCortex.Server).
///
/// Three ways to frame the picture:
///   - region "full" (default): the whole view, any view or sheet in the document;
///   - region "visible": what is on screen now, at the current zoom (active view only);
///   - elementIds: zoom the active view to those elements, capture, put the zoom back.
///
/// Nothing in the model changes: no Transaction is opened. Zooming is a UI state.
/// </summary>
[ToolSafety(true, false)]
public class GetViewImageTool : ICortexTool
{
    public string Name => "get_view_image";
    public string Category => "Views";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Exports a view or sheet as an image and returns it to the AI, so it can see the model without a screenshot.";

    /// <summary>Stop shrinking an oversized image once the exports have taken this long.</summary>
    private const int ExportBudgetSeconds = 75;

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var (doc, docError) = ToolHelpers.RequireDocument(session);
        if (doc == null) return docError!;

        long? viewId;
        string? viewName;
        List<long> elementIds;
        int pixelSize;
        string? formatInput;
        string? regionInput;
        try
        {
            viewId = input["viewId"]?.Value<long?>();
            viewName = input["viewName"]?.Value<string>();
            elementIds = input["elementIds"]?.ToObject<List<long>>() ?? new List<long>();
            pixelSize = ViewImageSupport.ClampPixelSize(input["pixelSize"]?.Value<int?>());
            formatInput = input["format"]?.Value<string>();
            regionInput = input["region"]?.Value<string>();
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                $"Could not read the parameters: {ex.Message}",
                suggestion: "viewId and pixelSize are numbers, elementIds is an array of numbers, viewName, region and format are strings");
        }

        if (!ViewImageSupport.TryNormalizeFormat(formatInput, out var format))
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                $"Unsupported format: {formatInput}", suggestion: "Use: png, jpeg");

        if (!ViewImageSupport.TryNormalizeRegion(regionInput, out var region))
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                $"Unsupported region: {regionInput}", suggestion: "Use: full, visible");

        try
        {
            var view = ResolveView(doc, viewId, viewName, out var viewError);
            if (view == null) return viewError!;

            if (view.IsTemplate)
                return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                    $"'{view.Name}' is a view template, not a view",
                    suggestion: "Pass the ID of a view or a sheet");

            if (!view.CanBePrinted)
                return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                    $"A view of type {view.ViewType} cannot be exported as an image",
                    suggestion: "Open a model view, a drafting view or a sheet. For a schedule use get_schedule_data");

            var frameElements = elementIds.Count > 0;
            var visibleRegion = frameElements || region == ViewImageSupport.RegionVisible;

            UIView? uiView = null;
            if (visibleRegion)
            {
                var activeView = doc.ActiveView;
                if (activeView == null
                    || ToolHelpers.GetElementIdValue(activeView.Id) != ToolHelpers.GetElementIdValue(view.Id))
                    return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                        "region \"visible\" and elementIds work on the active view only",
                        suggestion: "Leave viewId out to use the active view, or use region \"full\" for any other view");

                uiView = FindUiView(doc, view);
                if (uiView == null)
                    return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                        $"The view '{view.Name}' has no open window in Revit",
                        suggestion: "Use region \"full\", which does not need the view to be open");
            }

            IList<XYZ>? zoomBefore = null;
            if (frameElements)
            {
                if (!TryGetFrameCorners(doc, view, elementIds, out var corner1, out var corner2))
                    return CortexResult<object>.Fail(CortexErrorCode.ElementNotFound,
                        "None of the elementIds has geometry in this view",
                        suggestion: "Check the IDs, or open a view where the elements are visible");

                try
                {
                    zoomBefore = uiView!.GetZoomCorners();
                    uiView.ZoomAndCenterRectangle(corner1, corner2);
                }
                catch (Exception ex)
                {
                    return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                        $"Could not zoom the view '{view.Name}' to the elements: {ex.Message}",
                        suggestion: "Some views cannot be zoomed from the API (a perspective 3D view, for one). Use region \"visible\" or \"full\" instead of elementIds");
                }
            }

            try
            {
                return ExportWithinBudget(doc, view, uiView, visibleRegion, frameElements, region, format, pixelSize);
            }
            finally
            {
                // Put the user's zoom back: looking at the model must not move their screen.
                if (zoomBefore != null && zoomBefore.Count >= 2)
                {
                    try { uiView!.ZoomAndCenterRectangle(zoomBefore[0], zoomBefore[1]); }
                    catch { /* the picture is already taken; a zoom that cannot be restored is not an error */ }
                }
            }
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown,
                $"Failed to export the view image: {ex.Message}",
                suggestion: "Revit cannot export an empty view. If the view has content, try a smaller pixelSize");
        }
    }

    private static CortexResult<object> ExportWithinBudget(
        Document doc, View view, UIView? uiView,
        bool visibleRegion, bool frameElements, string region,
        string requestedFormat, int requestedPixelSize)
    {
        var format = requestedFormat;
        var pixelSize = requestedPixelSize;
        var fit = GuessFitDirection(view, uiView);
        var fitCorrected = false;
        var stopwatch = Stopwatch.StartNew();

        byte[]? image = null;
        var mimeType = string.Empty;
        var width = 0;
        var height = 0;
        long lastByteLength = 0;

        for (var attempt = 0; attempt < ViewImageSupport.MaxAttempts; attempt++)
        {
            var bytes = ExportOnce(doc, view, visibleRegion, format, pixelSize, fit);
            lastByteLength = bytes.Length;

            if (!ViewImageSupport.TryReadImageInfo(bytes, out mimeType, out width, out height))
                return CortexResult<object>.Fail(CortexErrorCode.Unknown,
                    "Revit wrote a file that is not a PNG or JPEG image");

            // PixelSize applies to the fit direction only. If the guess was wrong the
            // other side came out longer than asked: export once more the other way.
            if (!fitCorrected && Math.Max(width, height) > pixelSize * 1.1)
            {
                fitCorrected = true;
                fit = fit == FitDirectionType.Horizontal ? FitDirectionType.Vertical : FitDirectionType.Horizontal;
                continue;
            }

            if (bytes.Length <= ViewImageSupport.MaxImageBytes)
            {
                image = bytes;
                break;
            }

            if (stopwatch.Elapsed.TotalSeconds > ExportBudgetSeconds
                || !ViewImageSupport.TryNextAttempt(format, pixelSize, out format, out pixelSize))
                break;
        }

        if (image == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                $"The image is too large to return ({lastByteLength / 1000} kB, limit {ViewImageSupport.MaxImageBytes / 1000} kB)",
                suggestion: "Lower pixelSize, use format \"jpeg\", or capture a smaller area with region \"visible\" or elementIds");

        string? note = null;
        if (format != requestedFormat || pixelSize != requestedPixelSize)
            note = $"Returned as {format} at {pixelSize} px to stay under {ViewImageSupport.MaxImageBytes / 1000} kB " +
                   $"(asked for {requestedFormat} at {requestedPixelSize} px).";

        return CortexResult<object>.Ok(new
        {
            viewId = ToolHelpers.GetElementIdValue(view.Id),
            viewName = view.Name,
            viewType = view.ViewType.ToString(),
            scale = view.Scale,
            region = frameElements ? "elements" : region,
            mimeType,
            width,
            height,
            byteLength = image.Length,
            note,
            imageBase64 = Convert.ToBase64String(image)
        });
    }

    private static View? ResolveView(Document doc, long? viewId, string? viewName, out CortexResult<object>? error)
    {
        error = null;

        if (viewId != null)
        {
            var byId = doc.GetElement(ToolHelpers.ToElementId(viewId.Value)) as View;
            if (byId == null)
                error = CortexResult<object>.Fail(CortexErrorCode.ElementNotFound,
                    $"No view with ID {viewId.Value} in the active document",
                    suggestion: "Leave viewId out to capture the active view");
            return byId;
        }

        if (!string.IsNullOrWhiteSpace(viewName))
        {
            var wanted = viewName!.Trim();
            var matches = new FilteredElementCollector(doc)
                .OfClass(typeof(View))
                .Cast<View>()
                .Where(v => !v.IsTemplate && string.Equals(v.Name, wanted, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (matches.Count == 1) return matches[0];

            if (matches.Count == 0)
            {
                error = CortexResult<object>.Fail(CortexErrorCode.ElementNotFound,
                    $"No view named '{wanted}' in the active document",
                    suggestion: "Check the name, or pass viewId");
                return null;
            }

            var candidates = string.Join(", ", matches
                .Take(10)
                .Select(v => $"{ToolHelpers.GetElementIdValue(v.Id)} ({v.ViewType})"));
            error = CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                $"{matches.Count} views are named '{wanted}'",
                suggestion: $"Pass viewId instead. Candidates: {candidates}");
            return null;
        }

        var active = doc.ActiveView;
        if (active == null)
            error = CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "The document has no active view",
                suggestion: "Open a view in Revit, or pass viewId");
        return active;
    }

    private static UIView? FindUiView(Document doc, View view)
    {
        var wanted = ToolHelpers.GetElementIdValue(view.Id);
        return new UIDocument(doc).GetOpenUIViews()
            .FirstOrDefault(v => ToolHelpers.GetElementIdValue(v.ViewId) == wanted);
    }

    /// <summary>
    /// PixelSize sets the image's width (Horizontal) or its height (Vertical). To make it
    /// the longest side, fit along whichever side of the picture is longer.
    /// </summary>
    private static FitDirectionType GuessFitDirection(View view, UIView? uiView)
    {
        try
        {
            if (uiView != null)
            {
                var window = uiView.GetWindowRectangle();
                return (window.Bottom - window.Top) > (window.Right - window.Left)
                    ? FitDirectionType.Vertical
                    : FitDirectionType.Horizontal;
            }

            var outline = view.Outline;
            if (outline != null)
                return (outline.Max.V - outline.Min.V) > (outline.Max.U - outline.Min.U)
                    ? FitDirectionType.Vertical
                    : FitDirectionType.Horizontal;
        }
        catch
        {
            // Not every view reports an outline; the export corrects a wrong guess.
        }

        return FitDirectionType.Horizontal;
    }

    /// <summary>
    /// Two opposite corners of the rectangle, in the plane of the view, that holds every
    /// element plus a margin. All eight corners of the combined box are projected: in a
    /// 3D view its two extreme corners alone do not bound it on screen.
    /// </summary>
    private static bool TryGetFrameCorners(
        Document doc, View view, IEnumerable<long> elementIds, out XYZ corner1, out XYZ corner2)
    {
        corner1 = XYZ.Zero;
        corner2 = XYZ.Zero;

        XYZ? min = null;
        XYZ? max = null;
        foreach (var id in elementIds)
        {
            var element = doc.GetElement(ToolHelpers.ToElementId(id));
            if (element == null) continue;

            var box = element.get_BoundingBox(view) ?? element.get_BoundingBox(null);
            if (box == null) continue;

            min = min == null ? box.Min : new XYZ(
                Math.Min(min.X, box.Min.X), Math.Min(min.Y, box.Min.Y), Math.Min(min.Z, box.Min.Z));
            max = max == null ? box.Max : new XYZ(
                Math.Max(max.X, box.Max.X), Math.Max(max.Y, box.Max.Y), Math.Max(max.Z, box.Max.Z));
        }

        if (min == null || max == null) return false;

        var right = view.RightDirection;
        var up = view.UpDirection;
        var toViewer = view.ViewDirection;

        double uMin = double.MaxValue, uMax = double.MinValue;
        double vMin = double.MaxValue, vMax = double.MinValue;
        double depthSum = 0;
        for (var i = 0; i < 8; i++)
        {
            var point = new XYZ(
                (i & 1) == 0 ? min.X : max.X,
                (i & 2) == 0 ? min.Y : max.Y,
                (i & 4) == 0 ? min.Z : max.Z);

            var u = point.DotProduct(right);
            var v = point.DotProduct(up);
            uMin = Math.Min(uMin, u);
            uMax = Math.Max(uMax, u);
            vMin = Math.Min(vMin, v);
            vMax = Math.Max(vMax, v);
            depthSum += point.DotProduct(toViewer);
        }

        // Margin: 15% of the longer side, and never less than half a foot (about 150 mm),
        // so a single small element is not framed edge to edge.
        var margin = Math.Max(0.5, Math.Max(uMax - uMin, vMax - vMin) * 0.15);
        var depth = depthSum / 8;

        corner1 = right.Multiply(uMin - margin).Add(up.Multiply(vMin - margin)).Add(toViewer.Multiply(depth));
        corner2 = right.Multiply(uMax + margin).Add(up.Multiply(vMax + margin)).Add(toViewer.Multiply(depth));
        return true;
    }

    /// <summary>
    /// One export to a folder of its own. Revit decides the file name (for a set of views
    /// it appends the view type and name), so the file is found by listing that folder.
    /// </summary>
    private static byte[] ExportOnce(
        Document doc, View view, bool visibleRegion, string format, int pixelSize, FitDirectionType fit)
    {
        var folder = Path.Combine(Path.GetTempPath(), "RevitCortex", "view-image-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var fileType = format == ViewImageSupport.FormatJpeg ? ImageFileType.JPEGMedium : ImageFileType.PNG;
            using var options = new ImageExportOptions
            {
                FilePath = Path.Combine(folder, "view"),
                ExportRange = visibleRegion ? ExportRange.VisibleRegionOfCurrentView : ExportRange.SetOfViews,
                ZoomType = ZoomFitType.FitToPage,
                FitDirection = fit,
                PixelSize = pixelSize,
                ImageResolution = ImageResolution.DPI_72,
                HLRandWFViewsFileType = fileType,
                ShadowViewsFileType = fileType
            };
            if (!visibleRegion)
                options.SetViewsAndSheets(new List<ElementId> { view.Id });

            doc.ExportImage(options);

            var file = new DirectoryInfo(folder).GetFiles()
                .OrderByDescending(f => f.Length)
                .FirstOrDefault();
            if (file == null)
                throw new InvalidOperationException("Revit did not write an image file");

            return File.ReadAllBytes(file.FullName);
        }
        finally
        {
            try { Directory.Delete(folder, true); }
            catch { /* a leftover temp folder must not fail the tool */ }
        }
    }
}
