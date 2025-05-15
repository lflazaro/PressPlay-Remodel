using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using FFMpegCore;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using OpenCvSharp.WpfExtensions;
using PressPlay.Helpers;
using PressPlay.Models;

namespace PressPlay.Services
{
    public interface IPlaybackService
    {
        event Action<TimeSpan> PositionChanged;
        void Play();
        void Pause();
        void LoadMedia(string path);
        void Seek(TimeCode time);
        void Rewind();
        void FastForward();
        void LoadProject(Project project);
    }

    public class PlaybackService : IPlaybackService, IDisposable
    {
        private Project _project;
        private readonly DispatcherTimer _timer;
        private readonly Image _previewControl;
        private IWavePlayer _videoAudioWaveOut;
        private AudioFileReader _videoAudioReader;
        private string _videoAudioPath;
        // --- audio for the loaded media file ---
        private IWavePlayer _mainWaveOut;
        private WaveStream _mainAudioStream;
        private string _mainAudioPath;
        // Collection of all active video audio players (based on clip ID)
        private Dictionary<string, (WaveOutEvent Player, AudioFileReader Reader)> _videoAudioPlayers
            = new Dictionary<string, (WaveOutEvent, AudioFileReader)>();

        // Collection of all active audio track players (based on clip ID)
        private Dictionary<string, (WaveOutEvent Player, AudioFileReader Reader)> _audioTrackPlayers
            = new Dictionary<string, (WaveOutEvent, AudioFileReader)>();
        // --- audio for timeline clips ---
        private IWavePlayer _clipWaveOut;
        private AudioFileReader _clipAudioReader;
        private string _currentClipPath;

        private DateTime _playStartTimeUtc;
        private TimeCode _startPosition;

        public event Action<TimeSpan> PositionChanged;

