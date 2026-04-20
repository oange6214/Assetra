using System.Reactive.Disposables;

namespace Assetra.WPF.Infrastructure;

internal static class DisposableExtensions
{
    public static T DisposeWith<T>(this T disposable, CompositeDisposable compositeDisposable)
        where T : IDisposable
    {
        compositeDisposable.Add(disposable);
        return disposable;
    }
}
