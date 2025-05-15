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

            // Initialize the project
            project.Initialize();

            // Ensure collections exist
            if (project.StepOutlineEntries == null)
                project.StepOutlineEntries = new ObservableCollection<MainWindowViewModel.StepOutlineEntry>();

            // Process all tracks and items
            foreach (var track in project.Tracks)
            {
                foreach (var item in track.Items)
                {
                    // Verify essential properties
                    if (item.Position == null) item.Position = new TimeCode(0, project.FPS);
                    if (item.Start == null) item.Start = new TimeCode(0, project.FPS);
                    if (item.End == null) item.End = new TimeCode(10, project.FPS);
                    if (item.OriginalEnd == null) item.OriginalEnd = item.End;

                    // Link clip by FilePath
                    var clip = project.Clips.FirstOrDefault(c =>
                        !string.IsNullOrWhiteSpace(item.FilePath) &&
                        string.Equals(c.FilePath, item.FilePath, StringComparison.OrdinalIgnoreCase));

                    if (clip != null)
                    {
                        item.FileName = clip.FileName;
                        item.FilePath = clip.FilePath;

                        // For AudioTrackItem, ensure ClipId is set
                        if (item is AudioTrackItem audioItem && string.IsNullOrEmpty(audioItem.ClipId))
                        {
                            audioItem.ClipId = clip.Id;
                            Debug.WriteLine($"Setting ClipId {clip.Id} for AudioTrackItem with FilePath {clip.FilePath}");
                        }
                    }
                    else if (item is AudioTrackItem audioItem && !string.IsNullOrEmpty(audioItem.ClipId))
                    {
                        // Find clip by ClipId for AudioTrackItems
                        clip = project.Clips.FirstOrDefault(c => c.Id == audioItem.ClipId);
                        if (clip != null)
                        {
                            audioItem.FilePath = clip.FilePath;
                            audioItem.FileName = clip.FileName;
                            Debug.WriteLine($"Found clip by ClipId {audioItem.ClipId}: {clip.FileName}");
                        }
                        else
                        {
                            Debug.WriteLine($"Warning: Could not find clip with ID {audioItem.ClipId}");
                        }
                    }

                    // Restore transform effect
                    if (item is TrackItem ti)
                        RestoreEffectsForTrackItem(project, ti);

                    // Initialize the item
                    item.Initialize();
                }
            }

            // Validate project after processing
            Debug.WriteLine($"Project post-processing complete: {project.Tracks.Count} tracks, {project.Clips.Count} clips");
        }

        private static void RestoreEffectsForTrackItem(Project project, TrackItem trackItem)
        {
            ProjectClip clip = null;

            // Try to find clip by FilePath
            if (!string.IsNullOrEmpty(trackItem.FilePath))
            {
                clip = project.Clips.FirstOrDefault(c =>
                    string.Equals(c.FilePath, trackItem.FilePath, StringComparison.OrdinalIgnoreCase));
            }

            // If no clip found, there's nothing to restore effects for
            if (clip == null)
            {
                Debug.WriteLine($"Warning: Could not find clip for track item {trackItem.FileName}");
                return;
            }

            // Check if we need to restore a transform effect
            bool needsTransform =
                trackItem.TranslateX != 0 ||
                trackItem.TranslateY != 0 ||
                trackItem.ScaleX != 1 ||
                trackItem.ScaleY != 1 ||
                trackItem.Rotation != 0 ||
                trackItem.Opacity < 0.999;

            if (needsTransform && !clip.Effects.OfType<TransformEffect>().Any())
            {
                clip.Effects.Add(new TransformEffect(trackItem));
                Debug.WriteLine($"Restored TransformEffect for clip {clip.FileName}");
            }

            // Check for ChromaKey effects in clip effects collection
            var chromaKey = clip.Effects.OfType<ChromaKeyEffect>().FirstOrDefault();
            if (chromaKey != null)
            {
                Debug.WriteLine($"Found ChromaKeyEffect for clip {clip.FileName} with tolerance {chromaKey.Tolerance}");
            }

            // Check for Blending effects
            var blending = clip.Effects.OfType<BlendingEffect>().FirstOrDefault();
            if (blending != null)
            {
                Debug.WriteLine($"Found BlendingEffect for clip {clip.FileName} with mode {blending.BlendMode}");
            }
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

            // Determine item type 
            bool isAudio = root.GetProperty("IsAudio").GetBoolean();
            ITrackItem item = isAudio ? new AudioTrackItem() : new TrackItem();

            // Make sure InstanceId is preserved correctly
            if (root.TryGetProperty("InstanceId", out var instanceIdElement))
            {
                item.InstanceId = Guid.Parse(instanceIdElement.GetString());
            }
            else
            {
                item.InstanceId = Guid.NewGuid(); // Generate new if missing
                Debug.WriteLine("Warning: Missing InstanceId in deserialized item, generated new one");
            }

            // Ensure FadeColor is properly deserialized
            if (root.TryGetProperty("FadeColor", out var fadeColorElement))
            {
                item.FadeColor = Enum.Parse<Track.FadeColor>(fadeColorElement.GetString());
            }
            else
            {
                item.FadeColor = Track.FadeColor.Black; // Default
                Debug.WriteLine("Warning: Missing FadeColor in deserialized item, using default Black");
            }

            // Core properties - with careful null checking
            if (root.TryGetProperty("Position", out var posElement))
                item.Position = JsonSerializer.Deserialize<TimeCode>(posElement.GetRawText(), options);

            if (root.TryGetProperty("Start", out var startElement))
                item.Start = JsonSerializer.Deserialize<TimeCode>(startElement.GetRawText(), options);

            if (root.TryGetProperty("End", out var endElement))
                item.End = JsonSerializer.Deserialize<TimeCode>(endElement.GetRawText(), options);

            // CRITICAL: Ensure OriginalEnd is properly preserved
            if (root.TryGetProperty("OriginalEnd", out var originalEndElement))
            {
                item.OriginalEnd = JsonSerializer.Deserialize<TimeCode>(originalEndElement.GetRawText(), options);
            }
            else if (item.End != null)
            {
                // Fallback - set OriginalEnd to End if not present
                item.OriginalEnd = item.End;
                Debug.WriteLine("Warning: Missing OriginalEnd in deserialized item, using End value");
            }

            // Source length
            if (root.TryGetProperty("SourceLength", out var sourceLengthElement))
                item.SourceLength = JsonSerializer.Deserialize<TimeCode>(sourceLengthElement.GetRawText(), options);

            // Fade properties
            if (root.TryGetProperty("FadeInFrame", out var fadeInElement))
                item.FadeInFrame = fadeInElement.GetInt32();

            if (root.TryGetProperty("FadeOutFrame", out var fadeOutElement))
                item.FadeOutFrame = fadeOutElement.GetInt32();

            if (root.TryGetProperty("IsSelected", out var isSelectedElement))
                item.IsSelected = isSelectedElement.GetBoolean();

            // Media properties
            if (root.TryGetProperty("FileName", out var fileNameElement))
                item.FileName = fileNameElement.GetString();

            if (root.TryGetProperty("FilePath", out var filePathElement))
                item.FilePath = filePathElement.GetString();

            if (root.TryGetProperty("FullPath", out var fullPathElement))
                item.FullPath = fullPathElement.GetString();

            if (root.TryGetProperty("Thumbnail", out var thumbnailElement))
                item.Thumbnail = thumbnailElement.GetBytesFromBase64();

            // Transform properties for TrackItem
            if (item is TrackItem ti)
            {
                if (root.TryGetProperty("TranslateX", out var txElement))
                    ti.TranslateX = txElement.GetDouble();

                if (root.TryGetProperty("TranslateY", out var tyElement))
                    ti.TranslateY = tyElement.GetDouble();

                if (root.TryGetProperty("ScaleX", out var sxElement))
                    ti.ScaleX = sxElement.GetDouble();

                if (root.TryGetProperty("ScaleY", out var syElement))
                    ti.ScaleY = syElement.GetDouble();

                if (root.TryGetProperty("Rotation", out var rotElement))
                    ti.Rotation = rotElement.GetDouble();

                if (root.TryGetProperty("Opacity", out var opacityElement))
                    ti.Opacity = opacityElement.GetDouble();
            }

            // Special handling for AudioTrackItem
            if (item is AudioTrackItem audioItem)
            {
                if (root.TryGetProperty("Volume", out var volumeElement))
                    audioItem.Volume = volumeElement.GetSingle();

                // CRITICAL: Ensure ClipId is preserved
                if (root.TryGetProperty("ClipId", out var clipIdElement))
                {
                    audioItem.ClipId = clipIdElement.GetString();
                    Debug.WriteLine($"Deserialized AudioTrackItem with ClipId: {audioItem.ClipId}");
                }
            }

            Debug.WriteLine($"Deserialized {(isAudio ? "Audio" : "Video")} track item: Position={item.Position?.TotalFrames}, Start={item.Start?.TotalFrames}, End={item.End?.TotalFrames}");

            return item;
        }

        public override void Write(Utf8JsonWriter writer, ITrackItem value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();

            // Type identification
            writer.WriteBoolean("IsAudio", value is AudioTrackItem);
            writer.WriteString("InstanceId", value.InstanceId.ToString());
            writer.WriteString("FadeColor", value.FadeColor.ToString());

            // Core timing properties
            writer.WritePropertyName("Position");
            JsonSerializer.Serialize(writer, value.Position, options);

            writer.WritePropertyName("Start");
            JsonSerializer.Serialize(writer, value.Start, options);

            writer.WritePropertyName("End");
            JsonSerializer.Serialize(writer, value.End, options);

            writer.WritePropertyName("OriginalEnd");
            JsonSerializer.Serialize(writer, value.OriginalEnd, options);

            writer.WritePropertyName("SourceLength");
            JsonSerializer.Serialize(writer, value.SourceLength, options);

            // Fade properties
            writer.WriteNumber("FadeInFrame", value.FadeInFrame);
            writer.WriteNumber("FadeOutFrame", value.FadeOutFrame);
            writer.WriteBoolean("IsSelected", value.IsSelected);

            // Media properties
            writer.WriteString("FileName", value.FileName);
            writer.WriteString("FilePath", value.FilePath);
            writer.WriteString("FullPath", value.FullPath);

            if (value.Thumbnail != null)
                writer.WriteBase64String("Thumbnail", value.Thumbnail);

            // Transform properties
            if (value is TrackItem ti)
            {
                writer.WriteNumber("TranslateX", ti.TranslateX);
                writer.WriteNumber("TranslateY", ti.TranslateY);
                writer.WriteNumber("ScaleX", ti.ScaleX);
                writer.WriteNumber("ScaleY", ti.ScaleY);
                writer.WriteNumber("Rotation", ti.Rotation);
                writer.WriteNumber("Opacity", ti.Opacity);
            }

            // Audio-specific properties
            if (value is AudioTrackItem ai)
            {
                writer.WriteNumber("Volume", ai.Volume);

                // CRITICAL: Include ClipId for audio items
                if (!string.IsNullOrEmpty(ai.ClipId))
                {
                    writer.WriteString("ClipId", ai.ClipId);
                }
            }

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

            // Extract key properties with safety checks
            int frames = 0;
            double fps = 25.0; // Default

            if (root.TryGetProperty("TotalFrames", out var framesElement))
                frames = framesElement.GetInt32();

            if (root.TryGetProperty("FPS", out var fpsElement))
                fps = fpsElement.GetDouble();

            // Ensure we have valid FPS
            if (fps <= 0)
            {
                fps = 25.0;
                Debug.WriteLine("Warning: Invalid FPS in TimeCode, using default 25.0");
            }

            return new TimeCode(frames, fps);
        }

        public override void Write(Utf8JsonWriter writer, TimeCode value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            writer.WriteNumber("TotalFrames", value?.TotalFrames ?? 0);
            writer.WriteNumber("FPS", value?.FPS ?? 25.0);
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
                    te.Parameters[0].Value = root.GetProperty("TranslateX").GetDouble();
                    te.Parameters[1].Value = root.GetProperty("TranslateY").GetDouble();
                    te.Parameters[2].Value = root.GetProperty("ScaleX").GetDouble();
                    te.Parameters[3].Value = root.GetProperty("ScaleY").GetDouble();
                    te.Parameters[4].Value = root.GetProperty("Rotation").GetDouble();
                    break;
                case ChromaKeyEffect ck:
                    ck.Enabled = root.GetProperty("Enabled").GetBoolean();
                    ck.KeyColor = DeserializeColor(root.GetProperty("KeyColor"));
                    ck.Tolerance = root.GetProperty("Tolerance").GetDouble();
                    break;
                case BlendingEffect be:
                    be.Enabled = root.GetProperty("Enabled").GetBoolean();
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
                    writer.WriteNumber("TranslateX", (double)te.Parameters[0].Value);
                    writer.WriteNumber("TranslateY", (double)te.Parameters[1].Value);
                    writer.WriteNumber("ScaleX", (double)te.Parameters[2].Value);
                    writer.WriteNumber("ScaleY", (double)te.Parameters[3].Value);
                    writer.WriteNumber("Rotation", (double)te.Parameters[4].Value);
                    break;
                case ChromaKeyEffect ck:
                    writer.WriteBoolean("Enabled", ck.Enabled);
                    writer.WritePropertyName("KeyColor"); WriteColor(writer, ck.KeyColor);
                    writer.WriteNumber("Tolerance", ck.Tolerance);
                    break;
                case BlendingEffect be:
                    writer.WriteBoolean("Enabled", be.Enabled);
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

            // Initialize the project
            project.Initialize();

            // Ensure collections exist
            if (project.StepOutlineEntries == null)
                project.StepOutlineEntries = new ObservableCollection<MainWindowViewModel.StepOutlineEntry>();

            // Process all items
            foreach (var track in project.Tracks)
            {
                foreach (var item in track.Items)
                {
                    // Link clip by FilePath
                    var clip = project.Clips.FirstOrDefault(c =>
                        string.Equals(c.FilePath, item.FilePath, StringComparison.OrdinalIgnoreCase));

                    if (clip != null)
                    {
                        if (item is TrackItem ti)
                        {
                            ti.FilePath = clip.FilePath;
                            ti.FileName = clip.FileName;
                        }
                        else if (item is AudioTrackItem audioItem && string.IsNullOrEmpty(audioItem.ClipId))
                        {
                            audioItem.ClipId = clip.Id;
                        }
                    }
                    else if (item is AudioTrackItem audioItem && !string.IsNullOrEmpty(audioItem.ClipId))
                    {
                        // Try to find by ClipId
                        clip = project.Clips.FirstOrDefault(c => c.Id == audioItem.ClipId);
                        if (clip != null)
                        {
                            audioItem.FilePath = clip.FilePath;
                            audioItem.FileName = clip.FileName;
                        }
                    }

                    // Ensure all properties are valid
                    item.Initialize();
                }
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