        public PlaybackService(Project project, Image previewControl)
        {
            _project = project ?? throw new ArgumentNullException(nameof(project));
            _previewControl = previewControl ?? throw new ArgumentNullException(nameof(previewControl));

            _timer = new DispatcherTimer(DispatcherPriority.Render)
            {
                Interval = TimeSpan.FromMilliseconds(1000.0 / _project.FPS)
            };
            _timer.Tick += OnTick;
        }
        public void LoadProject(Project project)
        {
            if (project == null) throw new ArgumentNullException(nameof(project));

            // 1) Pause and tear down any existing playback
            Pause();
            _timer.Stop();

            // 2) Dispose all per-clip audio players
            foreach (var player in _audioPlayers) player.Dispose();
            _audioPlayers.Clear();
            StopMainAudio();

            // 3) (Optional) dispose any video-audio players you’re tracking
            foreach (var kv in _videoAudioPlayers.Values)
            {
                kv.Player.Stop();
                kv.Player.Dispose();
                kv.Reader.Dispose();
            }
            _videoAudioPlayers.Clear();

            foreach (var kv in _audioTrackPlayers.Values)
            {
                kv.Player.Stop();
                kv.Player.Dispose();
                kv.Reader.Dispose();
            }
            _audioTrackPlayers.Clear();

            // 4) Adopt the new project
            _project = project;

            // 5) Reset timer interval to match its FPS
            _timer.Interval = TimeSpan.FromMilliseconds(1000.0 / _project.FPS);

            // 6) Jump back to zero and render the first frame
            Seek(new TimeCode(0, _project.FPS));
        }
        public void LoadMedia(string path)
        {
            try
            {
                Debug.WriteLine($"[PlaybackService] Loading media: {path}");

                // 1) Stop+dispose any prior main audio
                StopMainAudio();

                if (string.IsNullOrEmpty(path) || !File.Exists(path))
                {
                    Debug.WriteLine("[PlaybackService] Invalid media path");
                    return;
                }

                // 2) Check if file has audio
                bool hasAudio = true;
                string extension = Path.GetExtension(path).ToLowerInvariant();

                if (FileFormats.SupportedAudioFormats.Contains(extension))
                {
                    hasAudio = true;
                    Debug.WriteLine("[PlaybackService] Audio file detected");
                }
                else if (FileFormats.SupportedVideoFormats.Contains(extension))
                {
                    // Video file - check for audio streams
                    try
                    {
                        var mediaInfo = FFProbe.Analyse(path);
                        hasAudio = mediaInfo.AudioStreams?.Any() == true;
                        Debug.WriteLine($"[PlaybackService] Video has audio streams: {hasAudio}");

                        // Force to true for testing
                        if (!hasAudio)
                        {
                            Debug.WriteLine("[PlaybackService] Forcing HasAudio=true for testing");
                            hasAudio = true;
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[PlaybackService] Error analyzing audio streams: {ex.Message}");
                        // Assume it has audio for now
                        hasAudio = true;
                    }
                }

                // 3) Initialize audio playback if file has audio
                if (hasAudio)
                {
                    try
                    {
                        _mainAudioPath = path;
                        _mainAudioStream = new MediaFoundationReader(path);
                        _mainWaveOut = new WaveOutEvent();
                        _mainWaveOut.Init(_mainAudioStream);

                        Debug.WriteLine($"[PlaybackService] Audio initialized successfully for: {path}");
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[PlaybackService] Error initializing audio: {ex.Message}");
                        StopMainAudio();
                    }
                }
                else
                {
                    Debug.WriteLine("[PlaybackService] Media has no audio streams");
                }

                // 4) Reset the playhead to zero (this will also call UpdateAudio())
                Seek(new TimeCode(0, _project.FPS));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[PlaybackService] Error in LoadMedia: {ex.Message}");
            }
        }

        // Replace the Play method in PlaybackService to debug audio issues

        public void Play()
        {
            Debug.WriteLine("[PlaybackService] Play method called");

            if (_timer.IsEnabled)
            {
                Debug.WriteLine("[PlaybackService] Timer already running, skipping Play()");
                return;
            }

            // Start the frame timer for video rendering
            // Prime first frame & audio so they start truly together
            _playStartTimeUtc = DateTime.UtcNow;
            _startPosition = _project.NeedlePositionTime;
            RenderFrame();
            UpdateAudio();

            // Now kick off the timer
            _timer.Interval = TimeSpan.FromMilliseconds(1000.0 / _project.FPS);
            _timer.Start();
            _project.IsPlaying = true;

            // Start all already-initialized audio sources
            if (_videoAudioWaveOut != null && _videoAudioWaveOut.PlaybackState != PlaybackState.Playing)
            {
                Debug.WriteLine("[PlaybackService] Starting video audio");
                _videoAudioWaveOut.Play();
            }

            if (_clipWaveOut != null && _clipWaveOut.PlaybackState != PlaybackState.Playing)
            {
                Debug.WriteLine("[PlaybackService] Starting clip audio");
                _clipWaveOut.Play();
            }

            // Start main audio only if no other audio is playing
            if (_videoAudioWaveOut == null && _clipWaveOut == null &&
                _mainWaveOut != null && _mainWaveOut.PlaybackState != PlaybackState.Playing)
            {
                Debug.WriteLine("[PlaybackService] Starting main audio");
                _mainAudioStream.CurrentTime = _project.NeedlePositionTime.ToTimeSpan();
                _mainWaveOut.Play();
            }
        }


        public void Pause()
        {
            if (!_timer.IsEnabled) return;

            _timer.Stop();
            _project.IsPlaying = false;

            // Pause all audio
            _mainWaveOut?.Pause();

            foreach (var playerState in _audioPlayers)
            {
                if (playerState.Player.PlaybackState == PlaybackState.Playing)
                {
                    playerState.Player.Pause();
                    Debug.WriteLine($"[{playerState.ItemIdentifier}] Paused on global pause");
                }
            }
        }



        public void Seek(TimeCode time)
        {
            // clamp frames
            double total = _project.GetTotalFrames();
            int target = (int)Math.Round(Math.Max(0, Math.Min(time.TotalFrames, total)));
            _project.NeedlePositionTime = new TimeCode(target, _project.FPS);

            RenderFrame();
            UpdateAudio();

            PositionChanged?.Invoke(_project.NeedlePositionTime.ToTimeSpan());
        }

        public void Rewind()
        {
            var delta = (int)(5 * _project.FPS);
            Seek(new TimeCode(_project.NeedlePositionTime.TotalFrames - delta, _project.FPS));
            Debug.WriteLine("Rewind");
        }

        public void FastForward()
        {
            var delta = (int)(5 * _project.FPS);
            Seek(new TimeCode(_project.NeedlePositionTime.TotalFrames + delta, _project.FPS));
            Debug.WriteLine("Fast forward");
        }

        private void OnTick(object s, EventArgs e)
        {
            var elapsed = DateTime.UtcNow - _playStartTimeUtc;
            double desiredFrm = _startPosition.TotalFrames + elapsed.TotalSeconds * _project.FPS;
            double maxFrames = _project.GetTotalFrames();

            if (desiredFrm >= maxFrames)
            {
                // end of media
                Seek(new TimeCode((int)maxFrames - 1, _project.FPS));
                Pause();
                return;
            }

            int frameIndex = (int)Math.Round(desiredFrm);
            var newTime = new TimeCode(frameIndex, _project.FPS);
            _project.NeedlePositionTime = newTime;

            RenderFrame();
            UpdateAudio();

            PositionChanged?.Invoke(newTime.ToTimeSpan());
        }
        private void RenderFrame()
        {
            // 1) Early-out if no canvas
            if (_project.ProjectWidth == 0 || _project.ProjectHeight == 0)
            {
                _previewControl.Source = null;
                return;
            }

            int width = _project.ProjectWidth;
            int height = _project.ProjectHeight;
            int idx = (int)Math.Round((double)_project.NeedlePositionTime.TotalFrames);

            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                // 2) Black background
                dc.DrawRectangle(Brushes.Black, null, new Rect(0, 0, width, height));

                // 3) For each video track, from bottom to top
                foreach (var track in _project.Tracks.OfType<Track>().Reverse())
                {
                    if (track.Type == TimelineTrackType.Audio)
                        continue;

                    // 4) Grab *all* items active at this frame, sorted by start time
                    var activeItems = track.Items
                        .Where(i =>
                            i.Position.TotalFrames <= idx &&
                            idx < i.Position.TotalFrames + i.Duration.TotalFrames
                        )
                        .OrderBy(i => i.Position.TotalFrames);

                    // 5) Draw each with its fade
                    foreach (var item in activeItems)
                        DrawItemWithFade(item, idx, width, height, dc);
                }
            }

            // 6) Push to the preview control
            var rtb = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(dv);
            _previewControl.Source = rtb;
        }


