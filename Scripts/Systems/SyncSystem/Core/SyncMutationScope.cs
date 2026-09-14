namespace Assets.Shared.SyncSystem.Core
{
    /// <summary>
    /// Suppresses outgoing Changed events while remote patches or snapshots are being applied.
    /// This is a defensive loop breaker for future Sync types; existing SetValueSilent paths already avoid Changed.
    /// </summary>
    public readonly struct SyncMutationScope : System.IDisposable
    {
        [System.ThreadStatic]
        private static int _silentDepth;

        public static bool IsSilent => _silentDepth > 0;

        public static SyncMutationScope EnterSilent()
        {
            _silentDepth++;
            return new SyncMutationScope();
        }

        public void Dispose()
        {
            if (_silentDepth > 0)
            {
                _silentDepth--;
            }
        }
    }
}
