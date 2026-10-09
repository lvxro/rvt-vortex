using System;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Threading;
using RevitCortex.Server.Connection;
using RevitCortex.Server.Tools;
using Xunit;

namespace RevitCortex.Tests.Server
{
    public class ServerToolContractTests
    {
        private static MethodInfo GetMethod(Type declaringType, string methodName)
        {
            return Assert.Single(declaringType.GetMethods(BindingFlags.Public | BindingFlags.Static), m => m.Name == methodName);
        }

        private static ParameterInfo GetParameter(MethodInfo method, string parameterName)
        {
            return Assert.Single(method.GetParameters(), p => p.Name == parameterName);
        }

        private static void AssertDescription(MethodInfo method, string expected)
        {
            var attribute = method.GetCustomAttribute<DescriptionAttribute>();
            Assert.NotNull(attribute);
            Assert.Equal(expected, attribute!.Description);
        }

        [Fact]
        public void GetCurrentViewElements_ExposesExplicitCategoryLists()
        {
            var method = GetMethod(typeof(ViewTools), nameof(ViewTools.GetCurrentViewElements));
            var parameters = method.GetParameters();

            Assert.Collection(
                parameters.Select(p => p.Name),
                name => Assert.Equal("revit", name),
                name => Assert.Equal("limit", name),
                name => Assert.Equal("modelCategoryList", name),
                name => Assert.Equal("annotationCategoryList", name),
                name => Assert.Equal("categoryFilter", name),
                name => Assert.Equal("fields", name),
                name => Assert.Equal("ct", name));

            Assert.Equal(typeof(RevitConnectionManager), GetParameter(method, "revit").ParameterType);
            Assert.Equal(typeof(int?), GetParameter(method, "limit").ParameterType);
            Assert.Equal(typeof(string[]), GetParameter(method, "modelCategoryList").ParameterType);
            Assert.Equal(typeof(string[]), GetParameter(method, "annotationCategoryList").ParameterType);
            Assert.Equal(typeof(string), GetParameter(method, "categoryFilter").ParameterType);
            Assert.Equal(typeof(string[]), GetParameter(method, "fields").ParameterType);
            Assert.Equal(typeof(CancellationToken), GetParameter(method, "ct").ParameterType);

            Assert.True(GetParameter(method, "modelCategoryList").HasDefaultValue);
            Assert.Null(GetParameter(method, "modelCategoryList").DefaultValue);
            Assert.True(GetParameter(method, "annotationCategoryList").HasDefaultValue);
            Assert.Null(GetParameter(method, "annotationCategoryList").DefaultValue);
            Assert.True(GetParameter(method, "categoryFilter").HasDefaultValue);
            Assert.Null(GetParameter(method, "categoryFilter").DefaultValue);

            AssertDescription(method,
                "List elements visible in the currently active view. Filter with modelCategoryList / annotationCategoryList (preferred over the legacy categoryFilter), pick only the fields you need and keep limit low.");
        }

        [Fact]
        public void GetScheduleData_ExposesMaxRows()
        {
            var method = GetMethod(typeof(ViewTools), nameof(ViewTools.GetScheduleData));
            var parameters = method.GetParameters();

            Assert.Collection(
                parameters.Select(p => p.Name),
                name => Assert.Equal("revit", name),
                name => Assert.Equal("scheduleId", name),
                name => Assert.Equal("maxRows", name),
                name => Assert.Equal("ct", name));

            Assert.Equal(typeof(long), GetParameter(method, "scheduleId").ParameterType);
            Assert.Equal(typeof(int?), GetParameter(method, "maxRows").ParameterType);
            Assert.True(GetParameter(method, "maxRows").HasDefaultValue);
            Assert.Null(GetParameter(method, "maxRows").DefaultValue);

            AssertDescription(method,
                "Export schedule data as JSON from an existing schedule view. Always set maxRows when inspecting; pull all rows only when exporting.");
        }

