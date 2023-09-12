namespace SIL.Machine.AspNetCore.Services;

public class HangfireBuildJobRunner : IBuildJobRunner
{
    private readonly IBackgroundJobClient _jobClient;
    private readonly Dictionary<TranslationEngineType, IHangfireBuildJobFactory> _buildJobFactories;

    public HangfireBuildJobRunner(
        IBackgroundJobClient jobClient,
        IEnumerable<IHangfireBuildJobFactory> buildJobFactories
    )
    {
        _jobClient = jobClient;
        _buildJobFactories = buildJobFactories.ToDictionary(f => f.EngineType);
    }

    public BuildJobRunnerType Type => BuildJobRunnerType.Hangfire;

    public Task CreateEngineAsync(string engineId, string? name = null, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public Task DeleteEngineAsync(string engineId, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public async Task<string> CreateJobAsync(
        TranslationEngineType engineType,
        string engineId,
        string buildId,
        string stage,
        object? data = null,
        CancellationToken cancellationToken = default
    )
    {
        IHangfireBuildJobFactory buildJobFactory = _buildJobFactories[engineType];
        return await buildJobFactory.CreateJobAsync(engineId, buildId, stage, data);
    }

    public Task<bool> DeleteJobAsync(string jobId, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_jobClient.Delete(jobId));
    }

    public Task<bool> EnqueueJobAsync(string jobId, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_jobClient.Requeue(jobId));
    }

    public Task<bool> PingAsync(CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    public Task<bool> StopJobAsync(string jobId, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_jobClient.Delete(jobId));
    }

    public Task<bool> WorkersAreAssignedToQueue(CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }
}
