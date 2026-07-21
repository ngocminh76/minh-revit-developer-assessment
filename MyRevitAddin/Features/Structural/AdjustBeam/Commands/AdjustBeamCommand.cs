using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using MyRevitAddin.Features.Structural.AdjustBeam.ViewModels;
using MyRevitAddin.Features.Structural.AdjustBeam.Views;
using MyRevitAddin.Features.Structural.AdjustBeam.Logic;

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
                Autodesk.Revit.UI.TaskDialog.Show("Error", "Please select beams, columns, and walls before running the tool.");
                return Result.Cancelled;
            }

            try
            {
                var viewModel = new AdjustBeamViewModel();
                viewModel.AdjustAction = (config) =>
                {
                    var adjuster = new BeamAdjuster();
                    adjuster.AdjustBeams(doc, selectedIds, config);
                };

                var window = new AdjustBeamWindow(viewModel);
                window.ShowDialog();

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
