using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using MyRevitAddin.Features.Structural.AdjustBeam.Logic;
using MyRevitAddin.Features.Structural.AdjustBeam.ViewModels;
using MyRevitAddin.Features.Structural.AdjustBeam.Views;

namespace MyRevitAddin.Features.Structural.AdjustBeam.Commands
{
    /// <summary>
    /// Command to execute the Adjust Beam tool.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class AdjustBeamCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;

            // Get current selection set (if any)
            var selectedIds = uidoc.Selection.GetElementIds() as ICollection<ElementId> ?? new List<ElementId>();

            try
            {
                var viewModel = new AdjustBeamViewModel();
                var window = new AdjustBeamWindow(viewModel);

                // Attach View to ViewModel for window control
                viewModel.AdjustBeamView = window;

                // Display dialog modally
                window.ShowDialog();

                // Proceed if user clicked OK
                if (viewModel.IsOKClicked)
                {
                    // Prompt element selection if nothing pre-selected
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
                            // User canceled selection
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
