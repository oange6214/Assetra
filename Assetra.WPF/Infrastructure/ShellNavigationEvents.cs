namespace Assetra.WPF.Infrastructure;

/// <summary>
/// Shell-level navigation event aggregator. Lets feature VMs (e.g. transaction
/// dialog success-toast「查看交易」action) request navigation to a navrail page
/// without taking a hard dependency on the navrail VM through the entire DI tree.
///
/// <para>
/// Usage：
///   <c>ShellNavigationEvents.RequestNavigateTo("TransactionLog");</c>
/// NavRailViewModel subscribes to <see cref="NavigationRequested"/> at startup
/// and forwards the section name to its own NavigateTo handler.
/// </para>
///
/// <para>
/// Section names match the <c>NavSection</c> enum string values. Use the enum
/// in callers via <c>nameof(NavSection.TransactionLog)</c> for compile-time
/// safety; the static class itself only sees strings to avoid pulling the
/// Shell namespace into Portfolio code.
/// </para>
/// </summary>
public static class ShellNavigationEvents
{
    /// <summary>
    /// Raised when any feature wants the shell to switch the active navrail page.
    /// Subscribers are NavRailViewModel (the only one expected) — if no listener,
    /// the request is silently dropped, which is safe behaviour for tests.
    /// </summary>
    public static event Action<string>? NavigationRequested;

    /// <summary>Raises <see cref="NavigationRequested"/> with the given section name.</summary>
    public static void RequestNavigateTo(string sectionName) =>
        NavigationRequested?.Invoke(sectionName);
}
