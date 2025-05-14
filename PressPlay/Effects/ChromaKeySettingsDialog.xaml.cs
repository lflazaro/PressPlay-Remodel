using System;
using System.Windows;
using System.Windows.Media;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace PressPlay.Effects
{
    public partial class ChromaKeySettingsDialog : Window, INotifyPropertyChanged
    {
        private ChromaKeyEffect _effect;

        // Implement INotifyPropertyChanged
        public event PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        // Bindable properties with proper change notifications
        private Color _keyColor;
        public Color KeyColor
        {
            get => _keyColor;
            set
            {
                if (_keyColor != value)
                {
                    _keyColor = value;
                    OnPropertyChanged();
                    // Debug update
                    System.Diagnostics.Debug.WriteLine($"KeyColor changed to: R:{_keyColor.R} G:{_keyColor.G} B:{_keyColor.B}");
                }
            }
        }

        private double _tolerance;
        public double Tolerance
        {
            get => _tolerance;
            set
            {
                if (_tolerance != value)
                {
                    _tolerance = value;
                    OnPropertyChanged();
                    // Debug update
                    System.Diagnostics.Debug.WriteLine($"Tolerance changed to: {_tolerance:P0}");
                }
            }
        }

        public ChromaKeySettingsDialog(ChromaKeyEffect effect)
        {
            // Set DataContext before InitializeComponent
            DataContext = this;

            InitializeComponent();

            _effect = effect ?? throw new ArgumentNullException(nameof(effect));

            // Initialize properties from effect
            KeyColor = effect.KeyColor;
            Tolerance = effect.Tolerance;

            System.Diagnostics.Debug.WriteLine($"Dialog initialized with KeyColor: R:{KeyColor.R} G:{KeyColor.G} B:{KeyColor.B}, Tolerance: {Tolerance:P0}");
        }

        private void ChooseColor_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Use Windows Forms ColorDialog for better color selection
                var colorDialog = new System.Windows.Forms.ColorDialog();

                // Initialize with current color
                colorDialog.Color = System.Drawing.Color.FromArgb(
                    KeyColor.A, KeyColor.R, KeyColor.G, KeyColor.B);

                colorDialog.FullOpen = true; // Show full dialog with custom colors

                if (colorDialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    // Update the color
                    KeyColor = Color.FromArgb(
                        colorDialog.Color.A,
                        colorDialog.Color.R,
                        colorDialog.Color.G,
                        colorDialog.Color.B);

                    System.Diagnostics.Debug.WriteLine($"Color chosen: R:{KeyColor.R} G:{KeyColor.G} B:{KeyColor.B}");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in color picker: {ex.Message}");
                MessageBox.Show($"Error selecting color: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OK_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Apply settings to the effect
                _effect.KeyColor = KeyColor;
                _effect.Tolerance = Tolerance;

                System.Diagnostics.Debug.WriteLine($"Applied to effect - KeyColor: R:{KeyColor.R} G:{KeyColor.G} B:{KeyColor.B}, Tolerance: {Tolerance:P0}");

                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error applying settings: {ex.Message}");
                MessageBox.Show($"Error applying settings: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}