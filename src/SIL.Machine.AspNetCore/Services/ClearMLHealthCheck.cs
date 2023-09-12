public class ClearMLHealthCheck : IHealthCheck
{
    private readonly IBuildJobRunner _clearMLService;

    public ClearMLHealthCheck(IBuildJobRunner clearMLService, ILoggerFactory loggerFactory)
    {
        _clearMLService = clearMLService;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            bool success = await _clearMLService.PingAsync();
            if (!success)
                return HealthCheckResult.Unhealthy("ClearML is unresponsive");
            bool workersAvailable = await _clearMLService.WorkersAreAssignedToQueue();
            if (!workersAvailable)
                return HealthCheckResult.Unhealthy("No ClearML agents are available");
            return HealthCheckResult.Healthy("ClearML is available");
        }
        catch (Exception e)
        {
            return HealthCheckResult.Unhealthy(exception: e);
        }
    }
}
