using System.Collections.Concurrent;

namespace SkyWatch.Api.Services;

public class ApiStatusService
{
    private readonly ConcurrentDictionary<string, DataSourceStatus> _statuses = new();

    public void ReportSuccess(string source, int itemCount, int? httpStatusCode = null)
    {
        _statuses.AddOrUpdate(source,
            _ => new DataSourceStatus
            {
                Source = source,
                LastSuccess = DateTime.UtcNow,
                LastAttempt = DateTime.UtcNow,
                LastItemCount = itemCount,
                LastHttpStatus = httpStatusCode,
                LastError = null,
                ConsecutiveFailures = 0
            },
            (_, existing) =>
            {
                existing.LastSuccess = DateTime.UtcNow;
                existing.LastAttempt = DateTime.UtcNow;
                existing.LastItemCount = itemCount;
                existing.LastHttpStatus = httpStatusCode;
                existing.LastError = null;
                existing.ConsecutiveFailures = 0;
                return existing;
            });
    }

    public void ReportFailure(string source, string error, int? httpStatusCode = null)
    {
        _statuses.AddOrUpdate(source,
            _ => new DataSourceStatus
            {
                Source = source,
                LastAttempt = DateTime.UtcNow,
                LastError = error,
                LastHttpStatus = httpStatusCode,
                ConsecutiveFailures = 1
            },
            (_, existing) =>
            {
                existing.LastAttempt = DateTime.UtcNow;
                existing.LastError = error;
                existing.LastHttpStatus = httpStatusCode;
                existing.ConsecutiveFailures++;
                return existing;
            });
    }

    public Dictionary<string, DataSourceStatus> GetAllStatuses() =>
        new(_statuses);
}

public class DataSourceStatus
{
    public string Source { get; set; } = "";
    public DateTime? LastSuccess { get; set; }
    public DateTime? LastAttempt { get; set; }
    public int LastItemCount { get; set; }
    public int? LastHttpStatus { get; set; }
    public string? LastError { get; set; }
    public int ConsecutiveFailures { get; set; }
}
