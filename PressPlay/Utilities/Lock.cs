public class Lock
{
    private readonly object _lockObj = new object();

    public IDisposable EnterScope()
    {
        Monitor.Enter(_lockObj);
        return new LockReleaser(_lockObj);
    }

    private class LockReleaser : IDisposable
    {
        private readonly object _lockObj;

        public LockReleaser(object lockObj)
        {
            _lockObj = lockObj;
        }

        public void Dispose()
        {
            Monitor.Exit(_lockObj);
        }
    }
}