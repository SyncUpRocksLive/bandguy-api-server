namespace SyncUpRocks.Types;

public enum Health
{
    Healthy,
    Unhealthy
}

public record HealthReport(
    string System,
    Health SystemStatus,
    bool RequiresRestart,
    string Message
);

public interface IHealthCheck
{
    public Task<HealthReport> GetReport(CancellationToken cancellationToken = default);
}
