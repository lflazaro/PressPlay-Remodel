public class FrameContainer
{
    public int FrameNumber { get; set; }
    public byte[] Data { get; set; }

    public FrameContainer()
    {
    }

    public FrameContainer(int frameNumber, byte[] data)
    {
        FrameNumber = frameNumber;
        Data = data;
    }
}