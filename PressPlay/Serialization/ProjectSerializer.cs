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
using System.ComponentModel;
using DocumentFormat.OpenXml.Bibliography;
using PressPlay.Helpers;

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
                project.RefreshAllTransformEffects();
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

            // Add debug output to track project loading
            Debug.WriteLine($"Processing project with {project.Clips.Count} clips");

            // Log all clips and their effects before processing
            foreach (var clip in project.Clips)
            {
                var tCount = clip.Effects.OfType<TransformEffect>().Count();
                Debug.WriteLine($"Clip “{clip.FileName}” has {tCount} TransformEffect(s)");
                Debug.WriteLine($"Clip: {clip.FileName} (ID: {clip.Id}) has {clip.Effects.Count} effects");
                foreach (var effect in clip.Effects)
                {
                    if (effect is ChromaKeyEffect ck)
                    {
                        Debug.WriteLine($"  - ChromaKeyEffect: KeyColor=R:{ck.KeyColor.R},G:{ck.KeyColor.G},B:{ck.KeyColor.B}, Tolerance={ck.Tolerance}");
                    }
                    else if (effect is TransformEffect)
                    {
                        Debug.WriteLine($"  - TransformEffect: Enabled={((TransformEffect)effect).Enabled}");
                    }
                    else if (effect is BlendingEffect be)
                    {
                        Debug.WriteLine($"  - BlendingEffect: Mode={be.BlendMode}");
                    }
                    else
                    {
                        Debug.WriteLine($"  - Unknown effect type: {effect.GetType().Name}");
                    }
                }
            }

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

                        // Log transform properties for TrackItems
                        if (item is TrackItem ti2)
                        {
                            Debug.WriteLine($"TrackItem: {ti2.FileName} - Rotation={ti2.Rotation}, TranslateX={ti2.TranslateX}, " +
                                           $"TranslateY={ti2.TranslateY}, ScaleX={ti2.ScaleX}, ScaleY={ti2.ScaleY}, Opacity={ti2.Opacity}");
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

                    // Restore effects for TrackItem
                    if (item is TrackItem ti)
                        RestoreEffectsForTrackItem(project, ti);
                }
            }

            // Verify all effects were properly restored
            foreach (var clip in project.Clips)
            {
                Debug.WriteLine($"After processing - Clip: {clip.FileName} has {clip.Effects.Count} effects");
                foreach (var effect in clip.Effects)
                {
                    Debug.WriteLine($"  - {effect.Name} ({effect.GetType().Name})");
                }
            }

            // Validate project after processing
            Debug.WriteLine($"Project post-processing complete: {project.Tracks.Count} tracks, {project.Clips.Count} clips");
            double savedFps = project.FPS;
            // Initialize the project
            project.Initialize();
            project.FPS = savedFps;
        }

        private static void RestoreEffectsForTrackItem(Project project, TrackItem trackItem)
        {
            // find the clip that this TrackItem belongs to…
            var clip = project.Clips.FirstOrDefault(c =>
                string.Equals(c.FilePath, trackItem.FilePath, StringComparison.OrdinalIgnoreCase));
            if (clip == null) return;

            bool hasTransform =
                Math.Abs(trackItem.TranslateX) > 1e-3 ||
                Math.Abs(trackItem.TranslateY) > 1e-3 ||
                Math.Abs(trackItem.ScaleX - 1) > 1e-3 ||
                Math.Abs(trackItem.ScaleY - 1) > 1e-3 ||
                Math.Abs(trackItem.Rotation) > 1e-3 ||
                trackItem.Opacity < 0.999;

            // **New**: look for an existing TransformEffect
            var existing = clip.Effects.OfType<TransformEffect>().FirstOrDefault();

            if (existing != null)
            {
                // just re-bind it and enable/disable
                existing.SetTrackItem(trackItem);
                existing.Enabled = hasTransform;
                Debug.WriteLine($"Re-bound TransformEffect to '{trackItem.FileName}'");
            }
            else if (hasTransform)
            {
                // only create a brand-new one if none existed
                var fx = new TransformEffect(trackItem);
                clip.Effects.Add(fx);
                Debug.WriteLine($"Added TransformEffect to '{trackItem.FileName}'");
            }

            // Process ChromaKey effects - ensure they're properly initialized
            var chromaKeyEffects = clip.Effects.OfType<ChromaKeyEffect>().ToList();
            if (chromaKeyEffects.Any())
            {
                foreach (var ck in chromaKeyEffects)
                {
                    // Log ChromaKey properties for debugging
                    Debug.WriteLine($"Restored ChromaKeyEffect for clip {clip.FileName} - " +
                                   $"KeyColor=R:{ck.KeyColor.R},G:{ck.KeyColor.G},B:{ck.KeyColor.B}, " +
                                   $"Tolerance={ck.Tolerance}");

                    // Extra validation that ChromaKey has proper default values if needed
                    if (ck.KeyColor.R == 0 && ck.KeyColor.G == 0 && ck.KeyColor.B == 0 && ck.KeyColor.A == 0)
                    {
                        // Default to green if color is invalid
                        ck.KeyColor = System.Windows.Media.Colors.Green;
                        Debug.WriteLine("Warning: ChromaKey had invalid color - set to default green");
                    }

                    if (ck.Tolerance <= 0)
                    {
                        // Set default tolerance if invalid
                        ck.Tolerance = 0.3;
                        Debug.WriteLine("Warning: ChromaKey had invalid tolerance - set to default 0.3");
                    }
                }
            }

            // Process BlendingEffects
            var blendingEffects = clip.Effects.OfType<BlendingEffect>().ToList();
            if (blendingEffects.Any())
            {
                foreach (var be in blendingEffects)
                {
                    Debug.WriteLine($"Restored BlendingEffect for clip {clip.FileName} - Mode={be.BlendMode}");
                }
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

            if (root.TryGetProperty("UnlimitedSourceLength", out var unlimitedElement))
                item.SetUnlimitedSourceLength(unlimitedElement.GetBoolean());
            else
            {
                // If not saved, re-determine based on file extension for images
                string extension = Path.GetExtension(item.FilePath)?.ToLowerInvariant();
                if (!string.IsNullOrEmpty(extension) && FileFormats.SupportedImageFormats.Contains(extension))
                {
                    item.SetUnlimitedSourceLength(true);
                    Debug.WriteLine($"Set unlimited length for image: {item.FileName}");
                }
            }

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

                // ---- New: Pivot / origin for scale and rotation ----
                if (root.TryGetProperty("ScaleOrigin", out var so))
                {
                    var x = so.GetProperty("X").GetDouble();
                    var y = so.GetProperty("Y").GetDouble();
                    ti.ScaleOrigin = new System.Windows.Point(x, y);
                }
                else
                {
                    // Default to center if missing
                    ti.ScaleOrigin = new System.Windows.Point(0.5, 0.5);
                }

                if (root.TryGetProperty("RotationOrigin", out var ro))
                {
                    var x = ro.GetProperty("X").GetDouble();
                    var y = ro.GetProperty("Y").GetDouble();
                    ti.RotationOrigin = new System.Windows.Point(x, y);
                }
                else
                {
                    // Default to center if missing
                    ti.RotationOrigin = new System.Windows.Point(0.5, 0.5);
                }
                if (root.TryGetProperty("Volume", out var volumeElement))
                    ti.Volume = volumeElement.GetSingle();
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
            writer.WriteBoolean("UnlimitedSourceLength", value.UnlimitedSourceLength);

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
                writer.WritePropertyName("ScaleOrigin");
                writer.WriteStartObject();
                writer.WriteNumber("X", ti.ScaleOrigin.X);
                writer.WriteNumber("Y", ti.ScaleOrigin.Y);
                writer.WriteEndObject();

                writer.WritePropertyName("RotationOrigin");
                writer.WriteStartObject();
                writer.WriteNumber("X", ti.RotationOrigin.X);
                writer.WriteNumber("Y", ti.RotationOrigin.Y);
                writer.WriteEndObject();
                writer.WriteNumber("Volume", ti.Volume);
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

            Debug.WriteLine($"Deserializing effect of type: '{typeName}'");

            IEffect effect;

            // Match effect by name - case-insensitive comparison for robustness
            if (string.Equals(typeName, "Chroma Key", StringComparison.OrdinalIgnoreCase))
            {
                effect = new ChromaKeyEffect();

                // Read ChromaKey properties
                if (root.TryGetProperty("KeyColor", out var keyColorProp))
                {
                    ((ChromaKeyEffect)effect).KeyColor = DeserializeColor(keyColorProp);
                    Debug.WriteLine($"  Deserialized KeyColor: R={((ChromaKeyEffect)effect).KeyColor.R}, G={((ChromaKeyEffect)effect).KeyColor.G}, B={((ChromaKeyEffect)effect).KeyColor.B}");
                }

                if (root.TryGetProperty("Tolerance", out var toleranceProp))
                {
                    ((ChromaKeyEffect)effect).Tolerance = toleranceProp.GetDouble();
                    Debug.WriteLine($"  Deserialized Tolerance: {((ChromaKeyEffect)effect).Tolerance}");
                }

                // FIX: Explicitly set the Enabled property based on the saved value or default to true
                if (root.TryGetProperty("Enabled", out var enabledProp))
                {
                    ((ChromaKeyEffect)effect).Enabled = enabledProp.GetBoolean();
                    Debug.WriteLine($"  Deserialized Enabled: {((ChromaKeyEffect)effect).Enabled}");
                }
                else
                {
                    ((ChromaKeyEffect)effect).Enabled = true; // Default to enabled if not specified
                    Debug.WriteLine("  Enabled property not found, defaulting to true");
                }
            }
            else if (string.Equals(typeName, "Transform", StringComparison.OrdinalIgnoreCase))
            {
                var te = new TransformEffect(null);
                te.Enabled = true;
                effect = te;
            }
            else if (string.Equals(typeName, "Blending", StringComparison.OrdinalIgnoreCase))
            {
                var be = new BlendingEffect(null);
                if (root.TryGetProperty("BlendMode", out var blendModeProp))
                    be.BlendMode = Enum.Parse<BlendMode>(blendModeProp.GetString());
                effect = be;
            }
            else
            {
                Debug.WriteLine($"WARNING: Unknown effect type: {typeName}");
                throw new JsonException($"Unknown effect type: {typeName}");
            }

            return effect;
        }

        public override void Write(Utf8JsonWriter writer, IEffect value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();

            // Always write the effect Type
            writer.WriteString("Type", value.Name);

            if (value is ChromaKeyEffect ck)
            {
                // ChromaKey-specific props
                writer.WritePropertyName("KeyColor");
                WriteColor(writer, ck.KeyColor);

                writer.WriteNumber("Tolerance", ck.Tolerance);

                // **Write the actual Enabled flag**
                writer.WriteBoolean("Enabled", ck.Enabled);

                Debug.WriteLine($"Serializing ChromaKeyEffect: KeyColor={ck.KeyColor}, Tolerance={ck.Tolerance}, Enabled={ck.Enabled}");
            }
            else if (value is TransformEffect te)
            {
                // No transform parameters here, but preserve Enabled
                writer.WriteBoolean("Enabled", te.Enabled);
            }
            else if (value is BlendingEffect be)
            {
                writer.WriteString("BlendMode", be.BlendMode.ToString());
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