        [Fact]
        public void WorkflowModelAudit_ExposesStructuredAuditFlags()
        {
            var method = GetMethod(typeof(ProjectTools), nameof(ProjectTools.WorkflowModelAudit));
            var parameters = method.GetParameters();

            Assert.Collection(
                parameters.Select(p => p.Name),
                name => Assert.Equal("revit", name),
                name => Assert.Equal("includeWarnings", name),
                name => Assert.Equal("includeFamilies", name),
                name => Assert.Equal("maxWarnings", name),
                name => Assert.Equal("compact", name),
                name => Assert.Equal("ct", name));

            Assert.Equal(typeof(bool?), GetParameter(method, "includeWarnings").ParameterType);
            Assert.Equal(typeof(bool?), GetParameter(method, "includeFamilies").ParameterType);
            Assert.Equal(typeof(int?), GetParameter(method, "maxWarnings").ParameterType);

            Assert.True(GetParameter(method, "includeWarnings").HasDefaultValue);
            Assert.Null(GetParameter(method, "includeWarnings").DefaultValue);
            Assert.True(GetParameter(method, "includeFamilies").HasDefaultValue);
            Assert.Null(GetParameter(method, "includeFamilies").DefaultValue);
            Assert.True(GetParameter(method, "maxWarnings").HasDefaultValue);
            Assert.Null(GetParameter(method, "maxWarnings").DefaultValue);

            AssertDescription(method,
                "Run a complete model audit (warnings, families, statistics). The most expensive health check: try check_model_health first, and use includeWarnings/includeFamilies/maxWarnings and compact:true to limit the output.");
        }

        [Fact]
        public void HighCostWrappers_ExposeCompactFlags()
        {
            var familyTypes = GetMethod(typeof(ProjectTools), nameof(ProjectTools.GetAvailableFamilyTypes));
            var schedulableFields = GetMethod(typeof(ProjectTools), nameof(ProjectTools.ListSchedulableFields));
            var roomOpenings = GetMethod(typeof(ElementTools), nameof(ElementTools.GetRoomOpenings));

            Assert.Contains("compact", familyTypes.GetParameters().Select(p => p.Name));
            Assert.Contains("compact", schedulableFields.GetParameters().Select(p => p.Name));
            Assert.Contains("summaryOnly", schedulableFields.GetParameters().Select(p => p.Name));
            Assert.Contains("compact", roomOpenings.GetParameters().Select(p => p.Name));
            Assert.Contains("summaryOnly", roomOpenings.GetParameters().Select(p => p.Name));
        }

        [Fact]
        public void GetElementParameters_ExposesCompactFlag()
        {
            var method = GetMethod(typeof(ElementTools), nameof(ElementTools.GetElementParameters));
            var parameters = method.GetParameters();

            Assert.Contains("compact", parameters.Select(p => p.Name));
            Assert.Equal(typeof(bool), GetParameter(method, "compact").ParameterType);
            Assert.True(GetParameter(method, "compact").HasDefaultValue);
            Assert.Equal(false, GetParameter(method, "compact").DefaultValue);
        }

        [Fact]
        public void GetElementSolidGeometry_ExposesElementIdAndMaxSolids()
        {
            var method = GetMethod(typeof(ElementTools), nameof(ElementTools.GetElementSolidGeometry));
            var parameters = method.GetParameters();

            Assert.Collection(
                parameters.Select(p => p.Name),
                name => Assert.Equal("revit", name),
                name => Assert.Equal("elementId", name),
                name => Assert.Equal("maxSolids", name),
                name => Assert.Equal("ct", name));

            Assert.Equal(typeof(RevitConnectionManager), GetParameter(method, "revit").ParameterType);
            Assert.Equal(typeof(long), GetParameter(method, "elementId").ParameterType);
            Assert.Equal(typeof(int), GetParameter(method, "maxSolids").ParameterType);
            Assert.Equal(typeof(CancellationToken), GetParameter(method, "ct").ParameterType);

            Assert.True(GetParameter(method, "maxSolids").HasDefaultValue);
            Assert.Equal(20, GetParameter(method, "maxSolids").DefaultValue);

            Assert.NotNull(method.GetCustomAttribute<DescriptionAttribute>());
        }

        [Fact]
        public void ResolveElementsByUniqueId_ExposesBatchUniqueIds()
        {
            var method = GetMethod(typeof(ElementTools), nameof(ElementTools.ResolveElementsByUniqueId));
            var parameters = method.GetParameters();

            Assert.Collection(
                parameters.Select(p => p.Name),
                name => Assert.Equal("revit", name),
                name => Assert.Equal("uniqueIds", name),
                name => Assert.Equal("ct", name));

            Assert.Equal(typeof(string[]), GetParameter(method, "uniqueIds").ParameterType);
            AssertDescription(method, "Resolve Revit UniqueId strings to ElementId records for cross-app workflows.");
        }

