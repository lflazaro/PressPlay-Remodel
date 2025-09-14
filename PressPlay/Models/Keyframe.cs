namespace PressPlay.Models
{
    public class Keyframe
    {
        public int Frame { get; set; }
        public double Value { get; set; }
        public string Interpolation { get; set; } = "Linear";
    }
}

