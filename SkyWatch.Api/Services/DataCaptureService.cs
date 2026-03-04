using System.Collections.Concurrent;
using System.Text.Json;

namespace SkyWatch.Api.Services;

/// <summary>
/// Server-side data capture service. When enabled via web toggle, writes raw API
/// response data to per-stream log files for offline review.
/// </summary>
public class DataCaptureService
{
    private readonly ILogger<DataCaptureService> _logger;
    private readonly string _logDirectory;
    private volatile bool _enabled;
    private readonly ConcurrentDictionary<string, StreamWriter> _writers = new();
    private readonly object _lock = new();

    public bool IsEnabled => _enabled;

    public DataCaptureService(ILogger<DataCaptureService> logger, IConfiguration configuration)
    {
        _logger = logger;
        _logDirectory = configuration["DataCapture:LogDirectory"]
            ?? Path.Combine(AppContext.BaseDirectory, "capture-logs");
    }

    public void SetEnabled(bool enabled)
    {
        if (enabled == _enabled) return;

        lock (_lock)
        {
            if (enabled)
            {
                Directory.CreateDirectory(_logDirectory);
                _enabled = true;
                _logger.LogInformation("Data capture ENABLED. Logs → {Dir}", _logDirectory);
            }
            else
            {
                _enabled = false;
                FlushAndCloseAll();
                _logger.LogInformation("Data capture DISABLED. Writers closed.");
            }
        }
    }

    /// <summary>
    /// Log a data snapshot for a given stream (e.g. "flights", "satellites", "ships", "imagery", "airspace").
    /// </summary>
    public void LogData(string streamName, object data)
    {
        if (!_enabled) return;

        try
        {
            var writer = GetOrCreateWriter(streamName);
            var entry = new
            {
                timestamp = DateTime.UtcNow.ToString("o"),
                stream = streamName,
                data
            };
            var json = JsonSerializer.Serialize(entry, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = false
            });

            lock (writer)
            {
                writer.WriteLine(json);
                writer.Flush();
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to write capture log for {Stream}", streamName);
        }
    }

    public DataCaptureStatus GetStatus()
    {
        var files = new Dictionary<string, DataCaptureFileInfo>();

        if (Directory.Exists(_logDirectory))
        {
            foreach (var file in Directory.GetFiles(_logDirectory, "*.jsonl"))
            {
                var fi = new FileInfo(file);
                files[Path.GetFileNameWithoutExtension(file)] = new DataCaptureFileInfo
                {
                    FileName = fi.Name,
                    SizeBytes = fi.Length,
                    LastModified = fi.LastWriteTimeUtc
                };
            }
        }

        return new DataCaptureStatus
        {
            Enabled = _enabled,
            LogDirectory = _logDirectory,
            Files = files
        };
    }

    public (Stream? stream, string? fileName) GetLogFile(string streamName)
    {
        var path = Path.Combine(_logDirectory, $"{streamName}.jsonl");
        if (!File.Exists(path)) return (null, null);

        // Flush the writer first if active
        if (_writers.TryGetValue(streamName, out var writer))
        {
            lock (writer)
            {
                writer.Flush();
            }
        }

        var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        return (fs, $"{streamName}.jsonl");
    }

    public void ClearLogs()
    {
        lock (_lock)
        {
            FlushAndCloseAll();
            if (Directory.Exists(_logDirectory))
            {
                foreach (var file in Directory.GetFiles(_logDirectory, "*.jsonl"))
                {
                    try { File.Delete(file); } catch { /* best effort */ }
                }
            }
        }
    }

    private StreamWriter GetOrCreateWriter(string streamName)
    {
        return _writers.GetOrAdd(streamName, name =>
        {
            Directory.CreateDirectory(_logDirectory);
            var path = Path.Combine(_logDirectory, $"{name}.jsonl");
            return new StreamWriter(path, append: true) { AutoFlush = false };
        });
    }

    private void FlushAndCloseAll()
    {
        foreach (var (name, writer) in _writers)
        {
            try
            {
                lock (writer)
                {
                    writer.Flush();
                    writer.Close();
                }
            }
            catch { /* best effort */ }
        }
        _writers.Clear();
    }
}

public class DataCaptureStatus
{
    public bool Enabled { get; set; }
    public string LogDirectory { get; set; } = "";
    public Dictionary<string, DataCaptureFileInfo> Files { get; set; } = new();
}

public class DataCaptureFileInfo
{
    public string FileName { get; set; } = "";
    public long SizeBytes { get; set; }
    public DateTime LastModified { get; set; }
}
