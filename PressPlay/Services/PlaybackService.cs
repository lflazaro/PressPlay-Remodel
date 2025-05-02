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

            // Create a more reliable timer
            _timer = new DispatcherTimer(DispatcherPriority.Render)
            {
                Interval = TimeSpan.FromMilliseconds(1000.0 / project.FPS)
            };
            _timer.Tick += OnTick;
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

            // Make sure we have a valid interval (recalculate in case FPS changed)
            _timer.Interval = TimeSpan.FromMilliseconds(1000.0 / _project.FPS);

            // Start the timer
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
            try
            {
                // Get current frame
                int currentFrame = _project.NeedlePositionTime.TotalFrames;

                // Advance by one frame
                int nextFrame = currentFrame + 1;

                // Check if we've reached the end
                double totalFrames = _project.GetTotalFrames();
                if (nextFrame > totalFrames - 1)
                {
                    Pause();
                    return;
                }

                // Update the time code directly without using Seek to avoid
                // potentially recursive calls or timer conflicts
                _project.NeedlePositionTime = new TimeCode(nextFrame, _project.FPS);

                // Render the frame
                RenderFrame();

                // Notify position change
                PositionChanged?.Invoke(_project.NeedlePositionTime.ToTimeSpan());
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in playback tick: {ex.Message}");
                // Continue playback despite errors
            }
        }

        private void RenderFrame()
        {
            // Get clip and offset
            var (clip, clipOffset) = _project.GetClipAtWithOffset(_project.NeedlePositionTime);

            if (clip != null)
            {
                // Use the clip offset to get the correct frame
                var bmp = clip.GetFrameAt(clipOffset);
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
