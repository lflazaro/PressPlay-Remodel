// PressPlay/Export/ExportService.cs
using FFMpegCore;
using FFMpegCore.Enums;
using FFMpegCore.Pipes;
using FFMpegCore.Extensions.System.Drawing.Common;
using OpenCvSharp;
using PressPlay.Helpers;
using PressPlay.Models;
using PressPlay.Utilities;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using Size = System.Drawing.Size;
using Color = System.Windows.Media.Color;
using Rect = System.Windows.Rect;
using Brushes = System.Windows.Media.Brushes;
using DocumentFormat.OpenXml.Drawing.Charts;
using FFMpegCore.Pipes;


namespace PressPlay.Export
{
    /// <summary>
    /// Service responsible for exporting timeline content to video files
    /// </summary>
    public class ExportService
    {
        private readonly Project _project;
        private CancellationTokenSource _cancellationTokenSource;
        private ExportSettings _settings;
        private string _tempFolder;
        private bool _isExporting = false;

        // Events
        public event EventHandler<ExportProgressEventArgs> ProgressChanged;
        public event EventHandler<ExportCompletedEventArgs> ExportCompleted;

        public bool IsExporting => _isExporting;

        /// <summary>
        /// Creates a new export service for the specified project
        /// </summary>
        public ExportService(Project project)
        {
            _project = project ?? throw new ArgumentNullException(nameof(project));
            _tempFolder = Path.Combine(Path.GetTempPath(), "PressPlay", "Export", Guid.NewGuid().ToString());
        }

        /// <summary>
        /// Starts the export process with the specified settings
        /// </summary>
        public async Task<bool> StartExportAsync(ExportSettings settings)
        {
            if (_isExporting)
                throw new InvalidOperationException("An export is already in progress");

            if (_project == null || settings == null)
                return false;

            _settings = settings;
            _cancellationTokenSource = new CancellationTokenSource();
            _isExporting = true;

            try
            {
                // Create temp directory
                Directory.CreateDirectory(_tempFolder);

                // Start the export process
                var result = await Task.Run(() => ExportProjectAsync(_cancellationTokenSource.Token));

                // Raise completed event
                OnExportCompleted(new ExportCompletedEventArgs(result, settings.OutputPath, null));

                return result;
            }
            catch (OperationCanceledException)
            {
                OnExportCompleted(new ExportCompletedEventArgs(false, settings.OutputPath, "Export was cancelled"));
                return false;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Export error: {ex.Message}");
                Debug.WriteLine(ex.StackTrace);
                OnExportCompleted(new ExportCompletedEventArgs(false, settings.OutputPath, ex.Message));
                return false;
            }
            finally
            {
                _isExporting = false;
                CleanupTempFiles();
            }
        }

        /// <summary>
        /// Cancels the current export process
        /// </summary>
        public void CancelExport()
        {
            _cancellationTokenSource?.Cancel();
        }

