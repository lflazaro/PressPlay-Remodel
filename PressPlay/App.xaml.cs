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
                // Check if FFMpeg binaries exist
                // This is a placeholder - you would implement proper FFMpeg initialization
                // based on the FFMpegCore package requirements
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