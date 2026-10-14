namespace Fsp.Orchestrator.Api.Hosting;

public sealed class StartupReadiness
{
    private int ready;

    public bool IsReady => Volatile.Read(ref ready) == 1;

    public void MarkReady()
    {
        Interlocked.Exchange(ref ready, 1);
    }

    public void MarkStopping()
    {
        Interlocked.Exchange(ref ready, 0);
    }
}
