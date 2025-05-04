using System;
using System.IO;
using System.Windows;

namespace PressPlay
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Set up exception handling
            this.DispatcherUnhandledException += App_DispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;

            // Ensure application directories exist
            EnsureApplicationDirectories();

            // Initialize FFMpeg (if needed)
            InitializeFFMpeg();
        }

        private void App_DispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
        {
            // Log the exception
            LogException(e.Exception);

            // Show error message to user
            MessageBox.Show(
                $"An unexpected error occurred: {e.Exception.Message}\n\nThe application will continue running, but some operations may not work correctly.",
                "Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            // Mark as handled so application doesn't crash
            e.Handled = true;
        }

        private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            // Log the exception
            if (e.ExceptionObject is Exception ex)
            {
                LogException(ex);

                // Show error message to user
                MessageBox.Show(
                    $"A critical error occurred: {ex.Message}\n\nThe application will now close.",
                    "Critical Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void LogException(Exception ex)
        {
            try
            {
                string appDataPath = GetAppDataPath();
                string logDir = Path.Combine(appDataPath, "Logs");

                // Ensure log directory exists
                Directory.CreateDirectory(logDir);

                // Create log file name with date
                string logFile = Path.Combine(logDir, $"PressPlay_Error_{DateTime.Now:yyyy-MM-dd}.log");

                // Append to log file
                using (var writer = new StreamWriter(logFile, true))
                {
                    writer.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Exception: {ex.GetType().Name}");
                    writer.WriteLine($"Message: {ex.Message}");
                    writer.WriteLine($"Stack Trace: {ex.StackTrace}");

                    if (ex.InnerException != null)
                    {
                        writer.WriteLine($"Inner Exception: {ex.InnerException.GetType().Name}");
                        writer.WriteLine($"Inner Message: {ex.InnerException.Message}");
                        writer.WriteLine($"Inner Stack Trace: {ex.InnerException.StackTrace}");
                    }

                    writer.WriteLine(new string('-', 80));
                }
            }
            catch
            {
                // Failed to log the exception, nothing we can do
            }
        }

        private string GetAppDataPath()
        {
            string appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return Path.Combine(appDataPath, "PressPlay");
        }

        private void EnsureApplicationDirectories()
        {
            try
            {
                // Ensure main app data directory exists
                string appDataPath = GetAppDataPath();
                Directory.CreateDirectory(appDataPath);

                // Ensure cache directory exists
                string cachePath = Path.Combine(appDataPath, "Cache");
                Directory.CreateDirectory(cachePath);

                // Ensure temp directory exists
                string tempPath = Path.Combine(Path.GetTempPath(), "PressPlay");
                Directory.CreateDirectory(tempPath);
            }
            catch (Exception ex)
            {
                // Log the error but continue
                System.Diagnostics.Debug.WriteLine($"Error creating application directories: {ex.Message}");
            }
        }

        private void InitializeFFMpeg()
        {
            try
            {
                string appDataPath = GetAppDataPath();
                string ffmpegPath = Path.Combine(appDataPath, "FFmpeg");

                // If FFmpeg binaries don't exist in app data, check application directory
                if (!Directory.Exists(ffmpegPath))
                {
                    string executablePath = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
                    string alternativePath = Path.Combine(executablePath, "ffmpeg");

                    if (Directory.Exists(alternativePath))
                    {
                        ffmpegPath = alternativePath;
                    }
                    else
                    {
                        // Create directory for FFmpeg binaries
                        Directory.CreateDirectory(ffmpegPath);

                        // In a real app, you'd download or extract FFmpeg binaries here
                        // For now, just show a message
                        MessageBox.Show(
                            "FFmpeg binaries not found. Please ensure FFmpeg is properly installed.",
                            "FFmpeg Configuration",
                            MessageBoxButton.OK,
                            MessageBoxImage.Warning);
                    }
                }

                // Configure FFMpegCore to use the correct path
                FFMpegCore.GlobalFFOptions.Configure(options =>
                {
                    options.BinaryFolder = ffmpegPath;
                    options.TemporaryFilesFolder = Path.Combine(appDataPath, "Temp");
                });

                // Set environment variable for other components
                Environment.SetEnvironmentVariable("FFMPEG_CORE_DIR", ffmpegPath);

                System.Diagnostics.Debug.WriteLine($"FFmpeg initialized with path: {ffmpegPath}");
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Error initializing FFMpeg: {ex.Message}\n\nMedia processing features may not work correctly.",
                    "FFMpeg Initialization Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }
    }
}