        private void DrawItemWithFade(ITrackItem item, int idx, int width, int height, DrawingContext dc)
        {

            // A) Find the clip behind this track-item
            var clip = _project.Clips.FirstOrDefault(c =>
                string.Equals(c.FilePath, item.FilePath, StringComparison.OrdinalIgnoreCase)
                || c.Id == (item as AudioTrackItem)?.ClipId);
            if (clip == null) return;

            // B) Figure out which frame of the media to draw
            int clipFrame = idx - item.Position.TotalFrames + item.Start.TotalFrames;
            clipFrame = Math.Clamp(clipFrame, 0, (int)clip.Length.TotalFrames - 1);
            // Use project.FPS so timeline and source stay in sync
            double clipSeconds = clipFrame / (double)_project.FPS;
            var bmp = clip.GetFrameAt(TimeSpan.FromSeconds(clipSeconds));

            // C) Compute fade parameters
            double framePos = idx - item.Position.TotalFrames;
            double dur = item.Duration.TotalFrames;
            double fadeInF = item.FadeInFrame;
            double fadeOutF = item.FadeOutFrame;
            double fadeOutStart = dur - fadeOutF;

            if (item is TrackItem ti)
            {
                // 1) global clip opacity
                dc.PushOpacity(ti.Opacity);

                // 2) combine translate → scale → rotate around the canvas center
                var tx = new TranslateTransform(ti.TranslateX, ti.TranslateY);
                var sc = new ScaleTransform(ti.ScaleX, ti.ScaleY, width * 0.5, height * 0.5);
                var rt = new RotateTransform(ti.Rotation, width * 0.5, height * 0.5);
                var group = new TransformGroup();
                group.Children.Add(tx);
                group.Children.Add(sc);
                group.Children.Add(rt);

                dc.PushTransform(group);
            }

            if (item.FadeColor == Track.FadeColor.White)
            {
                // 1) Draw the clip at full opacity…
                dc.DrawImage(bmp, new Rect(0, 0, width, height));

                // 2) Overlay a white quad whose alpha goes 1→0 then 0→1
                double overlayOpacity = 0;

                // fade-in region
                if (fadeInF > 0 && framePos < fadeInF)
                    overlayOpacity = 1 - (framePos / fadeInF);

                // fade-out region (inclusive start)
                if (fadeOutF > 0 && framePos >= fadeOutStart)
                {
                    double t = (framePos - fadeOutStart) / fadeOutF;
                    overlayOpacity = Math.Max(overlayOpacity, Math.Min(1, t));
                }

                if (overlayOpacity > 0)
                {
                    dc.PushOpacity(overlayOpacity);
                    dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, width, height));
                    dc.Pop();
                }
            }
            else
            {
                // BLACK fade: composite the clip with varying opacity over black
                double opacity = 1.0;

                // fade-in region
                if (fadeInF > 0 && framePos < fadeInF)
                    opacity = framePos / fadeInF;

                // fade-out region (inclusive start)
                if (fadeOutF > 0 && framePos >= fadeOutStart)
                    opacity = Math.Min(opacity, (dur - framePos) / fadeOutF);

                dc.PushOpacity(opacity);
                dc.DrawImage(bmp, new Rect(0, 0, width, height));
                dc.Pop();
            }
            if (item is TrackItem)
            {
                dc.Pop();  // pop TransformGroup
                dc.Pop();  // pop Opacity
            }
        }


        // Fields to track audio players
        private List<AudioPlayerState> _audioPlayers = new List<AudioPlayerState>();
        private class AudioPlayerState
        {
            public WaveOutEvent Player { get; private set; }
            public AudioFileReader Reader { get; private set; }
            public VolumeSampleProvider VolumeProvider { get; private set; }
            public ITrackItem Item { get; set; }
            public string ItemIdentifier { get; set; }

            public AudioPlayerState(string filePath, TimeSpan position, ITrackItem item, string itemId)
            {
                ItemIdentifier = itemId;
                Item = item;

                // Create reader and set position
                Reader = new AudioFileReader(filePath);
                Reader.CurrentTime = position;

                // Create volume provider with initial volume
                float volume = GetItemVolume(item);
                VolumeProvider = new VolumeSampleProvider(Reader.ToSampleProvider());
                VolumeProvider.Volume = volume;

                // Create player with volume provider
                Player = new WaveOutEvent();
                Player.Init(VolumeProvider);

                Debug.WriteLine($"[{ItemIdentifier}] Created with volume {volume}");
            }

            // Update volume by applying to VolumeSampleProvider (not WaveOutEvent)
            public void UpdateVolume()
            {
                float newVolume = GetItemVolume(Item);
                if (Math.Abs(VolumeProvider.Volume - newVolume) > 0.01f)
                {
                    VolumeProvider.Volume = newVolume;
                    Debug.WriteLine($"[{ItemIdentifier}] Updated sample provider volume to {newVolume}");
                }
            }

            private float GetItemVolume(ITrackItem item)
            {
                if (item is TrackItem ti)
                    return ti.Volume;
                else if (item is AudioTrackItem ati)
                    return ati.Volume;
                return 1.0f;
            }

            public void Seek(TimeSpan position)
            {
                // Only seek if drift exceeds half a frame (~20ms at 25 fps)
                const double thresholdMs = 1000.0 / 25.0 / 2.0;
                if (Reader != null && Math.Abs((Reader.CurrentTime - position).TotalMilliseconds) > thresholdMs)
                {
                    Reader.CurrentTime = position;
                    Debug.WriteLine($"[{ItemIdentifier}] Seeking to {position}");
                }
            }

            public void Play()
            {
                if (Player != null && Player.PlaybackState != PlaybackState.Playing)
                {
                    Player.Play();
                    Debug.WriteLine($"[{ItemIdentifier}] Started playback");
                }
            }

            public void Pause()
            {
                if (Player != null && Player.PlaybackState == PlaybackState.Playing)
                {
                    Player.Pause();
                    Debug.WriteLine($"[{ItemIdentifier}] Paused playback");
                }
            }

            public void Dispose()
            {
                if (Player != null)
                {
                    if (Player.PlaybackState == PlaybackState.Playing)
                        Player.Stop();

                    Player.Dispose();
                    Player = null;
                }

                if (Reader != null)
                {
                    Reader.Dispose();
                    Reader = null;
                }

                VolumeProvider = null;
                Item = null;
            }
        }

        private void UpdateAudio()
        {
            try
            {
                int frameIndex = (int)Math.Round((double)_project.NeedlePositionTime.TotalFrames);

                // Collect all active items at current frame
                List<(ITrackItem Item, ProjectClip Clip, TimeSpan Position, string ItemId)> activeItems =
                    new List<(ITrackItem, ProjectClip, TimeSpan, string)>();

                // Find all active video items with audio
                foreach (var track in _project.Tracks.Where(t => t.Type == TimelineTrackType.Video))
                {
                    foreach (var item in track.Items.Where(i =>
                        i.Position.TotalFrames <= frameIndex &&
                        frameIndex < i.Position.TotalFrames + i.Duration.TotalFrames))
                    {
                        var clip = _project.Clips
                            .OfType<ProjectClip>()
                            .FirstOrDefault(c => string.Equals(c.FilePath, item.FilePath, StringComparison.OrdinalIgnoreCase));

                        if (clip != null && clip.HasAudio && File.Exists(clip.FilePath))
                        {
                            // Calculate position in clip
                            double offsetFrames = frameIndex - item.Position.TotalFrames + item.Start.TotalFrames;
                            offsetFrames = Math.Max(0, offsetFrames);
                            TimeSpan clipPos = TimeSpan.FromSeconds(offsetFrames / clip.FPS);

                            // Create a truly unique identifier for this item instance
                            string itemId = $"V_{item.InstanceId}";

                            activeItems.Add((item, clip, clipPos, itemId));
                        }
                    }
                }

                // Find all active audio track items
                foreach (var track in _project.Tracks.Where(t => t.Type == TimelineTrackType.Audio))
                {
                    foreach (var item in track.Items.Where(i =>
                        i.Position.TotalFrames <= frameIndex &&
                        frameIndex < i.Position.TotalFrames + i.Duration.TotalFrames))
                    {
                        var clip = _project.Clips
                            .OfType<ProjectClip>()
                            .FirstOrDefault(c =>
                                item is AudioTrackItem ati ? c.Id == ati.ClipId :
                                string.Equals(c.FilePath, item.FilePath, StringComparison.OrdinalIgnoreCase));

                        if (clip != null && File.Exists(clip.FilePath))
                        {
                            // Calculate position in clip
                            double offsetFrames = frameIndex - item.Position.TotalFrames + item.Start.TotalFrames;
                            offsetFrames = Math.Max(0, offsetFrames);
                            TimeSpan clipPos = TimeSpan.FromSeconds(offsetFrames / _project.FPS);

                            // Create a truly unique identifier for this item instance
                            string itemId = $"V_{item.InstanceId}";

                            activeItems.Add((item, clip, clipPos, itemId));
                        }
                    }
                }

                // Get the IDs of all active items
                var activeItemIds = activeItems.Select(x => x.ItemId).ToHashSet();

                // Remove players that are no longer active
                var inactivePlayers = _audioPlayers.Where(p => !activeItemIds.Contains(p.ItemIdentifier)).ToList();
                foreach (var player in inactivePlayers)
                {
                    Debug.WriteLine($"[{player.ItemIdentifier}] Removing inactive player");
                    player.Dispose();
                    _audioPlayers.Remove(player);
                }

                // Update existing players or create new ones
                foreach (var (item, clip, position, itemId) in activeItems)
                {
                    // Find existing player for this item
                    var playerState = _audioPlayers.FirstOrDefault(p => p.ItemIdentifier == itemId);

                    if (playerState != null)
                    {
                        // Update existing player
                        playerState.Item = item; // Update item reference 
                        playerState.UpdateVolume(); // Update volume settings
                                                    // --- apply fade‐in/out envelope ---
                        {
                            double framePos = frameIndex - item.Position.TotalFrames;
                            double dur = item.Duration.TotalFrames;
                            double fadeInF = item.FadeInFrame;
                            double fadeOutF = item.FadeOutFrame;
                            double fadeOutStart = dur - fadeOutF;
                            double fadeFactor = 1.0;
                            if (fadeInF > 0 && framePos < fadeInF)
                                fadeFactor = framePos / fadeInF;
                            if (fadeOutF > 0 && framePos >= fadeOutStart)
                                fadeFactor = Math.Min(fadeFactor, (dur - framePos) / fadeOutF);
                            // adjust the mixer volume
                            playerState.VolumeProvider.Volume *= (float)fadeFactor;
                        }
                        if (!_project.IsPlaying)
                            playerState.Seek(position);

                    }
                    else
                    {
                        // Create new player with the VolumeSampleProvider approach
                        try
                        {
                            var newState = new AudioPlayerState(clip.FilePath, position, item, itemId);
                            // --- apply initial fade‐in/out envelope ---
                            {
                                double framePos = frameIndex - item.Position.TotalFrames;
                                double dur = item.Duration.TotalFrames;
                                double fadeInF = item.FadeInFrame;
                                double fadeOutF = item.FadeOutFrame;
                                double fadeOutStart = dur - fadeOutF;
                                double fadeFactor = 1.0;
                                if (fadeInF > 0 && framePos < fadeInF)
                                    fadeFactor = framePos / fadeInF;
                                if (fadeOutF > 0 && framePos >= fadeOutStart)
                                    fadeFactor = Math.Min(fadeFactor, (dur - framePos) / fadeOutF);
                                newState.VolumeProvider.Volume *= (float)fadeFactor;
                            }
                            // Start if needed
                            if (_project.IsPlaying)
                                newState.Play();

                            _audioPlayers.Add(newState);
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"[{itemId}] Error creating player: {ex.Message}");
                        }
                    }
                }

                // After we’ve updated all players, enforce global play/pause
                if (_project.IsPlaying)
                {
                    foreach (var p in _audioPlayers)
                        p.Play();
                }
                else
                {
                    foreach (var p in _audioPlayers)
                        p.Pause();
                }

                // Handle main audio only if no other audio is playing
                if (_audioPlayers.Count == 0 &&
                    _mainWaveOut != null && _mainAudioStream != null)
                {
                    var desired = _project.NeedlePositionTime.ToTimeSpan();

                    if (Math.Abs((_mainAudioStream.CurrentTime - desired).TotalMilliseconds) > 100)
                        _mainAudioStream.CurrentTime = desired;

                    if (_project.IsPlaying && _mainWaveOut.PlaybackState != PlaybackState.Playing)
                        _mainWaveOut.Play();
                    else if (!_project.IsPlaying && _mainWaveOut.PlaybackState == PlaybackState.Playing)
                        _mainWaveOut.Pause();
                }
                else if (_mainWaveOut != null && _mainWaveOut.PlaybackState == PlaybackState.Playing)
                {
                    _mainWaveOut.Pause();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in UpdateAudio: {ex.Message}");
            }
        }

        private void StopMainAudio()
        {
            if (_mainWaveOut != null)
            {
                if (_mainWaveOut.PlaybackState == PlaybackState.Playing)
                    _mainWaveOut.Stop();
                _mainWaveOut.Dispose();
                _mainAudioStream.Dispose();
                _mainWaveOut = null;
                _mainAudioStream = null;
            }
            _mainAudioPath = null;
        }


        // Update the Dispose method
        public void Dispose()
        {
            Pause();
            _timer.Tick -= OnTick;

            // Dispose all audio players
            foreach (var playerState in _audioPlayers)
            {
                playerState.Dispose();
            }
            _audioPlayers.Clear();

            StopMainAudio();
        }

    }
}