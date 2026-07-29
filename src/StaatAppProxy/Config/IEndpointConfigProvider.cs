namespace StaatAppProxy.Config;

/// <summary>
/// Supplies the endpoint list currently in force. Implementations must be safe to read from many
/// threads while the configuration is being replaced.
/// </summary>
/// <remarks>
/// Today the only implementation reads a local file. When the configuration moves to a remote
/// service, add an implementation here and change the registration in Program.cs — nothing else
/// in the proxy needs to know where the endpoints came from.
/// </remarks>
public interface IEndpointConfigProvider
{
    /// <summary>The endpoints in force right now. Never null; may be empty.</summary>
    IReadOnlyList<ProxyEndpoint> Current { get; }

    /// <summary>Where the configuration came from, shown in the admin UI.</summary>
    string SourcePath { get; }
}
