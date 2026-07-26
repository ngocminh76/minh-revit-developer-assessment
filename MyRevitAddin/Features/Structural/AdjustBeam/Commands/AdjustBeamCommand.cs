using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using MyRevitAddin.Features.Structural.AdjustBeam.Logic;
using MyRevitAddin.Features.Structural.AdjustBeam.ViewModels;
using MyRevitAddin.Features.Structural.AdjustBeam.Views;

namespace MyRevitAddin.Features.Structural.AdjustBeam.Commands
{
    [Transaction(TransactionMode.Manual)]
    public class AdjustBeamCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;

            // Lấy danh sách chọn trước (nếu có)
            var selectedIds = uidoc.Selection.GetElementIds() as ICollection<ElementId> ?? new List<ElementId>();

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
                    // Nếu chưa chọn gì trước đó, cho phép PickObjects
                    if (selectedIds.Count == 0)
                    {
                        try
                        {
                            var pickedRefs = uidoc.Selection.PickObjects(Autodesk.Revit.UI.Selection.ObjectType.Element, "Please select beams, columns, and walls to adjust.");
                            selectedIds = new List<ElementId>();
                            foreach (var r in pickedRefs)
                            {
                                selectedIds.Add(r.ElementId);
                            }
                        }
                        catch (Autodesk.Revit.Exceptions.OperationCanceledException)
                        {
                            // Người dùng bấm ESC khi đang quét chuột
                            return Result.Cancelled;
                        }
                    }

                    if (selectedIds.Count > 0)
                    {
                        var adjuster = new BeamAdjuster();
                        adjuster.AdjustBeams(doc, selectedIds, viewModel.Config);
                    }
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
