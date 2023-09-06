using SIL.Machine.AspNetCore.Models;

namespace SIL.Machine.AspNetCore.Services;

public class NmtEnginePipeline
{
    private readonly IPlatformService _platformService;
    private readonly IRepository<TranslationEngine> _engines;
    private readonly ILogger<NmtEnginePipeline> _logger;
    private readonly INmtJobService _nmtJobService;
    private readonly ISharedFileService _sharedFileService;
    private readonly ICorpusService _corpusService;
    private readonly IDistributedReaderWriterLockFactory _lockFactory;

    public NmtEnginePipeline(
        IPlatformService platformService,
        IRepository<TranslationEngine> engines,
        ILogger<NmtEnginePipeline> logger,
        INmtJobService nmtJobService,
        ISharedFileService sharedFileService,
        ICorpusService corpusService,
        IDistributedReaderWriterLockFactory lockFactory
    )
    {
        _platformService = platformService;
        _engines = engines;
        _logger = logger;
        _nmtJobService = nmtJobService;
        _sharedFileService = sharedFileService;
        _corpusService = corpusService;
        _lockFactory = lockFactory;
    }

    [Queue("nmt")]
    [AutomaticRetry(Attempts = 0)]
    public async Task PreprocessAsync(
        string engineId,
        string buildId,
        IReadOnlyList<Corpus> corpora,
        CancellationToken cancellationToken
    )
    {
        IDistributedReaderWriterLock @lock = await _lockFactory.CreateAsync(engineId, cancellationToken);
        string? jobId = null;
        try
        {
            TranslationEngine? engine = await _engines.GetAsync(
                e => e.EngineId == engineId && e.BuildId == buildId,
                cancellationToken: cancellationToken
            );
            if (engine is null)
                throw new OperationCanceledException();

            if (engine.BuildState is BuildState.Pending)
                await WriteDataFilesAsync(buildId, corpora, cancellationToken);

            await using (await @lock.WriterLockAsync(cancellationToken: cancellationToken))
            {
                jobId = await _nmtJobService.CreateJobAsync(
                    buildId,
                    engineId,
                    engine.SourceLanguage,
                    engine.TargetLanguage,
                    _sharedFileService.GetBaseUri().ToString(),
                    cancellationToken
                );
                engine = await _engines.UpdateAsync(
                    e =>
                        e.EngineId == engineId
                        && e.BuildId == buildId
                        && e.BuildState == BuildState.Pending
                        && !e.IsCanceled,
                    u => u.Set(e => e.JobId, jobId),
                    cancellationToken: CancellationToken.None
                );
                if (engine is null)
                    throw new OperationCanceledException();
                await _nmtJobService.EnqueueJobAsync(jobId, cancellationToken);
            }
        }
        catch (OperationCanceledException oce) when (oce.CancellationToken.IsCancellationRequested)
        {
            // the job server is shutting down
            await _platformService.BuildRestartingAsync(buildId, CancellationToken.None);
            throw;
        }
        catch (OperationCanceledException)
        {
            // cancellation was requested through the API
            if (jobId != null)
            {
                try
                {
                    await _nmtJobService.DeleteJobAsync(jobId, CancellationToken.None);
                }
                catch (Exception e)
                {
                    _logger.LogWarning(e, "Unable to delete NMT job for build {0}.", buildId);
                }
            }

            try
            {
                await _sharedFileService.DeleteAsync($"builds/{buildId}/");
            }
            catch (Exception e)
            {
                _logger.LogWarning(e, "Unable to to delete job data for build {0}.", buildId);
            }
            throw;
        }
        catch (Exception e)
        {
            if (jobId != null)
            {
                try
                {
                    await _nmtJobService.DeleteJobAsync(jobId, CancellationToken.None);
                }
                catch (Exception e2)
                {
                    _logger.LogWarning(e2, "Unable to delete NMT job for build {0}.", buildId);
                }
            }

            await using (await @lock.WriterLockAsync(cancellationToken: CancellationToken.None))
            {
                await _engines.UpdateAsync(
                    e => e.EngineId == engineId && e.BuildId == buildId,
                    u =>
                        u.Set(e => e.BuildState, BuildState.None)
                            .Set(e => e.IsCanceled, false)
                            .Unset(e => e.JobId)
                            .Unset(e => e.BuildId)
                );
            }

            await _platformService.BuildFaultedAsync(buildId, e.Message, CancellationToken.None);
            _logger.LogError(e, "Build faulted ({0}).", buildId);
            throw;
        }
    }

