using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using RevitCortex.Server.Connection;

var port = RevitConnectionManager.ResolvePort();

var builder = Host.CreateApplicationBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);
builder.Services.AddSingleton(new RevitConnectionManager(port));
builder.Services
    .AddMcpServer(options =>
    {
        options.ServerInfo = new()
        {
            Name = "RVT Vortex",
            Version = "2.0.0"
        };
        options.ServerInstructions = string.Join("\n", new[]
        {
            "You are driving a LIVE Autodesk Revit model. Changes are real, so work precisely and verify with small queries.",
            "",
            "START",
            "- Call say_hello once: it confirms the connection and returns `locale` (en/it/fr/de/es). Revit localizes category and parameter display names, so use OST_* category codes (language-independent) and read exact parameter names from ONE sample element with get_element_parameters(compact:true). Never guess parameter names.",
            "- Call get_project_info once per session (turn on includeWorksets/includeLinks only if needed); later calls set includeLevels/includePhases to false.",
            "- Lengths and coordinates are in millimetres unless a parameter description says otherwise.",
            "",
            "CHOOSING TOOLS (most specific first; never re-fetch data you already have)",
            "- Model health, cheapest first: check_model_health, then analyze_model_statistics(compact:true), then workflow_model_audit with filters.",
            "- Find elements: export_elements_data with a filter for one exact value; filter_by_parameter_value for parameter conditions (parameterType:\"type\" for type parameters such as Type Name); ai_element_filter for category/level/bounding box.",
            "- Edit parameters: set_element_parameters for a few elements; bulk_modify_parameter_values for one value on many; sync_csv_parameters for different values per element.",
            "- tag_rooms, tag_walls, color_elements and get_current_view_elements act on the ACTIVE view, which must be a model view (not a sheet). Check get_current_view_info first.",
            "",
            "SAVING TOKENS",
            "- Pass compact:true / summaryOnly:true whenever a tool accepts them, and always set limits (maxElements, maxRows, maxResults, maxWarnings:10).",
            "- After a dryRun, read only the counters (modifiedCount, skippedCount), not the element lists.",
            "- Do not narrate between calls or repeat tool output back.",
            "",
            "WRITING",
            "- Tools with a dryRun option default to dryRun:true (preview). Preview, then run again with dryRun:false.",
            "- Run heavy operations one at a time (3D create_view, purge_unused, analyze_model_statistics); at most 3-4 writes in parallel.",
            "- A `Cancelled` result means the user declined the confirmation: ask before retrying. If the message mentions unattended mode / Autopilot, the user is AWAY: do not ask and do not wait; skip that step, continue the task, and list it as pending in your final summary.",
            "- Under Autopilot use only RevitCortex tools. Never use screen control (screenshots, clicks) or a browser: they raise permission prompts nobody will answer.",
            "",
            "SCRIPTS",
            "- send_code_to_revit is a LAST RESORT. Use it only when no dedicated tool covers the operation (exotic geometry, read-only inspection of an uncovered Revit API, a one-off operation), never for modal family editing (Document.EditFamily deadlocks from the external-event context), and only after proposing the dedicated-tool alternative and getting the user's explicit consent. Exception: if the user is away under Autopilot and allowed scripts, you may use it when clearly better; keep scripts short and list each one in your summary. Inside scripts the document variable is `document`.",
        });
    })
    .WithStdioServerTransport()
    .WithToolsFromAssembly();
await builder.Build().RunAsync();
