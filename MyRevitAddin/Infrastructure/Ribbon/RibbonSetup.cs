using Autodesk.Revit.UI;
using System.Reflection;

namespace MyRevitAddin.Infrastructure.Ribbon
{
    /// <summary>
    /// Manages creation and configuration of the add-in Ribbon UI.
    /// </summary>
    public static class RibbonSetup
    {
        // Ribbon configuration
        private const string TabName = "Dev Assessment";
        private static readonly string AssemblyPath = Assembly.GetExecutingAssembly().Location;

        /// <summary>
        /// Initializes the Ribbon UI. Called once during application startup.
        /// </summary>
        public static void Initialize(UIControlledApplication app)
        {
            CreateTab(app, TabName);

            // PANEL: Structural
            var structuralPanel = app.CreateRibbonPanel(TabName, "Structural");

            AddPushButton(structuralPanel, new ButtonInfo
            {
                Name      = "cmdAdjustBeam",
                Text      = "Adjust\nBeams",
                ClassName = "MyRevitAddin.Features.Structural.AdjustBeam.Commands.AdjustBeamCommand",
                Tooltip   = "Adjust structural beam clearance gaps.\nSelect beams, columns, walls → auto-adjust endpoints with configurable gaps.",
                IconKey   = "adjust_beam",
            });

            // PANEL: Annotations
            var annotationsPanel = app.CreateRibbonPanel(TabName, "Annotations");

            AddPushButton(annotationsPanel, new ButtonInfo
            {
                Name = "cmdBearingPlate",
                Text = "Bearing Plate\nDrawing",
                ClassName = "MyRevitAddin.Features.Annotations.BearingPlate.Commands.CmdGenerateBearingPlate",
                Tooltip = "Generate assembly views and drawings for PL-* bearing plate elements.",
                IconKey = "bearing_plate",
            });
        }

        #region Helpers 

        private static void CreateTab(UIControlledApplication app, string tabName)
        {
            try { app.CreateRibbonTab(tabName); }
            catch (Exception) { /* Tab already exists */ }
        }

        private static PushButton AddPushButton(RibbonPanel panel, ButtonInfo info)
        {
            var data = new PushButtonData(info.Name, info.Text, AssemblyPath, info.ClassName)
            {
                ToolTip = info.Tooltip,
            };

            var btn = panel.AddItem(data) as PushButton;

            // Set icons
            try
            {
                btn.LargeImage = IconHelper.GetIcon(info.IconKey, 32);
                btn.Image = IconHelper.GetIcon(info.IconKey, 16);
            }
            catch { /* Fallback if icon cannot be loaded */ }

            return btn;
        }

        private static void AddSplitButton(RibbonPanel panel, string splitName, ButtonInfo[] buttons)
        {
            if (buttons == null || buttons.Length == 0) return;

            var splitData = new SplitButtonData(splitName, buttons[0].Text);
            var splitBtn = panel.AddItem(splitData) as SplitButton;

            foreach (var info in buttons)
            {
                var data = new PushButtonData(info.Name, info.Text, AssemblyPath, info.ClassName)
                {
                    ToolTip = info.Tooltip,
                };

                var btn = splitBtn.AddPushButton(data);

                try
                {
                    btn.LargeImage = IconHelper.GetIcon(info.IconKey, 32);
                    btn.Image = IconHelper.GetIcon(info.IconKey, 16);
                }
                catch { }
            }
        }

        #endregion

        /// <summary>
        /// Represents configuration metadata for a ribbon button.
        /// </summary>
        private class ButtonInfo
        {
            public string Name { get; set; }
            public string Text { get; set; }
            public string ClassName { get; set; }
            public string Tooltip { get; set; }
            public string IconKey { get; set; }
        }
    }
}
