using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Results;
using RevitCortex.Core.Session;
using RevitCortex.Core.Tools;
using RevitCortex.Tools.Utilities;

namespace RevitCortex.Tools.Elements;

/// <summary>
/// Returns the elements currently selected in the Revit UI, with what is needed to
/// know what each one is without a second call: category, family, type and level.
/// </summary>
[ToolSafety(true, false)]
public class GetSelectedElementsTool : ICortexTool
{
    public string Name => "get_selected_elements";
    public string Category => "Elements";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Returns the elements currently selected in the Revit UI, with category, family, type and level.";

    /// <summary>
    /// Where Revit keeps an element's level when <see cref="Element.LevelId"/> is empty
    /// (beams, for one, keep it in "Reference Level"). Tried in this order.
    /// </summary>
    private static readonly BuiltInParameter[] LevelParameters =
    {
        BuiltInParameter.FAMILY_LEVEL_PARAM,
        BuiltInParameter.INSTANCE_REFERENCE_LEVEL_PARAM,
        BuiltInParameter.FAMILY_BASE_LEVEL_PARAM,
        BuiltInParameter.SCHEDULE_LEVEL_PARAM,
        BuiltInParameter.LEVEL_PARAM,
        BuiltInParameter.WALL_BASE_CONSTRAINT,
        BuiltInParameter.RBS_START_LEVEL_PARAM,
        BuiltInParameter.STAIRS_BASE_LEVEL_PARAM,
        BuiltInParameter.ROOF_BASE_LEVEL_PARAM,
        BuiltInParameter.ROOF_CONSTRAINT_LEVEL_PARAM
    };

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var doc = session.Store.Get<object>("activeDocument") as Document;
        if (doc == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "No active document in session");

        var limit = input["limit"]?.Value<int?>() ?? 500;

        try
        {
            var uiDoc = new UIDocument(doc);
            var selected = uiDoc.Selection.GetElementIds()
                .Select(id => doc.GetElement(id))
                .Where(e => e != null)
                .ToList();

            IEnumerable<Element> returned = selected;
            if (limit > 0)
                returned = returned.Take(limit);

            var elements = returned.Select(e => Describe(doc, e)).ToList();
            var truncated = elements.Count < selected.Count;

            return CortexResult<object>.Ok(new
            {
                message          = selected.Count == 0
                                       ? "No elements are currently selected"
                                       : truncated
                                           ? $"Returning {elements.Count} of {selected.Count} selected element(s)"
                                           : $"Found {selected.Count} selected element(s)",
                selectedCount    = selected.Count,
                returnedCount    = elements.Count,
                truncated,
                elements
            });
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown,
                $"Failed to retrieve selected elements: {ex.Message}");
        }
    }

    private static object Describe(Document doc, Element element)
    {
        var type = GetTypeElement(doc, element);
        var level = GetLevel(doc, element);

        return new
        {
            id         = ToolHelpers.GetElementIdValue(element),
            uniqueId   = element.UniqueId,
            name       = element.Name,
            category   = element.Category?.Name,
            familyName = GetFamilyName(element, type),
            typeName   = type?.Name,
            typeId     = type != null ? ToolHelpers.GetElementIdValue(type) : (long?)null,
            levelName  = level?.Name,
            levelId    = level != null ? ToolHelpers.GetElementIdValue(level) : (long?)null
        };
    }

    private static Element? GetTypeElement(Document doc, Element element)
    {
        try
        {
            var typeId = element.GetTypeId();
            return typeId == null || typeId == ElementId.InvalidElementId ? null : doc.GetElement(typeId);
        }
        catch
        {
            return null;
        }
    }

    private static string? GetFamilyName(Element element, Element? type)
    {
        try
        {
            // The built-in parameter also covers system families ("Basic Wall").
            var param = element.get_Parameter(BuiltInParameter.ELEM_FAMILY_PARAM);
            if (param?.HasValue == true)
            {
                var name = param.AsValueString();
                if (!string.IsNullOrEmpty(name)) return name;
            }

            if (type is ElementType elementType && !string.IsNullOrEmpty(elementType.FamilyName))
                return elementType.FamilyName;
        }
        catch
        {
            // One odd element must not fail the whole selection.
        }

        return null;
    }

    private static Level? GetLevel(Document doc, Element element)
    {
        try
        {
            var levelId = element.LevelId;
            if (levelId != null && levelId != ElementId.InvalidElementId
                && doc.GetElement(levelId) is Level direct)
                return direct;

            foreach (var builtIn in LevelParameters)
            {
                var param = element.get_Parameter(builtIn);
                if (param == null || !param.HasValue || param.StorageType != StorageType.ElementId)
                    continue;

                var id = param.AsElementId();
                if (id != null && id != ElementId.InvalidElementId && doc.GetElement(id) is Level level)
                    return level;
            }
        }
        catch
        {
            // Same as above: report the element without a level.
        }

        return null;
    }
}
