using Shared_Core.Base;

namespace MyRevitAddin.Features.Annotations.BearingPlate.Models
{
    public class BearingPlateModel : ViewModelBase
    {
        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                _isSelected = value;
                OnPropertyChanged();
            }
        }

        public string Family { get; set; }
        public string Type { get; set; }
        public bool HasAssembly { get; set; }
        public long ElementId { get; set; } 
    }
}
