using System.Threading;
using System.Threading.Tasks;

namespace GearInvest;

/// <summary>Shared cancellation/fault-observation primitive. Domain controllers retain their own typed revisions and state.</summary>
internal static class AuthoringWork
{
    internal static void ObserveFault(Task? task) { if (task != null) _ = task.ContinueWith(t => { _ = t.Exception; }, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default); }
    internal static void Retire(ref CancellationTokenSource? source, Task? task)
    { source?.Cancel(); source?.Dispose(); source = null; ObserveFault(task); }
}
