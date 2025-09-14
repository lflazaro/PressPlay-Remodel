using System.Text.Json.Nodes;
using PressPlay.Models;
using PressPlay.Serialization;
using PressPlay.Effects;
using Xunit;

namespace PressPlay.Tests;

public class ProjectSerializationTests
{
    private static string RemoveKeyframes(string json)
    {
        var node = JsonNode.Parse(json)!;
        var item = node["Tracks"]?[0]?["Items"]?[0] as JsonObject;
        item?.Remove("Keyframes");
        return node.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
    }

    [Fact]
    public void Deserializing_OldProject_WithoutKeyframes_UsesStaticValues()
    {
        var project = new Project { FPS = 25 };
        var track = new Track { Name = "V1" };
        var item = new TrackItem
        {
            Position = new TimeCode(0, 25),
            Start = new TimeCode(0, 25),
            End = new TimeCode(10, 25),
            OriginalEnd = new TimeCode(10, 25),
            SourceLength = new TimeCode(10, 25),
            FileName = "clip.mp4",
            FilePath = "clip.mp4",
            FullPath = "clip.mp4",
            TranslateX = 12.5,
            TranslateY = -3.0,
            ScaleX = 1.5,
            ScaleY = 0.75,
            Rotation = 45.0,
            Opacity = 0.8,
            Volume = 0.9f
        };
        track.Items.Add(item);
        project.Tracks.Add(track);

        var json = ProjectSerializer.SerializeProject(project);
        json = RemoveKeyframes(json);

        var loaded = ProjectSerializer.DeserializeProject(json);
        var loadedItem = Assert.IsType<TrackItem>(loaded.Tracks[0].Items[0]);

        Assert.Equal(12.5, loadedItem.TranslateX);
        Assert.Equal(-3.0, loadedItem.TranslateY);
        Assert.Equal(1.5, loadedItem.ScaleX);
        Assert.Equal(0.75, loadedItem.ScaleY);
        Assert.Equal(45.0, loadedItem.Rotation);
        Assert.Equal(0.8, loadedItem.Opacity);
        Assert.Empty(loadedItem.Keyframes[nameof(TrackItem.TranslateX)]);

        loadedItem.EvaluateKeyframes(0);
        Assert.Equal(12.5, loadedItem.TranslateX);
    }

    [Fact]
    public void ColorCorrectionEffect_IsSerializedWithSliderValues()
    {
        var project = new Project { FPS = 25 };

        // Create a clip and a track item referencing it
        var clip = new ProjectClip { FilePath = "clip.mp4" };
        project.Clips.Add(clip);

        var track = new Track { Name = "V1" };
        var item = new TrackItem
        {
            FilePath = "clip.mp4",
            Start = new TimeCode(0, 25),
            End = new TimeCode(10, 25),
            OriginalEnd = new TimeCode(10, 25),
            SourceLength = new TimeCode(10, 25)
        };

        item.Effects.Add(new ColorCorrectionEffect
        {
            Brightness = 10,
            Contrast = 1.5,
            Saturation = 0.5
        });

        track.Items.Add(item);
        project.Tracks.Add(track);

        var json = ProjectSerializer.SerializeProject(project);
        var loaded = ProjectSerializer.DeserializeProject(json);

        var loadedItem = Assert.IsType<TrackItem>(loaded.Tracks[0].Items[0]);
        var cc = Assert.IsType<ColorCorrectionEffect>(Assert.Single(loadedItem.Effects));
        Assert.Equal(10, cc.Brightness);
        Assert.Equal(1.5, cc.Contrast);
        Assert.Equal(0.5, cc.Saturation);
    }
}
