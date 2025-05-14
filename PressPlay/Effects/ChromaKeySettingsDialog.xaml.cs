using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace PressPlay.Effects
{
    /// <summary>
    /// Lógica de interacción para ChromaKeySettingsDialog.xaml
    /// </summary>
    public partial class ChromaKeySettingsDialog : Window
    {
        private ChromaKeyEffect _effect;

        // Bindable properties
        public Color KeyColor { get; set; }
        public double Tolerance { get; set; }

        public ChromaKeySettingsDialog(ChromaKeyEffect effect)
        {
            InitializeComponent();

            _effect = effect ?? throw new ArgumentNullException(nameof(effect));

            // Initialize properties from effect
            KeyColor = effect.KeyColor;
            Tolerance = effect.Tolerance;

            // Set DataContext to this for binding
            DataContext = this;
        }

        private void ChooseColor_Click(object sender, RoutedEventArgs e)
        {
            // Use the system color picker dialog
            var colorDialog = new System.Windows.Forms.ColorDialog();

            // Initialize with current color
            colorDialog.Color = System.Drawing.Color.FromArgb(
                KeyColor.A, KeyColor.R, KeyColor.G, KeyColor.B);

            if (colorDialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                // Update the color
                KeyColor = Color.FromArgb(
                    colorDialog.Color.A,
                    colorDialog.Color.R,
                    colorDialog.Color.G,
                    colorDialog.Color.B);

                // Notify property changed for UI update
                OnPropertyChanged(nameof(KeyColor));
            }
        }

        private void OK_Click(object sender, RoutedEventArgs e)
        {
            // Apply settings to the effect
            _effect.KeyColor = KeyColor;
            _effect.Tolerance = Tolerance;

            DialogResult = true;
            Close();
        }

        // Basic INotifyPropertyChanged implementation for binding
        public event System.ComponentModel.PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(propertyName));
        }
    }
}
