namespace SIL.Machine.AspNetCore.Services;

public class SmtTransferEngineBuildJob : HangfireBuildJob
{
    private readonly IRepository<TrainSegmentPair> _trainSegmentPairs;
    private readonly ITruecaserFactory _truecaserFactory;
    private readonly ISmtModelFactory _smtModelFactory;
    private readonly ICorpusService _corpusService;

    public SmtTransferEngineBuildJob(
        IPlatformService platformService,
        IRepository<TranslationEngine> engines,
        IDistributedReaderWriterLockFactory lockFactory,
        IBuildJobService buildJobService,
        ILogger<SmtTransferEngineBuildJob> logger,
        IRepository<TrainSegmentPair> trainSegmentPairs,
        ITruecaserFactory truecaserFactory,
        ISmtModelFactory smtModelFactory,
        ICorpusService corpusService
    )
        : base(platformService, engines, lockFactory, buildJobService, logger)
    {
        _trainSegmentPairs = trainSegmentPairs;
        _truecaserFactory = truecaserFactory;
        _smtModelFactory = smtModelFactory;
        _corpusService = corpusService;
    }

    [Queue("smt_transfer")]
    [AutomaticRetry(Attempts = 0)]
    public override Task RunAsync(string engineId, string buildId, object? data, CancellationToken cancellationToken)
    {
        return base.RunAsync(engineId, buildId, data, cancellationToken);
    }

    protected override Task InitializeAsync(
        string engineId,
        string buildId,
        object? data,
        IDistributedReaderWriterLock @lock,
        CancellationToken cancellationToken
    )
    {
        return _trainSegmentPairs.DeleteAllAsync(p => p.TranslationEngineRef == engineId, cancellationToken);
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

        await PlatformService.BuildStartedAsync(buildId, cancellationToken);
        Logger.LogInformation("Build started ({0})", buildId);
        var stopwatch = new Stopwatch();
        stopwatch.Start();

        cancellationToken.ThrowIfCancellationRequested();

        var targetCorpora = new List<ITextCorpus>();
        var parallelCorpora = new List<IParallelTextCorpus>();
        foreach (Corpus corpus in corpora)
        {
            ITextCorpus sc = _corpusService.CreateTextCorpus(corpus.SourceFiles);
            ITextCorpus tc = _corpusService.CreateTextCorpus(corpus.TargetFiles);

            targetCorpora.Add(tc);
            parallelCorpora.Add(sc.AlignRows(tc));
        }

        IParallelTextCorpus parallelCorpus = parallelCorpora.Flatten();
        ITextCorpus targetCorpus = targetCorpora.Flatten();

        var tokenizer = new LatinWordTokenizer();
        var detokenizer = new LatinWordDetokenizer();

        using ITrainer smtModelTrainer = _smtModelFactory.CreateTrainer(engineId, tokenizer, parallelCorpus);
        using ITrainer truecaseTrainer = _truecaserFactory.CreateTrainer(engineId, tokenizer, targetCorpus);

        cancellationToken.ThrowIfCancellationRequested();

        var progress = new BuildProgress(PlatformService, buildId);
        await smtModelTrainer.TrainAsync(progress, cancellationToken);
        await truecaseTrainer.TrainAsync(cancellationToken: cancellationToken);

        TranslationEngine? engine = await Engines.GetAsync(e => e.EngineId == engineId, cancellationToken);
        if (engine is null)
            throw new OperationCanceledException();
        int trainSegmentPairCount;
        await using (await @lock.WriterLockAsync(cancellationToken: cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            await smtModelTrainer.SaveAsync(CancellationToken.None);
            await truecaseTrainer.SaveAsync(CancellationToken.None);
            ITruecaser truecaser = await _truecaserFactory.CreateAsync(engineId);
            IReadOnlyList<TrainSegmentPair> segmentPairs = await _trainSegmentPairs.GetAllAsync(
                p => p.TranslationEngineRef == engine.Id,
                CancellationToken.None
            );
            using (
                IInteractiveTranslationModel smtModel = _smtModelFactory.Create(
                    engineId,
                    tokenizer,
                    detokenizer,
                    truecaser
                )
            )
            {
                foreach (TrainSegmentPair segmentPair in segmentPairs)
                {
                    await smtModel.TrainSegmentAsync(
                        segmentPair.Source,
                        segmentPair.Target,
                        cancellationToken: CancellationToken.None
                    );
                }
            }

            trainSegmentPairCount = segmentPairs.Count;

            await BuildJobService.BuildJobFinishedAsync(engineId, buildId, CancellationToken.None);
        }

        await PlatformService.BuildCompletedAsync(
            buildId,
            smtModelTrainer.Stats.TrainCorpusSize + trainSegmentPairCount,
            smtModelTrainer.Stats.Metrics["bleu"] * 100.0,
            CancellationToken.None
        );

        stopwatch.Stop();
        Logger.LogInformation("Build completed in {0}s ({1})", stopwatch.Elapsed.TotalSeconds, buildId);
    }
}
