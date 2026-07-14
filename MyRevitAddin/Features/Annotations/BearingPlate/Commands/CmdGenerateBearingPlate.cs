using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using MyRevitAddin.Features.Annotations.BearingPlate.Models;
using MyRevitAddin.Features.Annotations.BearingPlate.ViewModels;
using MyRevitAddin.Features.Annotations.BearingPlate.Views;

namespace MyRevitAddin.Features.Annotations.BearingPlate.Commands
{
    [Transaction(TransactionMode.Manual)]
    public class CmdGenerateBearingPlate : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIApplication uiapp = commandData.Application;
            UIDocument uidoc = uiapp.ActiveUIDocument;
            Document doc = uidoc.Document;

            try
            {
                var collector = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_GenericModel)
                    .WhereElementIsNotElementType()
                    .ToElements();

                List<BearingPlateModel> dataList = new List<BearingPlateModel>();

                foreach (var elem in collector)
                {
                    ElementType type = doc.GetElement(elem.GetTypeId()) as ElementType;
                    if (type != null && type.Name.StartsWith("PL-"))
                    {
                        dataList.Add(new BearingPlateModel
                        {
                            Family = type.FamilyName,
                            Type = type.Name,
                            HasAssembly = elem.AssemblyInstanceId != ElementId.InvalidElementId,
                            ElementId = elem.Id.Value,
                            IsSelected = false
                        });
                    }
                }

                if (dataList.Count == 0)
                {
                    Autodesk.Revit.UI.TaskDialog.Show("Info", "No Bearing Plates found in the model.");
                    return Result.Cancelled;
                }

                var viewModel = new BearingPlateViewModel();
                viewModel.LoadData(dataList.OrderBy(x => x.Type));

                var window = new BearingPlateWindow();
                window.DataContext = viewModel;

                viewModel.CloseAction = () => window.Close();

                viewModel.GenerateAction = (selected) =>
                {
                    AssemblyDrawingGenerator generator = new AssemblyDrawingGenerator(doc);
                    generator.GenerateDrawings(selected, (val, max, txt) =>
                    {
                        viewModel.ProgressValue = val;
                        viewModel.ProgressMax = max;
                        viewModel.ProgressText = txt;
                        // Force UI to update
                        System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(System.Windows.Threading.DispatcherPriority.Background, new Action(delegate { }));
                    });
                    Autodesk.Revit.UI.TaskDialog.Show("Success", $"Successfully generated drawings for {selected.Count()} plates.");
                };

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
