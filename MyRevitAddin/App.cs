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

            return Result.Succeeded;
        }

        public Result OnShutdown(UIControlledApplication application)
        {
            return Result.Succeeded;
        }
    }
}
