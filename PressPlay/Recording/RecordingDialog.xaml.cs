using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using NAudio.Wave;
using OpenCvSharp;
using OpenCvSharp.WpfExtensions;

namespace PressPlay.Recording
{
    public partial class RecordingDialog : System.Windows.Window
    {
        // Camera preview & recording
        private VideoCapture _capture;
        private VideoWriter _videoWriter;
        private Mat _frame;
        private DispatcherTimer _previewTimer;
        private bool _isCapturing;

        // Audio capture & recording
        private WaveInEvent _waveIn;
        private WaveFileWriter _waveWriter;

        // Recording state
        private bool _isRecording;
        private DateTime _recordingStartTime;
        private DispatcherTimer _recordingTimer;

        // Settings
        private int _targetWidth = 1280;
        private int _targetHeight = 720;
        private int _targetFps = 30;

        // Output paths
        private readonly string _videoOutputPath;
        private readonly string _audioOutputPath;

        // Import callback
        private readonly Action<string> _importAction;

        public RecordingDialog(Action<string> importAction)
        {
            InitializeComponent();
            _importAction = importAction;

            string tempDir = Path.Combine(Path.GetTempPath(), "PressPlay", "Recordings");
            Directory.CreateDirectory(tempDir);
            string ts = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            _videoOutputPath = Path.Combine(tempDir, $"video_{ts}.avi");
            _audioOutputPath = Path.Combine(tempDir, $"audio_{ts}.wav");

            // Setup frame & timers
            _frame = new Mat();
            _previewTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1000.0 / _targetFps) };
            _previewTimer.Tick += PreviewTimer_Tick;

