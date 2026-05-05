namespace Firestarter.Core.Sync;

public class SyncStatusHub
{
    readonly Lock _gate = new();
    SyncStatusSnapshot _snapshot = new() { State = SyncState.Idle };

    public SyncStatusSnapshot Snapshot
    {
        get { lock (_gate) return _snapshot; }
    }

    public void Update(Func<SyncStatusSnapshot, SyncStatusSnapshot> mutate)
    {
        lock (_gate) _snapshot = mutate(_snapshot);
    }
}
