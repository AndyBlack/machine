namespace SIL.Machine.AspNetCore.Services;

public interface ITranslationEngineService
{
    TranslationEngineType Type { get; }

    Task CreateAsync(
        string engineId,
        string? engineName,
        string sourceLanguage,
        string targetLanguage,
        CancellationToken cancellationToken = default
    );
    Task DeleteAsync(string engineId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TranslationResult>> TranslateAsync(
        string engineId,
        int n,
        string segment,
        CancellationToken cancellationToken = default
    );

    Task<WordGraph> GetWordGraphAsync(string engineId, string segment, CancellationToken cancellationToken = default);

    Task TrainSegmentPairAsync(
        string engineId,
        string sourceSegment,
        string targetSegment,
        bool sentenceStart,
        CancellationToken cancellationToken = default
    );

    Task StartBuildAsync(
        string engineId,
        string buildId,
        IReadOnlyList<Corpus> corpora,
        CancellationToken cancellationToken = default
    );

    Task CancelBuildAsync(string engineId, CancellationToken cancellationToken = default);

    Task<bool> BuildJobStartedAsync(string engineId, string buildId, CancellationToken cancellationToken = default);

    Task BuildJobCompletedAsync(
        string engineId,
        string buildId,
        int corpusSize,
        double confidence,
        CancellationToken cancellationToken = default
    );

    Task BuildJobFaultedAsync(
        string engineId,
        string buildId,
        string message,
        CancellationToken cancellationToken = default
    );

    Task BuildJobCanceledAsync(string engineId, string buildId, CancellationToken cancellationToken = default);

    Task UpdateBuildJobStatus(
        string engineId,
        string buildId,
        ProgressStatus progressStatus,
        CancellationToken cancellationToken = default
    );
}
