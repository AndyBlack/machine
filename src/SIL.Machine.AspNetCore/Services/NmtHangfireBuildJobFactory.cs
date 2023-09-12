namespace SIL.Machine.AspNetCore.Services;

public class NmtHangfireBuildJobFactory : HangfireBuildJobFactoryBase
{
    public NmtHangfireBuildJobFactory(IBackgroundJobClient jobClient)
        : base(jobClient) { }

    public override TranslationEngineType EngineType => TranslationEngineType.Nmt;

    public override Task<string> CreateJobAsync(
        string engineId,
        string buildId,
        string stage,
        object? data,
        CancellationToken cancellationToken = default
    )
    {
        string jobId = stage switch
        {
            "preprocess" => CreateJob<NmtEnginePreprocessBuildJob>(engineId, buildId, data),
            "postprocess" => CreateJob<NmtEnginePostprocessBuildJob>(engineId, buildId, data),
            _ => throw new ArgumentException("Unknown build stage.", nameof(stage)),
        };
        return Task.FromResult(jobId);
    }
}
