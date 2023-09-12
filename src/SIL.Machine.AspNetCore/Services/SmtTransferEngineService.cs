﻿namespace SIL.Machine.AspNetCore.Services;

public class SmtTransferEngineService : ITranslationEngineService
{
    private readonly IDistributedReaderWriterLockFactory _lockFactory;
    private readonly IPlatformService _platformService;
    private readonly IDataAccessContext _dataAccessContext;
    private readonly IRepository<TranslationEngine> _engines;
    private readonly IRepository<TrainSegmentPair> _trainSegmentPairs;
    private readonly SmtTransferEngineStateService _stateService;
    private readonly IBuildJobService _buildJobService;

    public SmtTransferEngineService(
        IDistributedReaderWriterLockFactory lockFactory,
        IPlatformService platformService,
        IDataAccessContext dataAccessContext,
        IRepository<TranslationEngine> engines,
        IRepository<TrainSegmentPair> trainSegmentPairs,
        SmtTransferEngineStateService stateService,
        IBuildJobService buildJobService
    )
    {
        _lockFactory = lockFactory;
        _platformService = platformService;
        _dataAccessContext = dataAccessContext;
        _engines = engines;
        _trainSegmentPairs = trainSegmentPairs;
        _stateService = stateService;
        _buildJobService = buildJobService;
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
        await _dataAccessContext.BeginTransactionAsync(cancellationToken);
        await _engines.InsertAsync(
            new TranslationEngine
            {
                EngineId = engineId,
                SourceLanguage = sourceLanguage,
                TargetLanguage = targetLanguage
            },
            cancellationToken
        );
        await _buildJobService.CreateEngineAsync(new[] { BuildJobType.Cpu }, engineId, engineName, cancellationToken);
        await _dataAccessContext.CommitTransactionAsync(CancellationToken.None);

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
                (string? buildId, BuildJobState jobState) = await _buildJobService.CancelBuildJobAsync(
                    engineId,
                    CancellationToken.None
                );
                if (buildId is not null)
                {
                    if (jobState is BuildJobState.Canceling)
                        await WaitForBuildToFinishAsync(engineId, buildId, CancellationToken.None);
                    else
                        await _platformService.BuildCanceledAsync(buildId, CancellationToken.None);
                }

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
            if (engine.CurrentBuild?.JobState is BuildJobState.Active)
            {
                await _dataAccessContext.BeginTransactionAsync(cancellationToken);
                await _trainSegmentPairs.InsertAsync(
                    new TrainSegmentPair
                    {
                        TranslationEngineRef = engineId,
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
            if (engine.CurrentBuild?.JobState is BuildJobState.Active)
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
            // If there is a pending/running build, then no need to start a new one.
            if (await _engines.ExistsAsync(e => e.EngineId == engineId && e.CurrentBuild != null, cancellationToken))
                throw new InvalidOperationException("The engine has already started a build.");

            await _buildJobService.StartBuildJobAsync(
                BuildJobType.Cpu,
                TranslationEngineType.SmtTransfer,
                engineId,
                buildId,
                "train",
                corpora,
                cancellationToken
            );
            state.LastUsedTime = DateTime.UtcNow;
        }
    }

    public async Task CancelBuildAsync(string engineId, CancellationToken cancellationToken = default)
    {
        SmtTransferEngineState state = _stateService.Get(engineId);
        IDistributedReaderWriterLock @lock = await _lockFactory.CreateAsync(engineId, cancellationToken);
        await using (await @lock.WriterLockAsync(cancellationToken: cancellationToken))
        {
            await _buildJobService.CancelBuildJobAsync(engineId, cancellationToken);
            state.LastUsedTime = DateTime.UtcNow;
        }
    }

    private async Task<bool> WaitForBuildToFinishAsync(
        string engineId,
        string buildId,
        CancellationToken cancellationToken
    )
    {
        using ISubscription<TranslationEngine> sub = await _engines.SubscribeAsync(
            e => e.EngineId == engineId && e.CurrentBuild != null && e.CurrentBuild.Id == buildId,
            cancellationToken
        );
        if (sub.Change.Entity is null)
            return true;

        var timeout = DateTime.UtcNow + TimeSpan.FromSeconds(20);
        while (DateTime.UtcNow < timeout)
        {
            await sub.WaitForChangeAsync(TimeSpan.FromSeconds(2), cancellationToken);
            TranslationEngine? engine = sub.Change.Entity;
            if (engine is null)
                return true;
        }
        return false;
    }

    public Task<bool> BuildJobStartedAsync(
        string engineId,
        string buildId,
        CancellationToken cancellationToken = default
    )
    {
        throw new NotImplementedException();
    }

    public Task BuildJobCompletedAsync(
        string engineId,
        string buildId,
        int corpusSize,
        double confidence,
        CancellationToken cancellationToken = default
    )
    {
        throw new NotImplementedException();
    }

    public Task BuildJobFaultedAsync(
        string engineId,
        string buildId,
        string message,
        CancellationToken cancellationToken = default
    )
    {
        throw new NotImplementedException();
    }

    public Task BuildJobCanceledAsync(string engineId, string buildId, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    public Task UpdateBuildJobStatus(
        string engineId,
        string buildId,
        ProgressStatus progressStatus,
        CancellationToken cancellationToken = default
    )
    {
        throw new NotImplementedException();
    }

    private async Task<TranslationEngine> GetEngineAsync(string engineId, CancellationToken cancellationToken)
    {
        TranslationEngine? engine = await _engines.GetAsync(e => e.EngineId == engineId, cancellationToken);
        if (engine is null)
            throw new InvalidOperationException($"The engine {engineId} does not exist.");
        return engine;
    }

    private async Task<TranslationEngine> GetBuiltEngineAsync(string engineId, CancellationToken cancellationToken)
    {
        TranslationEngine engine = await GetEngineAsync(engineId, cancellationToken);
        if (engine.BuildRevision == 0)
            throw new EngineNotBuiltException("The engine must be built first.");
        return engine;
    }
}
