using Microsoft.Win32;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace PressPlay.Teleprompter
{
    public partial class TeleprompterDialog : Window
    {
        public TeleprompterDialog()
        {
            InitializeComponent();
        }

        private void FontColorButton_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new System.Windows.Forms.ColorDialog();
            if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                var color = Color.FromArgb(dlg.Color.A, dlg.Color.R, dlg.Color.G, dlg.Color.B);
                Editor.Selection.ApplyPropertyValue(TextElement.ForegroundProperty, new SolidColorBrush(color));
            }
        }

        private void HighlightButton_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new System.Windows.Forms.ColorDialog();
            if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                var color = Color.FromArgb(dlg.Color.A, dlg.Color.R, dlg.Color.G, dlg.Color.B);
                Editor.Selection.ApplyPropertyValue(TextElement.BackgroundProperty, new SolidColorBrush(color));
            }
        }

        private void BoldButton_Click(object sender, RoutedEventArgs e)
        {
            var current = Editor.Selection.GetPropertyValue(TextElement.FontWeightProperty);
            var isBold = current != DependencyProperty.UnsetValue && current.Equals(FontWeights.Bold);
            Editor.Selection.ApplyPropertyValue(TextElement.FontWeightProperty, isBold ? FontWeights.Normal : FontWeights.Bold);
        }

        private void ItalicButton_Click(object sender, RoutedEventArgs e)
        {
            var current = Editor.Selection.GetPropertyValue(TextElement.FontStyleProperty);
            var isItalic = current != DependencyProperty.UnsetValue && current.Equals(FontStyles.Italic);
            Editor.Selection.ApplyPropertyValue(TextElement.FontStyleProperty, isItalic ? FontStyles.Normal : FontStyles.Italic);
        }

        private void UnderlineButton_Click(object sender, RoutedEventArgs e)
        {
            var decorations = Editor.Selection.GetPropertyValue(Inline.TextDecorationsProperty) as TextDecorationCollection;
            bool hasUnderline = decorations != null && decorations.Contains(TextDecorations.Underline[0]);
            if (hasUnderline)
            {
                var withoutUnderline = new TextDecorationCollection(decorations);
                withoutUnderline.Remove(TextDecorations.Underline[0]);
                Editor.Selection.ApplyPropertyValue(Inline.TextDecorationsProperty, withoutUnderline);
            }
            else
            {
                var newDecorations = decorations == null ? new TextDecorationCollection() : new TextDecorationCollection(decorations);
                newDecorations.Add(TextDecorations.Underline[0]);
                Editor.Selection.ApplyPropertyValue(Inline.TextDecorationsProperty, newDecorations);
            }
        }
        private void FontSizeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (Editor == null)
            { return; }

            if (FontSizeComboBox.SelectedItem is ComboBoxItem item && double.TryParse(item.Content.ToString(), out double size))
            {
                Editor.Selection.ApplyPropertyValue(TextElement.FontSizeProperty, size);
            }
        }

        private void ImportButton_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Filter = "PressPlay Rich Text Teleprompter (*.rttp)|*.rttp"
            };
            if (dlg.ShowDialog() == true)
            {
                var range = new TextRange(Editor.Document.ContentStart, Editor.Document.ContentEnd);
                using var stream = dlg.OpenFile();
                range.Load(stream, DataFormats.Rtf);
            }
        }

        private void ExportButton_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new SaveFileDialog
            {
                Filter = "PressPlay Rich Text Teleprompter (*.rttp)|*.rttp"
            };
            if (dlg.ShowDialog() == true)
            {
                var range = new TextRange(Editor.Document.ContentStart, Editor.Document.ContentEnd);
                using var stream = dlg.OpenFile();
                range.Save(stream, DataFormats.Rtf);
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
    }
}