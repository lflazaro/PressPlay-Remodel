using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using NAudio.Wave;
using OpenCvSharp.WpfExtensions;
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

        public void LoadMedia(string path)
        {
            // 1) Stop+dispose any prior main audio
            StopMainAudio();

            // 2) Remember the path
            _mainAudioPath = path;

            // 3) Open the media’s built-in audio
            _mainAudioStream = new MediaFoundationReader(path);
            _mainWaveOut = new WaveOutEvent();
            _mainWaveOut.Init(_mainAudioStream);

            Debug.WriteLine($"[PlaybackService] Loaded main audio from: {path}");

            // 4) Reset the playhead to zero (this will also call UpdateAudio())
            Seek(new TimeCode(0, _project.FPS));
        }

        public void Play()
        {
            if (_timer.IsEnabled)
                return;

            // 1) Lazy‐init main audio if LoadMedia wasn't called
            if ((_mainAudioStream == null || _mainWaveOut == null)
                && !string.IsNullOrEmpty(_mainAudioPath))
            {
                try
                {
                    StopMainAudio();

                    _mainAudioStream = new MediaFoundationReader(_mainAudioPath);
                    _mainWaveOut = new WaveOutEvent();
                    _mainWaveOut.Init(_mainAudioStream);

                    Debug.WriteLine($"[PlaybackService] Lazy‐initialized main audio: {_mainAudioPath}");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[PlaybackService] Failed to lazy‐init audio: {ex}");
                }
            }

            // 2) If still no audio, log and continue (video will still play)
            if (_mainAudioStream == null || _mainWaveOut == null)
            {
                Debug.WriteLine("[PlaybackService] Play() called with no audio stream; skipping audio");
            }

            // Start the frame timer
            _playStartTimeUtc = DateTime.UtcNow;
            _startPosition = _project.NeedlePositionTime;
            _timer.Interval = TimeSpan.FromMilliseconds(1000.0 / _project.FPS);
            _timer.Start();
            _project.IsPlaying = true;

            // 3) Kick off audio if no clip is playing AND we have a main stream
            if (_clipWaveOut?.PlaybackState != PlaybackState.Playing
                && _mainAudioStream != null
                && _mainWaveOut.PlaybackState != PlaybackState.Playing)
            {
                try
                {
                    _mainAudioStream.CurrentTime = _project.NeedlePositionTime.ToTimeSpan();
                    _mainWaveOut.Play();
                    Debug.WriteLine("[PlaybackService] Main audio playback started");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[PlaybackService] Error starting audio: {ex}");
                }
            }
        }


        public void Pause()
        {
            if (!_timer.IsEnabled) return;
            _timer.Stop();
            _project.IsPlaying = false;

            _mainWaveOut?.Pause();
            _clipWaveOut?.Pause();
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
            int frameIndex = (int)Math.Round((double)_project.NeedlePositionTime.TotalFrames);

            // 1) look for a clip-based AudioTrackItem
            var audioTrack = _project.Tracks.FirstOrDefault(t => t.Type == TimelineTrackType.Audio);
            var audioItem = audioTrack?.Items
                                  .OfType<AudioTrackItem>()
                                  .FirstOrDefault(i =>
                                      i.Position.TotalFrames <= frameIndex &&
                                      frameIndex < i.Position.TotalFrames + i.Duration.TotalFrames);

            if (audioItem != null)
            {
                // stop main audio, play this clip
                StopMainAudio();

                var clip = _project.Clips
                           .OfType<ProjectClip>()
                           .FirstOrDefault(c => c.Id == audioItem.ClipId);
                if (clip == null || !File.Exists(clip.FilePath))
                {
                    StopClipAudio();
                    return;
                }

                // compute position within that clip
                double offsetFrames = frameIndex - audioItem.Position.TotalFrames + audioItem.Start.TotalFrames;
                if (offsetFrames < 0)
                    offsetFrames = 0;
                TimeSpan clipPos = TimeSpan.FromSeconds(offsetFrames / _project.FPS);

                // if same clip, just reposition / resume
                if (_currentClipPath == clip.FilePath && _clipAudioReader != null)
                {
                    if (Math.Abs((_clipAudioReader.CurrentTime - clipPos).TotalMilliseconds) > 100)
                        _clipAudioReader.CurrentTime = clipPos;
                    if (_project.IsPlaying && _clipWaveOut.PlaybackState != PlaybackState.Playing)
                        _clipWaveOut.Play();
                }
                else
                {
                    // new clip → restart
                    StopClipAudio();
                    try
                    {
                        _clipAudioReader = new AudioFileReader(clip.FilePath);
                        _clipAudioReader.CurrentTime = clipPos;
                        _clipWaveOut = new WaveOutEvent();
                        _clipWaveOut.Init(_clipAudioReader);
                        if (_project.IsPlaying)
                            _clipWaveOut.Play();
                        _currentClipPath = clip.FilePath;
                    }
                    catch
                    {
                        StopClipAudio();
                    }
                }
            }
            else
            {
                // no clip → stop clip playback, resume main audio
                StopClipAudio();

                if (_mainWaveOut != null)
                {
                    var desired = _project.NeedlePositionTime.ToTimeSpan();
                    if (Math.Abs((_mainAudioStream.CurrentTime - desired).TotalMilliseconds) > 100)
                        _mainAudioStream.CurrentTime = desired;
                    if (_project.IsPlaying && _mainWaveOut.PlaybackState != PlaybackState.Playing)
                        _mainWaveOut.Play();
                }
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
            StopMainAudio();
        }
    }
}
