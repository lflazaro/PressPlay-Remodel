namespace PressPlay.Models
{
    public class FrameCache
    {
        public int FrameNumber { get; set; }
        public byte[] ImageBytes { get; set; }

        public FrameCache() { }

        public FrameCache(int frameNumber, byte[] imageBytes)
        {
            FrameNumber = frameNumber;
            ImageBytes = imageBytes;
        }
    }
}