namespace ClipSync.WindowsApp.Server;

/// <summary>
/// Wakes every registered iOS device whenever the clipboard changes, regardless
/// of which client caused the change — the woken app then pulls the real
/// content itself via GET /clipboard/latest.
/// </summary>
internal sealed class PushNotificationService
{
    private readonly DeviceStore _devices;
    private readonly IPushSender _sender;

    public PushNotificationService(DeviceStore devices, IPushSender sender)
    {
        _devices = devices;
        _sender = sender;
    }

    public async Task NotifyAllAsync()
    {
        if (!_sender.IsConfigured)
        {
            return;
        }

        foreach (var token in _devices.GetAll())
        {
            try
            {
                await _sender.SendSilentPushAsync(token);
            }
            catch (HttpRequestException)
            {
                // Network hiccup or an APNs error for this one device; the next
                // clipboard change will retry, so just move on to the rest.
            }
        }
    }
}
