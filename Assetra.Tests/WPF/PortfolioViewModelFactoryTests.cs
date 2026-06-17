using System.Reflection;
using Assetra.WPF.Features.Portfolio;
using Moq;
using Xunit;

namespace Assetra.Tests.WPF;

/// <summary>
/// WHY: the production <see cref="PortfolioViewModelFactory"/> builds the <c>PortfolioServices</c>
/// bundle for the 2-arg <c>PortfolioViewModel</c> ctor, whose fallback for a missing workflow
/// service is a silent <c>Null*</c> no-op. A v0.30.x bug shipped because the factory forgot to
/// pass <c>PositionMetadata</c>: "移至投資組合" updated the row in-session but never wrote the DB,
/// so the assignment vanished on restart (no exception, no test failure — the unit tests inject
/// the service explicitly via the 3-arg compat ctor, which has a *real* fallback).
///
/// These tests assert the factory actually wires the services whose absence causes silent data loss.
/// </summary>
public sealed class PortfolioViewModelFactoryTests
{
    [Fact]
    public void BuildPortfolioServices_WiresPositionMetadata()
    {
        // Regression: a null PositionMetadata here is exactly the shipped bug — the dialog's
        // group reassignment would silently no-op instead of persisting.
        var services = PortfolioViewModelFactory.BuildPortfolioServices(new AutoMockServiceProvider());

        Assert.NotNull(services.PositionMetadata);
    }

    /// <summary>
    /// Resolves any interface request with a Moq mock so the factory's many
    /// <c>GetRequiredService</c> calls succeed; concrete optional deps resolve to null.
    /// </summary>
    private sealed class AutoMockServiceProvider : IServiceProvider
    {
        private static readonly MethodInfo MockOf = typeof(Mock)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .First(m => m.Name == nameof(Mock.Of)
                        && m.IsGenericMethodDefinition
                        && m.GetParameters().Length == 0);

        public object? GetService(Type serviceType)
        {
            ArgumentNullException.ThrowIfNull(serviceType);
            return serviceType.IsInterface
                ? MockOf.MakeGenericMethod(serviceType).Invoke(null, null)
                : null;
        }
    }
}
