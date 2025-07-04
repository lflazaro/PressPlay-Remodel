﻿using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.VisualBasic;
using Microsoft.Win32;
using PressPlay.Effects;
using PressPlay.Helpers;
using PressPlay.Models;
using PressPlay.Services;
using PressPlay.Timeline;
using PressPlay.Undo;
using PressPlay.Undo.UndoUnits;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using PressPlay.Serialization;
using System.Windows.Input;
using System.Windows.Media;
using PressPlay.Export;
using ClosedXML.Excel;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;
using TimeCode = PressPlay.Models.TimeCode;
using BlendMode = PressPlay.Effects.BlendMode;
using QuestPDF.Helpers;
using Colors = System.Windows.Media.Colors;
using PressPlay.Recording;

namespace PressPlay
{
    public partial class MainWindowViewModel : ObservableObject
    {
        #region Models & Classes

        public class ProjectData
        {
            public string Name { get; set; }
            public string FilePath { get; set; }
            public DateTime LastOpened { get; set; }
        }

        public class StepOutlineEntry
        {
            public string Timecode { get; set; }
            public string Description { get; set; }
        }

        #endregion

        #region Fields & Properties

        [ObservableProperty]
        private string _title = "PressPlay";

        [ObservableProperty]
        private Project _currentProject = new Project();

        [ObservableProperty]
        private ObservableCollection<ProjectData> _recentProjects = new ObservableCollection<ProjectData>();

        [ObservableProperty]
        private bool _hasUnsavedChanges;

        [ObservableProperty]
        private bool _autoPlayNewMedia = true;

        private string _currentProjectPath;

        [ObservableProperty]
        private ITrackItem _selectedTrackItem;

        [NotifyPropertyChangedFor(nameof(HasClip))]
        [ObservableProperty]
        private ProjectClip _selectedProjectClip;

        public bool HasClip => SelectedProjectClip != null;


        [ObservableProperty]
        private ObservableCollection<string> _blendModes = new ObservableCollection<string> {
    "Normal", "Multiply", "Screen", "Overlay", "Darken", "Lighten"
};

        // Playback service – to be set from the view
        public IPlaybackService PlaybackService { get; set; }
        public static MainWindowViewModel Instance { get; private set; }

        #endregion

        #region Commands

        // File commands
        [RelayCommand]
        private void NewProject() => CreateNewProject();

        [RelayCommand]
        private void OpenProject() => OpenProjectDialog();

        [RelayCommand]
        private void SaveProjectAs() => SaveCurrentProject(true);

        [RelayCommand]
        private void ExitApplication() => Application.Current.Shutdown();

        // Edit commands
        [RelayCommand(CanExecute = nameof(CanUndo))]
        private void Undo()
        {
            if (UndoEngine.Instance.CanUndo)
            {
                UndoEngine.Instance.Undo();
                HasUnsavedChanges = true;
            }
        }

        [RelayCommand(CanExecute = nameof(CanRedo))]
        private void Redo()
        {
            if (UndoEngine.Instance.CanRedo)
            {
                UndoEngine.Instance.Redo();
                HasUnsavedChanges = true;
            }
        }

        [RelayCommand]
        private void Cut()
        {
            var selectedItems = CurrentProject.Tracks
                .SelectMany(t => t.Items.Where(i => i.IsSelected))
                .ToList();

            if (!selectedItems.Any())
                return;

            Debug.WriteLine($"Cutting {selectedItems.Count} items to clipboard");

            // 1) Copy  
            this.CopySelectedItemsToClipboard();

            // 2) Delete  
            CurrentProject.DeleteSelectedItems();

            // 3) Mark modified  
            HasUnsavedChanges = true;
        }

