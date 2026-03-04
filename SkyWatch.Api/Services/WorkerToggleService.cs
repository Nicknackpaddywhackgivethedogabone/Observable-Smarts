namespace SkyWatch.Api.Services;

/// <summary>
/// Singleton service that tracks which data streams are enabled.
/// Workers check this before fetching external data.
/// All streams default to disabled — data only flows when the user toggles a layer ON.
/// </summary>
public class WorkerToggleService
{
    private readonly Dictionary<string, bool> _streams = new()
    {
        ["satellites"] = false,
        ["flights"] = false,
        ["ships"] = false,
        ["imagery"] = false
    };

    private readonly object _lock = new();

    public bool IsEnabled(string stream)
    {
        lock (_lock)
        {
            return _streams.TryGetValue(stream, out var enabled) && enabled;
        }
    }

    public void SetEnabled(string stream, bool enabled)
    {
        lock (_lock)
        {
            _streams[stream] = enabled;
        }
    }

    public Dictionary<string, bool> GetStatus()
    {
        lock (_lock)
        {
            return new Dictionary<string, bool>(_streams);
        }
    }
}
