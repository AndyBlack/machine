namespace SIL.Machine.AspNetCore.Services;

public class SmtTransferEngineService : ITranslationEngineService
{
    private readonly IBackgroundJobClient _jobClient;
    private readonly IDistributedReaderWriterLockFactory _lockFactory;
    private readonly IPlatformService _platformService;
    private readonly IDataAccessContext _dataAccessContext;
    private readonly IRepository<TranslationEngine> _engines;
    private readonly IRepository<TrainSegmentPair> _trainSegmentPairs;
    private readonly SmtTransferEngineStateService _stateService;

    public SmtTransferEngineService(
        IBackgroundJobClient jobClient,
        IDistributedReaderWriterLockFactory lockFactory,
        IPlatformService platformService,
        IDataAccessContext dataAccessContext,
        IRepository<TranslationEngine> engines,
        IRepository<TrainSegmentPair> trainSegmentPairs,
        SmtTransferEngineStateService stateService
    )
    {
        _jobClient = jobClient;
        _lockFactory = lockFactory;
        _platformService = platformService;
        _dataAccessContext = dataAccessContext;
        _engines = engines;
        _trainSegmentPairs = trainSegmentPairs;
        _stateService = stateService;
    }

    public TranslationEngineType Type => TranslationEngineType.SmtTransfer;

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

