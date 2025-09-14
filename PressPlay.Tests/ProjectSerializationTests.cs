using System.Text.Json.Nodes;
using PressPlay.Models;
using PressPlay.Serialization;
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
    public void KeyframesEnabled_Persists_WhenSerialized()
    {
        var project = new Project { FPS = 25 };
        var track = new Track { Name = "V1" };
        var item = new TrackItem
        {
            Position = new TimeCode(0, 25),
            Start = new TimeCode(0, 25),
            End = new TimeCode(5, 25),
            OriginalEnd = new TimeCode(5, 25),
            SourceLength = new TimeCode(5, 25),
            FileName = "clip.mp4",
            FilePath = "clip.mp4",
            FullPath = "clip.mp4",
            KeyframesEnabled = true
        };
        track.Items.Add(item);
        project.Tracks.Add(track);

        var json = ProjectSerializer.SerializeProject(project);
        var loaded = ProjectSerializer.DeserializeProject(json);
        var loadedItem = Assert.IsType<TrackItem>(loaded.Tracks[0].Items[0]);
        Assert.True(loadedItem.KeyframesEnabled);
    }

    [Fact]
    public void ColorCorrection_Properties_Persist()
    {
        var project = new Project { FPS = 25 };
        var track = new Track { Name = "V1" };
        var item = new TrackItem
        {
            Position = new TimeCode(0, 25),
            Start = new TimeCode(0, 25),
            End = new TimeCode(5, 25),
            OriginalEnd = new TimeCode(5, 25),
            SourceLength = new TimeCode(5, 25),
            FileName = "clip.mp4",
            FilePath = "clip.mp4",
            FullPath = "clip.mp4",
            Brightness = 10,
            Contrast = 1.5,
            Gamma = 0.8,
            Hue = 30,
            Saturation = 1.2
        };
        track.Items.Add(item);
        project.Tracks.Add(track);

        var json = ProjectSerializer.SerializeProject(project);
        var loaded = ProjectSerializer.DeserializeProject(json);
        var loadedItem = Assert.IsType<TrackItem>(loaded.Tracks[0].Items[0]);
        Assert.Equal(10, loadedItem.Brightness);
        Assert.Equal(1.5, loadedItem.Contrast);
        Assert.Equal(0.8, loadedItem.Gamma);
        Assert.Equal(30, loadedItem.Hue);
        Assert.Equal(1.2, loadedItem.Saturation);
    }
}
