using System;
using Autodesk.Revit.UI;
using Autodesk.Revit.DB;

namespace MyRevitAddin
{
    public class App : IExternalApplication
    {
        public Result OnStartup(UIControlledApplication application)
        {
            // Tạo một Ribbon tab
            string tabName = "My Addins";
            try
            {
                application.CreateRibbonTab(tabName);
            }
            catch (Exception)
            {
                // Tab có thể đã tồn tại
            }

            // Tạo một Ribbon panel
            RibbonPanel panel = application.CreateRibbonPanel(tabName, "Tools");

            // Tạo button cho lệnh
            string thisAssemblyPath = System.Reflection.Assembly.GetExecutingAssembly().Location;
            PushButtonData buttonData = new PushButtonData("cmdMyCommand", "Run Command", thisAssemblyPath, "MyRevitAddin.Command");
            
            PushButton pushButton = panel.AddItem(buttonData) as PushButton;
            pushButton.ToolTip = "This is a multi-version Revit Addin test command.";

            PushButtonData btnAdjustBeam = new PushButtonData("cmdAdjustBeam", "Adjust Beams", thisAssemblyPath, "MyRevitAddin.Features.Structural.AdjustBeam.Commands.AdjustBeamCommand");
            PushButton pushButtonAdjustBeam = panel.AddItem(btnAdjustBeam) as PushButton;
            pushButtonAdjustBeam.ToolTip = "Adjust structural beam clearance gaps.";

            PushButtonData btnInspect = new PushButtonData("cmdInspectFaces", "Inspect Faces", thisAssemblyPath, "MyRevitAddin.Features.Structural.AdjustBeam.Commands.InspectFacesCommand");
            PushButton pushButtonInspect = panel.AddItem(btnInspect) as PushButton;
            pushButtonInspect.ToolTip = "Log PlanarFace data of selected elements.";

            return Result.Succeeded;
        }

        public Result OnShutdown(UIControlledApplication application)
        {
            return Result.Succeeded;
        }
    }
}
