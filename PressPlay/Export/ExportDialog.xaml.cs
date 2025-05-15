using Microsoft.Win32;
using PressPlay.Models;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace PressPlay.Export
{
    /// <summary>
    /// Interaction logic for ExportDialog.xaml
    /// </summary>
    public partial class ExportDialog : Window
    {
        private readonly Project _project;
        private ExportSettings _settings;
        private ExportService _exportService;
        private bool _isExporting = false;

        public ExportDialog(Project project)
        {
            InitializeComponent();

            _project = project ?? throw new ArgumentNullException(nameof(project));

            // Initialize export service
            _exportService = new ExportService(_project);
            _exportService.ProgressChanged += ExportService_ProgressChanged;
            _exportService.ExportCompleted += ExportService_ExportCompleted;

            // Initialize settings
            _settings = ExportSettings.CreateDefault(_project.ProjectWidth, _project.ProjectHeight);

            // Populate UI
            InitializeUI();

            // Update export button state
            UpdateExportButtonState();
        }

        private void InitializeUI()
        {
            // Initialize format combo box
            FormatComboBox.ItemsSource = Enum.GetValues(typeof(OutputFormat));
            FormatComboBox.SelectedItem = _settings.OutputFormat;

            // Initialize codec combo box
            CodecComboBox.ItemsSource = Enum.GetValues(typeof(VideoCodec));
            CodecComboBox.SelectedItem = _settings.VideoCodec;

            // Initialize quality combo box
            QualityComboBox.ItemsSource = Enum.GetValues(typeof(VideoQuality));
            QualityComboBox.SelectedItem = _settings.VideoQuality;

            // Initialize dimension text boxes
            WidthTextBox.Text = _settings.Width.ToString();
            HeightTextBox.Text = _settings.Height.ToString();
            UpdateAspectRatio();

            // Initialize advanced settings
            VideoBitrateTextBox.Text = _settings.VideoBitrate.ToString();
            AudioBitrateTextBox.Text = _settings.AudioBitrate.ToString();
            CustomArgsTextBox.Text = _settings.CustomFFmpegArgs;

            // Initialize audio checkbox
            IncludeAudioCheckBox.IsChecked = _settings.IncludeAudio;

            // Register text changed events
            WidthTextBox.TextChanged += DimensionTextBox_TextChanged;
            HeightTextBox.TextChanged += DimensionTextBox_TextChanged;
            VideoBitrateTextBox.TextChanged += BitrateTextBox_TextChanged;
            AudioBitrateTextBox.TextChanged += BitrateTextBox_TextChanged;
            CustomArgsTextBox.TextChanged += CustomArgsTextBox_TextChanged;

            // Apply validation to numeric fields
            WidthTextBox.PreviewTextInput += NumericTextBox_PreviewTextInput;
            HeightTextBox.PreviewTextInput += NumericTextBox_PreviewTextInput;
            VideoBitrateTextBox.PreviewTextInput += NumericTextBox_PreviewTextInput;
            AudioBitrateTextBox.PreviewTextInput += NumericTextBox_PreviewTextInput;
        }

        private void UpdateAspectRatio()
        {
            if (int.TryParse(WidthTextBox.Text, out int width) &&
                int.TryParse(HeightTextBox.Text, out int height) &&
                width > 0 && height > 0)
            {
                // Calculate greatest common divisor
                int gcd = CalculateGCD(width, height);

                // Calculate simplified ratio
                int ratioWidth = width / gcd;
                int ratioHeight = height / gcd;

                // Update aspect ratio text
                AspectRatioTextBlock.Text = $"{ratioWidth}:{ratioHeight} ({width}x{height})";
            }
            else
            {
                AspectRatioTextBlock.Text = "Invalid dimensions";
            }
        }

        private int CalculateGCD(int a, int b)
        {
            while (b != 0)
            {
                int temp = b;
                b = a % b;
                a = temp;
            }
            return a;
        }

        private void UpdateSettings()
        {
            // Update format and codec
            _settings.OutputFormat = (OutputFormat)FormatComboBox.SelectedItem;
            _settings.VideoCodec = (VideoCodec)CodecComboBox.SelectedItem;
            _settings.VideoQuality = (VideoQuality)QualityComboBox.SelectedItem;

            // Update dimensions
            if (int.TryParse(WidthTextBox.Text, out int width) && width > 0)
                _settings.Width = width;

            if (int.TryParse(HeightTextBox.Text, out int height) && height > 0)
                _settings.Height = height;

            // Update bitrates
            if (int.TryParse(VideoBitrateTextBox.Text, out int videoBitrate) && videoBitrate > 0)
                _settings.VideoBitrate = videoBitrate;

            if (int.TryParse(AudioBitrateTextBox.Text, out int audioBitrate) && audioBitrate > 0)
                _settings.AudioBitrate = audioBitrate;

            // Update custom args
            _settings.CustomFFmpegArgs = CustomArgsTextBox.Text;

            // Update audio inclusion
            _settings.IncludeAudio = IncludeAudioCheckBox.IsChecked == true;

            // Update file extension if needed
            _settings.UpdateOutputPath();
            OutputPathTextBox.Text = _settings.OutputPath;
        }

        private void UpdateExportButtonState()
        {
            // Enable export button only if a valid output path is set and not currently exporting
            bool canExport = !string.IsNullOrEmpty(_settings.OutputPath) && !_isExporting;
            ExportButton.IsEnabled = canExport;

            // Update export button text
            ExportButton.Content = _isExporting ? "Exporting..." : "Start Export";

            // Update cancel button
            CancelButton.Content = _isExporting ? "Stop Export" : "Cancel";
        }

        private void UpdateUIForExportState(bool isExporting)
        {
            _isExporting = isExporting;

            // Disable input controls during export
            FormatComboBox.IsEnabled = !isExporting;
            CodecComboBox.IsEnabled = !isExporting;
            QualityComboBox.IsEnabled = !isExporting;
            WidthTextBox.IsEnabled = !isExporting;
            HeightTextBox.IsEnabled = !isExporting;
            ResolutionPresetsButton.IsEnabled = !isExporting;
            IncludeAudioCheckBox.IsEnabled = !isExporting;
            VideoBitrateTextBox.IsEnabled = !isExporting;
            AudioBitrateTextBox.IsEnabled = !isExporting;
            CustomArgsTextBox.IsEnabled = !isExporting;
            OutputPathTextBox.IsEnabled = !isExporting;
            BrowseButton.IsEnabled = !isExporting;

            // Update buttons
            UpdateExportButtonState();
        }

        private void LogMessage(string message)
        {
            // Add timestamp
            string timestampedMessage = $"[{DateTime.Now:HH:mm:ss}] {message}";

            // Log to TextBox
            LogTextBox.AppendText(timestampedMessage + Environment.NewLine);
            LogTextBox.ScrollToEnd();

            // Also log to debug window
            Debug.WriteLine(timestampedMessage);
        }

        private async void StartExport()
        {
            // Update UI
            UpdateUIForExportState(true);
            ExportProgressBar.Value = 0;
            ProgressStatusTextBlock.Text = "Initializing export...";
            LogTextBox.Clear();

            // Log settings
            LogMessage($"Starting export to {_settings.OutputPath}");
            LogMessage($"Resolution: {_settings.Width}x{_settings.Height}");
            LogMessage($"Format: {_settings.OutputFormat}, Codec: {_settings.VideoCodec}, Quality: {_settings.VideoQuality}");
            LogMessage($"Video bitrate: {_settings.VideoBitrate} kbps, Audio bitrate: {_settings.AudioBitrate} kbps");
            LogMessage($"Include audio: {_settings.IncludeAudio}");

            try
            {
                // Start export
                bool result = await _exportService.StartExportAsync(_settings);

                if (result)
                {
                    LogMessage("Export completed successfully!");
                }
                else
                {
                    LogMessage("Export failed. Check the log for details.");
                }
            }
            catch (Exception ex)
            {
                LogMessage($"Export error: {ex.Message}");
                MessageBox.Show($"Error during export: {ex.Message}", "Export Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                // Update UI
                UpdateUIForExportState(false);
            }
        }

        #region Event Handlers

        private void BrowseButton_Click(object sender, RoutedEventArgs e)
        {
            // Get default extension
            string ext = _settings.GetFileExtension();

            // Create save file dialog
            var saveFileDialog = new SaveFileDialog
            {
                Title = "Export Project",
                Filter = $"Video Files (*{ext})|*{ext}|All Files (*.*)|*.*",
                DefaultExt = ext.TrimStart('.')
            };

            // Show dialog
            if (saveFileDialog.ShowDialog() == true)
            {
                // Update output path
                _settings.OutputPath = saveFileDialog.FileName;
                OutputPathTextBox.Text = _settings.OutputPath;

                // Update export button state
                UpdateExportButtonState();
            }
        }

        private void FormatComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (FormatComboBox.SelectedItem != null)
            {
                // Update format
                _settings.OutputFormat = (OutputFormat)FormatComboBox.SelectedItem;

                // Update file extension if needed
                _settings.UpdateOutputPath();
                OutputPathTextBox.Text = _settings.OutputPath;

                // Disable certain codecs for some formats
                CodecComboBox.IsEnabled = _settings.OutputFormat != OutputFormat.GIF;

                // Set appropriate codec for format
                switch (_settings.OutputFormat)
                {
                    case OutputFormat.GIF:
                        // GIF doesn't use a traditional codec
                        CodecComboBox.SelectedItem = VideoCodec.H264; // Doesn't matter
                        CodecComboBox.IsEnabled = false;
                        break;
                    case OutputFormat.WebM:
                        CodecComboBox.SelectedItem = VideoCodec.VP9;
                        break;
                    case OutputFormat.MOV:
                        // ProRes is often used for MOV
                        CodecComboBox.SelectedItem = VideoCodec.ProRes;
                        break;
                    default:
                        // Default to H264 for other formats
                        CodecComboBox.SelectedItem = VideoCodec.H264;
                        break;
                }
            }
        }

        private void QualityComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (QualityComboBox.SelectedItem != null)
            {
                // Update quality
                _settings.VideoQuality = (VideoQuality)QualityComboBox.SelectedItem;

                // Enable/disable bitrate text box
                bool isCustomQuality = _settings.VideoQuality == VideoQuality.Custom;
                VideoBitrateTextBox.IsEnabled = isCustomQuality;

                // Update bitrate text box with preset value if not custom
                if (!isCustomQuality)
                {
                    int preset = _settings.VideoQuality switch
                    {
                        VideoQuality.Low => 2000,
                        VideoQuality.Medium => 5000,
                        VideoQuality.High => 10000,
                        VideoQuality.Ultra => 20000,
                        _ => 5000
                    };

                    VideoBitrateTextBox.Text = preset.ToString();
                }
            }
        }

        private void ResolutionPresetsButton_Click(object sender, RoutedEventArgs e)
        {
            // Create context menu
            var contextMenu = new ContextMenu();

            // Add resolution presets
            var presets = new Dictionary<string, (int width, int height)>
            {
                { "Project Size", (_project.ProjectWidth, _project.ProjectHeight) },
                { "HD (1280x720)", (1280, 720) },
                { "Full HD (1920x1080)", (1920, 1080) },
                { "2K (2560x1440)", (2560, 1440) },
                { "4K (3840x2160)", (3840, 2160) },
                { "Instagram (1080x1080)", (1080, 1080) },
                { "YouTube (1920x1080)", (1920, 1080) },
                { "Twitter (1280x720)", (1280, 720) },
                { "Mobile (720x1280)", (720, 1280) }
            };

            foreach (var preset in presets)
            {
                var menuItem = new MenuItem
                {
                    Header = preset.Key,
                    Tag = preset.Value
                };

                menuItem.Click += (s, args) =>
                {
                    var (width, height) = ((int, int))((MenuItem)s).Tag;
                    WidthTextBox.Text = width.ToString();
                    HeightTextBox.Text = height.ToString();
                };

                contextMenu.Items.Add(menuItem);
            }

            // Show context menu
            contextMenu.PlacementTarget = ResolutionPresetsButton;
            contextMenu.Placement = PlacementMode.Bottom;
            contextMenu.IsOpen = true;
        }

        private void DimensionTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            UpdateAspectRatio();
            UpdateSettings();
        }

        private void BitrateTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            UpdateSettings();
        }

        private void CustomArgsTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            UpdateSettings();
        }

        private void NumericTextBox_PreviewTextInput(object sender, System.Windows.Input.TextCompositionEventArgs e)
        {
            // Only allow digits
            foreach (char c in e.Text)
            {
                if (!char.IsDigit(c))
                {
                    e.Handled = true;
                    return;
                }
            }
        }

        private void ExportButton_Click(object sender, RoutedEventArgs e)
        {
            // Update settings
            UpdateSettings();

            // Start export
            StartExport();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isExporting)
            {
                // Stop export
                _exportService.CancelExport();
                LogMessage("Export cancelled by user.");
            }
            else
            {
                // Close dialog
                DialogResult = false;
                Close();
            }
        }

        private void ExportService_ProgressChanged(object sender, ExportProgressEventArgs e)
        {
            // Update UI on UI thread
            Dispatcher.Invoke(() =>
            {
                // Update progress bar
                ExportProgressBar.Value = e.Progress * 100;

                // Update status text
                ProgressStatusTextBlock.Text = e.StatusMessage;

                // Log message
                LogMessage(e.StatusMessage);
            });
        }

        private void ExportService_ExportCompleted(object sender, ExportCompletedEventArgs e)
        {
            // Update UI on UI thread
            Dispatcher.Invoke(() =>
            {
                // Log result
                if (e.Success)
                {
                    LogMessage($"Export completed successfully to: {e.OutputPath}");

                    // Ask if user wants to open the file
                    var result = MessageBox.Show(
                        $"Export completed successfully!\n\nDo you want to open the exported file?",
                        "Export Complete",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Information);

                    if (result == MessageBoxResult.Yes)
                    {
                        try
                        {
                            Process.Start(new ProcessStartInfo
                            {
                                FileName = e.OutputPath,
                                UseShellExecute = true
                            });
                        }
                        catch (Exception ex)
                        {
                            MessageBox.Show($"Error opening file: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                        }
                    }
                }
                else
                {
                    LogMessage($"Export failed: {e.ErrorMessage}");

                    // Show error message
                    MessageBox.Show(
                        $"Export failed: {e.ErrorMessage}",
                        "Export Error",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }

                // Update UI for non-exporting state
                UpdateUIForExportState(false);
            });
        }

        #endregion

        protected override void OnClosing(CancelEventArgs e)
        {
            // Confirm if export is in progress
            if (_isExporting)
            {
                var result = MessageBox.Show(
                    "An export is currently in progress. Are you sure you want to cancel it and close?",
                    "Cancel Export",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (result == MessageBoxResult.No)
                {
                    // Cancel closing
                    e.Cancel = true;
                    return;
                }

                // Cancel export
                _exportService.CancelExport();
            }

            // Unregister event handlers
            if (_exportService != null)
            {
                _exportService.ProgressChanged -= ExportService_ProgressChanged;
                _exportService.ExportCompleted -= ExportService_ExportCompleted;
            }

            base.OnClosing(e);
        }
    }
}