        /// <summary>
        /// Main export process
        /// </summary>
        private async Task<bool> ExportProjectAsync(CancellationToken cancellationToken)
        {
            // Calculate project parameters
            double fps = _project.FPS;
            int totalFrames = (int)_project.GetTotalFrames();
            int frameWidth = _settings.Width > 0 ? _settings.Width : _project.ProjectWidth;
            int frameHeight = _settings.Height > 0 ? _settings.Height : _project.ProjectHeight;

            // Ensure valid dimensions
            if (frameWidth <= 0 || frameHeight <= 0)
            {
                frameWidth = 1920;
                frameHeight = 1080;
            }

            // Create output directories
            Directory.CreateDirectory(Path.GetDirectoryName(_settings.OutputPath));
            Directory.CreateDirectory(_tempFolder);

            // Setup progress tracking
            int processedFrames = 0;

            // Setup frame and audio processing
            var framePipeSource = new MultithreadedFramePipeSource(fps, totalFrames);
            string audioFile = Path.Combine(_tempFolder, "audio.wav");

            // Start background threads for frame processing
            var frameRenderTask = Task.Run(() => ProcessFramesAsync(framePipeSource, frameWidth, frameHeight, totalFrames, cancellationToken));
            var audioRenderTask = Task.Run(() => RenderAudioAsync(audioFile, fps, totalFrames, cancellationToken));

            // Wait for rendering tasks to complete
            await frameRenderTask;
            await audioRenderTask;

            // Check for cancellation
            cancellationToken.ThrowIfCancellationRequested();

            // Set FFmpeg arguments based on export settings
            var ffmpegArgs = BuildFFmpegArguments(_settings);

            OnProgressChanged(new ExportProgressEventArgs(0.8, "Encoding video with FFmpeg…"));

            // 1) local aliases
            int width = frameWidth;
            int height = frameHeight;

            // 2) Build an IEnumerable<IVideoFrame> that yields each System.Drawing.Bitmap
            IEnumerable<IVideoFrame> GenerateFrames()
            {
                for (int i = 0; i < totalFrames; i++)
                {
                    using var bmp = RenderFrame(new TimeCode(i, fps), frameWidth, frameHeight);
                    yield return new BitmapVideoFrameWrapper(bmp);
                }
            }

            // 2) Create the RawVideoPipeSource (it will auto-read width/height from the first frame)
            var videoSource = new RawVideoPipeSource(GenerateFrames())
            {
                FrameRate = (int)fps
            };

            // 3) Fire off FFmpeg with your video frames + audio
            bool success = await FFMpegArguments
                .FromPipeInput(videoSource)
                .AddFileInput(audioFile)              // your rendered WAV
                .OutputToFile(
                    _settings.OutputPath,
                    false,
                    opts =>
                    {
                        // **1a) Make sure FFmpeg maps video from the pipe and audio from the WAV**
                        opts.WithCustomArgument("-map 0:v:0")
                            .WithCustomArgument("-map 1:a:0")
                            .WithCustomArgument("-shortest");

                        // **1b) Then apply all your normal codec / bitrate / filter flags**
                        ffmpegArgs(opts);
                    }
                )
                .ProcessAsynchronously();

            return success;
        }

        /// <summary>
        /// Processes all frames in the timeline and adds them to the frame pipe
        /// </summary>
        private async Task ProcessFramesAsync(MultithreadedFramePipeSource framePipeSource, int width, int height, int totalFrames, CancellationToken cancellationToken)
        {
            int processedFrames = 0;

            // Render each frame
            for (int frameIndex = 0; frameIndex < totalFrames; frameIndex++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // Update progress every few frames
                if (frameIndex % 10 == 0)
                {
                    double progress = (double)frameIndex / totalFrames * 0.7; // 70% of progress is frame rendering
                    OnProgressChanged(new ExportProgressEventArgs(progress, $"Rendering frame {frameIndex}/{totalFrames}"));
                }

                // Convert frame index to TimeCode
                var timeCode = new TimeCode(frameIndex, _project.FPS);

                // Render frame
                using (var frameBitmap = RenderFrame(timeCode, width, height))
                {
                    if (frameBitmap != null)
                    {
                        // Convert to bytes
                        byte[] frameBytes;
                        using (var memoryStream = new MemoryStream())
                        {
                            frameBitmap.Save(memoryStream, System.Drawing.Imaging.ImageFormat.Png);
                            frameBytes = memoryStream.ToArray();
                        }

                        // Add to pipe source
                        var frameContainer = new FrameContainer(frameIndex, frameBytes);
                        framePipeSource.AddFrame(frameContainer);

                        // Update counter
                        processedFrames++;
                    }
                }

                // Optional: Add some delay to prevent thread starvation
                if (frameIndex % 10 == 0)
                    await Task.Delay(1);
            }

            // Mark the pipe as finished
            framePipeSource.IsFinished = true;

            OnProgressChanged(new ExportProgressEventArgs(0.7, $"All frames rendered: {processedFrames}/{totalFrames}"));
        }

