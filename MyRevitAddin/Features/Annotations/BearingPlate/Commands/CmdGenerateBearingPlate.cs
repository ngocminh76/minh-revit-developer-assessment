using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace MyRevitAddin.Features.Annotations.BearingPlate.Commands
{
    [Transaction(TransactionMode.Manual)]
    public class CmdGenerateBearingPlate : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            Autodesk.Revit.UI.TaskDialog.Show("Bearing Plate Drawing", "Coming soon!");
            return Result.Succeeded;
        }
    }
}
