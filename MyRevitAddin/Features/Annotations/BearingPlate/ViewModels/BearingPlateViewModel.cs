using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows.Input;
using Shared_Core.Base;
using MyRevitAddin.Features.Annotations.BearingPlate.Models;

namespace MyRevitAddin.Features.Annotations.BearingPlate.ViewModels
{
    public class BearingPlateViewModel : ViewModelBase
    {
        public ObservableCollection<BearingPlateModel> BearingPlates { get; set; }

        private string _title = "Bearing Plate Drawings (0 checked)";
        public string Title
        {
            get => _title;
            set
            {
                _title = value;
                OnPropertyChanged();
            }
        }

        public ICommand GenerateCommand { get; }
        public ICommand CancelCommand { get; }

        public Action CloseAction { get; set; }
        public Action<System.Collections.Generic.IEnumerable<BearingPlateModel>> GenerateAction { get; set; }

        private bool _isBusy;
        public bool IsBusy
        {
            get => _isBusy;
            set { _isBusy = value; OnPropertyChanged(); }
        }

        private int _progressValue;
        public int ProgressValue
        {
            get => _progressValue;
            set { _progressValue = value; OnPropertyChanged(); }
        }

        private int _progressMax = 100;
        public int ProgressMax
        {
            get => _progressMax;
            set { _progressMax = value; OnPropertyChanged(); }
        }

        private string _progressText;
        public string ProgressText
        {
            get => _progressText;
            set { _progressText = value; OnPropertyChanged(); }
        }

        public BearingPlateViewModel()
        {
            BearingPlates = new ObservableCollection<BearingPlateModel>();
            
            GenerateCommand = new MyCmd(() =>
            {
                IsBusy = true;
                
                var selected = BearingPlates.Where(x => x.IsSelected).ToList();
                if (selected.Count > 0)
                {
                    GenerateAction?.Invoke(selected);
                }

                IsBusy = false;
                CloseAction?.Invoke();
            }, (obj) => !IsBusy);
            
            CancelCommand = new MyCmd(() =>
            {
                CloseAction?.Invoke();
            }, (obj) => !IsBusy);
        }

        public void LoadData(System.Collections.Generic.IEnumerable<BearingPlateModel> data)
        {
            BearingPlates.Clear();
            foreach (var item in data)
            {
                item.PropertyChanged += Item_PropertyChanged;
                BearingPlates.Add(item);
            }
            UpdateTitle();
        }

        private void Item_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(BearingPlateModel.IsSelected))
            {
                UpdateTitle();
            }
        }

        private void UpdateTitle()
        {
            int count = BearingPlates.Count(x => x.IsSelected);
            Title = $"Bearing Plate Drawings ({count} checked)";
        }
    }
}
