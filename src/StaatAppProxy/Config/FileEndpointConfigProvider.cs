using System.Text.Json;
using StaatAppProxy.Proxy;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace StaatAppProxy.Config;

/// <summary>
/// Loads proxy endpoints from a local JSON or YAML file and reloads them when the file changes.
/// A bad edit is logged and ignored: the last configuration that validated stays in force.
/// </summary>
public sealed class FileEndpointConfigProvider : IEndpointConfigProvider, IDisposable
{
    /// <summary>Paths the proxy serves itself, so they can never be claimed by an endpoint.</summary>
    private static readonly string[] ReservedPrefixes = ["/admin", "/echo", "/healthz", "/dev"];

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private static readonly IDeserializer YamlDeserializer = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    private readonly ILogger<FileEndpointConfigProvider> _log;
    private readonly FileSystemWatcher _watcher;
    private readonly Timer _debounce;

    private volatile IReadOnlyList<ProxyEndpoint> _current = [];

    public FileEndpointConfigProvider(
        IConfiguration configuration,
        IHostEnvironment environment,
        ILogger<FileEndpointConfigProvider> log)
    {
        _log = log;

        var configured = configuration["EndpointConfig:Path"];
        if (string.IsNullOrWhiteSpace(configured))
        {
            configured = "endpoints.json";
        }

        SourcePath = Path.GetFullPath(Path.IsPathRooted(configured)
            ? configured
            : Path.Combine(environment.ContentRootPath, configured));

        // Startup fails loudly: running with no routes is never what anyone wanted.
        _current = Load();
        _log.LogInformation("Loaded {Count} proxy endpoint(s) from {Path}", _current.Count, SourcePath);

        _debounce = new Timer(_ => Reload(), null, Timeout.Infinite, Timeout.Infinite);
        _watcher = new FileSystemWatcher(Path.GetDirectoryName(SourcePath)!, Path.GetFileName(SourcePath))
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
            EnableRaisingEvents = true,
        };
        _watcher.Changed += OnFileChanged;
        _watcher.Created += OnFileChanged;
        _watcher.Renamed += OnFileChanged;
    }

    public string SourcePath { get; }

    public IReadOnlyList<ProxyEndpoint> Current => _current;

    public void Dispose()
    {
        _watcher.Dispose();
        _debounce.Dispose();
    }

    // Editors write a config file in several bursts; collapse them into one reload.
    private void OnFileChanged(object sender, FileSystemEventArgs e) =>
        _debounce.Change(TimeSpan.FromMilliseconds(300), Timeout.InfiniteTimeSpan);

    private void Reload()
    {
        try
        {
            var endpoints = Load();
            _current = endpoints;
            _log.LogInformation("Endpoint config reloaded: {Count} endpoint(s)", endpoints.Count);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Endpoint config reload failed; keeping the previous {Count} endpoint(s)", _current.Count);
        }
    }

    private IReadOnlyList<ProxyEndpoint> Load()
    {
        if (!File.Exists(SourcePath))
        {
            throw new FileNotFoundException($"Endpoint config file not found: {SourcePath}");
        }

        var text = ReadWithRetry(SourcePath);
        var endpoints = Parse(text, SourcePath)
            .Select(Normalize)
            .ToList();

        Validate(endpoints);
        return endpoints;
    }

    private static List<ProxyEndpoint> Parse(string text, string path)
    {
        var isYaml = Path.GetExtension(path).ToLowerInvariant() is ".yml" or ".yaml";

        try
        {
            var file = isYaml
                ? YamlDeserializer.Deserialize<EndpointConfigFile>(new StringReader(text))
                : JsonSerializer.Deserialize<EndpointConfigFile>(text, JsonOptions);

            return file?.Endpoints ?? [];
        }
        catch (Exception ex) when (ex is JsonException or YamlDotNet.Core.YamlException)
        {
            throw new InvalidOperationException($"Could not parse endpoint config '{path}': {ex.Message}", ex);
        }
    }

    // The watcher usually fires while the editor still holds the file open.
    private static string ReadWithRetry(string path)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                return File.ReadAllText(path);
            }
            catch (IOException) when (attempt < 3)
            {
                Thread.Sleep(100);
            }
        }
    }

    private static ProxyEndpoint Normalize(ProxyEndpoint endpoint) => endpoint with
    {
        Name = Clean(endpoint.Name),
        RoutePrefix = NormalizePrefix(endpoint.RoutePrefix),
        BackendBaseUrl = Clean(endpoint.BackendBaseUrl).TrimEnd('/'),
        Mode = Clean(endpoint.Mode).ToLowerInvariant(),
        SseMode = Clean(endpoint.SseMode).ToLowerInvariant(),
        SseConcatField = Clean(endpoint.SseConcatField),
        Auth = Clean(endpoint.Auth).ToLowerInvariant(),
        OboScopes = endpoint.OboScopes?.Where(s => !string.IsNullOrWhiteSpace(s)).Select(Clean).ToArray() ?? [],
        ForwardHeaders = endpoint.ForwardHeaders?.Where(h => !string.IsNullOrWhiteSpace(h)).Select(Clean).ToArray() ?? [],
        Headers = endpoint.Headers?
            .Where(header => !string.IsNullOrWhiteSpace(header.Key))
            .ToDictionary(header => Clean(header.Key), header => Clean(header.Value), StringComparer.OrdinalIgnoreCase)
            ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
    };

    private static string Clean(string? value) => (value ?? "").Trim();

    private static string NormalizePrefix(string? prefix)
    {
        var trimmed = Clean(prefix).Trim('/');
        return trimmed.Length == 0 ? "/" : "/" + trimmed;
    }

    private static void Validate(IReadOnlyList<ProxyEndpoint> endpoints)
    {
        var errors = new List<string>();

        for (var i = 0; i < endpoints.Count; i++)
        {
            var endpoint = endpoints[i];
            var label = endpoint.Name.Length == 0 ? $"endpoints[{i}]" : $"'{endpoint.Name}'";

            if (endpoint.Name.Length == 0)
            {
                errors.Add($"{label}: name is required.");
            }

            if (endpoint.RoutePrefix == "/")
            {
                errors.Add($"{label}: routePrefix must contain at least one path segment, e.g. \"/api/orders\".");
            }
            else if (ReservedPrefixes.Any(reserved =>
                         endpoint.RoutePrefix.Equals(reserved, StringComparison.OrdinalIgnoreCase) ||
                         endpoint.RoutePrefix.StartsWith(reserved + "/", StringComparison.OrdinalIgnoreCase)))
            {
                errors.Add($"{label}: routePrefix \"{endpoint.RoutePrefix}\" is reserved by the proxy " +
                           $"({string.Join(", ", ReservedPrefixes)}).");
            }

            if (!Uri.TryCreate(endpoint.BackendBaseUrl, UriKind.Absolute, out var backend) ||
                (backend.Scheme != Uri.UriSchemeHttp && backend.Scheme != Uri.UriSchemeHttps))
            {
                errors.Add($"{label}: backendBaseUrl must be an absolute http or https URL (got \"{endpoint.BackendBaseUrl}\").");
            }

            if (!ProxyModes.All.Contains(endpoint.Mode))
            {
                errors.Add($"{label}: mode must be one of {Quote(ProxyModes.All)} (got \"{endpoint.Mode}\").");
            }

            if (endpoint.Mode == ProxyModes.Sse && !SseModes.All.Contains(endpoint.SseMode))
            {
                errors.Add($"{label}: sseMode must be one of {Quote(SseModes.All)} (got \"{endpoint.SseMode}\").");
            }

            if (!AuthModes.All.Contains(endpoint.Auth))
            {
                errors.Add($"{label}: auth must be one of {Quote(AuthModes.All)} (got \"{endpoint.Auth}\").");
            }

            if (endpoint.Auth == AuthModes.Obo && endpoint.OboScopes.Length == 0)
            {
                errors.Add($"{label}: oboScopes is required when auth is \"obo\".");
            }

            if (endpoint.TimeoutSeconds <= 0)
            {
                errors.Add($"{label}: timeoutSeconds must be greater than zero (got {endpoint.TimeoutSeconds}).");
            }

            // Said out loud rather than ignored: someone listing "Authorization" here is expecting
            // it to be forwarded, and would otherwise be debugging a header that silently vanished.
            foreach (var header in endpoint.ForwardHeaders.Where(HopByHopHeaders.NeverForward))
            {
                errors.Add($"{label}: forwardHeaders cannot include \"{header}\" — the proxy sets that " +
                           "header itself. Use \"auth\" for Authorization, or \"headers\" to pin a value.");
            }
        }

        errors.AddRange(endpoints
            .Where(e => e.Name.Length > 0)
            .GroupBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => $"duplicate name \"{g.Key}\"."));

        errors.AddRange(endpoints
            .GroupBy(e => e.RoutePrefix, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => $"duplicate routePrefix \"{g.Key}\"."));

        if (errors.Count > 0)
        {
            throw new InvalidOperationException(
                "Invalid endpoint configuration:" + Environment.NewLine +
                string.Join(Environment.NewLine, errors.Select(e => "  - " + e)));
        }
    }

    private static string Quote(string[] values) => string.Join(", ", values.Select(v => $"\"{v}\""));

    private sealed record EndpointConfigFile
    {
        public List<ProxyEndpoint>? Endpoints { get; init; }
    }
}