        [Fact]
        public void ShowCrossModelElements_ExposesHostAndLinkedTargets()
        {
            var method = GetMethod(typeof(LinkTools), nameof(LinkTools.ShowCrossModelElements));
            var parameters = method.GetParameters();

            Assert.Collection(
                parameters.Select(p => p.Name),
                name => Assert.Equal("revit", name),
                name => Assert.Equal("hostElementIds", name),
                name => Assert.Equal("linkedElements", name),
                name => Assert.Equal("select", name),
                name => Assert.Equal("isolate", name),
                name => Assert.Equal("createSectionBox", name),
                name => Assert.Equal("createLinkedMarkers", name),
                name => Assert.Equal("usePostCommandIsolate", name),
                name => Assert.Equal("offset", name),
                name => Assert.Equal("ct", name));

            Assert.Equal(typeof(long[]), GetParameter(method, "hostElementIds").ParameterType);
            Assert.Equal(typeof(string), GetParameter(method, "linkedElements").ParameterType);
            Assert.Equal(typeof(bool?), GetParameter(method, "select").ParameterType);
            Assert.Equal(typeof(bool?), GetParameter(method, "isolate").ParameterType);
            Assert.Equal(typeof(bool?), GetParameter(method, "createSectionBox").ParameterType);
            Assert.Equal(typeof(bool?), GetParameter(method, "createLinkedMarkers").ParameterType);
            Assert.Equal(typeof(bool?), GetParameter(method, "usePostCommandIsolate").ParameterType);
            Assert.Equal(typeof(double?), GetParameter(method, "offset").ParameterType);
        }

        [Fact]
        public void AuditFamilies_ExposesCompactFlag()
        {
            var method = GetMethod(typeof(ProjectTools), nameof(ProjectTools.AuditFamilies));
            var parameters = method.GetParameters();

            Assert.Contains("compact", parameters.Select(p => p.Name));
            Assert.Equal(typeof(bool), GetParameter(method, "compact").ParameterType);
            Assert.True(GetParameter(method, "compact").HasDefaultValue);
            Assert.Equal(false, GetParameter(method, "compact").DefaultValue);
        }

        [Fact]
        public void GetAvailableFamilyTypes_ExposesToolNativeParameters()
        {
            var method = GetMethod(typeof(ProjectTools), nameof(ProjectTools.GetAvailableFamilyTypes));
            var parameters = method.GetParameters();

            Assert.Collection(
                parameters.Select(p => p.Name),
                name => Assert.Equal("revit", name),
                name => Assert.Equal("categoryList", name),
                name => Assert.Equal("familyNameFilter", name),
                name => Assert.Equal("limit", name),
                name => Assert.Equal("compact", name),
                name => Assert.Equal("ct", name));

            Assert.Equal(typeof(string[]), GetParameter(method, "categoryList").ParameterType);
            Assert.Equal(typeof(string), GetParameter(method, "familyNameFilter").ParameterType);
            Assert.Equal(typeof(int?), GetParameter(method, "limit").ParameterType);

            Assert.True(GetParameter(method, "categoryList").HasDefaultValue);
            Assert.Null(GetParameter(method, "categoryList").DefaultValue);
            Assert.True(GetParameter(method, "familyNameFilter").HasDefaultValue);
            Assert.Null(GetParameter(method, "familyNameFilter").DefaultValue);
            Assert.True(GetParameter(method, "limit").HasDefaultValue);
            Assert.Null(GetParameter(method, "limit").DefaultValue);
        }

        [Fact]
        public void ModifySchedule_DescribesPluginNativeSortingActions()
        {
            var method = GetMethod(typeof(ProjectTools), nameof(ProjectTools.ModifySchedule));
            var action = GetParameter(method, "action");

            var description = action.GetCustomAttribute<DescriptionAttribute>()?.Description;
            Assert.NotNull(description);
            Assert.Contains("set_sorting", description);
            Assert.Contains("clear_sorting", description);
            Assert.Contains("set_filter", description);
            Assert.Contains("clear_filter", description);
            Assert.DoesNotContain("set_sort |", description);

            AssertDescription(method,
                "Modify schedule fields, sorting, filters, or rename the schedule. Supported actions: add_field, remove_field, set_sorting, clear_sorting, set_filter, clear_filter, rename.");
        }

