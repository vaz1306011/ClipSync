namespace ClipSync.WindowsApp.Server;

/// <summary>
/// Wakes an iOS device via a silent push. Two implementations: ApnsClient talks
/// to Apple directly using a local .p8 (fine for personal use), RelayPushClient
/// forwards the request to a relay server that holds the .p8 instead — the
/// only way to distribute this app without shipping the private key with it.
/// </summary>
internal interface IPushSender
{
    bool IsConfigured { get; }

    Task<bool> SendSilentPushAsync(string deviceToken);
}