        SmtTransferEngineState state = _stateService.Get(engineId);
        IDistributedReaderWriterLock @lock = await _lockFactory.CreateAsync(engineId, CancellationToken.None);
        await using (await @lock.WriterLockAsync(cancellationToken: CancellationToken.None))
        {
            state.InitNew();
        }
    }

    public async Task DeleteAsync(string engineId, CancellationToken cancellationToken = default)
    {
        await _dataAccessContext.BeginTransactionAsync(cancellationToken);
        await _engines.DeleteAsync(e => e.EngineId == engineId, cancellationToken);
        await _lockFactory.DeleteAsync(engineId, cancellationToken);
        await _dataAccessContext.CommitTransactionAsync(CancellationToken.None);

        if (_stateService.TryRemove(engineId, out SmtTransferEngineState? state))
        {
            IDistributedReaderWriterLock @lock = await _lockFactory.CreateAsync(engineId, CancellationToken.None);
            await using (await @lock.WriterLockAsync(cancellationToken: CancellationToken.None))
            {
                // ensure that there is no build running before unloading
                string? buildId = await CancelBuildInternalAsync(engineId, CancellationToken.None);
                if (buildId is not null)
                    await WaitForBuildToFinishAsync(engineId, buildId, CancellationToken.None);

                await state.DeleteDataAsync();
                await state.DisposeAsync();
            }
        }
    }

    public async Task<IReadOnlyList<TranslationResult>> TranslateAsync(
        string engineId,
        int n,
        string segment,
        CancellationToken cancellationToken = default
    )
    {
        SmtTransferEngineState state = _stateService.Get(engineId);
        IDistributedReaderWriterLock @lock = await _lockFactory.CreateAsync(engineId, cancellationToken);
        await using (await @lock.ReaderLockAsync(cancellationToken: cancellationToken))
        {
            TranslationEngine engine = await GetBuiltEngineAsync(engineId, cancellationToken);
            HybridTranslationEngine hybridEngine = await state.GetHybridEngineAsync(engine.BuildRevision);
            IReadOnlyList<TranslationResult> results = await hybridEngine.TranslateAsync(n, segment, cancellationToken);
            state.LastUsedTime = DateTime.Now;
            return results;
        }
    }

    public async Task<WordGraph> GetWordGraphAsync(
        string engineId,
        string segment,
        CancellationToken cancellationToken = default
    )
    {
        SmtTransferEngineState state = _stateService.Get(engineId);
        IDistributedReaderWriterLock @lock = await _lockFactory.CreateAsync(engineId, cancellationToken);
        await using (await @lock.ReaderLockAsync(cancellationToken: cancellationToken))
        {
            TranslationEngine engine = await GetBuiltEngineAsync(engineId, cancellationToken);
            HybridTranslationEngine hybridEngine = await state.GetHybridEngineAsync(engine.BuildRevision);
            WordGraph result = await hybridEngine.GetWordGraphAsync(segment, cancellationToken);
            state.LastUsedTime = DateTime.Now;
            return result;
        }
    }

    public async Task TrainSegmentPairAsync(
        string engineId,
        string sourceSegment,
        string targetSegment,
        bool sentenceStart,
        CancellationToken cancellationToken = default
    )
    {
        SmtTransferEngineState state = _stateService.Get(engineId);
        IDistributedReaderWriterLock @lock = await _lockFactory.CreateAsync(engineId, cancellationToken);
        await using (await @lock.WriterLockAsync(cancellationToken: cancellationToken))
        {
            TranslationEngine engine = await GetEngineAsync(engineId, cancellationToken);
            if (engine.BuildState is BuildState.Active)
            {
                await _dataAccessContext.BeginTransactionAsync(cancellationToken);
                await _trainSegmentPairs.InsertAsync(
                    new TrainSegmentPair
                    {
                        TranslationEngineRef = engine.Id,
                        Source = sourceSegment,
                        Target = targetSegment,
                        SentenceStart = sentenceStart
                    },
                    cancellationToken
                );
            }

            HybridTranslationEngine hybridEngine = await state.GetHybridEngineAsync(engine.BuildRevision);
            await hybridEngine.TrainSegmentAsync(sourceSegment, targetSegment, sentenceStart, cancellationToken);
            await _platformService.IncrementTrainSizeAsync(engineId, cancellationToken: CancellationToken.None);
            if (engine.BuildState is BuildState.Active)
                await _dataAccessContext.CommitTransactionAsync(CancellationToken.None);
            state.IsUpdated = true;
            state.LastUsedTime = DateTime.Now;
        }
    }

    public async Task StartBuildAsync(
        string engineId,
        string buildId,
        IReadOnlyList<Corpus> corpora,
        CancellationToken cancellationToken = default
    )
    {
        SmtTransferEngineState state = _stateService.Get(engineId);
        IDistributedReaderWriterLock @lock = await _lockFactory.CreateAsync(engineId, cancellationToken);
        await using (await @lock.WriterLockAsync(cancellationToken: cancellationToken))
        {
            // If there is a pending job, then no need to start a new one.
            if (
                await _engines.ExistsAsync(
                    e =>
                        e.EngineId == engineId
                        && (e.BuildState == BuildState.Pending || e.BuildState == BuildState.Active),
                    cancellationToken
                )
            )
                throw new InvalidOperationException("Engine is already building or pending.");

            // Schedule the job to occur way in the future, just so we can get the job id.
            // Token "None" is used here because hangfire injects the proper cancellation token
            string jobId = _jobClient.Schedule<SmtTransferEngineBuildJob>(
                r => r.RunAsync(engineId, buildId, corpora, CancellationToken.None),
                TimeSpan.FromDays(10000)
            );
            try
            {
                await _engines.UpdateAsync(
                    e => e.EngineId == engineId,
                    u =>
                        u.Set(e => e.BuildState, BuildState.Pending)
                            .Set(e => e.IsCanceled, false)
                            .Set(e => e.JobId, jobId)
                            .Set(e => e.BuildId, buildId),
                    cancellationToken: CancellationToken.None
                );
                // Enqueue the job now that the build has been created.
                _jobClient.Requeue(jobId);
            }
            catch
            {
                _jobClient.Delete(jobId);
                throw;
            }
            state.LastUsedTime = DateTime.UtcNow;
        }
    }

    public async Task CancelBuildAsync(string engineId, CancellationToken cancellationToken = default)
    {
        SmtTransferEngineState state = _stateService.Get(engineId);
        IDistributedReaderWriterLock @lock = await _lockFactory.CreateAsync(engineId, cancellationToken);
        await using (await @lock.WriterLockAsync(cancellationToken: cancellationToken))
        {
            await CancelBuildInternalAsync(engineId, cancellationToken);
            state.LastUsedTime = DateTime.UtcNow;
        }
    }

    private async Task<string?> CancelBuildInternalAsync(string engineId, CancellationToken cancellationToken)
    {
        await _dataAccessContext.BeginTransactionAsync(cancellationToken);
        // First, try to cancel a job that hasn't started yet
        TranslationEngine? engine = await _engines.UpdateAsync(
            e => e.EngineId == engineId && e.BuildState == BuildState.Pending,
            u => u.Set(b => b.BuildState, BuildState.None).Set(e => e.IsCanceled, true),
            cancellationToken: cancellationToken
        );
        if (engine is not null && engine.BuildId is not null)
        {
            // job will be deleted from the queue
            _jobClient.Delete(engine.JobId);
            await _platformService.BuildCanceledAsync(engine.BuildId, CancellationToken.None);
        }
        else
        {
            // Second, try to cancel a job that is already running
            engine = await _engines.UpdateAsync(
                e => e.EngineId == engineId && e.BuildState == BuildState.Active,
                u => u.Set(b => b.IsCanceled, true),
                cancellationToken: cancellationToken
            );
            if (engine is not null)
            {
                // Trigger the cancellation token for the job
                _jobClient.Delete(engine.JobId);
            }
        }
        await _dataAccessContext.CommitTransactionAsync(CancellationToken.None);
        return engine?.BuildId;
    }

    private async Task<bool> WaitForBuildToFinishAsync(
        string engineId,
        string buildId,
        CancellationToken cancellationToken
    )
    {
        using ISubscription<TranslationEngine> sub = await _engines.SubscribeAsync(
            e => e.EngineId == engineId && e.BuildId == buildId,
            cancellationToken
        );
        if (sub.Change.Entity is null)
            return true;

        var timeout = DateTime.UtcNow + TimeSpan.FromSeconds(20);
        while (DateTime.UtcNow < timeout)
        {
            await sub.WaitForChangeAsync(TimeSpan.FromSeconds(2), cancellationToken);
            TranslationEngine? engine = sub.Change.Entity;
            if (engine is null || engine.BuildState is BuildState.None)
                return true;
        }
        return false;
    }

    public Task<bool> BuildStartedAsync(string engineId, string buildId, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    public Task BuildCompletedAsync(
        string engineId,
        string buildId,
        int corpusSize,
        double confidence,
        CancellationToken cancellationToken = default
    )
    {
        throw new NotImplementedException();
    }

    public Task BuildFaultedAsync(
        string engineId,
        string buildId,
        string message,
        CancellationToken cancellationToken = default
    )
    {
        throw new NotImplementedException();
    }

    public Task BuildCanceledAsync(string engineId, string buildId, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    public Task UpdateBuildStatus(
        string engineId,
        string buildId,
        ProgressStatus progressStatus,
        CancellationToken cancellationToken = default
    )
    {
        throw new NotImplementedException();
    }
}
