using System.Runtime.ExceptionServices;
using System.Windows.Threading;

namespace Assetra.Tests.WPF;

internal static class WpfTestHost
{
    private static readonly ManualResetEventSlim Ready = new(false);
    private static readonly Thread Worker = StartWorker();
    private static Dispatcher? dispatcher;

    public static void Run(Action action)
    {
        _ = Worker.ManagedThreadId;
        Ready.Wait();

        Exception? error = null;
        dispatcher!.Invoke(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                error = ex;
            }
        });

        if (error is not null)
        {
            ExceptionDispatchInfo.Capture(error).Throw();
        }
    }

    private static Thread StartWorker()
    {
        var thread = new Thread(RunWorker)
        {
            IsBackground = true,
            Name = "Assetra WPF test host",
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return thread;
    }

    private static void RunWorker()
    {
        dispatcher = Dispatcher.CurrentDispatcher;
        SynchronizationContext.SetSynchronizationContext(
            new DispatcherSynchronizationContext(dispatcher));

        if (System.Windows.Application.Current is null)
        {
            _ = new System.Windows.Application { ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown };
        }

        Ready.Set();
        Dispatcher.Run();
    }
}
