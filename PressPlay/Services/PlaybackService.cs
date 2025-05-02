using System;
using System.Windows.Controls;
using System.Windows.Threading;
using PressPlay.Models;
using OpenCvSharp.WpfExtensions;
using System.Windows.Media.Imaging;
using System.Windows.Media;
using System.Windows;   // for Mat.ToBitmapSource()

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
        private DateTime _playStartTimeUtc;
        private TimeCode _startPosition;


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

            // reset or continue from current needle
            _playStartTimeUtc = DateTime.UtcNow;
            _startPosition = _project.NeedlePositionTime;

            // ensure interval matches FPS
            _timer.Interval = TimeSpan.FromMilliseconds(1000.0 / _project.FPS);
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
            // elapsed wall-clock time since Play()
            var elapsed = DateTime.UtcNow - _playStartTimeUtc;

            // compute the desired total frames offset from the start
            double desiredTotalFrames = _startPosition.TotalFrames
                                        + elapsed.TotalSeconds * _project.FPS;

            // clamp to [0, lastFrame]
            double maxFrames = _project.GetTotalFrames();
            if (desiredTotalFrames >= maxFrames)
            {
                // compute and render the very last frame, then stop
                int lastFrame = (int)Math.Ceiling(maxFrames) - 1;
                var finalTime = new TimeCode(lastFrame, _project.FPS);
                Seek(finalTime);
                Pause();
                return;
            }

            // jump the needle there
            var frameIndex = (int)Math.Round(desiredTotalFrames);
            var newTime = new TimeCode(frameIndex, _project.FPS);
            _project.NeedlePositionTime = newTime;

            // draw it and notify
            RenderFrame();
            PositionChanged?.Invoke(newTime.ToTimeSpan());
        }

        private void RenderFrame()
        {
            if (_project.ProjectWidth == 0 || _project.ProjectHeight == 0)
            {
                _previewControl.Source = null;
                return;
            }

            int width = _project.ProjectWidth;
            int height = _project.ProjectHeight;
            int frameIndex = (int)Math.Round((double)_project.NeedlePositionTime.TotalFrames);

            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                // 1) Paint a solid black background
                dc.DrawRectangle(Brushes.Black, null, new Rect(0, 0, width, height));

                // 2) Draw each track in reverse order (bottom track first,
                //    top track last, so overlays actually show up)
                foreach (var track in _project.Tracks.OfType<Track>().Reverse())
                {
                    // find the item on this track at the current frame
                    var item = track.Items.FirstOrDefault(i =>
                        i.Position.TotalFrames <= frameIndex &&
                        frameIndex < i.Position.TotalFrames + i.Duration.TotalFrames);

                    if (item == null)
                        continue;

                    // match it back to your ProjectClip
                    var clip = _project.Clips.FirstOrDefault(c =>
                        string.Equals(c.FilePath, item.FilePath, StringComparison.OrdinalIgnoreCase) ||
                        c.Id == (item as AudioTrackItem)?.ClipId);
                    if (clip == null)
                        continue;

                    // compute in-clip frame offset
                    int clipFrame = frameIndex
                                    - item.Position.TotalFrames
                                    + item.Start.TotalFrames;
                    clipFrame = Math.Clamp(clipFrame, 0, (int)clip.Length.TotalFrames - 1);
                    var clipOffset = TimeSpan.FromSeconds(clipFrame / clip.FPS);

                    // grab the bitmap for that frame
                    var bmp = clip.GetFrameAt(clipOffset);

                    // draw it full-frame (it will respect PNG alpha)
                    dc.DrawImage(bmp, new Rect(0, 0, width, height));
                }
            }

            // push the composite to your Image control
            var rtb = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(dv);
            _previewControl.Source = rtb;
        }

        public void Dispose()
        {
            Pause();
            _timer.Tick -= OnTick;
        }
    }
}
