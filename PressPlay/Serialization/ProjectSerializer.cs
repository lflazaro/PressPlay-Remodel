using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Serialization;
using PressPlay.Models;
using PressPlay.Effects;
using JsonSerializer = System.Text.Json.JsonSerializer;
using JsonException = System.Text.Json.JsonException;
using JsonConverter = Newtonsoft.Json.JsonConverter;

namespace PressPlay.Serialization
{
    /// <summary>
    /// JSON serializer for PressPlay projects using System.Text.Json.
    /// </summary>
    public static class ProjectSerializer
    {
        public static string SerializeProject(Project project)
        {
            try
            {
                var options = CreateSerializerOptions();
                return JsonSerializer.Serialize(project, options);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error serializing project: {ex.Message}");
                throw;
            }
        }

        public static Project DeserializeProject(string json)
        {
            try
            {
                var options = CreateSerializerOptions();
                var project = JsonSerializer.Deserialize<Project>(json, options);
                PostProcessProject(project);
                return project;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error deserializing project: {ex.Message}");
                Debug.WriteLine(ex.StackTrace);
                throw;
            }
        }

        public static void SaveProjectToFile(Project project, string filePath)
        {
            var json = SerializeProject(project);
            File.WriteAllText(filePath, json);
        }

        public static Project LoadProjectFromFile(string filePath)
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException("Project file not found", filePath);

            var json = File.ReadAllText(filePath);
            return DeserializeProject(json);
        }