            _recordingTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _recordingTimer.Tick += RecordingTimer_Tick;
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            EnumerateCameras();
            EnumerateMicrophones();
            SelectDefaultDevices();
            ResolutionComboBox.SelectedIndex = 1; // Tag="1280,720"
            StartPreview();
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            Cleanup();
        }

        #region Device Enumeration
        private void EnumerateCameras()
        {
            var cams = Enumerable.Range(0, 5)
                .Select(i => { using var c = new VideoCapture(i); return c.IsOpened() ? (i, $"Camera {i}") : ((int, string)?)null; })
                .Where(x => x.HasValue).Select(x => x.Value).ToList();
            CameraComboBox.ItemsSource = cams;
            CameraComboBox.DisplayMemberPath = "Item2";
        }

        private void EnumerateMicrophones()
        {
            var mics = Enumerable.Range(0, WaveIn.DeviceCount)
                .Select(i => (i, WaveIn.GetCapabilities(i).ProductName))
                .ToList();
            MicrophoneComboBox.ItemsSource = mics;
            MicrophoneComboBox.DisplayMemberPath = "Item2";
        }

        private void SelectDefaultDevices()
        {
            if (CameraComboBox.Items.Count > 0) CameraComboBox.SelectedIndex = 0;
            if (MicrophoneComboBox.Items.Count > 0) MicrophoneComboBox.SelectedIndex = 0;
        }
        #endregion

        #region Preview
        private void StartPreview()
        {
            StopPreview();
            if (CameraComboBox.SelectedItem is not (int camIndex, string _)) { StatusTextBlock.Text = "No camera"; return; }
            _capture = new VideoCapture(camIndex);
            if (!_capture.IsOpened()) { StatusTextBlock.Text = "Cannot open camera"; return; }
            _capture.FrameWidth = _targetWidth;
            _capture.FrameHeight = _targetHeight;
            _capture.Fps = _targetFps;
            _isCapturing = true;
            _previewTimer.Start();
            StatusTextBlock.Text = "Preview running";
        }

        private void StopPreview()
        {
            if (!_isCapturing) return;
            _previewTimer.Stop();
            _isCapturing = false;
            _capture.Release(); _capture.Dispose(); _capture = null;
        }

        private void PreviewTimer_Tick(object sender, EventArgs e)
        {
            if (_capture == null || !_capture.IsOpened()) return;
            _capture.Read(_frame);
            if (_frame.Empty()) return;
            PreviewImage.Source = _frame.ToBitmapSource();
            if (_isRecording)
            {
                if (_videoWriter == null) InitVideoWriter();
                _videoWriter.Write(_frame);
            }
        }

        private void InitVideoWriter()
        {
            var sz = new OpenCvSharp.Size((int)_capture.FrameWidth, (int)_capture.FrameHeight);
            _videoWriter = new VideoWriter(_videoOutputPath, FourCC.MJPG, _targetFps, sz);
            if (!_videoWriter.IsOpened()) Debug.WriteLine("VideoWriter open failed");
        }
        #endregion

        #region Recording Control
        private void StartRecording()
        {
            if (!_isCapturing) return;
            try
            {
                // VideoWriter will init on first frame
                // Audio: lazy init in DataAvailable
                if (MicrophoneComboBox.SelectedItem is (int micIndex, string _))
                {
                    _waveIn = new WaveInEvent
                    {
                        DeviceNumber = micIndex,
                        WaveFormat = new WaveFormat(44100, 16, 1),
                        BufferMilliseconds = 50,
                        NumberOfBuffers = 3
                    };
                    _waveIn.DataAvailable += OnAudioData;
                    _waveIn.StartRecording();
                }

                _isRecording = true;
                _recordingStartTime = DateTime.Now;
                _recordingTimer.Start();

                RecordButton.IsEnabled = false;
                StopButton.IsEnabled = true;
                StatusTextBlock.Text = "Recording...";
            }
            catch (Exception ex)
            {
                StatusTextBlock.Text = $"Record start error: {ex.Message}";
                Debug.WriteLine(ex);
            }
        }

        private void OnAudioData(object s, WaveInEventArgs e)
        {
            if (!_isRecording) return;
            // Lazy create writer
            if (_waveWriter == null)
            {
                _waveWriter = new WaveFileWriter(_audioOutputPath, _waveIn.WaveFormat);
            }
            // Update level bar
            float max = 0;
            for (int i = 0; i < e.BytesRecorded; i += 2)
            {
                float v = Math.Abs(BitConverter.ToInt16(e.Buffer, i) / 32768f);
                if (v > max) max = v;
            }
            Dispatcher.Invoke(() => AudioLevelBar.Value = max);

            // Write PCM
            _waveWriter.Write(e.Buffer, 0, e.BytesRecorded);
            _waveWriter.Flush();
        }

        private void StopRecording()
        {
            if (!_isRecording) return;
            _recordingTimer.Stop();
            _isRecording = false;

            // finalize video
            _videoWriter?.Release(); _videoWriter?.Dispose(); _videoWriter = null;

            // finalize audio
            if (_waveIn != null)
            {
                _waveIn.DataAvailable -= OnAudioData;
                _waveIn.StopRecording(); _waveIn.Dispose(); _waveIn = null;
            }
            _waveWriter?.Dispose(); _waveWriter = null;

            StatusTextBlock.Text = $"Saved: {Path.GetFileName(_videoOutputPath)}, {Path.GetFileName(_audioOutputPath)}";
            ImportButton.IsEnabled = true;
            RecordButton.IsEnabled = true;
            StopButton.IsEnabled = false;
        }

        private void RecordingTimer_Tick(object sender, EventArgs e)
        {
            if (!_isRecording) return;
            RecordingTimeTextBlock.Text = (DateTime.Now - _recordingStartTime).ToString(@"hh\:mm\:ss");
        }
        #endregion

        #region UI Handlers
        private void CameraComboBox_SelectionChanged(object s, SelectionChangedEventArgs e)
        {
            if (_isCapturing) StartPreview();
        }
        private void MicrophoneComboBox_SelectionChanged(object s, SelectionChangedEventArgs e) { /* no-op */ }
        private void ResolutionComboBox_SelectionChanged(object s, SelectionChangedEventArgs e)
        {
            if (_isCapturing) StartPreview();
        }
        private void RecordButton_Click(object s, RoutedEventArgs e) => StartRecording();
        private void StopButton_Click(object s, RoutedEventArgs e) => StopRecording();
        private void ImportButton_Click(object s, RoutedEventArgs e)
        {
            if (File.Exists(_videoOutputPath)) _importAction?.Invoke(_videoOutputPath);
            if (File.Exists(_audioOutputPath)) _importAction?.Invoke(_audioOutputPath);
            StatusTextBlock.Text = "Imported streams";
        }
        private void CloseButton_Click(object s, RoutedEventArgs e) => Close();
        #endregion

        private void Cleanup()
        {
            StopRecording();
            StopPreview();
            _frame.Dispose();
        }
    }
}
