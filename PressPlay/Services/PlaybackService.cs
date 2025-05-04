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
    }

    public class PlaybackService : IPlaybackService, IDisposable
    {
        private readonly Project _project;
        private readonly DispatcherTimer _timer;
        private readonly Image _previewControl;
        private IWavePlayer _videoAudioWaveOut;
        private AudioFileReader _videoAudioReader;
        private string _videoAudioPath;
        // --- audio for the loaded media file ---
        private IWavePlayer _mainWaveOut;
        private WaveStream _mainAudioStream;
        private string _mainAudioPath;

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

        // Update the LoadMedia method in PlaybackService.cs to better handle audio:
        private void StopVideoAudio()
        {
            if (_videoAudioWaveOut != null)
            {
                if (_videoAudioWaveOut.PlaybackState == PlaybackState.Playing)
                    _videoAudioWaveOut.Stop();
                _videoAudioWaveOut.Dispose();
                _videoAudioReader.Dispose();
                _videoAudioWaveOut = null;
                _videoAudioReader = null;
            }
            _videoAudioPath = null;
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
            _playStartTimeUtc = DateTime.UtcNow;
            _startPosition = _project.NeedlePositionTime;
            _timer.Interval = TimeSpan.FromMilliseconds(1000.0 / _project.FPS);
            _timer.Start();
            _project.IsPlaying = true;
            Debug.WriteLine("[PlaybackService] Frame timer started");

            // Call UpdateAudio to handle all audio sources
            UpdateAudio();

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

            _mainWaveOut?.Pause();
            _clipWaveOut?.Pause();
            _videoAudioWaveOut?.Pause();
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
            // *** All your existing video‐only rendering code UNCHANGED ***
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
                dc.DrawRectangle(Brushes.Black, null, new Rect(0, 0, width, height));
                foreach (var track in _project.Tracks.OfType<Track>().Reverse())
                {
                    if (track.Type == TimelineTrackType.Audio) continue;
                    var item = track.Items.FirstOrDefault(i =>
                        i.Position.TotalFrames <= idx &&
                        idx < i.Position.TotalFrames + i.Duration.TotalFrames);
                    if (item == null) continue;

                    var clip = _project.Clips.FirstOrDefault(c =>
                        string.Equals(c.FilePath, item.FilePath, StringComparison.OrdinalIgnoreCase)
                        || c.Id == (item as AudioTrackItem)?.ClipId);
                    if (clip == null) continue;

                    int clipFrame = idx - item.Position.TotalFrames + item.Start.TotalFrames;
                    clipFrame = Math.Clamp(clipFrame, 0, (int)clip.Length.TotalFrames - 1);
                    var ts = TimeSpan.FromSeconds(clipFrame / clip.FPS);
                    var bmp = clip.GetFrameAt(ts);
                    dc.DrawImage(bmp, new Rect(0, 0, width, height));
                }
            }

            var rtb = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(dv);
            _previewControl.Source = rtb;
        }

        private void UpdateAudio()
        {
            try
            {
                int frameIndex = (int)Math.Round((double)_project.NeedlePositionTime.TotalFrames);
                Debug.WriteLine($"UpdateAudio at frame {frameIndex}");

                // First, check for video items with audio
                ITrackItem videoItem = null;
                ProjectClip videoClip = null;

                foreach (var track in _project.Tracks.Where(t => t.Type == TimelineTrackType.Video))
                {
                    videoItem = track.Items.FirstOrDefault(i =>
                        i.Position.TotalFrames <= frameIndex &&
                        frameIndex < i.Position.TotalFrames + i.Duration.TotalFrames);

                    if (videoItem != null)
                    {
                        // Find the matching video clip
                        videoClip = _project.Clips
                            .FirstOrDefault(c => string.Equals(c.FilePath, videoItem.FilePath, StringComparison.OrdinalIgnoreCase)) as ProjectClip;

                        if (videoClip != null)
                        {
                            // Always treat videos as having audio for now
                            Debug.WriteLine($"Found video clip: {videoClip.FileName}, HasAudio=True (forced)");
                            break;  // Found a video clip with audio
                        }
                    }
                }

                // Next, check for audio track items
                var audioTrack = _project.Tracks.FirstOrDefault(t => t.Type == TimelineTrackType.Audio);
                var audioItem = audioTrack?.Items
                    .FirstOrDefault(i =>
                        i.Position.TotalFrames <= frameIndex &&
                        frameIndex < i.Position.TotalFrames + i.Duration.TotalFrames);

                // Handle VIDEO AUDIO (don't stop other audio)
                if (videoItem != null && videoClip != null)
                {
                    Debug.WriteLine($"Processing video audio: {videoClip.FileName}");

                    if (!File.Exists(videoClip.FilePath))
                    {
                        Debug.WriteLine("Video file not found, stopping video audio");
                        StopVideoAudio();
                    }
                    else
                    {
                        // Calculate position within the clip
                        double offsetFrames = frameIndex - videoItem.Position.TotalFrames + videoItem.Start.TotalFrames;
                        offsetFrames = Math.Max(0, offsetFrames);
                        TimeSpan clipPos = TimeSpan.FromSeconds(offsetFrames / videoClip.FPS);

                        Debug.WriteLine($"Video position: {clipPos}");

                        // If same clip is already playing, just update position and volume
                        if (_videoAudioPath == videoClip.FilePath && _videoAudioReader != null && _videoAudioWaveOut != null)
                        {
                            Debug.WriteLine("Same video still playing, updating position");

                            if (Math.Abs((_videoAudioReader.CurrentTime - clipPos).TotalMilliseconds) > 100)
                            {
                                Debug.WriteLine($"Seeking video audio to {clipPos}");
                                _videoAudioReader.CurrentTime = clipPos;
                            }

                            // Update volume if changed
                            if (videoItem is TrackItem trackItem)
                            {
                                _videoAudioWaveOut.Volume = trackItem.Volume;
                                Debug.WriteLine($"Updated video volume: {trackItem.Volume}");
                            }

                            // Play if needed
                            if (_project.IsPlaying && _videoAudioWaveOut.PlaybackState != PlaybackState.Playing)
                            {
                                Debug.WriteLine("Starting video audio playback");
                                _videoAudioWaveOut.Play();
                            }
                        }
                        else
                        {
                            // New video clip - initialize audio
                            Debug.WriteLine("Initializing new video audio stream");
                            StopVideoAudio();

                            try
                            {
                                _videoAudioReader = new AudioFileReader(videoClip.FilePath);
                                _videoAudioReader.CurrentTime = clipPos;

                                _videoAudioWaveOut = new WaveOutEvent();
                                _videoAudioWaveOut.Init(_videoAudioReader);

                                // Set volume
                                if (videoItem is TrackItem trackItem)
                                {
                                    _videoAudioWaveOut.Volume = trackItem.Volume;
                                    Debug.WriteLine($"Set video volume: {trackItem.Volume}");
                                }

                                // Start playback if project is playing
                                if (_project.IsPlaying)
                                {
                                    Debug.WriteLine("Starting video audio playback");
                                    _videoAudioWaveOut.Play();
                                }

                                _videoAudioPath = videoClip.FilePath;
                            }
                            catch (Exception ex)
                            {
                                Debug.WriteLine($"Error initializing video audio: {ex.Message}");
                                StopVideoAudio();
                            }
                        }
                    }
                }
                else
                {
                    // No video with audio, stop video audio
                    Debug.WriteLine("No video at current position, stopping video audio");
                    StopVideoAudio();
                }

                // Handle AUDIO TRACK ITEMS (independent of video audio)
                if (audioItem != null)
                {
                    Debug.WriteLine("Processing audio track item");

                    // Find the clip for this audio item
                    var clip = _project.Clips
                        .OfType<ProjectClip>()
                        .FirstOrDefault(c =>
                            audioItem is AudioTrackItem ati ? c.Id == ati.ClipId :
                            string.Equals(c.FilePath, audioItem.FilePath, StringComparison.OrdinalIgnoreCase));

                    if (clip == null || !File.Exists(clip.FilePath))
                    {
                        Debug.WriteLine("Audio file not found, stopping clip audio");
                        StopClipAudio();
                    }
                    else
                    {
                        // Compute position within that clip
                        double offsetFrames = frameIndex - audioItem.Position.TotalFrames + audioItem.Start.TotalFrames;
                        offsetFrames = Math.Max(0, offsetFrames);
                        TimeSpan clipPos = TimeSpan.FromSeconds(offsetFrames / _project.FPS);

                        Debug.WriteLine($"Audio position: {clipPos}");

                        // If same clip, just reposition / resume
                        if (_currentClipPath == clip.FilePath && _clipAudioReader != null)
                        {
                            if (Math.Abs((_clipAudioReader.CurrentTime - clipPos).TotalMilliseconds) > 100)
                            {
                                Debug.WriteLine($"Seeking audio to {clipPos}");
                                _clipAudioReader.CurrentTime = clipPos;
                            }

                            // Update volume
                            if (audioItem is AudioTrackItem audioTrackItem && _clipWaveOut != null)
                            {
                                _clipWaveOut.Volume = audioTrackItem.Volume;
                                Debug.WriteLine($"Updated audio track volume: {audioTrackItem.Volume}");
                            }

                            if (_project.IsPlaying && _clipWaveOut.PlaybackState != PlaybackState.Playing)
                            {
                                Debug.WriteLine("Starting audio playback");
                                _clipWaveOut.Play();
                            }
                        }
                        else
                        {
                            // New clip → restart
                            StopClipAudio();
                            try
                            {
                                _clipAudioReader = new AudioFileReader(clip.FilePath);
                                _clipAudioReader.CurrentTime = clipPos;
                                _clipWaveOut = new WaveOutEvent();
                                _clipWaveOut.Init(_clipAudioReader);

                                // Set volume
                                if (audioItem is AudioTrackItem audioTrackItem)
                                {
                                    _clipWaveOut.Volume = audioTrackItem.Volume;
                                    Debug.WriteLine($"Set audio track volume: {audioTrackItem.Volume}");
                                }

                                if (_project.IsPlaying)
                                {
                                    Debug.WriteLine("Starting audio track playback");
                                    _clipWaveOut.Play();
                                }
                                _currentClipPath = clip.FilePath;
                            }
                            catch (Exception ex)
                            {
                                Debug.WriteLine($"Error initializing audio: {ex.Message}");
                                StopClipAudio();
                            }
                        }
                    }
                }
                else
                {
                    // No audio track item, stop clip audio
                    Debug.WriteLine("No audio track item at current position, stopping clip audio");
                    StopClipAudio();
                }

                // Handle main audio as lowest priority
                if (audioItem == null && videoItem == null)
                {
                    Debug.WriteLine("No active clip audio, checking for main audio");

                    if (_mainWaveOut != null && _mainAudioStream != null)
                    {
                        var desired = _project.NeedlePositionTime.ToTimeSpan();

                        if (Math.Abs((_mainAudioStream.CurrentTime - desired).TotalMilliseconds) > 100)
                        {
                            Debug.WriteLine($"Seeking main audio to {desired}");
                            _mainAudioStream.CurrentTime = desired;
                        }

                        if (_project.IsPlaying && _mainWaveOut.PlaybackState != PlaybackState.Playing)
                        {
                            Debug.WriteLine("Playing main audio");
                            _mainWaveOut.Play();
                        }
                    }
                    else
                    {
                        Debug.WriteLine("No main audio available");
                    }
                }
                else if (_mainWaveOut != null && _mainWaveOut.PlaybackState == PlaybackState.Playing)
                {
                    // Pause main audio when clips are playing
                    Debug.WriteLine("Pausing main audio while clips are playing");
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

        private void StopClipAudio()
        {
            if (_clipWaveOut != null)
            {
                if (_clipWaveOut.PlaybackState == PlaybackState.Playing)
                    _clipWaveOut.Stop();
                _clipWaveOut.Dispose();
                _clipAudioReader.Dispose();
                _clipWaveOut = null;
                _clipAudioReader = null;
            }
            _currentClipPath = null;
        }

        public void Dispose()
        {
            Pause();
            _timer.Tick -= OnTick;
            StopClipAudio();
            StopVideoAudio();
            StopMainAudio();
        }
    }
}
