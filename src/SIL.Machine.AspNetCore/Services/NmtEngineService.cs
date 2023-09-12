namespace SIL.Machine.AspNetCore.Services;

public class NmtEngineService : ITranslationEngineService
{
    private readonly IBackgroundJobClient _jobClient;
    private readonly IDistributedReaderWriterLockFactory _lockFactory;
    private readonly IPlatformService _platformService;
    private readonly IDataAccessContext _dataAccessContext;
    private readonly IRepository<TranslationEngine> _engines;
    private readonly IBuildJobService _buildJobService;
    private readonly ISharedFileService _sharedFileService;
    private readonly ILogger<NmtEngineService> _logger;

    public NmtEngineService(
        IBackgroundJobClient jobClient,
        IPlatformService platformService,
        IDistributedReaderWriterLockFactory lockFactory,
        IDataAccessContext dataAccessContext,
        IRepository<TranslationEngine> engines,
        IBuildJobService buildJobService,
        ISharedFileService sharedFileService,
        ILogger<NmtEngineService> logger
    )
    {
        _jobClient = jobClient;
        _lockFactory = lockFactory;
        _platformService = platformService;
        _dataAccessContext = dataAccessContext;
        _engines = engines;
        _buildJobService = buildJobService;
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
        await _buildJobService.CreateEngineAsync(
            new[] { BuildJobType.Cpu, BuildJobType.Gpu },
            engineId,
            engineName,
            cancellationToken
        );
        await _dataAccessContext.CommitTransactionAsync(CancellationToken.None);
    }

    public async Task DeleteAsync(string engineId, CancellationToken cancellationToken = default)
    {
        await _dataAccessContext.BeginTransactionAsync(cancellationToken);
        await _engines.DeleteAsync(e => e.EngineId == engineId, cancellationToken);
        await _lockFactory.DeleteAsync(engineId, cancellationToken);
        await _buildJobService.DeleteEngineAsync(
            new[] { BuildJobType.Cpu, BuildJobType.Gpu },
            engineId,
            cancellationToken
        );
        await _dataAccessContext.CommitTransactionAsync(CancellationToken.None);
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
            // If there is a pending/running build, then no need to start a new one.
            if (await _engines.ExistsAsync(e => e.EngineId == engineId && e.CurrentBuild != null, cancellationToken))
                throw new InvalidOperationException("The engine has already started a build.");

            await _buildJobService.StartBuildJobAsync(
                BuildJobType.Cpu,
                TranslationEngineType.Nmt,
                engineId,
                buildId,
                "preprocess",
                corpora,
                cancellationToken
            );
        }
    }

    public async Task CancelBuildAsync(string engineId, CancellationToken cancellationToken = default)
    {
        IDistributedReaderWriterLock @lock = await _lockFactory.CreateAsync(engineId, cancellationToken);
        await using (await @lock.WriterLockAsync(cancellationToken: cancellationToken))
        {
            await _buildJobService.CancelBuildJobAsync(engineId, cancellationToken);
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

    public async Task<bool> BuildJobStartedAsync(
        string engineId,
        string buildId,
        CancellationToken cancellationToken = default
    )
    {
        IDistributedReaderWriterLock @lock = await _lockFactory.CreateAsync(engineId, cancellationToken);
        await using (await @lock.WriterLockAsync(cancellationToken: cancellationToken))
        {
            if (!await _buildJobService.BuildJobStartedAsync(engineId, buildId, cancellationToken))
                return false;
        }
        await _platformService.BuildStartedAsync(buildId, CancellationToken.None);
        _logger.LogInformation("Build started ({0})", buildId);
        return true;
    }

    public async Task BuildJobCompletedAsync(
        string engineId,
        string buildId,
        int corpusSize,
        double confidence,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            IDistributedReaderWriterLock @lock = await _lockFactory.CreateAsync(engineId, cancellationToken);
            await using (await @lock.WriterLockAsync(cancellationToken: cancellationToken))
            {
                await _buildJobService.StartBuildJobAsync(
                    BuildJobType.Cpu,
                    TranslationEngineType.Nmt,
                    engineId,
                    buildId,
                    "postprocess",
                    (corpusSize, confidence),
                    cancellationToken
                );
            }
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

    public async Task BuildJobFaultedAsync(
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
                await _buildJobService.BuildJobFinishedAsync(engineId, buildId, cancellationToken);
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

    public async Task BuildJobCanceledAsync(
        string engineId,
        string buildId,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            IDistributedReaderWriterLock @lock = await _lockFactory.CreateAsync(engineId, cancellationToken);
            await using (await @lock.WriterLockAsync(cancellationToken: cancellationToken))
            {
                await _buildJobService.BuildJobFinishedAsync(engineId, buildId, cancellationToken);
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

    public Task UpdateBuildJobStatus(
        string engineId,
        string buildId,
        ProgressStatus progressStatus,
        CancellationToken cancellationToken = default
    )
    {
        return _platformService.UpdateBuildStatusAsync(buildId, progressStatus, cancellationToken);
    }
}
