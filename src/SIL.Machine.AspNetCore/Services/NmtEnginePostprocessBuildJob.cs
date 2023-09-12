namespace SIL.Machine.AspNetCore.Services;

public class NmtEnginePostprocessBuildJob : HangfireBuildJob
{
    private readonly ISharedFileService _sharedFileService;

    public NmtEnginePostprocessBuildJob(
        IPlatformService platformService,
        IRepository<TranslationEngine> engines,
        IDistributedReaderWriterLockFactory lockFactory,
        IBuildJobService buildJobService,
        ILogger<NmtEnginePostprocessBuildJob> logger,
        ISharedFileService sharedFileService
    )
        : base(platformService, engines, lockFactory, buildJobService, logger)
    {
        _sharedFileService = sharedFileService;
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
        (var corpusSize, var confidence) = ((int, double))data;

        // The NMT job has successfully completed, so insert the generated pretranslations into the database.
        await InsertPretranslationsAsync(engineId, buildId, cancellationToken);

        await using (await @lock.WriterLockAsync(cancellationToken: cancellationToken))
        {
            await BuildJobService.BuildJobFinishedAsync(engineId, buildId, cancellationToken);
        }

        await PlatformService.BuildCompletedAsync(
            buildId,
            corpusSize,
            Math.Round(confidence, 2, MidpointRounding.AwayFromZero),
            CancellationToken.None
        );
        Logger.LogInformation("Build completed ({0}).", buildId);
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
        if (restarting)
            return;

        try
        {
            await _sharedFileService.DeleteAsync($"builds/{buildId}/", CancellationToken.None);
        }
        catch (Exception e)
        {
            Logger.LogWarning(e, "Unable to to delete job data for build {0}.", buildId);
        }
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

        await PlatformService.InsertPretranslationsAsync(engineId, pretranslations, cancellationToken);
    }
}
