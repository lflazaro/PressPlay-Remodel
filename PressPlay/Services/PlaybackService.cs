using System;
using System.Windows.Controls;
using System.Windows.Threading;
using PressPlay.Models;
using OpenCvSharp.WpfExtensions;   // for Mat.ToBitmapSource()

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

        public event Action<TimeSpan> PositionChanged;

        public PlaybackService(Project project, Image previewControl)
        {
            _project = project ?? throw new ArgumentNullException(nameof(project));
            _previewControl = previewControl ?? throw new ArgumentNullException(nameof(previewControl));

            _timer = new DispatcherTimer(
                TimeSpan.FromSeconds(1.0 / _project.FPS),
                DispatcherPriority.Render,
                OnTick,
                Dispatcher.CurrentDispatcher);
        }

        /// <summary>
        /// Resets playhead to start. Your Project model already knows which clips are on the timeline.
        /// </summary>
        public void LoadMedia(string path)
        {
            // if you need to do anything with a standalone media file, do it here.
            // otherwise just reset to zero:
            Seek(new TimeCode(0, _project.FPS));
        }

        public void Play()
        {
            if (_timer.IsEnabled) return;
            _timer.Start();
        }

        public void Pause()
        {
            if (!_timer.IsEnabled) return;
            _timer.Stop();
        }

        /// <summary>
        /// Moves the playhead, clamps to [0, totalFrames], renders that frame, and notifies.
        /// </summary>
        public void Seek(TimeCode time)
        {
            // clamp
            double totalFrames = _project.GetTotalFrames();

            // make sure time.TotalFrames is treated as a double
            double desired = (double)time.TotalFrames;

            // clamp into valid range
            double target = Math.Max(0, Math.Min(desired, totalFrames));

            // now call the double overload unambiguously
            int frameCount = (int)Math.Round(target);

            _project.NeedlePositionTime = new TimeCode(frameCount, _project.FPS);

            RenderFrame();
            PositionChanged?.Invoke(_project.NeedlePositionTime.ToTimeSpan());
        }

        public void Rewind()
        {
            var delta = (int)(5 * _project.FPS);
            Seek(new TimeCode(_project.NeedlePositionTime.TotalFrames - delta, _project.FPS));
        }

        public void FastForward()
        {
            var delta = (int)(5 * _project.FPS);
            Seek(new TimeCode(_project.NeedlePositionTime.TotalFrames + delta, _project.FPS));
        }

        private void OnTick(object sender, EventArgs e)
        {
            // advance one frame
            Seek(_project.NeedlePositionTime.AddFrames(1));

            // if we hit the end, Seek will clamp and render, but let's also stop
            if (_project.NeedlePositionTime.TotalFrames >= _project.GetTotalFrames())
                Pause();
        }

        private void RenderFrame()
        {
            // TODO: Your Project class needs a method that, given a TimeCode,
            // returns the ProjectClip on the timeline at that time (or null).
            // I’ll call it GetClipAt(...) here, but implement that lookup in your Project.
            var clip = _project.GetClipAt(_project.NeedlePositionTime);
            if (clip != null)
            {
                var bmp = clip.GetFrameAt(_project.NeedlePositionTime.ToTimeSpan());
                _previewControl.Source = bmp;
            }
            else
            {
                _previewControl.Source = null;
            }
        }

        public void Dispose()
        {
            Pause();
            _timer.Tick -= OnTick;
        }
    }
}
