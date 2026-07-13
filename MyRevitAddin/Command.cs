using System;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.Attributes;

namespace MyRevitAddin
{
    [Transaction(TransactionMode.Manual)]
    public class Command : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIApplication uiapp = commandData.Application;
            UIDocument uidoc = uiapp.ActiveUIDocument;
            Document doc = uidoc.Document;

            try
            {
#if REVIT2024
                Autodesk.Revit.UI.TaskDialog.Show("Multi-Version Addin", $"Hello from Revit 2024 (.NET 4.8)!\nActive Document: {doc.Title}");
#elif REVIT2026
                Autodesk.Revit.UI.TaskDialog.Show("Multi-Version Addin", $"Hello from Revit 2026 (.NET 8.0)!\nActive Document: {doc.Title}");
#else
                Autodesk.Revit.UI.TaskDialog.Show("Multi-Version Addin", $"Hello from Revit Addin!\nActive Document: {doc.Title}");
#endif

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                return Result.Failed;
            }
        }
    }
}
