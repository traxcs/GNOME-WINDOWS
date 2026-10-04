using System.Collections.Concurrent;

namespace GnomeWin.Platform.Win32;

public static class StaWorker
{
    private static readonly BlockingCollection<Action> Queue = new();
    private static readonly Thread Thread;

    static StaWorker()
    {
        Thread = new Thread(() =>
        {
            foreach (var work in Queue.GetConsumingEnumerable())
            {
                try { work(); } catch (Exception ex) { Services.Logging.Log.Debug("StaWorker item failed: " + ex.Message); }
            }
        })
        { IsBackground = true, Name = "GnomeWin.StaWorker", Priority = ThreadPriority.BelowNormal };
        Thread.SetApartmentState(ApartmentState.STA);
        Thread.Start();
    }

    public static Task<T> Run<T>(Func<T> func)
    {
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        Queue.Add(() =>
        {
            try { tcs.SetResult(func()); }
            catch (Exception ex) { tcs.SetException(ex); }
        });
        return tcs.Task;
    }
}