        /// <summary>
        /// Renders a single frame of the timeline at the specified position
        /// </summary>
        private Bitmap RenderFrame(TimeCode position, int width, int height)
        {
            // Create DrawingVisual to render the frame
            var drawingVisual = new DrawingVisual();

            using (var drawingContext = drawingVisual.RenderOpen())
            {
                // Set background color
                drawingContext.DrawRectangle(
                    new SolidColorBrush(Colors.Black),
                    null,
                    new Rect(0, 0, width, height));

                // Get frame index
                int idx = position.TotalFrames;

                // Draw each track in reverse order (bottom to top)
                foreach (var track in _project.Tracks.OfType<Track>().Reverse())
                {
                    if (track.Type == TimelineTrackType.Audio)
                        continue; // Skip audio tracks for video rendering

                    // Get all items active at this frame
                    var activeItems = track.Items
                        .Where(i =>
                            i.Position.TotalFrames <= idx &&
                            idx < i.Position.TotalFrames + i.Duration.TotalFrames)
                        .OrderBy(i => i.Position.TotalFrames);

                    // Draw each item
                    foreach (var item in activeItems)
                    {
                        DrawItemWithFade(item, idx, width, height, drawingContext);
                    }
                }
            }

            // Render the visual to a bitmap
            var renderBitmap = new RenderTargetBitmap(
                width, height, 96, 96, PixelFormats.Pbgra32);
            renderBitmap.Render(drawingVisual);

            // Convert to System.Drawing.Bitmap
            Bitmap bitmap;
            using (var outStream = new MemoryStream())
            {
                // Encode to PNG
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(renderBitmap));
                encoder.Save(outStream);
                outStream.Flush();
                outStream.Position = 0;

                // Create bitmap from stream
                bitmap = new Bitmap(outStream);
            }

            return bitmap;
        }

        /// <summary>
        /// Draws a timeline item with fade effects
        /// </summary>
        private void DrawItemWithFade(ITrackItem item, int idx, int width, int height, DrawingContext dc)
        {
            // Find the clip behind this track-item
            var clip = _project.Clips.FirstOrDefault(c =>
                string.Equals(c.FilePath, item.FilePath, StringComparison.OrdinalIgnoreCase) ||
                c.Id == (item as AudioTrackItem)?.ClipId);

            if (clip == null) return;

            // Figure out which frame of the media to draw
            int clipFrame = idx - item.Position.TotalFrames + item.Start.TotalFrames;
            clipFrame = Math.Clamp(clipFrame, 0, (int)clip.Length.TotalFrames - 1);
            var ts = TimeSpan.FromSeconds(clipFrame / clip.FPS);

            // Get the frame
            var bmp = clip.GetFrameAt(ts);

            // Compute fade parameters
            double framePos = idx - item.Position.TotalFrames;
            double dur = item.Duration.TotalFrames;
            double fadeInF = item.FadeInFrame;
            double fadeOutF = item.FadeOutFrame;
            double fadeOutStart = dur - fadeOutF;

            if (item is TrackItem ti)
            {
                // Apply global clip opacity
                dc.PushOpacity(ti.Opacity);

                // Apply transformation (translate, scale, rotate)
                var tx = new TranslateTransform(ti.TranslateX, ti.TranslateY);
                var sc = new ScaleTransform(ti.ScaleX, ti.ScaleY, width * 0.5, height * 0.5);
                var rt = new RotateTransform(ti.Rotation, width * 0.5, height * 0.5);
                var group = new TransformGroup();
                group.Children.Add(tx);
                group.Children.Add(sc);
                group.Children.Add(rt);

                dc.PushTransform(group);
            }

            // Apply appropriate fade effect based on fade color
            if (item.FadeColor == Track.FadeColor.White)
            {
                // Draw the clip at full opacity
                dc.DrawImage(bmp, new Rect(0, 0, width, height));

                // Overlay a white quad whose alpha goes 1→0 then 0→1
                double overlayOpacity = 0;

                // Fade-in region
                if (fadeInF > 0 && framePos < fadeInF)
                    overlayOpacity = 1 - (framePos / fadeInF);

                // Fade-out region (inclusive start)
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

                // Fade-in region
                if (fadeInF > 0 && framePos < fadeInF)
                    opacity = framePos / fadeInF;

                // Fade-out region (inclusive start)
                if (fadeOutF > 0 && framePos >= fadeOutStart)
                    opacity = Math.Min(opacity, (dur - framePos) / fadeOutF);

                dc.PushOpacity(opacity);
                dc.DrawImage(bmp, new Rect(0, 0, width, height));
                dc.Pop();
            }

            // Pop transforms if applied
            if (item is TrackItem)
            {
                dc.Pop();  // pop TransformGroup
                dc.Pop();  // pop Opacity
            }
        }

