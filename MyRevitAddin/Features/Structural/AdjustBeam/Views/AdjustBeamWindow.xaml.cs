using System.Windows;
using MyRevitAddin.Features.Structural.AdjustBeam.ViewModels;

namespace MyRevitAddin.Features.Structural.AdjustBeam.Views
{
    public partial class AdjustBeamWindow : Window
    {
        public AdjustBeamWindow(AdjustBeamViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
            viewModel.CloseAction = () =>
            {
                this.DialogResult = true;
                this.Close();
            };
        }
    }
}