        [Fact]
        public void GetSelectedElements_ExposesLimit()
        {
            // The plugin always read `limit`, but the wrapper did not declare it,
            // so no client could ever send it.
            var method = GetMethod(typeof(ElementTools), nameof(ElementTools.GetSelectedElements));

            Assert.Collection(
                method.GetParameters().Select(p => p.Name),
                name => Assert.Equal("revit", name),
                name => Assert.Equal("limit", name),
                name => Assert.Equal("ct", name));

            Assert.Equal(typeof(int?), GetParameter(method, "limit").ParameterType);
            Assert.True(GetParameter(method, "limit").HasDefaultValue);
            Assert.Null(GetParameter(method, "limit").DefaultValue);
        }

        [Fact]
        public void GetViewImage_ReturnsContentBlocks_AndEveryInputIsOptional()
        {
            var method = GetMethod(typeof(ViewTools), nameof(ViewTools.GetViewImage));

            // Not Task<string>: the picture must travel as an MCP image block.
            Assert.Equal(
                typeof(System.Threading.Tasks.Task<System.Collections.Generic.IEnumerable<ModelContextProtocol.Protocol.ContentBlock>>),
                method.ReturnType);

            Assert.Collection(
                method.GetParameters().Select(p => p.Name),
                name => Assert.Equal("revit", name),
                name => Assert.Equal("viewId", name),
                name => Assert.Equal("viewName", name),
                name => Assert.Equal("region", name),
                name => Assert.Equal("elementIds", name),
                name => Assert.Equal("pixelSize", name),
                name => Assert.Equal("format", name),
                name => Assert.Equal("ct", name));

            Assert.Equal(typeof(long?), GetParameter(method, "viewId").ParameterType);
            Assert.Equal(typeof(long[]), GetParameter(method, "elementIds").ParameterType);
            Assert.Equal(typeof(int?), GetParameter(method, "pixelSize").ParameterType);

            // With no arguments the tool captures the active view.
            foreach (var name in new[] { "viewId", "viewName", "region", "elementIds", "pixelSize", "format" })
            {
                Assert.True(GetParameter(method, name).HasDefaultValue, name);
                Assert.Null(GetParameter(method, name).DefaultValue);
            }
        }

        [Fact]
        public void GetElementSummary_TakesIdsAndAnOptionalSolidsSwitch()
        {
            var method = GetMethod(typeof(ElementTools), nameof(ElementTools.GetElementSummary));

            Assert.Collection(
                method.GetParameters().Select(p => p.Name),
                name => Assert.Equal("revit", name),
                name => Assert.Equal("elementIds", name),
                name => Assert.Equal("includeSolids", name),
                name => Assert.Equal("ct", name));

            Assert.Equal(typeof(long[]), GetParameter(method, "elementIds").ParameterType);
            Assert.False(GetParameter(method, "elementIds").HasDefaultValue);
            Assert.Equal(typeof(bool?), GetParameter(method, "includeSolids").ParameterType);
            Assert.Null(GetParameter(method, "includeSolids").DefaultValue);
        }

        [Fact]
        public void SaveDocument_TakesNoInput_AndSaysToWaitForTheUser()
        {
            var method = GetMethod(typeof(ProjectTools), nameof(ProjectTools.SaveDocument));

            Assert.Collection(
                method.GetParameters().Select(p => p.Name),
                name => Assert.Equal("revit", name),
                name => Assert.Equal("ct", name));

            // Saving is the user's decision: the description is what keeps the AI from doing it unasked.
            var description = method.GetCustomAttribute<DescriptionAttribute>()!.Description;
            Assert.Contains("ONLY when the user asks", description);
            Assert.Contains("never synchronizes with central", description);
        }

        [Fact]
        public void SendCodeToRevit_DescribesTheRealTransactionModes()
        {
            var method = GetMethod(typeof(ProjectTools), nameof(ProjectTools.SendCodeToRevit));
            var description = GetParameter(method, "transactionMode").GetCustomAttribute<DescriptionAttribute>()!.Description;

            foreach (var mode in new[] { "auto", "none", "group", "preview" })
                Assert.Contains(mode, description);

            // The wrapper used to advertise "manual" and "readonly", which the plugin never had:
            // both fell through to "auto", so a script sent as "readonly" was committed.
            Assert.DoesNotContain("manual", description);
            Assert.DoesNotContain("readonly", description);
        }
    }
}