        [RelayCommand]
        private void Copy()
        {
            var selectedItems = CurrentProject.Tracks
                .SelectMany(t => t.Items.Where(i => i.IsSelected))
                .ToList();

            if (!selectedItems.Any())
            {
                MessageBox.Show("Please select at least one item on the timeline to copy.",
                                "No Selection", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            Debug.WriteLine($"Copying {selectedItems.Count} items to clipboard");

            // Copy only, don’t modify the timeline
            this.CopySelectedItemsToClipboard();

            // Let the user know
            //MessageBox.Show(
               // $"Copied {selectedItems.Count} item{(selectedItems.Count > 1 ? "s" : "")} to clipboard",
               // "Copy Complete", MessageBoxButton.OK, MessageBoxImage.Information
           // );
        }

        [RelayCommand]
        private void Paste()
        {
            this.PasteItemsFromClipboard();
        }

        [RelayCommand]
        private void SelectAll()
        {
            // Select all items in all tracks
            foreach (var track in CurrentProject.Tracks)
            {
                foreach (var item in track.Items)
                {
                    item.IsSelected = true;
                }
            }
        }

        // Playback commands
        [RelayCommand]
        private void ProjectPlay()
        {
            if (CurrentProject.IsPlaying)
            {
                PlaybackService?.Pause();
                CurrentProject.IsPlaying = false;
            }
            else
            {
                PlaybackService?.Play();
                CurrentProject.IsPlaying = true;
            }
        }

        [RelayCommand]
        private void ProjectRewind() => PlaybackService?.Rewind();

        [RelayCommand]
        private void ProjectFastForward() => PlaybackService?.FastForward();

        // Track management commands
        [RelayCommand]
        private void AddVideoTrack() => AddNewTrack(TimelineTrackType.Video);

        [RelayCommand]
        private void AddAudioTrack() => AddNewTrack(TimelineTrackType.Audio);

        [RelayCommand]
        private void AddTrack() => AddNewTrack(TimelineTrackType.Video);

        [RelayCommand]
        private void RemoveTrack() => RemoveLastTrack();

        // Project commands
        [RelayCommand]
        private void ExportProject() => ExportCurrentProject();

        [RelayCommand]
        private void ProjectSettings() => ShowProjectSettings();

        // Step outline commands
        [RelayCommand]
        private void AddOutlineEntry() => AddNewOutlineEntry();

        [RelayCommand]
        private void RemoveOutlineEntry() => RemoveLastOutlineEntry();

        [RelayCommand]
        private void ExportStepOutlinePdf() => ExportStepOutlineToFile("PDF");

        [RelayCommand]
        private void ExportStepOutlineXlsx() => ExportStepOutlineToFile("XLSX");
        // Media commands
        [RelayCommand]
        private void AddMedia() => ImportMediaDialog();

        [RelayCommand]
        private void RemoveMedia() => RemoveSelectedMedia();

        [RelayCommand]
        private void OpenRecentProject(object parameter) => OpenRecentProjectFromList(parameter);

        [RelayCommand]
        private void SelectTool(object toolParameter)
        {
            if (toolParameter is TimelineSelectedTool tool)
            {
                CurrentProject.SelectedTool = tool;
            }
        }
        // Add this method to apply transform effects when properties change
        private void EnsureTransformEffect(ProjectClip clip, TrackItem item)
        {
            if (clip == null || item == null) return;

            // Check if the clip already has a TransformEffect
            var existingEffect = clip.Effects.OfType<TransformEffect>().FirstOrDefault();

            if (existingEffect == null)
            {
                // Add a new TransformEffect
                var effect = new TransformEffect(item);
                clip.Effects.Add(effect);
            }

            // The effect will use the current property values from the TrackItem directly
            // We just need to ensure the effect exists

            HasUnsavedChanges = true;
        }

        // Add handler for property changes on TrackItem
        private void RegisterTrackItemPropertyChanges(TrackItem item)
        {
            if (item == null) return;

            // Subscribe to property changes
            item.PropertyChanged += (s, e) =>
            {
                // Check if this is a transform-related property
                if (e.PropertyName == nameof(TrackItem.TranslateX) ||
                    e.PropertyName == nameof(TrackItem.TranslateY) ||
                    e.PropertyName == nameof(TrackItem.ScaleX) ||
                    e.PropertyName == nameof(TrackItem.ScaleY) ||
                    e.PropertyName == nameof(TrackItem.Rotation) ||
                    e.PropertyName == nameof(TrackItem.Opacity))
                {
                    // Find the associated project clip
                    var clip = CurrentProject.Clips
                        .OfType<ProjectClip>()
                        .FirstOrDefault(c =>
                            string.Equals(c.FilePath, item.FilePath, StringComparison.OrdinalIgnoreCase));

                    if (clip != null)
                    {
                        EnsureTransformEffect(clip, item);

                        // Mark project as having unsaved changes
                        HasUnsavedChanges = true;
                    }
                }
            };
        }


        // Transition commands
        [RelayCommand]
        private void AddTransition(string transitionSpecifier)
        {
            // 1) Gather all selected items, in track order
            var selectedItems = CurrentProject.Tracks
                .OfType<Track>()
                .SelectMany(t => t.Items.Where(i => i.IsSelected))
                .OrderBy(i => i.Position.TotalFrames)
                .ToList();

            if (!selectedItems.Any())
            {
                MessageBox.Show("Please select at least one clip to apply a transition", "No Selection");
                return;
            }

            // 2) CROSSFADE: special case
            if (transitionSpecifier.Equals("Crossfade", StringComparison.OrdinalIgnoreCase))
            {
                // Find the single track that has exactly two selected clips
                var candidate = CurrentProject.Tracks
                    .OfType<Track>()
                    .Select(t => new {
                        Track = t,
                        Clips = t.Items.Where(i => i.IsSelected).OrderBy(i => i.Position.TotalFrames).ToList()
                    })
                    .FirstOrDefault(x => x.Clips.Count == 2);

                if (candidate == null)
                {
                    MessageBox.Show(
                      "Please select exactly two clips on the same track to crossfade.",
                      "Invalid Selection");
                    return;
                }

                var first = candidate.Clips[0];
                var second = candidate.Clips[1];

                // Ask for duration
                string cfStr = Interaction.InputBox(
                    "Enter crossfade duration (in frames):",
                    "Crossfade Length",
                    "15");
                if (!int.TryParse(cfStr, out int cfFrames) || cfFrames < 1)
                    cfFrames = 15;

                // 3) Reposition the *second* clip so it overlaps by cfFrames
                long newStartFrame = first.Position.TotalFrames
                                    + first.Duration.TotalFrames
                                    - cfFrames;

                // If your TimeCode type exposes a setter on TotalFrames:
                second.Position.TotalFrames = (int)newStartFrame;
                // Otherwise, if you have a constructor taking totalFrames:
                // second.Position = new TimeCode(newStartFrame);

                // 4) Apply fades (we’ll keep them black by default here)
                first.FadeColor = Track.FadeColor.Black;
                first.FadeOutFrame = cfFrames;
                second.FadeColor = Track.FadeColor.Black;
                second.FadeInFrame = cfFrames;

                HasUnsavedChanges = true;
                return;
            }
            // 3b) Handle Audio Fade
            if (transitionSpecifier.StartsWith("AudioFade", StringComparison.OrdinalIgnoreCase))
            {
                // parse modeaudio: In / Out / Both
                var modeaudio = transitionSpecifier.Split('_')[1];

                // prompt user
                const string defaultVal2 = "15";
                int fadeIn = 0;
                int fadeOut = 0;

                if (modeaudio == "Both" || modeaudio == "In")
                {
                    string inStr = Interaction.InputBox(
                        "Enter audio fade-in duration (in frames):",
                        "Audio Fade-In",
                        defaultVal2);
                    if (!int.TryParse(inStr, out fadeIn) || fadeIn < 0)
                        fadeIn = int.Parse(defaultVal2);
                }
                if (modeaudio == "Both" || modeaudio == "Out")
                {
                    string outStr = Interaction.InputBox(
                        "Enter audio fade-out duration (in frames):",
                        "Audio Fade-Out",
                        defaultVal2);
                    if (!int.TryParse(outStr, out fadeOut) || fadeOut < 0)
                        fadeOut = int.Parse(defaultVal2);
                }

                // apply only to audio items
                foreach (var item in selectedItems.OfType<AudioTrackItem>())
                {
                    item.FadeInFrame = fadeIn;
                    item.FadeOutFrame = fadeOut;
                }

                HasUnsavedChanges = true;
                return;
            }
            // 5) FADE TO BLACK/WHITE: parse color+mode
            var parts = transitionSpecifier.Split('_');
            var baseName = parts[0];                          // e.g. "FadeToWhite"
            var mode = parts.Length > 1 ? parts[1] : "Both";

            Track.FadeColor color = baseName.Equals("FadeToWhite", StringComparison.OrdinalIgnoreCase)
                ? Track.FadeColor.White
                : Track.FadeColor.Black;

            // 6) Prompt for fade lengths
            const string defaultVal = "15";
            int fadeInFrames = 0;
            int fadeOutFrames = 0;

            if (mode.Equals("Both", StringComparison.OrdinalIgnoreCase))
            {
                // Fade-In
                string inStr = Interaction.InputBox(
                    "Enter fade-in duration (frames):",
                    "Fade-In Duration",
                    defaultVal);
                if (!int.TryParse(inStr, out fadeInFrames) || fadeInFrames < 0)
                    fadeInFrames = int.Parse(defaultVal);

                // Fade-Out
                string outStr = Interaction.InputBox(
                    "Enter fade-out duration (frames):",
                    "Fade-Out Duration",
                    defaultVal);
                if (!int.TryParse(outStr, out fadeOutFrames) || fadeOutFrames < 0)
                    fadeOutFrames = int.Parse(defaultVal);
            }
            else if (mode.Equals("In", StringComparison.OrdinalIgnoreCase))
            {
                string inStr = Interaction.InputBox(
                    "Enter fade-in duration (frames):",
                    "Fade-In Duration",
                    defaultVal);
                if (!int.TryParse(inStr, out fadeInFrames) || fadeInFrames < 0)
                    fadeInFrames = int.Parse(defaultVal);
            }
            else // "Out"
            {
                string outStr = Interaction.InputBox(
                    "Enter fade-out duration (frames):",
                    "Fade-Out Duration",
                    defaultVal);
                if (!int.TryParse(outStr, out fadeOutFrames) || fadeOutFrames < 0)
                    fadeOutFrames = int.Parse(defaultVal);
            }

            // 7) Apply to every selected clip
            foreach (var item in selectedItems)
            {
                item.FadeColor = color;
                item.FadeInFrame = fadeInFrames;
                item.FadeOutFrame = fadeOutFrames;
            }

            HasUnsavedChanges = true;
        }

        [RelayCommand]
        private void AddEffect(string effectKey)
        {
            // 1) Gather all selected timeline items
            var selectedItems = CurrentProject.Tracks
                .OfType<Track>()
                .SelectMany(t => t.Items.Where(i => i.IsSelected))
                .ToList();

            if (!selectedItems.Any())
            {
                MessageBox.Show(
                    "Please select at least one clip to apply an effect.",
                    "No Selection",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            // 2) For each selected item, find its ProjectClip and add the effect
            foreach (var item in selectedItems)
            {
                var clip = CurrentProject.Clips
                    .OfType<ProjectClip>()
                    .FirstOrDefault(c =>
                        string.Equals(c.FilePath, item.FilePath, StringComparison.OrdinalIgnoreCase)
                        || (item is AudioTrackItem ati && c.Id == ati.ClipId)
                    );
                if (clip == null)
                    continue;

                IEffect fx = effectKey switch
                {
                    "ChromaKey" => new ChromaKeyEffect(),
                    _ => null
                };
                if (fx != null)
                {
                    clip.Effects.Add(fx);
                    Debug.WriteLine($"Added {fx.Name} effect to clip {clip.FileName}");
                }
            }

            HasUnsavedChanges = true;
        }

        private string _selectedTrackTrackName;
        public string SelectedTrackTrackName
        {
            get => _selectedTrackTrackName;
            set => SetProperty(ref _selectedTrackTrackName, value);
        }

        // Command to toggle chroma key effect
        [RelayCommand]
        private void ToggleChromaKey(ProjectClip clip)
        {
            if (clip == null) return;

            // Check if clip already has a ChromaKey effect
            var existingEffect = clip.Effects.OfType<ChromaKeyEffect>().FirstOrDefault();

            if (existingEffect != null)
            {
                // Remove existing effect
                clip.Effects.Remove(existingEffect);
                existingEffect.Enabled = !existingEffect.Enabled;
            }
            else
            {
                // Add new chroma key effect with default settings
                clip.Effects.Add(new ChromaKeyEffect
                {
                    KeyColor = Colors.Green,
                    Tolerance = 0.3,
                    Enabled = true
                });
            }

            HasUnsavedChanges = true;
            OnPropertyChanged(nameof(SelectedProjectClip));
        }

        // Command to edit chroma key settings
        [RelayCommand]
        private void EditChromaKeySettings(ProjectClip clip)
        {
            if (clip == null) return;

            var effect = clip.Effects.OfType<ChromaKeyEffect>().FirstOrDefault();
            if (effect == null) return;

            // Create and show chroma key settings dialog
            var dialog = new ChromaKeySettingsDialog(effect);
            if (dialog.ShowDialog() == true)
            {
                // Dialog applies changes directly to the effect
                HasUnsavedChanges = true;
            }
        }

        // Method to update the properties panel when selection changes
        private void UpdateSelectedItemProperties()
        {
            // Look for any selected track item
            ITrackItem selectedItem = CurrentProject.Tracks
                .SelectMany(t => t.Items)
                .FirstOrDefault(item => item.IsSelected);

            // Update SelectedTrackItem property - this will trigger UI update
            SelectedTrackItem = selectedItem;

            if (selectedItem != null)
            {
                // Find the associated project clip
                SelectedProjectClip = CurrentProject.Clips
                    .OfType<ProjectClip>()
                    .FirstOrDefault(c =>
                        string.Equals(c.FilePath, selectedItem.FilePath, StringComparison.OrdinalIgnoreCase) ||
                        (selectedItem is AudioTrackItem ati && c.Id == ati.ClipId));

                // Find the parent track of the selected item
                var parentTrack = CurrentProject.Tracks
                    .FirstOrDefault(t => t.Items.Contains(selectedItem));

                SelectedTrackTrackName = parentTrack?.Name ?? "Unknown Track";
            }
            else
            {
                // Clear selection-dependent properties
                SelectedProjectClip = null;
                SelectedTrackTrackName = null;
            }

            // Notify UI about possible changes
            OnPropertyChanged(nameof(SelectedTrackItem));
            OnPropertyChanged(nameof(SelectedProjectClip));
            OnPropertyChanged(nameof(SelectedTrackTrackName));
        }

        // Hook this up to selection changes in the timeline
        public void SelectionChanged()
        {
            UpdateSelectedItemProperties();
        }
        [RelayCommand]
        private void ChangeBlendMode(string blendModeStr)
        {
            // Ensure we have a selected item
            if (SelectedTrackItem == null || SelectedProjectClip == null)
                return;

            if (!Enum.TryParse<BlendMode>(blendModeStr, out var blendMode))
                return;

            // Find existing blend effect or create new one
            BlendingEffect blendEffect = SelectedProjectClip.Effects.OfType<BlendingEffect>().FirstOrDefault();

            if (blendEffect == null)
            {
                // Create new blend effect
                if (SelectedTrackItem is TrackItem trackItem)
                {
                    blendEffect = new BlendingEffect(trackItem);
                    SelectedProjectClip.Effects.Add(blendEffect);
                }
            }

            // Set blend mode
            if (blendEffect != null)
            {
                blendEffect.BlendMode = blendMode;
                HasUnsavedChanges = true;
            }
        }

        // Method to get current blend mode (for binding to UI)
        public string GetCurrentBlendMode()
        {
            if (SelectedProjectClip == null)
                return "Normal";

            var blendEffect = SelectedProjectClip.Effects.OfType<BlendingEffect>().FirstOrDefault();
            return blendEffect?.BlendMode.ToString() ?? "Normal";
        }

        // Helper for applying effects
        private void EnsureBlendingEffect(ProjectClip clip, TrackItem item)
        {
            if (clip == null || item == null) return;

            // Check if the clip already has a BlendingEffect
            var existingEffect = clip.Effects.OfType<BlendingEffect>().FirstOrDefault();

            if (existingEffect == null)
            {
                // Add a new BlendingEffect with default settings
                var effect = new BlendingEffect(item);
                clip.Effects.Add(effect);

                HasUnsavedChanges = true;
            }
        }
        // Help commands
        [RelayCommand]
        private void ReportIssue() => OpenUrl("https://github.com/lflazaro/PressPlay-Remodel/issues");

        [RelayCommand]
        private void GoToWebsite() => OpenUrl("https://intelligencecasino.neocities.org/");

        [RelayCommand]
        private void About() => MessageBox.Show("PressPlay Video Editor\nVersion 0.1\n©2025", "About PressPlay");

        #endregion

        #region Constructor

        public MainWindowViewModel()
        {
            Instance = this;
            // Initialize the project
            CurrentProject = new Project();

            // Set initial title
            UpdateWindowTitle();

            // Create an initial track 
            if (CurrentProject.Tracks.Count == 0)
            {
                AddNewTrack(TimelineTrackType.Video);
            }

            // Subscribe to undo engine events to update command states
            UndoEngine.Instance.PropertyChanged += (s, e) =>
            {
                UndoCommand.NotifyCanExecuteChanged();
                RedoCommand.NotifyCanExecuteChanged();
            };

            // Subscribe to project changes
            CurrentProject.PropertyChanged += (s, e) =>
            {
                // Mark as having unsaved changes
                if (e.PropertyName != nameof(CurrentProject.NeedlePositionTime) &&
                    e.PropertyName != nameof(CurrentProject.IsPlaying))
                {
                    HasUnsavedChanges = true;
                }
            };

            // Load recent projects list
            LoadRecentProjects();
        }

        #endregion

        #region Helper Methods

        private bool CanUndo() => UndoEngine.Instance.CanUndo;
        private bool CanRedo() => UndoEngine.Instance.CanRedo;

        private void CreateNewProject()
        {
            // Check for unsaved changes
            if (HasUnsavedChanges)
            {
                var result = MessageBox.Show(
                    "You have unsaved changes. Do you want to save before creating a new project?",
                    "Save Changes",
                    MessageBoxButton.YesNoCancel,
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    if (!SaveCurrentProject(false))
                        return; // User cancelled save
                }
                else if (result == MessageBoxResult.Cancel)
                {
                    return; // User cancelled new project
                }
            }

            // Create a new project
            CurrentProject = new Project();

            // Add initial track
            AddNewTrack(TimelineTrackType.Video);
            PlaybackService?.Pause();
            PlaybackService?.LoadProject(CurrentProject);

            // Reset undo/redo stack
            UndoEngine.Instance.ClearAll();

            // Reset project path
            _currentProjectPath = null;

            // Update window title
            UpdateWindowTitle();

            // Reset unsaved changes flag
            HasUnsavedChanges = false;
        }

        private void OpenProjectDialog()
        {
            // Check for unsaved changes
            if (HasUnsavedChanges)
            {
                var result = MessageBox.Show(
                    "You have unsaved changes. Do you want to save before opening another project?",
                    "Save Changes",
                    MessageBoxButton.YesNoCancel,
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    if (!SaveCurrentProject(false))
                        return; // User cancelled save
                }
                else if (result == MessageBoxResult.Cancel)
                {
                    return; // User cancelled open
                }
            }

            // Show open file dialog
            var openFileDialog = new OpenFileDialog
            {
                Filter = "PressPlay Projects (*.ppproj)|*.ppproj|All Files (*.*)|*.*",
                Title = "Open Project"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                OpenProjectFile(openFileDialog.FileName);
            }
        }

        private void OpenProjectFile(string filePath)
        {
            try
            {
                // Read project file
                var json = File.ReadAllText(filePath);
                var project = ProjectSerializer.DeserializeProject(json);

                // 2) If it succeeded, immediately give it to the playback service
                if (project != null)
                {
                    PlaybackService?.Pause();
                    PlaybackService?.LoadProject(project);

                    // 3) Now swap over your VM's CurrentProject
                    CurrentProject = project;

                    // Update current project path
                    _currentProjectPath = filePath;

                    // Update window title
                    UpdateWindowTitle();

                    // Reset undo/redo stack
                    UndoEngine.Instance.ClearAll();

                    // Reset unsaved changes flag
                    HasUnsavedChanges = false;

                    // Add to recent projects
                    AddToRecentProjects(filePath);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error opening project: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // Keep the RelayCommand version simple:
        [RelayCommand]
        private void SaveProject() => SaveCurrentProject(false);

        // Rename the public method
        public bool SaveProjectToFile()
        {
            return SaveCurrentProject(false);
        }

        private bool SaveCurrentProject(bool saveAs)
        {
            // If saveAs is true or we don't have a current path, show save dialog
            if (saveAs || string.IsNullOrEmpty(_currentProjectPath))
            {
                var saveFileDialog = new SaveFileDialog
                {
                    Filter = "PressPlay Projects (*.ppproj)|*.ppproj|All Files (*.*)|*.*",
                    Title = "Save Project",
                    DefaultExt = ".ppproj"
                };

                if (saveFileDialog.ShowDialog() != true)
                {
                    return false; // User cancelled
                }

                _currentProjectPath = saveFileDialog.FileName;
            }

            try
            {
                // Serialize project to JSON
                var json = ProjectSerializer.SerializeProject(CurrentProject);
                // Save to file
                File.WriteAllText(_currentProjectPath, json);
                UpdateWindowTitle();
                HasUnsavedChanges = false;
                AddToRecentProjects(_currentProjectPath);
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error saving project: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }

        private void AddToRecentProjects(string filePath)
        {
            // Check if already in list
            var existingProject = RecentProjects.FirstOrDefault(p => p.FilePath == filePath);
            if (existingProject != null)
            {
                // Update last opened date
                existingProject.LastOpened = DateTime.Now;

                // Move to top of list
                RecentProjects.Remove(existingProject);
                RecentProjects.Insert(0, existingProject);
            }
            else
            {
                // Add new project to list
                var projectData = new ProjectData
                {
                    Name = Path.GetFileNameWithoutExtension(filePath),
                    FilePath = filePath,
                    LastOpened = DateTime.Now
                };

                RecentProjects.Insert(0, projectData);

                // Limit to 10 recent projects
                while (RecentProjects.Count > 10)
                {
                    RecentProjects.RemoveAt(RecentProjects.Count - 1);
                }
            }

            // Save recent projects list
            SaveRecentProjects();
        }

        private void OpenRecentProjectFromList(object parameter)
        {
            if (parameter is ProjectData projectData)
            {
                // Check if file exists
                if (File.Exists(projectData.FilePath))
                {
                    // Check for unsaved changes
                    if (HasUnsavedChanges)
                    {
                        var result = MessageBox.Show(
                            "You have unsaved changes. Do you want to save before opening another project?",
                            "Save Changes",
                            MessageBoxButton.YesNoCancel,
                            MessageBoxImage.Question);

                        if (result == MessageBoxResult.Yes)
                        {
                            if (!SaveCurrentProject(false))
                                return; // User cancelled save
                        }
                        else if (result == MessageBoxResult.Cancel)
                        {
                            return; // User cancelled open
                        }
                    }

                    // Open project
                    OpenProjectFile(projectData.FilePath);
                }
                else
                {
                    // File doesn't exist anymore
                    MessageBox.Show($"Project file not found: {projectData.FilePath}", "File Not Found", MessageBoxButton.OK, MessageBoxImage.Warning);

                    // Remove from recent projects
                    RecentProjects.Remove(projectData);
                    SaveRecentProjects();
                }
            }
        }

        private void LoadRecentProjects()
        {
            try
            {
                string appDataPath = GetAppDataPath();
                string recentProjectsFile = Path.Combine(appDataPath, "RecentProjects.json");

                if (File.Exists(recentProjectsFile))
                {
                    var json = File.ReadAllText(recentProjectsFile);
                    var projects = JsonSerializer.Deserialize<ObservableCollection<ProjectData>>(json);

                    if (projects != null)
                    {
                        RecentProjects = projects;
                    }
                }
            }
            catch (Exception ex)
            {
                // Just log the error but don't show to user
                System.Diagnostics.Debug.WriteLine($"Error loading recent projects: {ex.Message}");
            }
        }

        private void SaveRecentProjects()
        {
            try
            {
                string appDataPath = GetAppDataPath();
                string recentProjectsFile = Path.Combine(appDataPath, "RecentProjects.json");

                // Ensure directory exists
                Directory.CreateDirectory(appDataPath);

                // Serialize and save
                var options = new JsonSerializerOptions { WriteIndented = true };
                var json = JsonSerializer.Serialize(RecentProjects, options);
                File.WriteAllText(recentProjectsFile, json);
            }
            catch (Exception ex)
            {
                // Just log the error but don't show to user
                System.Diagnostics.Debug.WriteLine($"Error saving recent projects: {ex.Message}");
            }
        }

        private string GetAppDataPath()
        {
            string appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return Path.Combine(appDataPath, "PressPlay");
        }

        private void UpdateWindowTitle()
        {
            string projectName = "Untitled";

            if (!string.IsNullOrEmpty(_currentProjectPath))
            {
                projectName = Path.GetFileNameWithoutExtension(_currentProjectPath);
            }

            Title = $"{projectName}{(HasUnsavedChanges ? "*" : "")} - PressPlay";
        }

        private void AddNewTrack(TimelineTrackType trackType)
        {
            // Create new track with default name  
            string trackName;

            // Count tracks of this type
            if (trackType == TimelineTrackType.Video)
            {
                int videoTrackCount = CurrentProject.Tracks.Count(t => t.Type == TimelineTrackType.Video) + 1;
                trackName = $"Video {videoTrackCount}";
            }
            else
            {
                int audioTrackCount = CurrentProject.Tracks.Count(t => t.Type == TimelineTrackType.Audio) + 1;
                trackName = $"Audio {audioTrackCount}";
            }

            var newTrack = new Track
            {
                Name = trackName,
                Type = trackType
            };

            // Insert track at proper position instead of just adding to end
            int insertIndex = GetProperTrackInsertionIndex(trackType);

            // Add to project at calculated position
            CurrentProject.Tracks.Insert(insertIndex, newTrack);

            // Register for undo  
            var undoUnit = new TrackAddUndoUnit(CurrentProject, newTrack, insertIndex);
            UndoEngine.Instance.AddUndoUnit(undoUnit);

            HasUnsavedChanges = true;
        }

        private void RemoveLastTrack()
        {
            if (CurrentProject.Tracks.Count > 0)
            {
                // Get last track
                var trackToRemove = CurrentProject.Tracks[CurrentProject.Tracks.Count - 1];

                // Check if track has items
                if (trackToRemove.Items.Count > 0)
                {
                    var result = MessageBox.Show(
                        $"Track '{trackToRemove.Name}' contains {trackToRemove.Items.Count} item(s). Are you sure you want to remove it?",
                        "Remove Track",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question);

                    if (result != MessageBoxResult.Yes)
                    {
                        return;
                    }
                }

                // Create undo unit
                var multiUndo = new MultipleUndoUnits("Remove Track");

                // Add undo for each item in the track
                var trackItemRemoveUndo = new TrackItemRemoveUndoUnit();
                foreach (var item in trackToRemove.Items)
                {
                    trackItemRemoveUndo.Items.Add(new TrackAndItemData(trackToRemove, item));
                }

                if (trackItemRemoveUndo.Items.Count > 0)
                {
                    multiUndo.UndoUnits.Add(trackItemRemoveUndo);
                }

                // Add track removal undo
                var trackRemoveUndo = new TrackRemoveUndoUnit(CurrentProject, trackToRemove, CurrentProject.Tracks.Count - 1);
                multiUndo.UndoUnits.Add(trackRemoveUndo);

                // Remove the track
                CurrentProject.Tracks.Remove(trackToRemove);

                // Register the undo unit
                UndoEngine.Instance.AddUndoUnit(multiUndo);

                HasUnsavedChanges = true;
            }
        }

        public void ImportMediaFiles(string[] files)
        {
            if (files == null || files.Length == 0)
                return;

            int importedCount = 0;
            int errorCount = 0;

            foreach (var filePath in files)
            {
                // Check if file exists
                if (!File.Exists(filePath))
                    continue;

                // Check if it's a supported file format
                string extension = Path.GetExtension(filePath).ToLower();
                bool isSupported = FileFormats.SupportedVideoFormats.Contains(extension) ||
                                  FileFormats.SupportedAudioFormats.Contains(extension) ||
                                  FileFormats.SupportedImageFormats.Contains(extension);

                if (!isSupported)
                    continue;

                try
                {
                    // Create a new project clip with proper analysis
                    var projectClip = new ProjectClip(filePath, CurrentProject.FPS);

                    // ProjectClip's constructor should now properly analyze and set the Length
                    // via the GetClipProperties method - no need to manually set it

                    // For images, we can still override with a default duration
                    if (FileFormats.SupportedImageFormats.Contains(extension))
                    {
                        // Images default to 5 seconds
                        projectClip.Length = new TimeCode((int)(5 * CurrentProject.FPS), CurrentProject.FPS);
                    }

                    // Log the actual detected duration
                    System.Diagnostics.Debug.WriteLine($"Imported {Path.GetFileName(filePath)}: {projectClip.Length.TotalFrames} frames, {projectClip.Length.TotalSeconds:F2} seconds");
                    
                    // Add to project clips
                    CurrentProject.Clips.Add(projectClip);

                    // Increment counter
                    importedCount++;

                    // Register for undo
                    var undoUnit = new ClipAddUndoUnit(CurrentProject, projectClip);
                    UndoEngine.Instance.AddUndoUnit(undoUnit);
                }
                catch (Exception ex)
                {
                    errorCount++;
                    System.Diagnostics.Debug.WriteLine($"Error importing file '{filePath}': {ex.Message}");
                    System.Diagnostics.Debug.WriteLine(ex.StackTrace);
                }
            }

            if (importedCount > 0)
            {
                HasUnsavedChanges = true;

                if (errorCount > 0)
                {
                    MessageBox.Show($"Successfully imported {importedCount} file(s).\nFailed to import {errorCount} file(s).\nCheck the debug output for details.",
                        "Import Media", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    MessageBox.Show($"Successfully imported {importedCount} file(s).",
                        "Import Media", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            else if (errorCount > 0)
            {
                MessageBox.Show($"Failed to import {errorCount} file(s). Check the debug output for details.",
                    "Import Media", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        [RelayCommand]
        private void OpenRecordingTool()
        {
            // Create an adapter function to convert a single string to string[] for ImportMediaFiles
            Action<string> importAction = filePath => ImportMediaFiles(new[] { filePath });

            var recordingDialog = new RecordingDialog(importAction);
            recordingDialog.Owner = Application.Current.MainWindow;
            recordingDialog.ShowDialog();
        }


        private Teleprompter.TeleprompterDialog? _teleprompterDialog;

        [RelayCommand]
        private void OpenTeleprompterDialog()
        {
            if (_teleprompterDialog == null)
            {
                _teleprompterDialog = new Teleprompter.TeleprompterDialog();
                _teleprompterDialog.Closed += (_, _) => _teleprompterDialog = null;
                _teleprompterDialog.Show();
            }
            else
            {
                _teleprompterDialog.Activate();
            }
        }
        private void ImportMediaDialog()
        {
            var openFileDialog = new OpenFileDialog
            {
                Filter = "Media Files|*.mp4;*.avi;*.mov;*.mkv;*.mp3;*.wav;*.aac;*.jpg;*.jpeg;*.png;*.bmp|All Files|*.*",
                Multiselect = true,
                Title = "Import Media"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                ImportMediaFiles(openFileDialog.FileNames);
            }
        }

        private void RemoveSelectedMedia()
        {
            // Get selected clips
            var selectedClips = CurrentProject.Clips.Where(c => c.IsSelected).ToList();
            if (selectedClips.Count == 0)
            {
                MessageBox.Show("Please select at least one media clip to remove.", "No Selection", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // Ask for confirmation
            var result = MessageBox.Show(
                $"Are you sure you want to remove {selectedClips.Count} selected media clip(s)?",
                "Remove Media",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes)
                return;

            // Find any timeline items that use these clips
            var affectedItems = new Dictionary<Track, List<ITrackItem>>();

            foreach (var track in CurrentProject.Tracks)
            {
                foreach (var item in track.Items)
                {
                    // Check if item is related to any selected clip
                    // We'll use a simple approach - check if lengths are similar
                    // and if item was created around the same time as the clip was added
                    bool isRelatedToSelectedClip = false;

                    foreach (var clip in selectedClips)
                    {
                        // Compare based on duration/length - this is a simple heuristic
                        // that might need to be refined based on your data model
                        if (Math.Abs(item.Duration.TotalFrames - clip.Length.TotalFrames) < 5)
                        {
                            isRelatedToSelectedClip = true;
                            break;
                        }

                        // You might also have some ID or other property to connect them
                        // For example, if items have FilePath property:
                        if (item.FilePath == clip.FilePath)
                        {
                            isRelatedToSelectedClip = true;
                            break;
                        }
                    }

                    if (isRelatedToSelectedClip)
                    {
                        if (!affectedItems.ContainsKey(track))
                        {
                            affectedItems[track] = new List<ITrackItem>();
                        }

                        affectedItems[track].Add(item);
                    }
                }
            }

            // If there are timeline items using these clips, warn the user
            if (affectedItems.Count > 0)
            {
                int totalItems = affectedItems.Values.Sum(list => list.Count);
                var warningResult = MessageBox.Show(
                    $"The selected media is used by {totalItems} item(s) in the timeline. Removing this media will also remove those items. Continue?",
                    "Warning",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (warningResult != MessageBoxResult.Yes)
                    return;
            }

            // Create multi-undo unit
            var multiUndo = new MultipleUndoUnits("Remove Media");

            // Add undo for clips
            var clipRemoveUndo = new ClipRemoveUndoUnit(CurrentProject);
            foreach (var clip in selectedClips)
            {
                clipRemoveUndo.Clips.Add(clip);
                // Remove clip from project
                CurrentProject.Clips.Remove(clip);
            }

            multiUndo.UndoUnits.Add(clipRemoveUndo);

            // Add undo for timeline items
            foreach (var trackItemsPair in affectedItems)
            {
                var track = trackItemsPair.Key;
                var items = trackItemsPair.Value;

                var trackItemRemoveUndo = new TrackItemRemoveUndoUnit();

                foreach (var item in items)
                {
                    trackItemRemoveUndo.Items.Add(new TrackAndItemData(track, item));
                    // Remove item from track
                    track.Items.Remove(item);
                }

                multiUndo.UndoUnits.Add(trackItemRemoveUndo);
            }

            // Register undo unit
            UndoEngine.Instance.AddUndoUnit(multiUndo);
            HasUnsavedChanges = true;
        }

        private void AddNewOutlineEntry()
        {
            // Add new entry with current time position
            string currentTimeCode = CurrentProject.NeedlePositionTime.ToString();

            CurrentProject.StepOutlineEntries.Add(new StepOutlineEntry
            {
                Timecode = currentTimeCode,
                Description = "New scene"
            });

            HasUnsavedChanges = true;
        }

        private void RemoveLastOutlineEntry()
        {
            if (CurrentProject.StepOutlineEntries.Count > 0)
            {
                CurrentProject.StepOutlineEntries.RemoveAt(CurrentProject.StepOutlineEntries.Count - 1);
                HasUnsavedChanges = true;
            }
        }

        private void ExportStepOutlineToFile(string format)
        {
            if (CurrentProject.StepOutlineEntries.Count == 0)
            {
                MessageBox.Show("There are no step outline entries to export.", "Nothing to Export", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // Determine export format and file extension
            string filter;
            string defaultExt;

            if (format == "PDF")
            {
                filter = "PDF Files (*.pdf)|*.pdf";
                defaultExt = ".pdf";
            }
            else if (format == "XLSX")
            {
                filter = "Excel Files (*.xlsx)|*.xlsx";
                defaultExt = ".xlsx";
            }
            else
            {
                MessageBox.Show($"Unsupported export format: {format}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            // Show save dialog
            var saveFileDialog = new SaveFileDialog
            {
                Filter = filter,
                DefaultExt = defaultExt,
                Title = $"Export Step Outline to {format}"
            };

            if (saveFileDialog.ShowDialog() != true)
                return;

            try
            {
                if (format == "PDF")
                {
                    ExportStepOutlineToPdf(saveFileDialog.FileName);
                }
                else if (format == "XLSX")
                {
                    ExportStepOutlineToExcel(saveFileDialog.FileName);
                }

                // Ask if user wants to open the file
                var result = MessageBox.Show(
                    "Step outline exported successfully. Would you like to open it?",
                    "Export Complete",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Information);

                if (result == MessageBoxResult.Yes)
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = saveFileDialog.FileName,
                        UseShellExecute = true
                    });
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error exporting step outline: {ex.Message}", "Export Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ExportStepOutlineToExcel(string filePath)
        {
            // 1) Create workbook & sheet
            using var workbook = new XLWorkbook();
            var sheet = workbook.Worksheets.Add("Step Outline");

            // 2) Header row
            sheet.Cell(1, 1).Value = "Timecode";
            sheet.Cell(1, 2).Value = "Description";

            // 3) Data rows
            int row = 2;
            foreach (var entry in CurrentProject.StepOutlineEntries)
            {
                sheet.Cell(row, 1).Value = entry.Timecode;
                sheet.Cell(row, 2).Value = entry.Description;
                row++;
            }

            // 4) Auto-fit and save
            sheet.Columns().AdjustToContents();
            workbook.SaveAs(filePath);
        }

        private void ExportStepOutlineToPdf(string filePath)
        {
            QuestPDF.Settings.License = LicenseType.Community;
            var entries = CurrentProject.StepOutlineEntries.ToList();

            Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(1, Unit.Centimetre);
                    page.Content()
                        .Table(table =>
                        {
                            // Columns: narrow + wide
                            table.ColumnsDefinition(columns =>
                            {
                                columns.ConstantColumn(50);
                                columns.RelativeColumn();
                            });

                            // Header
                            table.Header(header =>
                            {
                                header.Cell().Element(CellStyle).Text("Timecode");
                                header.Cell().Element(CellStyle).Text("Description");
                            });

                            // Rows
                            foreach (var e in entries)
                            {
                                table.Cell().Element(CellStyle).Text(e.Timecode);
                                table.Cell().Element(CellStyle).Text(e.Description);
                            }
                        });
                });
            })
            .GeneratePdf(filePath);

            static IContainer CellStyle(IContainer c) => c.Border(1).Padding(5);
        }
        private void ExportCurrentProject()
        {
            // Make sure project is saved first
            if (HasUnsavedChanges)
            {
                var saveResult = MessageBox.Show(
                    "Your project has unsaved changes. Do you want to save the project before exporting?",
                    "Save Project",
                    MessageBoxButton.YesNoCancel,
                    MessageBoxImage.Question);

                if (saveResult == MessageBoxResult.Cancel)
                    return;

                if (saveResult == MessageBoxResult.Yes)
                {
                    //var saved = SaveProjectAsync(false).GetAwaiter().GetResult();
                    //if (!saved) return; // User cancelled save
                }
            }

            // Create export settings with project dimensions
            var settings = ExportSettings.CreateDefault(CurrentProject.ProjectWidth, CurrentProject.ProjectHeight);

            // If project has a file path, use same directory for export
            if (!string.IsNullOrEmpty(_currentProjectPath))
            {
                var projectDir = Path.GetDirectoryName(_currentProjectPath);
                var projectName = Path.GetFileNameWithoutExtension(_currentProjectPath);

                settings.OutputPath = Path.Combine(projectDir, $"{projectName}_export{settings.GetFileExtension()}");
            }

            // Create and show export dialog
            var exportDialog = new ExportDialog(CurrentProject)
            {
                Owner = Application.Current.MainWindow
            };

            exportDialog.ShowDialog();
        }


        private void ShowProjectSettings()
        {
            // Placeholder for project settings dialog
            MessageBox.Show("Project settings dialog not yet implemented", "Not Implemented");
        }

        private void OpenUrl(string url)
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error opening URL: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        private int GetProperTrackInsertionIndex(TimelineTrackType trackType)
        {
            // For video tracks: insert at the end of existing video tracks
            if (trackType == TimelineTrackType.Video)
            {
                int videoTrackCount = CurrentProject.Tracks.Count(t => t.Type == TimelineTrackType.Video);
                return videoTrackCount; // Insert after the last video track
            }
            // For audio tracks: insert at the end of all tracks
            else
            {
                return CurrentProject.Tracks.Count; // Insert at the very end
            }
        }

        #endregion
    }
}