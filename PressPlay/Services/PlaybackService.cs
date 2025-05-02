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
    public class PlaybackService
    {
        private readonly Project _project;
        private CancellationTokenSource _cts;
        private bool _isPlaying;

        public PlaybackService(Project project)
        {
            _project = project;
        }

        public void Play()
        {
            if (_isPlaying) return;
            _isPlaying = true;
            _project.RaisePlaybackStarted();

            _cts = new CancellationTokenSource();
            Task.Run(async () =>
            {
                var start = DateTime.UtcNow;
                while (!_cts.Token.IsCancellationRequested)
                {
                    var elapsed = DateTime.UtcNow - start;
                    _project.Seek(TimeCode.FromSeconds(elapsed.TotalSeconds));
                    await Task.Delay(_project.FrameInterval, _cts.Token);
                }
            }, _cts.Token);
        }

        public void Pause()
        {
            if (!_isPlaying) return;
            _cts.Cancel();
            _isPlaying = false;
            _project.RaisePlaybackPaused();
        }

        public void Seek(TimeCode time)
        {
            _project.Seek(time);
        }
    }
}
