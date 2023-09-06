namespace SIL.Machine.AspNetCore.Services;

public class NmtEngineService : ITranslationEngineService
{
    private readonly IBackgroundJobClient _jobClient;
    private readonly IDistributedReaderWriterLockFactory _lockFactory;
    private readonly IPlatformService _platformService;
    private readonly IDataAccessContext _dataAccessContext;
    private readonly IRepository<TranslationEngine> _engines;
    private readonly INmtJobService _nmtJobService;
    private readonly ISharedFileService _sharedFileService;
    private readonly ILogger<NmtEngineService> _logger;

    public NmtEngineService(
        IBackgroundJobClient jobClient,
        IPlatformService platformService,
        IDistributedReaderWriterLockFactory lockFactory,
        IDataAccessContext dataAccessContext,
        IRepository<TranslationEngine> engines,
        INmtJobService nmtJobService,
        ISharedFileService sharedFileService,
        ILogger<NmtEngineService> logger
    )
    {
        _jobClient = jobClient;
        _lockFactory = lockFactory;
        _platformService = platformService;
        _dataAccessContext = dataAccessContext;
        _engines = engines;
        _nmtJobService = nmtJobService;
        _sharedFileService = sharedFileService;
        _logger = logger;
    }

    public TranslationEngineType Type => TranslationEngineType.Nmt;

    public async Task CreateAsync(
        string engineId,
        string? engineName,
        string sourceLanguage,
        string targetLanguage,
        CancellationToken cancellationToken = default
    )
    {
        await _engines.InsertAsync(
            new TranslationEngine
            {
                EngineId = engineId,
                SourceLanguage = sourceLanguage,
                TargetLanguage = targetLanguage
            },
            cancellationToken
        );
        await _nmtJobService.CreateEngineAsync(engineId, engineName, cancellationToken: CancellationToken.None);
    }

    public async Task DeleteAsync(string engineId, CancellationToken cancellationToken = default)
    {
        await _dataAccessContext.BeginTransactionAsync(cancellationToken);
        await _engines.DeleteAsync(e => e.EngineId == engineId, cancellationToken);
        await _lockFactory.DeleteAsync(engineId, cancellationToken);
        await _dataAccessContext.CommitTransactionAsync(CancellationToken.None);
        await _nmtJobService.DeleteEngineAsync(engineId, cancellationToken: CancellationToken.None);
    }

    public async Task StartBuildAsync(
        string engineId,
        string buildId,
        IReadOnlyList<Corpus> corpora,
        CancellationToken cancellationToken = default
    )
    {
        IDistributedReaderWriterLock @lock = await _lockFactory.CreateAsync(engineId, cancellationToken);
        await using (await @lock.WriterLockAsync(cancellationToken: cancellationToken))
        {
            TranslationEngine? engine = await _engines.UpdateAsync(
                e => e.EngineId == engineId && e.BuildState == BuildState.None,
                u =>
                    u.Set(e => e.BuildState, BuildState.Pending)
                        .Set(e => e.IsCanceled, false)
                        .Unset(e => e.JobId)
                        .Set(e => e.BuildId, buildId),
                cancellationToken: CancellationToken.None
            );
            // If there is a pending job, then no need to start a new one.
            if (engine is null)
                throw new InvalidOperationException("The engine is already building or pending.");

            // Token "None" is used here because hangfire injects the proper cancellation token
            _jobClient.Enqueue<NmtEnginePipeline>(
                p => p.PreprocessAsync(engineId, buildId, corpora, CancellationToken.None)
            );
        }
    }

    public async Task CancelBuildAsync(string engineId, CancellationToken cancellationToken = default)
    {
        IDistributedReaderWriterLock @lock = await _lockFactory.CreateAsync(engineId, cancellationToken);
        await using (await @lock.WriterLockAsync(cancellationToken: cancellationToken))
        {
            await _dataAccessContext.BeginTransactionAsync(cancellationToken);
            TranslationEngine? engine = await _engines.GetAsync(
                e => e.EngineId == engineId && e.BuildState != BuildState.None,
                cancellationToken
            );
            if (engine is null || engine.BuildId is null)
                return;

            if (engine.BuildState is BuildState.Pending)
            {
                if (engine.JobId is null)
                {
                    // cancel a job that hasn't been enqueued
                    await _engines.UpdateAsync(
                        e => e.EngineId == engineId,
                        u => u.Set(e => e.BuildState, BuildState.None).Unset(e => e.BuildId),
                        cancellationToken: cancellationToken
                    );
                    await _platformService.BuildCanceledAsync(engine.BuildId, CancellationToken.None);
                }
                else
                {
                    // cancel a job that has been enqueued, but not started
                    await _engines.UpdateAsync(
                        e => e.EngineId == engineId,
                        u => u.Set(e => e.BuildState, BuildState.None).Unset(e => e.BuildId).Unset(e => e.JobId),
                        cancellationToken: cancellationToken
                    );
                    await _nmtJobService.StopJobAsync(engine.JobId, CancellationToken.None);
                    await _platformService.BuildCanceledAsync(engine.BuildId, CancellationToken.None);
                }
            }
            else if (engine.JobId is not null)
            {
                // cancel a job that has started
                await _engines.UpdateAsync(
                    e => e.EngineId == engineId,
                    u => u.Set(e => e.IsCanceled, true),
                    cancellationToken: cancellationToken
                );
                await _nmtJobService.StopJobAsync(engine.JobId, CancellationToken.None);
            }
            await _dataAccessContext.CommitTransactionAsync(CancellationToken.None);
        }
    }

