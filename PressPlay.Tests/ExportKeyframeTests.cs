using System;
using System.IO;
using System.Reflection;
using System.Drawing;
using Xunit;
using PressPlay.Export;
using PressPlay.Models;

namespace PressPlay.Tests
{
    public class ExportKeyframeTests
    {
        [Fact]
        public void RenderFrame_UpdatesTransformFromKeyframes()
        {
            // Create temporary image file used by the clip
            var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(tempDir);
            var imgPath = Path.Combine(tempDir, "img.png");

            using (var bmp = new Bitmap(2, 2))
            {
                bmp.SetPixel(0, 0, Color.White);
                bmp.Save(imgPath, System.Drawing.Imaging.ImageFormat.Png);
            }

            // Build a minimal project with one track and one item
            var project = new Project();
            project.SetProjectResolution(100, 100);

            var clip = new ProjectClip(imgPath) { TrackType = TimelineTrackType.Video, ItemType = TrackItemType.Image, FPS = 25 };
            clip.Length = new TimeCode(20, 25);
            project.Clips.Add(clip);

            var track = new Track { Type = TimelineTrackType.Video };
            var item = new TrackItem
            {
                FilePath = imgPath,
                Position = new TimeCode(0, 25),
                Start = new TimeCode(0, 25),
                End = new TimeCode(20, 25),
                OriginalEnd = new TimeCode(20, 25),
                KeyframesEnabled = true
            };

            item.TranslateXKeyframes.Add(new Keyframe { Frame = 0, Value = 0, Interpolation = "Linear" });
            item.TranslateXKeyframes.Add(new Keyframe { Frame = 10, Value = 100, Interpolation = "Linear" });

            track.Items.Add(item);
            project.Tracks.Add(track);

            var exportService = new ExportService(project);

            // Invoke the private RenderFrame method via reflection at frame 5
            var method = typeof(ExportService).GetMethod("RenderFrame", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(method);
            var bmpResult = method.Invoke(exportService, new object[] { new TimeCode(5, 25), 100, 100 }) as Bitmap;
            bmpResult?.Dispose();

            // After rendering, the item's TranslateX should reflect interpolated keyframe value
            Assert.Equal(50, item.TranslateX);
        }
    }
}

