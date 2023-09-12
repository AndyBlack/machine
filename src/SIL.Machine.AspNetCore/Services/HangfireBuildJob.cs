namespace SIL.Machine.AspNetCore.Services;

public abstract class HangfireBuildJob
{
    protected HangfireBuildJob(
        IPlatformService platformService,
        IRepository<TranslationEngine> engines,
        IDistributedReaderWriterLockFactory lockFactory,
        IBuildJobService buildJobService,
        ILogger<HangfireBuildJob> logger
    )
    {
        PlatformService = platformService;
        Engines = engines;
        LockFactory = lockFactory;
        BuildJobService = buildJobService;
        Logger = logger;
    }

    protected IPlatformService PlatformService { get; }
    protected IRepository<TranslationEngine> Engines { get; }
    protected IDistributedReaderWriterLockFactory LockFactory { get; }
    protected IBuildJobService BuildJobService { get; }
    protected ILogger<HangfireBuildJob> Logger { get; }

    public virtual async Task RunAsync(
        string engineId,
        string buildId,
        object? data,
        CancellationToken cancellationToken
    )
    {
        IDistributedReaderWriterLock @lock = await LockFactory.CreateAsync(engineId, cancellationToken);
        bool restarting = false;
        try
        {
            await InitializeAsync(engineId, buildId, data, @lock, cancellationToken);
            await using (await @lock.WriterLockAsync(cancellationToken: cancellationToken))
            {
                if (!await BuildJobService.BuildJobStartedAsync(engineId, buildId, cancellationToken))
                    throw new OperationCanceledException();
            }
            await DoWorkAsync(engineId, buildId, data, @lock, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // Check if the cancellation was initiated by an API call or a shutdown.
            TranslationEngine? engine = await Engines.GetAsync(
                e => e.EngineId == engineId && e.CurrentBuild != null && e.CurrentBuild.Id == buildId,
                cancellationToken
            );
            if (engine?.CurrentBuild?.JobState is BuildJobState.Canceling)
            {
                await using (await @lock.WriterLockAsync(cancellationToken: CancellationToken.None))
                {
                    await BuildJobService.BuildJobFinishedAsync(engineId, buildId, CancellationToken.None);
                }
                await PlatformService.BuildCanceledAsync(buildId, CancellationToken.None);
                Logger.LogInformation("Build canceled ({0})", buildId);
            }
            else
            {
                // the build was canceled, because of a server shutdown
                // switch state back to pending
                restarting = true;
                await using (await @lock.WriterLockAsync(cancellationToken: CancellationToken.None))
                {
                    await BuildJobService.BuildJobRestartingAsync(engineId, buildId, CancellationToken.None);
                }
                await PlatformService.BuildRestartingAsync(buildId, CancellationToken.None);
            }

            throw;
        }
        catch (Exception e)
        {
            await using (await @lock.WriterLockAsync(cancellationToken: CancellationToken.None))
            {
                await BuildJobService.BuildJobFinishedAsync(engineId, buildId, CancellationToken.None);
            }
            await PlatformService.BuildFaultedAsync(buildId, e.Message, CancellationToken.None);
            Logger.LogError(0, e, "Build faulted ({0})", buildId);
            throw;
        }
        finally
        {
            await CleanupAsync(engineId, buildId, data, @lock, restarting, CancellationToken.None);
        }
    }

    protected virtual Task InitializeAsync(
        string engineId,
        string buildId,
        object? data,
        IDistributedReaderWriterLock @lock,
        CancellationToken cancellationToken
    )
    {
        return Task.CompletedTask;
    }

    protected abstract Task DoWorkAsync(
        string engineId,
        string buildId,
        object? data,
        IDistributedReaderWriterLock @lock,
        CancellationToken cancellationToken
    );

    protected virtual Task CleanupAsync(
        string engineId,
        string buildId,
        object? data,
        IDistributedReaderWriterLock @lock,
        bool restarting,
        CancellationToken cancellationToken
    )
    {
        return Task.CompletedTask;
    }
}
