namespace SIL.Machine.AspNetCore.Services;

public class NmtEnginePreprocessBuildJob : HangfireBuildJob
{
    private readonly ISharedFileService _sharedFileService;
    private readonly ICorpusService _corpusService;

    public NmtEnginePreprocessBuildJob(
        IPlatformService platformService,
        IRepository<TranslationEngine> engines,
        IDistributedReaderWriterLockFactory lockFactory,
        ILogger<NmtEnginePreprocessBuildJob> logger,
        IBuildJobService buildJobService,
        ISharedFileService sharedFileService,
        ICorpusService corpusService
    )
        : base(platformService, engines, lockFactory, buildJobService, logger)
    {
        _sharedFileService = sharedFileService;
        _corpusService = corpusService;
    }

    [Queue("nmt")]
    [AutomaticRetry(Attempts = 0)]
    public override Task RunAsync(string engineId, string buildId, object? data, CancellationToken cancellationToken)
    {
        return base.RunAsync(engineId, buildId, data, cancellationToken);
    }

    protected override async Task DoWorkAsync(
        string engineId,
        string buildId,
        object? data,
        IDistributedReaderWriterLock @lock,
        CancellationToken cancellationToken
    )
    {
        if (data is null)
            throw new ArgumentNullException(nameof(data));
        var corpora = (IReadOnlyList<Corpus>)data;

        await WriteDataFilesAsync(buildId, corpora, cancellationToken);

        await using (await @lock.WriterLockAsync(cancellationToken: cancellationToken))
        {
            await BuildJobService.StartBuildJobAsync(
                BuildJobType.Gpu,
                TranslationEngineType.Nmt,
                engineId,
                buildId,
                "train",
                cancellationToken: cancellationToken
            );
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

    protected override async Task CleanupAsync(
        string engineId,
        string buildId,
        object? data,
        IDistributedReaderWriterLock @lock,
        bool restarting,
        CancellationToken cancellationToken
    )
    {
        try
        {
            await _sharedFileService.DeleteAsync($"builds/{buildId}/", CancellationToken.None);
        }
        catch (Exception e)
        {
            Logger.LogWarning(e, "Unable to to delete job data for build {0}.", buildId);
        }
    }
}