        /// <summary>
        /// Renders the audio track for the timeline
        /// </summary>
        private async Task RenderAudioAsync(string outputAudioFile, double fps, int totalFrames, CancellationToken cancellationToken)
        {
            // Calculate total duration
            TimeSpan totalDuration = TimeSpan.FromSeconds(totalFrames / fps);

            // Create a mixer for all audio tracks
            var mixer = new MixingSampleProvider(WaveFormat.CreateIeeeFloatWaveFormat(44100, 2));

            // Keep track of all readers so we can dispose them later
            var readers = new List<AudioFileReader>();

            // Process audio tracks
            foreach (var track in _project.Tracks.Where(t => t.Type == TimelineTrackType.Audio))
            {
                // Get active audio items
                foreach (var item in track.Items.OfType<AudioTrackItem>())
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    // Get the source clip
                    var clip = _project.Clips
                        .FirstOrDefault(c => c.Id == item.ClipId ||
                                        string.Equals(c.FilePath, item.FilePath, StringComparison.OrdinalIgnoreCase));

                    if (clip == null || !File.Exists(clip.FilePath)) continue;

                    try
                    {
                        // Create audio reader
                        var reader = new AudioFileReader(clip.FilePath);
                        readers.Add(reader);

                        // Position in timeline
                        var startTime = TimeSpan.FromSeconds(item.Position.TotalFrames / fps);
                        var clipOffset = TimeSpan.FromSeconds(item.Start.TotalFrames / fps);

                        // Calculate fade values in seconds
                        double fadeInSeconds = item.FadeInFrame / fps;
                        double fadeOutSeconds = item.FadeOutFrame / fps;

                        // Set reader position
                        reader.CurrentTime = clipOffset;

                        // Apply volume
                        var volumeProvider = new VolumeSampleProvider(reader.ToSampleProvider());
                        volumeProvider.Volume = item.Volume;

                        // Apply fade in/out
                        var fader = new FadeInOutSampleProvider(volumeProvider);

                        if (fadeInSeconds > 0)
                            fader.BeginFadeIn(fadeInSeconds);

                        if (fadeOutSeconds > 0)
                        {
                            double clipDurationSeconds = item.Duration.TotalSeconds;
                            fader.BeginFadeOut(clipDurationSeconds - fadeOutSeconds);
                        }

                        // Add to mixer with delay
                        var delayProvider = new OffsetSampleProvider(fader);
                        delayProvider.DelayBy = startTime;
                        delayProvider.Take = TimeSpan.FromSeconds(item.Duration.TotalSeconds);

                        mixer.AddMixerInput(delayProvider);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error processing audio clip {clip.FileName}: {ex.Message}");
                    }
                }
            }

