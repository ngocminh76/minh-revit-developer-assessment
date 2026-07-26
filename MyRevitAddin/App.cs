using Autodesk.Revit.UI;
using MyRevitAddin.Infrastructure.Ribbon;

namespace MyRevitAddin
{
    public class App : IExternalApplication
    {
        public Result OnStartup(UIControlledApplication application)
        {
            RibbonSetup.Initialize(application);
            return Result.Succeeded;
        }

        public Result OnShutdown(UIControlledApplication application)
        {
            return Result.Succeeded;
        }
    }
}
