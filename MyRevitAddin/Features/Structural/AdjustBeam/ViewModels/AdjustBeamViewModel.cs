using System;
using System.Collections.ObjectModel;
using System.Windows.Input;
using Shared_Core.Base;
using MyRevitAddin.Features.Structural.AdjustBeam.Models;

namespace MyRevitAddin.Features.Structural.AdjustBeam.ViewModels
{
    public class AdjustBeamViewModel : ViewModelBase
    {
        private AdjustBeamConfig _config;
        public AdjustBeamConfig Config
        {
            get => _config;
            set { _config = value; OnPropertyChanged(); }
        }

        public ObservableCollection<string> CornerOptions { get; } = new ObservableCollection<string>
        {
            "Default"
        };

        public ICommand AdjustCommand { get; }

        public Action CloseAction { get; set; }
        public Action<AdjustBeamConfig> AdjustAction { get; set; }

        public AdjustBeamViewModel()
        {
            Config = new AdjustBeamConfig();
            AdjustCommand = new MyCmd(() =>
            {
                AdjustAction?.Invoke(Config);
                CloseAction?.Invoke();
            }, null);
        }
    }
}