            // Process video tracks with audio
            foreach (var track in _project.Tracks.Where(t => t.Type == TimelineTrackType.Video))
            {
                foreach (var item in track.Items.OfType<TrackItem>())
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    // Get the source clip
                    var clip = _project.Clips
                        .FirstOrDefault(c => string.Equals(c.FilePath, item.FilePath, StringComparison.OrdinalIgnoreCase));

                    if (clip == null || !File.Exists(clip.FilePath) || !clip.HasAudio) continue;

                    try
                    {
                        // Create audio reader
                        var reader = new AudioFileReader(clip.FilePath);
                        readers.Add(reader);

                        // Position in timeline
                        var startTime = TimeSpan.FromSeconds(item.Position.TotalFrames / fps);
                        var clipOffset = TimeSpan.FromSeconds(item.Start.TotalFrames / fps);

                        // Calculate fade values in seconds
                        double fadeInSeconds = item.FadeInFrame / fps;
                        double fadeOutSeconds = item.FadeOutFrame / fps;

                        // Set reader position
                        reader.CurrentTime = clipOffset;

                        // Apply volume
                        var volumeProvider = new VolumeSampleProvider(reader.ToSampleProvider());
                        volumeProvider.Volume = item.Volume;

                        // Apply fade in/out
                        var fader = new FadeInOutSampleProvider(volumeProvider);

                        if (fadeInSeconds > 0)
                            fader.BeginFadeIn(fadeInSeconds);

                        if (fadeOutSeconds > 0)
                        {
                            double clipDurationSeconds = item.Duration.TotalSeconds;
                            fader.BeginFadeOut(clipDurationSeconds - fadeOutSeconds);
                        }

                        // Add to mixer with delay
                        var delayProvider = new OffsetSampleProvider(fader);
                        delayProvider.DelayBy = startTime;
                        delayProvider.Take = TimeSpan.FromSeconds(item.Duration.TotalSeconds);

                        mixer.AddMixerInput(delayProvider);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error processing video audio clip {clip.FileName}: {ex.Message}");
                    }
                }
            }

            // If we have audio to mix
            if (mixer.MixerInputs.Any())
            {
                OnProgressChanged(new ExportProgressEventArgs(0.75, "Rendering audio..."));

                Directory.CreateDirectory(Path.GetDirectoryName(outputAudioFile));
                // this will pull from your float‐based mixer and produce a valid 16-bit PCM WAV
                WaveFileWriter.CreateWaveFile16(outputAudioFile, mixer);

                OnProgressChanged(new ExportProgressEventArgs(0.8, "Audio rendered successfully"));
            }
            else
            {
                // Create silent audio without using SilenceProvider
                OnProgressChanged(new ExportProgressEventArgs(0.75, "Creating silent audio..."));

                // Create silent WAV file manually
                using (var writer = new WaveFileWriter(outputAudioFile, new WaveFormat(44100, 16, 2)))
                {
                    // Calculate total bytes for silent audio
                    long totalBytes = (long)(totalDuration.TotalSeconds * 44100 * 2 * 2); // 2 channels, 2 bytes per sample

                    // Create a buffer of zeros (silence)
                    byte[] silenceBuffer = new byte[Math.Min(1024 * 1024, totalBytes)]; // 1MB or smaller

                    // Write silence to file in chunks
                    long remainingBytes = totalBytes;
                    while (remainingBytes > 0)
                    {
                        int bytesToWrite = (int)Math.Min(silenceBuffer.Length, remainingBytes);
                        writer.Write(silenceBuffer, 0, bytesToWrite);
                        remainingBytes -= bytesToWrite;
                    }
                }

                OnProgressChanged(new ExportProgressEventArgs(0.8, "Silent audio created"));
            }

            // Clean up
            foreach (var reader in readers)
            {
                reader.Dispose();
            }
        }

        /// <summary>
        /// Build FFmpeg arguments based on export settings
        /// </summary>
        private Action<FFMpegArgumentOptions> BuildFFmpegArguments(ExportSettings settings)
        {
            return options =>
            {
                // Set video codec
                switch (settings.VideoCodec)
                {
                    case VideoCodec.H264:
                        options.WithVideoCodec("libx264")
                               // a reasonable preset – tradeoff speed vs. filesize
                               .WithCustomArgument("-preset medium")
                               // baseline or main profile for maximum compatibility
                               .WithCustomArgument("-profile:v baseline")
                               .WithCustomArgument("-level 3.1")
                               // ensure chroma is 4:2:0 – every player supports this
                               .WithCustomArgument("-pix_fmt yuv420p")
                               // put the moov atom at the front of the file for “progressive download”
                               .WithCustomArgument("-movflags +faststart");
                        break;
                    case VideoCodec.H265:
                        options.WithVideoCodec("libx265");
                        break;
                    case VideoCodec.VP9:
                        options.WithVideoCodec("libvpx-vp9");
                        break;
                    case VideoCodec.ProRes:
                        options.WithVideoCodec("prores_ks");
                        options.WithCustomArgument("-profile:v 3"); // ProRes 422 HQ
                        break;
                    default:
                        options.WithVideoCodec("libx264")
                               // a reasonable preset – tradeoff speed vs. filesize
                               .WithCustomArgument("-preset medium")
                               // baseline or main profile for maximum compatibility
                               .WithCustomArgument("-profile:v baseline")
                               .WithCustomArgument("-level 3.1")
                               // ensure chroma is 4:2:0 – every player supports this
                               .WithCustomArgument("-pix_fmt yuv420p")
                               // put the moov atom at the front of the file for “progressive download”
                               .WithCustomArgument("-movflags +faststart");
                        break;
                }

                // Set bitrate or quality based on preset
                switch (settings.VideoQuality)
                {
                    case VideoQuality.Low:
                        options.WithVideoBitrate(2000);
                        break;
                    case VideoQuality.Medium:
                        options.WithVideoBitrate(5000);
                        break;
                    case VideoQuality.High:
                        options.WithVideoBitrate(10000);
                        break;
                    case VideoQuality.Ultra:
                        options.WithVideoBitrate(20000);
                        break;
                    case VideoQuality.Custom:
                        options.WithVideoBitrate(settings.VideoBitrate);
                        break;
                }

                // Set framerate
                options.WithFramerate(_project.FPS);

                // Set output size
                options.WithVideoFilters(filterOptions => {
                    // Check if the ffmpeg version has different filter methods
                    // Try direct filter string if methods are unavailable
                    try
                    {
                        // Try using Scale method if available
                        filterOptions.Scale(settings.Width, settings.Height);
                    }
                    catch
                    {
                        // Fallback: Add filter as a raw string
                        options.WithCustomArgument($"-vf scale={settings.Width}:{settings.Height}");
                    }
                });
                // Set audio codec
                options.WithAudioCodec(AudioCodec.Aac)
                       .WithCustomArgument("-ar 44100")
                       .WithCustomArgument("-ac 2");

                // (you can still set bitrate normally)
                options.WithAudioBitrate(settings.AudioBitrate);

                // Special handling for GIF export
                if (settings.OutputFormat == OutputFormat.GIF)
                {
                    // Replace video filters with direct custom argument for GIF
                    // Remove previous scale filter
                    options.WithCustomArgument($"-vf \"scale={settings.Width}:{settings.Height},split [a][b];[a] palettegen [p];[b][p] paletteuse\"");
                }

                // Add any additional custom arguments
                if (!string.IsNullOrEmpty(settings.CustomFFmpegArgs))
                {
                    options.WithCustomArgument(settings.CustomFFmpegArgs);
                }
            };
        }

        /// <summary>
        /// Clean up temporary files created during export
        /// </summary>
        private void CleanupTempFiles()
        {
            try
            {
                if (Directory.Exists(_tempFolder))
                {
                    Directory.Delete(_tempFolder, true);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error cleaning up temp files: {ex.Message}");
            }
        }

        /// <summary>
        /// Raise the progress changed event
        /// </summary>
        protected virtual void OnProgressChanged(ExportProgressEventArgs e)
        {
            ProgressChanged?.Invoke(this, e);
        }

        /// <summary>
        /// Raise the export completed event
        /// </summary>
        protected virtual void OnExportCompleted(ExportCompletedEventArgs e)
        {
            ExportCompleted?.Invoke(this, e);
        }
    }

    /// <summary>
    /// Event arguments for export progress updates
    /// </summary>
    public class ExportProgressEventArgs : EventArgs
    {
        /// <summary>
        /// Progress value from 0.0 to 1.0
        /// </summary>
        public double Progress { get; }

        /// <summary>
        /// Current status message
        /// </summary>
        public string StatusMessage { get; }

        public ExportProgressEventArgs(double progress, string statusMessage)
        {
            Progress = progress;
            StatusMessage = statusMessage;
        }
    }

    /// <summary>
    /// Event arguments for export completion
    /// </summary>
    public class ExportCompletedEventArgs : EventArgs
    {
        /// <summary>
        /// Whether the export was successful
        /// </summary>
        public bool Success { get; }

        /// <summary>
        /// Path to the exported file
        /// </summary>
        public string OutputPath { get; }

        /// <summary>
        /// Error message if export failed
        /// </summary>
        public string ErrorMessage { get; }

        public ExportCompletedEventArgs(bool success, string outputPath, string errorMessage)
        {
            Success = success;
            OutputPath = outputPath;
            ErrorMessage = errorMessage;
        }
    }
}