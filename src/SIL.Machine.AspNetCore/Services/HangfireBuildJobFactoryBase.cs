namespace SIL.Machine.AspNetCore.Services;

public abstract class HangfireBuildJobFactoryBase : IHangfireBuildJobFactory
{
    private readonly IBackgroundJobClient _jobClient;

    protected HangfireBuildJobFactoryBase(IBackgroundJobClient jobClient)
    {
        _jobClient = jobClient;
    }

    public abstract TranslationEngineType EngineType { get; }

    public abstract Task<string> CreateJobAsync(
        string engineId,
        string buildId,
        string stage,
        object? data,
        CancellationToken cancellationToken = default
    );

    protected string CreateJob<T>(string engineId, string buildId, object? data)
        where T : HangfireBuildJob
    {
        // Token "None" is used here because hangfire injects the proper cancellation token
        return _jobClient.Schedule<T>(
            j => j.RunAsync(engineId, buildId, data, CancellationToken.None),
            TimeSpan.FromDays(10000)
        );
    }
}
