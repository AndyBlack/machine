public class S3HealthCheck : IHealthCheck
{
    private readonly ISharedFileService _sharedFileService;
    private readonly ILogger _logger;

    public S3HealthCheck(ISharedFileService sharedFileService, ILoggerFactory loggerFactory)
    {
        _sharedFileService = sharedFileService;
        _logger = loggerFactory.CreateLogger<S3HealthCheck>();
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            await _sharedFileService.Ls("/models/");
            return HealthCheckResult.Healthy("The S3 bucket is available");
        }
        catch (Exception e)
        {
            if (
                e is HttpRequestException httpRequestException
                && httpRequestException.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized
            )
            {
                _logger.LogError(
                    "S3 bucket is not available because of an authentication error. Please verify that credentials are valid."
                );
                return HealthCheckResult.Unhealthy(
                    "S3 bucket is not available because of an authentication error. Please verify that credentials are valid."
                );
            }
            else
            {
                _logger.LogError("S3 bucket is not available. The following exception occurred: " + e.Message);
                return HealthCheckResult.Unhealthy(
                    "S3 bucket is not available. The following exception occurred: " + e.Message
                );
            }
        }
    }
}