    [Queue("nmt")]
    [AutomaticRetry(Attempts = 0)]
    public async Task PostprocessAsync(
        string engineId,
        string buildId,
        int corpusSize,
        double confidence,
        CancellationToken cancellationToken
    )
    {
        IDistributedReaderWriterLock @lock = await _lockFactory.CreateAsync(engineId, cancellationToken);
        try
        {
            // The NMT job has successfully completed, so insert the generated pretranslations into the database.
            await InsertPretranslationsAsync(engineId, buildId, cancellationToken);

            await using (await @lock.WriterLockAsync(cancellationToken: cancellationToken))
            {
                await _engines.UpdateAsync(
                    e => e.EngineId == engineId && e.BuildId == buildId,
                    u =>
                        u.Set(e => e.BuildState, BuildState.None)
                            .Set(e => e.IsCanceled, false)
                            .Inc(e => e.BuildRevision)
                            .Unset(e => e.JobId)
                            .Unset(e => e.BuildId),
                    cancellationToken: CancellationToken.None
                );
            }

            await _platformService.BuildCompletedAsync(
                buildId,
                corpusSize,
                Math.Round(confidence, 2, MidpointRounding.AwayFromZero),
                CancellationToken.None
            );
            _logger.LogInformation("Build completed ({0}).", buildId);
        }
        catch (OperationCanceledException)
        {
            await _platformService.BuildRestartingAsync(buildId, CancellationToken.None);
            throw;
        }
        catch (Exception e)
        {
            await using (await @lock.WriterLockAsync(cancellationToken: CancellationToken.None))
            {
                await _engines.UpdateAsync(
                    e => e.EngineId == engineId && e.BuildId == buildId,
                    u =>
                        u.Set(e => e.BuildState, BuildState.None)
                            .Set(e => e.IsCanceled, false)
                            .Unset(e => e.JobId)
                            .Unset(e => e.BuildId)
                );
            }

            await _platformService.BuildFaultedAsync(buildId, e.Message, CancellationToken.None);
            _logger.LogError(e, "Build faulted ({0}).", buildId);
            throw;
        }
        finally
        {
            try
            {
                await _sharedFileService.DeleteAsync($"builds/{buildId}/", CancellationToken.None);
            }
            catch (Exception e)
            {
                _logger.LogWarning(e, "Unable to to delete job data for build {0}.", buildId);
            }
        }
    }

    private async Task<int> WriteDataFilesAsync(
        string buildId,
        IReadOnlyList<Corpus> corpora,
        CancellationToken cancellationToken
    )
    {
        await using var sourceTrainWriter = new StreamWriter(
            await _sharedFileService.OpenWriteAsync($"builds/{buildId}/train.src.txt", cancellationToken)
        );
        await using var targetTrainWriter = new StreamWriter(
            await _sharedFileService.OpenWriteAsync($"builds/{buildId}/train.trg.txt", cancellationToken)
        );

        int corpusSize = 0;
        async IAsyncEnumerable<Pretranslation> ProcessRowsAsync()
        {
            foreach (Corpus corpus in corpora)
            {
                ITextCorpus sourceCorpus = _corpusService.CreateTextCorpus(corpus.SourceFiles);
                ITextCorpus targetCorpus = _corpusService.CreateTextCorpus(corpus.TargetFiles);

                IParallelTextCorpus parallelCorpus = sourceCorpus.AlignRows(
                    targetCorpus,
                    allSourceRows: true,
                    allTargetRows: true
                );

                foreach (ParallelTextRow row in parallelCorpus)
                {
                    await sourceTrainWriter.WriteAsync($"{row.SourceText}\n");
                    await targetTrainWriter.WriteAsync($"{row.TargetText}\n");
                    if (
                        (corpus.PretranslateAll || corpus.PretranslateTextIds.Contains(row.TextId))
                        && row.SourceSegment.Count > 0
                        && row.TargetSegment.Count == 0
                    )
                    {
                        yield return new Pretranslation
                        {
                            CorpusId = corpus.Id,
                            TextId = row.TextId,
                            Refs = row.TargetRefs.Select(r => r.ToString()!).ToList(),
                            Translation = row.SourceText
                        };
                    }
                    if (!row.IsEmpty)
                        corpusSize++;
                }
            }
        }

        await using var sourcePretranslateStream = await _sharedFileService.OpenWriteAsync(
            $"builds/{buildId}/pretranslate.src.json",
            cancellationToken
        );

        await JsonSerializer.SerializeAsync(
            sourcePretranslateStream,
            ProcessRowsAsync(),
            new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase },
            cancellationToken: cancellationToken
        );
        return corpusSize;
    }

    private int GetCorpusSize(IReadOnlyList<Corpus> corpora)
    {
        int corpusSize = 0;
        foreach (Corpus corpus in corpora)
        {
            ITextCorpus sourceCorpus = _corpusService.CreateTextCorpus(corpus.SourceFiles);
            ITextCorpus targetCorpus = _corpusService.CreateTextCorpus(corpus.TargetFiles);

            IParallelTextCorpus parallelCorpus = sourceCorpus.AlignRows(targetCorpus);

            corpusSize += parallelCorpus.Count(includeEmpty: false);
        }
        return corpusSize;
    }

    private async Task InsertPretranslationsAsync(string engineId, string buildId, CancellationToken cancellationToken)
    {
        await using var targetPretranslateStream = await _sharedFileService.OpenReadAsync(
            $"builds/{buildId}/pretranslate.trg.json",
            cancellationToken
        );

        IAsyncEnumerable<Pretranslation> pretranslations = JsonSerializer
            .DeserializeAsyncEnumerable<Pretranslation>(
                targetPretranslateStream,
                new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase },
                cancellationToken
            )
            .OfType<Pretranslation>();

        await _platformService.InsertPretranslationsAsync(engineId, pretranslations, cancellationToken);
    }
}
