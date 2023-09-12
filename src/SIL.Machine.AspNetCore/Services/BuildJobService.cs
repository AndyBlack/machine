namespace SIL.Machine.AspNetCore.Services;

public class BuildJobService : IBuildJobService
{
    private readonly Dictionary<BuildJobType, IBuildJobRunner> _runners;
    private readonly IRepository<TranslationEngine> _engines;

    public BuildJobService(
        IEnumerable<IBuildJobRunner> runners,
        IRepository<TranslationEngine> engines,
        IOptions<BuildJobOptions> options
    )
    {
        Dictionary<BuildJobRunnerType, IBuildJobRunner> runnerDictionary = runners.ToDictionary(r => r.Type);
        _runners = new Dictionary<BuildJobType, IBuildJobRunner>();
        foreach (KeyValuePair<BuildJobType, BuildJobRunnerType> kvp in options.Value.Runners)
            _runners.Add(kvp.Key, runnerDictionary[kvp.Value]);
        _engines = engines;
    }

    public async Task<Build?> GetBuildAsync(
        string engineId,
        string buildId,
        CancellationToken cancellationToken = default
    )
    {
        TranslationEngine? engine = await _engines.GetAsync(
            e => e.EngineId == engineId && e.CurrentBuild != null && e.CurrentBuild.Id == buildId,
            cancellationToken
        );
        return engine?.CurrentBuild;
    }

    public async Task CreateEngineAsync(
        IEnumerable<BuildJobType> jobTypes,
        string engineId,
        string? name = null,
        CancellationToken cancellationToken = default
    )
    {
        foreach (BuildJobType jobType in jobTypes)
        {
            IBuildJobRunner runner = _runners[jobType];
            await runner.CreateEngineAsync(engineId, name, cancellationToken);
        }
    }

    public async Task DeleteEngineAsync(
        IEnumerable<BuildJobType> jobTypes,
        string engineId,
        CancellationToken cancellationToken = default
    )
    {
        foreach (BuildJobType jobType in jobTypes)
        {
            IBuildJobRunner runner = _runners[jobType];
            await runner.DeleteEngineAsync(engineId, cancellationToken);
        }
    }

    public async Task StartBuildJobAsync(
        BuildJobType jobType,
        TranslationEngineType engineType,
        string engineId,
        string buildId,
        string stage,
        object? data = null,
        CancellationToken cancellationToken = default
    )
    {
        IBuildJobRunner runner = _runners[jobType];
        string jobId = await runner.CreateJobAsync(engineType, engineId, buildId, stage, data, cancellationToken);
        try
        {
            await _engines.UpdateAsync(
                e => e.EngineId == engineId,
                u =>
                    u.Set(
                        e => e.CurrentBuild,
                        new Build
                        {
                            Id = buildId,
                            JobId = jobId,
                            JobType = jobType,
                            Stage = stage
                        }
                    ),
                cancellationToken: cancellationToken
            );
            await runner.EnqueueJobAsync(jobId, cancellationToken);
        }
        catch
        {
            await runner.DeleteJobAsync(jobId, CancellationToken.None);
            throw;
        }
    }

    public async Task<(string? BuildId, BuildJobState State)> CancelBuildJobAsync(
        string engineId,
        CancellationToken cancellationToken = default
    )
    {
        TranslationEngine? engine = await _engines.GetAsync(
            e => e.EngineId == engineId && e.CurrentBuild != null,
            cancellationToken
        );
        if (engine is null || engine.CurrentBuild is null)
            return (null, BuildJobState.None);

        IBuildJobRunner runner = _runners[engine.CurrentBuild.JobType];

        if (engine.CurrentBuild.JobState is BuildJobState.Pending)
        {
            // cancel a job that hasn't started yet
            engine = await _engines.UpdateAsync(
                e => e.EngineId == engineId && e.CurrentBuild != null,
                u => u.Unset(b => b.CurrentBuild),
                returnOriginal: true,
                cancellationToken: cancellationToken
            );
            if (engine is not null && engine.CurrentBuild is not null)
            {
                // job will be deleted from the queue
                await runner.StopJobAsync(engine.CurrentBuild.JobId, CancellationToken.None);
                return (engine.CurrentBuild.Id, BuildJobState.None);
            }
        }
        else if (engine.CurrentBuild.JobState is BuildJobState.Active)
        {
            // cancel a job that is already running
            engine = await _engines.UpdateAsync(
                e => e.EngineId == engineId && e.CurrentBuild != null,
                u => u.Set(e => e.CurrentBuild!.JobState, BuildJobState.Canceling),
                cancellationToken: cancellationToken
            );
            if (engine is not null && engine.CurrentBuild is not null)
            {
                // Trigger the cancellation token for the job
                await runner.StopJobAsync(engine.CurrentBuild.JobId, CancellationToken.None);
                return (engine.CurrentBuild.Id, BuildJobState.Canceling);
            }
        }

        return (null, BuildJobState.None);
    }

    public async Task<bool> BuildJobStartedAsync(
        string engineId,
        string buildId,
        CancellationToken cancellationToken = default
    )
    {
        TranslationEngine? engine = await _engines.UpdateAsync(
            e =>
                e.EngineId == engineId
                && e.CurrentBuild != null
                && e.CurrentBuild.Id == buildId
                && e.CurrentBuild.JobState == BuildJobState.Pending,
            u => u.Set(e => e.CurrentBuild!.JobState, BuildJobState.Active),
            cancellationToken: cancellationToken
        );
        return engine is not null;
    }

    public Task BuildJobFinishedAsync(string engineId, string buildId, CancellationToken cancellationToken = default)
    {
        return _engines.UpdateAsync(
            e => e.EngineId == engineId && e.CurrentBuild != null && e.CurrentBuild.Id == buildId,
            u => u.Unset(e => e.CurrentBuild),
            cancellationToken: cancellationToken
        );
    }

    public Task BuildJobRestartingAsync(string engineId, string buildId, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }
}
