﻿using PressPlay.Models;
using PressPlay.Services;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;                  // ← for File.Exists
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PressPlayTitler;
using PressPlay.Helpers;
using CommunityToolkit.Mvvm.Input;
using PressPlay.Recording;

namespace PressPlay
{
    public partial class MainWindow : Window
    {
        private readonly IPlaybackService _playbackService;

        public MainWindow()
        {
            InitializeComponent();

            if (DataContext is MainWindowViewModel vm)
            {
                // 1) Initialize playback service
                _playbackService = new PlaybackService(vm.CurrentProject, PreviewImage);
                vm.PlaybackService = _playbackService;
                Debug.WriteLine("PlaybackService initialized successfully");

                // 2) Sync timeline needle when audio/video plays
                _playbackService.PositionChanged += position =>
                {
                    vm.CurrentProject.NeedlePositionTime =
                        TimeCode.FromTimeSpan(position, vm.CurrentProject.FPS);
                    Debug.WriteLine($"Updated needle position to: {position}");
                };

                // 3) React whenever someone sets CurrentMediaPath on the project
                vm.CurrentProject.PropertyChanged += OnProjectPropertyChanged;

                // 4) Initial load if there’s already a path set
                Loaded += MainWindow_Loaded;

                // 5) Clean up on close
                Closing += MainWindow_Closing;

                // 6) Enable drag/drop onto the preview
                PreviewImage.AllowDrop = true;
                PreviewImage.Drop += VideoPreview_Drop;
                PreviewImage.DragEnter += VideoPreview_DragEnter;
                PreviewImage.DragOver += VideoPreview_DragOver;
            }
        }

        private void OnProjectPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(Project.CurrentMediaPath))
            {
                var project = (Project)sender;
                var path = project.CurrentMediaPath;
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                {
                    Debug.WriteLine($"[MainWindow] Loading media: {path}");
                    _playbackService.LoadMedia(path);
                }
            }
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            if (DataContext is MainWindowViewModel vm &&
                !string.IsNullOrEmpty(vm.CurrentProject.CurrentMediaPath) &&
                File.Exists(vm.CurrentProject.CurrentMediaPath))
            {
                Debug.WriteLine($"Initial media load: {vm.CurrentProject.CurrentMediaPath}");
                _playbackService.LoadMedia(vm.CurrentProject.CurrentMediaPath);
            }
        }
        // Add this method to the MainWindow class to respond to TimeLine selection changes
        private void TimelineControl_SelectionChanged(object sender, EventArgs e)
        {
            if (DataContext is MainWindowViewModel vm)
            {
                vm.SelectionChanged();
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
                    if (!vm.SaveProjectToFile())
                        e.Cancel = true;
                }
                else if (result == MessageBoxResult.Cancel)
                {
                    e.Cancel = true;
                }
            }

            // Pause playback
            _playbackService?.Pause();

            // Clear PressPlay data from clipboard on exit
            if (!e.Cancel)
            {
                ClipboardExtensions.ClearClipboard();
            }
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
                    // Import into your project
                    vm.ImportMediaFiles(files);

                    // Pick the first imported file as the “current” media
                    var first = files.First();
                    vm.CurrentProject.CurrentMediaPath = first;

                    if (vm.AutoPlayNewMedia)
                        _playbackService.Play();

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
            e.Effects = (e.Data.GetDataPresent(typeof(ProjectClip)) ||
                         e.Data.GetDataPresent(DataFormats.FileDrop))
                        ? DragDropEffects.Copy
                        : DragDropEffects.None;
            e.Handled = true;
        }

        private void TimelineControl_Loaded(object sender, RoutedEventArgs e)
        {
            // (No changes here)
        }

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            // Pass a callback that will import the exported title to the media list
            PressPlayTitler.MainWindow.TitlerLauncher.ShowTitler(
                this,
                (exportedFilePath) => {
                    // When a title is exported, import it into the media list
                    if (DataContext is MainWindowViewModel vm && !string.IsNullOrEmpty(exportedFilePath))
                    {
                        // Import the PNG file into the project
                        vm.ImportMediaFiles(new[] { exportedFilePath });

                        // Optional: Display confirmation message
                        //MessageBox.Show(
                            //$"Title image from the Title Editor has been imported into your project.",
                            //"Title Imported",
                            //MessageBoxButton.OK,
                            //MessageBoxImage.Information);
                    }
                });
        }
        private void BlendMode_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (DataContext is MainWindowViewModel viewModel && sender is System.Windows.Controls.ComboBox comboBox)
            {
                // Get the selected blend mode name
                string blendModeName = comboBox.SelectedItem as string;

                if (!string.IsNullOrEmpty(blendModeName))
                {
                    // Execute the command to change the blend mode
                    viewModel.ChangeBlendModeCommand.Execute(blendModeName);
                }
            }
        }
    }
}
