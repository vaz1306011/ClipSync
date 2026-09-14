namespace ClipSync.RelayServer;

/// <summary>
/// All values come from environment variables (set in the Synology Container
/// Manager UI or a docker-compose.yml) — nothing sensitive lives in this repo.
/// </summary>
internal sealed class RelayConfig
{
    public required string RelayKey { get; init; }
    public required string ApnsKeyId { get; init; }
    public required string ApnsTeamId { get; init; }
    public required string ApnsBundleId { get; init; }
    public required string ApnsPrivateKeyPath { get; init; }
    public bool ApnsUseSandbox { get; init; }

    public static RelayConfig FromEnvironment()
    {
        return new RelayConfig
        {
            RelayKey = Require("RELAY_KEY"),
            ApnsKeyId = Require("APNS_KEY_ID"),
            ApnsTeamId = Require("APNS_TEAM_ID"),
            ApnsBundleId = Require("APNS_BUNDLE_ID"),
            ApnsPrivateKeyPath = Require("APNS_PRIVATE_KEY_PATH"),
            ApnsUseSandbox = Environment.GetEnvironmentVariable("APNS_USE_SANDBOX") == "true"
        };
    }

    private static string Require(string name) =>
        Environment.GetEnvironmentVariable(name)
        ?? throw new InvalidOperationException($"Missing required environment variable: {name}");
}