        private static JsonSerializerOptions CreateSerializerOptions()
        {
            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                ReferenceHandler = ReferenceHandler.Preserve,
                MaxDepth = 64,
                PropertyNameCaseInsensitive = true
            };
            options.Converters.Add(new JsonStringEnumConverter());
            options.Converters.Add(new TrackItemConverter());
            options.Converters.Add(new TimeCodeConverter());
            options.Converters.Add(new EffectConverter());
            return options;
        }

        private static void PostProcessProject(Project project)
        {
            if (project == null) return;
            project.Initialize();
            if (project.StepOutlineEntries == null)
                project.StepOutlineEntries = new ObservableCollection<MainWindowViewModel.StepOutlineEntry>();

            foreach (var track in project.Tracks)
                foreach (var item in track.Items)
                {
                    // Link clip by FilePath
                    var clip = project.Clips.FirstOrDefault(c =>
                        !string.IsNullOrWhiteSpace(item.FilePath) &&
                        string.Equals(c.FilePath, item.FilePath, StringComparison.OrdinalIgnoreCase));
                    if (clip != null)
                    {
                        item.FileName = clip.FileName;
                        item.FilePath = clip.FilePath;
                    }
                    // Restore transform effect
                    if (item is TrackItem ti)
                        RestoreEffectsForTrackItem(project, ti);

                    item.Initialize();
                }
        }

        private static void RestoreEffectsForTrackItem(Project project, TrackItem trackItem)
        {
            var clip = project.Clips.FirstOrDefault(c =>
                string.Equals(c.FilePath, trackItem.FilePath, StringComparison.OrdinalIgnoreCase));
            if (clip == null) return;

            bool needsTransform =
                trackItem.TranslateX != 0 ||
                trackItem.TranslateY != 0 ||
                trackItem.ScaleX != 1 ||
                trackItem.ScaleY != 1 ||
                trackItem.Rotation != 0 ||
                trackItem.Opacity < 0.999;

            if (needsTransform && !clip.Effects.OfType<TransformEffect>().Any())
                clip.Effects.Add(new TransformEffect(trackItem));
        }
    }

    /// <summary>
    /// Converter for TrackItem and AudioTrackItem
    /// </summary>
    public class TrackItemConverter : System.Text.Json.Serialization.JsonConverter<ITrackItem>
    {
        public override ITrackItem Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            using var doc = JsonDocument.ParseValue(ref reader);
            var root = doc.RootElement;
            bool isAudio = root.GetProperty("IsAudio").GetBoolean();
            ITrackItem item = isAudio ? new AudioTrackItem() : new TrackItem();
            item.InstanceId = Guid.Parse(root.GetProperty("InstanceId").GetString());
            item.FadeColor = Enum.Parse<Track.FadeColor>(root.GetProperty("FadeColor").GetString());
            // Core properties
            item.Position = JsonSerializer.Deserialize<TimeCode>(root.GetProperty("Position").GetRawText(), options);
            item.Start = JsonSerializer.Deserialize<TimeCode>(root.GetProperty("Start").GetRawText(), options);
            item.End = JsonSerializer.Deserialize<TimeCode>(root.GetProperty("End").GetRawText(), options);
            item.OriginalEnd = JsonSerializer.Deserialize<TimeCode>(root.GetProperty("OriginalEnd").GetRawText(), options);
            item.SourceLength = JsonSerializer.Deserialize<TimeCode>(root.GetProperty("SourceLength").GetRawText(), options);
            item.FadeInFrame = root.GetProperty("FadeInFrame").GetInt32();
            item.FadeOutFrame = root.GetProperty("FadeOutFrame").GetInt32();
            item.IsSelected = root.GetProperty("IsSelected").GetBoolean();

            // Media properties
            item.FileName = root.GetProperty("FileName").GetString();
            item.FilePath = root.GetProperty("FilePath").GetString();
            item.FullPath = root.GetProperty("FullPath").GetString();
            if (root.TryGetProperty("Thumbnail", out var t))
                item.Thumbnail = t.GetBytesFromBase64();

            // Transform
            if (item is TrackItem ti)
            {
                ti.TranslateX = root.GetProperty("TranslateX").GetDouble();
                ti.TranslateY = root.GetProperty("TranslateY").GetDouble();
                ti.ScaleX = root.GetProperty("ScaleX").GetDouble();
                ti.ScaleY = root.GetProperty("ScaleY").GetDouble();
                ti.Rotation = root.GetProperty("Rotation").GetDouble();
                ti.Opacity = root.GetProperty("Opacity").GetDouble();
            }

            // Audio-specific
            if (item is AudioTrackItem ai)
                ai.Volume = root.GetProperty("Volume").GetSingle();

            return item;
        }

        public override void Write(Utf8JsonWriter writer, ITrackItem value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            writer.WriteBoolean("IsAudio", value is AudioTrackItem);
            writer.WriteString("InstanceId", value.InstanceId.ToString());
            writer.WriteString("FadeColor", value.FadeColor.ToString());
            // Core
            writer.WritePropertyName("Position"); JsonSerializer.Serialize(writer, value.Position, options);
            writer.WritePropertyName("Start"); JsonSerializer.Serialize(writer, value.Start, options);
            writer.WritePropertyName("End"); JsonSerializer.Serialize(writer, value.End, options);
            writer.WritePropertyName("OriginalEnd"); JsonSerializer.Serialize(writer, value.OriginalEnd, options);
            writer.WritePropertyName("SourceLength"); JsonSerializer.Serialize(writer, value.SourceLength, options);
            writer.WriteNumber("FadeInFrame", value.FadeInFrame);
            writer.WriteNumber("FadeOutFrame", value.FadeOutFrame);
            writer.WriteBoolean("IsSelected", value.IsSelected);

            // Media
            writer.WriteString("FileName", value.FileName);
            writer.WriteString("FilePath", value.FilePath);
            writer.WriteString("FullPath", value.FullPath);
            if (value.Thumbnail != null)
                writer.WriteBase64String("Thumbnail", value.Thumbnail);

            // Transform
            if (value is TrackItem ti)
            {
                writer.WriteNumber("TranslateX", ti.TranslateX);
                writer.WriteNumber("TranslateY", ti.TranslateY);
                writer.WriteNumber("ScaleX", ti.ScaleX);
                writer.WriteNumber("ScaleY", ti.ScaleY);
                writer.WriteNumber("Rotation", ti.Rotation);
                writer.WriteNumber("Opacity", ti.Opacity);
            }

            // Audio-specific
            if (value is AudioTrackItem ai)
                writer.WriteNumber("Volume", ai.Volume);

            writer.WriteEndObject();
        }
    }

    /// <summary>
    /// Converter for TimeCode
    /// </summary>
    public class TimeCodeConverter : System.Text.Json.Serialization.JsonConverter<TimeCode>
    {
        public override TimeCode Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            using var doc = JsonDocument.ParseValue(ref reader);
            var root = doc.RootElement;
            int frames = root.GetProperty("TotalFrames").GetInt32();
            double fps = root.GetProperty("FPS").GetDouble();
            return new TimeCode(frames, fps);
        }

        public override void Write(Utf8JsonWriter writer, TimeCode value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            writer.WriteNumber("TotalFrames", value.TotalFrames);
            writer.WriteNumber("FPS", value.FPS);
            writer.WriteEndObject();
        }
    }

    /// <summary>
    /// Converter for IEffect
    /// </summary>
    public class EffectConverter : System.Text.Json.Serialization.JsonConverter<IEffect>
    {
        public override IEffect Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            using var doc = JsonDocument.ParseValue(ref reader);
            var root = doc.RootElement;
            var typeName = root.GetProperty("Type").GetString();
            IEffect effect = typeName switch
            {
                "Transform" => new TransformEffect(null),
                "ChromaKey" => new ChromaKeyEffect(),
                "Blending" => new BlendingEffect(null),
                _ => throw new JsonException($"Unknown effect type: {typeName}")
            };

            switch (effect)
            {
                case TransformEffect te:
                    te.Enabled = root.GetProperty("Enabled").GetBoolean();
                    break;
                case ChromaKeyEffect ck:
                    ck.KeyColor = DeserializeColor(root.GetProperty("KeyColor"));
                    ck.Tolerance = root.GetProperty("Tolerance").GetDouble();
                    break;
                case BlendingEffect be:
                    be.BlendMode = Enum.Parse<BlendMode>(root.GetProperty("BlendMode").GetString());
                    break;
            }

            return effect;
        }

        public override void Write(Utf8JsonWriter writer, IEffect value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            writer.WriteString("Type", value.Name);
            switch (value)
            {
                case TransformEffect te:
                    writer.WriteBoolean("Enabled", te.Enabled);
                    break;
                case ChromaKeyEffect ck:
                    writer.WritePropertyName("KeyColor"); WriteColor(writer, ck.KeyColor);
                    writer.WriteNumber("Tolerance", ck.Tolerance);
                    break;
                case BlendingEffect be:
                    writer.WriteString("BlendMode", be.BlendMode.ToString());
                    break;
            }
            writer.WriteEndObject();
        }

        private static void WriteColor(Utf8JsonWriter writer, System.Windows.Media.Color color)
        {
            writer.WriteStartObject();
            writer.WriteNumber("A", color.A);
            writer.WriteNumber("R", color.R);
            writer.WriteNumber("G", color.G);
            writer.WriteNumber("B", color.B);
            writer.WriteEndObject();
        }

        private static System.Windows.Media.Color DeserializeColor(JsonElement element)
        {
            byte a = (byte)element.GetProperty("A").GetInt32();
            byte r = (byte)element.GetProperty("R").GetInt32();
            byte g = (byte)element.GetProperty("G").GetInt32();
            byte b = (byte)element.GetProperty("B").GetInt32();
            return System.Windows.Media.Color.FromArgb(a, r, g, b);
        }
    }

    /// <summary>
    /// Alternative serializer using Newtonsoft.Json
    /// </summary>
    public static class CustomProjectSerializer
    {
        private static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
        {
            Formatting = Formatting.Indented,
            TypeNameHandling = TypeNameHandling.Auto,
            PreserveReferencesHandling = PreserveReferencesHandling.Objects,
            ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
            ContractResolver = new CamelCasePropertyNamesContractResolver(),
            Converters = new List<JsonConverter> { new StringEnumConverter() }
        };

        public static string Serialize(Project project) => JsonConvert.SerializeObject(project, Settings);
        public static Project Deserialize(string json)
        {
            var project = JsonConvert.DeserializeObject<Project>(json, Settings)
                         ?? throw new JsonSerializationException("Failed to deserialize Project");
            project.Initialize();
            if (project.StepOutlineEntries == null)
                project.StepOutlineEntries = new ObservableCollection<MainWindowViewModel.StepOutlineEntry>();
            foreach (var track in project.Tracks)
                foreach (var item in track.Items)
                {
                    var clip = project.Clips.FirstOrDefault(c =>
                        string.Equals(c.FilePath, item.FilePath, StringComparison.OrdinalIgnoreCase));
                    if (clip != null)
                    {
                        if (item is TrackItem ti)
                        {
                            ti.FilePath = clip.FilePath;
                            ti.FileName = clip.FileName;
                        }
                    }
                    item.Initialize();
                }
            return project;
        }

        public static void Save(Project project, string filePath) => File.WriteAllText(filePath, Serialize(project));
        public static Project Load(string filePath)
            => !File.Exists(filePath)
               ? throw new FileNotFoundException("Project file not found", filePath)
               : Deserialize(File.ReadAllText(filePath));
    }
}
