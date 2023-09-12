namespace SIL.Machine.AspNetCore.Services;

public interface IHangfireBuildJobFactory
{
    TranslationEngineType EngineType { get; }

    Task<string> CreateJobAsync(
        string engineId,
        string buildId,
        string stage,
        object? data,
        CancellationToken cancellationToken = default
    );
}
