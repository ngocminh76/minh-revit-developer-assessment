using System.Windows;
using MyRevitAddin.Features.Structural.AdjustBeam.ViewModels;

namespace MyRevitAddin.Features.Structural.AdjustBeam.Views
{
    public partial class AdjustBeamWindow : Window
    {
        public AdjustBeamWindow(AdjustBeamViewModel viewModel)
        {
            WPFUI.ThemeManager.Initialize();
            InitializeComponent();
            DataContext = viewModel;
        }
    }
}
