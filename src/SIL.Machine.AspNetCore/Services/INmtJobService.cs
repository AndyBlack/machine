namespace SIL.Machine.AspNetCore.Services;

public interface INmtJobService
{
    Task CreateEngineAsync(string engineId, string? name = null, CancellationToken cancellationToken = default);
    Task DeleteEngineAsync(string engineId, CancellationToken cancellationToken = default);

    Task<string> CreateJobAsync(
        string buildId,
        string engineId,
        string sourceLanguageTag,
        string targetLanguageTag,
        string sharedFileUri,
        CancellationToken cancellationToken = default
    );

    Task<bool> DeleteJobAsync(string jobId, CancellationToken cancellationToken = default);

    Task<bool> EnqueueJobAsync(string jobId, CancellationToken cancellationToken = default);

    Task<bool> StopJobAsync(string jobId, CancellationToken cancellationToken = default);

    Task<bool> PingAsync(CancellationToken cancellationToken = default);

    Task<bool> WorkersAreAssignedToQueue(CancellationToken cancellationToken = default);
}