    public Task<IReadOnlyList<TranslationResult>> TranslateAsync(
        string engineId,
        int n,
        string segment,
        CancellationToken cancellationToken = default
    )
    {
        throw new NotSupportedException();
    }

    public Task<WordGraph> GetWordGraphAsync(
        string engineId,
        string segment,
        CancellationToken cancellationToken = default
    )
    {
        throw new NotSupportedException();
    }

    public Task TrainSegmentPairAsync(
        string engineId,
        string sourceSegment,
        string targetSegment,
        bool sentenceStart,
        CancellationToken cancellationToken = default
    )
    {
        throw new NotSupportedException();
    }

    public async Task<bool> BuildStartedAsync(
        string engineId,
        string buildId,
        CancellationToken cancellationToken = default
    )
    {
        IDistributedReaderWriterLock @lock = await _lockFactory.CreateAsync(engineId, cancellationToken);
        await using (await @lock.WriterLockAsync(cancellationToken: cancellationToken))
        {
            TranslationEngine? engine = await _engines.UpdateAsync(
                e => e.EngineId == engineId && e.BuildId == buildId && !e.IsCanceled,
                u => u.Set(e => e.BuildState, BuildState.Active),
                cancellationToken: cancellationToken
            );
            if (engine is null)
                return false;
        }
        await _platformService.BuildStartedAsync(buildId, CancellationToken.None);
        _logger.LogInformation("Build started ({0})", buildId);
        return true;
    }

    public Task BuildCompletedAsync(
        string engineId,
        string buildId,
        int corpusSize,
        double confidence,
        CancellationToken cancellationToken = default
    )
    {
        // Token "None" is used here because hangfire injects the proper cancellation token
        _jobClient.Enqueue<NmtEnginePipeline>(
            p => p.PostprocessAsync(engineId, buildId, corpusSize, confidence, CancellationToken.None)
        );
        return Task.CompletedTask;
    }

    public async Task BuildFaultedAsync(
        string engineId,
        string buildId,
        string message,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            IDistributedReaderWriterLock @lock = await _lockFactory.CreateAsync(engineId, cancellationToken);
            await using (await @lock.WriterLockAsync(cancellationToken: cancellationToken))
            {
                await _engines.UpdateAsync(
                    e => e.EngineId == engineId && e.BuildId == buildId,
                    u =>
                        u.Set(e => e.BuildState, BuildState.None)
                            .Set(e => e.IsCanceled, false)
                            .Unset(e => e.JobId)
                            .Unset(e => e.BuildId),
                    cancellationToken: cancellationToken
                );
            }

            await _platformService.BuildFaultedAsync(buildId, message, CancellationToken.None);
            _logger.LogError("Build faulted ({0}). Error: {1}", buildId, message);
        }
        finally
        {
            try
            {
                await _sharedFileService.DeleteAsync($"builds/{buildId}/", cancellationToken);
            }
            catch (Exception e)
            {
                _logger.LogWarning(e, "Unable to to delete job data for build {0}.", buildId);
            }
        }
    }

    public async Task BuildCanceledAsync(string engineId, string buildId, CancellationToken cancellationToken = default)
    {
        try
        {
            IDistributedReaderWriterLock @lock = await _lockFactory.CreateAsync(engineId, cancellationToken);
            await using (await @lock.WriterLockAsync(cancellationToken: cancellationToken))
            {
                await _engines.UpdateAsync(
                    e => e.EngineId == engineId && e.BuildId == buildId,
                    u =>
                        u.Set(e => e.BuildState, BuildState.None)
                            .Set(e => e.IsCanceled, false)
                            .Unset(e => e.JobId)
                            .Unset(e => e.BuildId),
                    returnOriginal: true,
                    cancellationToken: cancellationToken
                );
            }

            await _platformService.BuildCanceledAsync(buildId, CancellationToken.None);
            _logger.LogInformation("Build canceled ({0})", buildId);
        }
        finally
        {
            try
            {
                await _sharedFileService.DeleteAsync($"builds/{buildId}/", cancellationToken);
            }
            catch (Exception e)
            {
                _logger.LogError(
                    e,
                    $"Unable to access S3 bucket to delete clearml job {buildId} because it threw the exception."
                );
            }
        }
    }

    public Task UpdateBuildStatus(
        string engineId,
        string buildId,
        ProgressStatus progressStatus,
        CancellationToken cancellationToken = default
    )
    {
        return _platformService.UpdateBuildStatusAsync(buildId, progressStatus, cancellationToken);
    }
}
