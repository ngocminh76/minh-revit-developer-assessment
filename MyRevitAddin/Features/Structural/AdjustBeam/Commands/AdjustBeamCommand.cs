using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using MyRevitAddin.Features.Structural.AdjustBeam.Logic;
using MyRevitAddin.Features.Structural.AdjustBeam.ViewModels;
using MyRevitAddin.Features.Structural.AdjustBeam.Views;
using TaskDialog = Autodesk.Revit.UI.TaskDialog;

namespace MyRevitAddin.Features.Structural.AdjustBeam.Commands
{
    [Transaction(TransactionMode.Manual)]
    public class AdjustBeamCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;

            // Check if there are selected elements
            var selectedIds = uidoc.Selection.GetElementIds();
            if (selectedIds == null || selectedIds.Count == 0)
            {
                TaskDialog.Show("Error", "Please select beams, columns, and walls before running the tool.");
                return Result.Cancelled;
            }

            try
            {
                var viewModel = new AdjustBeamViewModel();
                var window = new AdjustBeamWindow(viewModel);
                
                // Gắn View vào ViewModel để nó có thể tự Close()
                viewModel.AdjustBeamView = window;

                // Hiển thị dạng Modal, code sẽ dừng ở đây chờ người dùng đóng cửa sổ
                window.ShowDialog();

                // Người dùng đã bấm OK
                if (viewModel.IsOKClicked)
                {
                    var adjuster = new BeamAdjuster();
                    adjuster.AdjustBeams(doc, selectedIds, viewModel.Config);
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
}
