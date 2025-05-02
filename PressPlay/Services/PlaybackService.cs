using System;
using System.Threading;
using System.Threading.Tasks;
using PressPlay.Models;  // your refactored QVM models

namespace PressPlay.Services
{
    public interface IPlaybackService
    {
        event Action<TimeSpan> PositionChanged;
        void Play();
        void Pause();
        void LoadMedia(string path);
        // optionally:
        void Seek(TimeCode time);
        void Rewind();
        void FastForward();
    }
    public class PlaybackService : IPlaybackService
    {
        private readonly Project _project;
        private CancellationTokenSource _cts;
        private bool _isPlaying;

        public event Action<TimeSpan> PositionChanged;

        public PlaybackService(Project project)
        {
            _project = project;
        }

        public void LoadMedia(string path)
        {
            // Implementation for loading media
            _project.CurrentMediaPath = path;
        }

        public void Play()
        {
            if (_isPlaying) return;
            _isPlaying = true;
            _project.IsPlaying = true;

            _cts = new CancellationTokenSource();
            Task.Run(async () =>
            {
                var start = DateTime.UtcNow;
                var position = _project.NeedlePositionTime.ToTimeSpan();
                var startOffset = position;

                while (!_cts.Token.IsCancellationRequested)
                {
                    var elapsed = DateTime.UtcNow - start + startOffset;
                    PositionChanged?.Invoke(elapsed);
                    await Task.Delay(40, _cts.Token); // Approximately 25 fps
                }
            }, _cts.Token);
        }

        public void Pause()
        {
            if (!_isPlaying) return;
            _cts?.Cancel();
            _isPlaying = false;
            _project.IsPlaying = false;
        }

        public void Seek(TimeCode time)
        {
            _project.NeedlePositionTime = time;
            PositionChanged?.Invoke(time.ToTimeSpan());
        }

        public void Rewind()
        {
            // Move back a few seconds
            var currentPosition = _project.NeedlePositionTime;
            var newPosition = Math.Max(0, currentPosition.TotalFrames - (int)(5 * _project.FPS));
            _project.NeedlePositionTime = new TimeCode(newPosition, _project.FPS);
            PositionChanged?.Invoke(_project.NeedlePositionTime.ToTimeSpan());
        }

        public void FastForward()
        {
            // Move forward a few seconds
            var currentPosition = _project.NeedlePositionTime;
            var newPosition = currentPosition.TotalFrames + (int)(5 * _project.FPS);
            _project.NeedlePositionTime = new TimeCode(newPosition, _project.FPS);
            PositionChanged?.Invoke(_project.NeedlePositionTime.ToTimeSpan());
        }
    }
}
