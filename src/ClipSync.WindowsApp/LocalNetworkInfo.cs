using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace ClipSync.WindowsApp;

internal static class LocalNetworkInfo
{
    /// <summary>
    /// Best-guess LAN IPv4 address, so the pairing QR code can embed a URL
    /// the phone can actually reach without the user having to look it up.
    /// </summary>
    public static string? GetLocalIPv4()
    {
        var candidates = NetworkInterface.GetAllNetworkInterfaces()
            .Where(nic => nic.OperationalStatus == OperationalStatus.Up)
            .Where(nic => nic.NetworkInterfaceType != NetworkInterfaceType.Loopback)
            .SelectMany(nic => nic.GetIPProperties().UnicastAddresses)
            .Where(addr => addr.Address.AddressFamily == AddressFamily.InterNetwork)
            .Select(addr => addr.Address.ToString())
            .Where(ip => !ip.StartsWith("169.254.")); // APIPA, not a real LAN address

        return candidates.FirstOrDefault();
    }
}
