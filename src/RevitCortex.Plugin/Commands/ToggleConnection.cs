using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using RevitCortex.Plugin.UI;

namespace RevitCortex.Plugin.Commands;

[Transaction(TransactionMode.Manual)]
public class ToggleConnection : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        try
        {
            var app = RevitCortexApp.Instance;
            if (app == null)
            {
                TaskDialog.Show("RevitCortex", Localization.T("conn.not_initialized"));
                return Result.Failed;
            }

            if (app.IsServiceRunning)
            {
                app.StopService();
                TaskDialog.Show("RevitCortex", Localization.T("conn.stopped"));
            }
            else
            {
                // Pass active document so the session is initialized immediately
                var doc = commandData.Application.ActiveUIDocument?.Document;
                app.StartService(doc);
                TaskDialog.Show("RevitCortex", Localization.T("conn.started", app.Port));
            }

            return Result.Succeeded;
        }
        catch (Exception ex)
        {
            message = ex.Message;
            return Result.Failed;
        }
    }
}
