using System;

namespace PressPlay.Models
{
    public class TimeCode
    {
        public int TotalFrames { get; set; }
        public double FPS { get; }

        public TimeCode(int totalFrames, double fps)
        {
            TotalFrames = totalFrames;
            FPS = fps > 0 ? fps : 25; // Ensure FPS is never zero
        }

        public override string ToString()
        {
            if (FPS <= 0)
                return "00:00:00:00"; // Default string if FPS is invalid

            int totalSeconds = (int)(TotalFrames / FPS);
            int frames = FPS > 0 ? TotalFrames % (int)FPS : 0; // Prevent division by zero
            return TimeSpan.FromSeconds(totalSeconds).ToString(@"hh\:mm\:ss") + $":{frames:D2}";
        }

        public static TimeCode FromTimeSpan(TimeSpan time, double fps)
        {
            // Ensure fps is valid
            fps = fps > 0 ? fps : 25;
            int totalFrames = (int)(time.TotalSeconds * fps);
            return new TimeCode(totalFrames, fps);
        }

        public static TimeCode FromSeconds(double seconds, double fps)
        {
            // Ensure fps is valid
            fps = fps > 0 ? fps : 25;
            int totalFrames = (int)(seconds * fps);
            return new TimeCode(totalFrames, fps);
        }

        public TimeCode AddFrames(int frames)
        {
            return new TimeCode(TotalFrames + frames, FPS);
        }

        // Create a static Zero property that uses a valid FPS value
        public static TimeCode Zero => new TimeCode(0, 25);

        public double TotalSeconds => TotalFrames / FPS;
        public double TotalMilliseconds => TotalSeconds * 1000.0;
        public TimeSpan ToTimeSpan() => TimeSpan.FromSeconds(TotalSeconds);

        // IComparable
        public int CompareTo(TimeCode other) =>
            other is null ? 1 : TotalFrames.CompareTo(other.TotalFrames);

        // Operator overloads
        public static bool operator <(TimeCode left, TimeCode right) =>
            left?.TotalFrames < right?.TotalFrames;

        public static bool operator >(TimeCode left, TimeCode right) =>
            left?.TotalFrames > right?.TotalFrames;

        public static bool operator <=(TimeCode left, TimeCode right) =>
            left?.TotalFrames <= right?.TotalFrames;

        public static bool operator >=(TimeCode left, TimeCode right) =>
            left?.TotalFrames >= right?.TotalFrames;

        public const int DefaultFPS = 25;  // or whatever your fallback framerate is

        public static bool TryParse(string s, out TimeCode result)
        {
            // Provide default values for the required constructor parameters
            result = new TimeCode(0, DefaultFPS); // Use 0 frames and the default FPS
            // simple implementation: parse “hh:mm:ss” or “frames@fps” here
            // for now, if you don’t need string parsing, just return false
            return false;
        }

        // Or if you have an overload already, just add a second parameter:
        public static TimeCode FromSeconds(double seconds)
            => FromSeconds(seconds, DefaultFPS); // where DefaultFPS is a constant or fallback

    }
}