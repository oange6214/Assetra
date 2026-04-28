using Assetra.Core.Interfaces;

namespace Assetra.WPF.Infrastructure;

internal static class SyncDeviceIdProvider
{
    public static string Resolve(IAppSettingsService settings)
    {
        var deviceId = settings.Current.SyncDeviceId;
        return string.IsNullOrWhiteSpace(deviceId) ? "local" : deviceId;
    }
}
