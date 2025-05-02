using PressPlay.Models;
using PressPlay.Services;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace PressPlay
{
    public partial class MainWindow : Window
    {
        private readonly IPlaybackService _playbackService;

        public MainWindow()
        {
            InitializeComponent();

            // Grab VM
            if (DataContext is MainWindowViewModel vm)
            {
                // Initialize service (now takes just the Project)
                _playbackService = new PlaybackService(vm.CurrentProject);
                vm.PlaybackService = _playbackService;
                Debug.WriteLine("PlaybackService initialized successfully");

                // Whenever our service ticks a new Position,
                // update the Project's needle – TimelineControl will react.
                _playbackService.PositionChanged += position =>
                {
                    vm.CurrentProject.NeedlePositionTime =
                        TimeCode.FromTimeSpan(position, vm.CurrentProject.FPS);
                    Debug.WriteLine($"Updated needle position to: {position}");
                };

                // Watch for when the Project's media path changes
                vm.CurrentProject.PropertyChanged += (s, e) =>
                {
                    if (e.PropertyName == nameof(Project.CurrentMediaPath))
                    {
                        var path = vm.CurrentProject.CurrentMediaPath;
                        if (!string.IsNullOrEmpty(path))
                        {
                            Debug.WriteLine($"Loading media: {path}");
                            _playbackService.LoadMedia(path);
                        }
                    }
                };

                // Handle initial load
                Loaded += MainWindow_Loaded;
                // Handle unsaved‐changes on close
                Closing += MainWindow_Closing;

                // Enable drag/drop onto the preview
                VideoPreview.AllowDrop = true;
                VideoPreview.Drop += VideoPreview_Drop;
                VideoPreview.DragEnter += VideoPreview_DragEnter;
                VideoPreview.DragOver += VideoPreview_DragOver;
            }
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            if (DataContext is MainWindowViewModel vm &&
                !string.IsNullOrEmpty(vm.CurrentProject.CurrentMediaPath))
            {
                Debug.WriteLine($"Initial media load: {vm.CurrentProject.CurrentMediaPath}");
                _playbackService.LoadMedia(vm.CurrentProject.CurrentMediaPath);
            }
        }

        private void MainWindow_Closing(object sender, CancelEventArgs e)
        {
            if (DataContext is MainWindowViewModel vm && vm.HasUnsavedChanges)
            {
                var result = MessageBox.Show(
                    "You have unsaved changes. Do you want to save before closing?",
                    "Save Changes",
                    MessageBoxButton.YesNoCancel,
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    // You can call your Save command or method here:
                    if (!vm.SaveProjectToFile())
                        e.Cancel = true;
                }
                else if (result == MessageBoxResult.Cancel)
                {
                    e.Cancel = true;
                }
            }

            // Gracefully pause playback on exit
            _playbackService.Pause();
        }

        private void VideoPreview_Drop(object sender, DragEventArgs e)
        {
            if (!(DataContext is MainWindowViewModel vm)) return;

            // Dropped from Timeline (drag‐drop a ProjectClip)
            if (e.Data.GetDataPresent(typeof(ProjectClip)))
            {
                var clip = (ProjectClip)e.Data.GetData(typeof(ProjectClip));
                vm.CurrentProject.CurrentMediaPath = clip.FilePath;
                Debug.WriteLine($"Set current media path: {clip.FilePath}");

                if (vm.AutoPlayNewMedia)
                    _playbackService.Play();

                e.Handled = true;
            }
            // Dropped from Explorer (file URLs)
            else if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                var files = (string[])e.Data.GetData(DataFormats.FileDrop);
                if (files?.Length > 0)
                {
                    vm.ImportMediaFiles(files);
                    e.Handled = true;
                }
            }
        }

        private void VideoPreview_DragEnter(object sender, DragEventArgs e)
        {
            e.Effects = (e.Data.GetDataPresent(typeof(ProjectClip)) ||
                         e.Data.GetDataPresent(DataFormats.FileDrop))
                        ? DragDropEffects.Copy
                        : DragDropEffects.None;
            e.Handled = true;
        }

        private void VideoPreview_DragOver(object sender, DragEventArgs e)
        {
            // Same logic as DragEnter
            e.Effects = (e.Data.GetDataPresent(typeof(ProjectClip)) ||
                         e.Data.GetDataPresent(DataFormats.FileDrop))
                        ? DragDropEffects.Copy
                        : DragDropEffects.None;
            e.Handled = true;
        }
    }
}
