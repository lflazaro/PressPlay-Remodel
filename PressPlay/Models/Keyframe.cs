using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace PressPlay.Models
{
    public class Keyframe : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        private int _frame;
        public int Frame
        {
            get => _frame;
            set
            {
                if (_frame != value)
                {
                    _frame = value;
                    OnPropertyChanged();
                }
            }
        }

        private double _value;
        public double Value
        {
            get => _value;
            set
            {
                if (!value.Equals(_value))
                {
                    _value = value;
                    OnPropertyChanged();
                }
            }
        }

        private string _interpolation = "Linear";
        public string Interpolation
        {
            get => _interpolation;
            set
            {
                if (_interpolation != value)
                {
                    _interpolation = value;
                    OnPropertyChanged();
                }
            }
        }

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected != value)
                {
                    _isSelected = value;
                    OnPropertyChanged();
                }
            }
        }

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}

