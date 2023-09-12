namespace SIL.Machine.AspNetCore.Services;

public class SmtTransferHangfireBuildJobFactory : HangfireBuildJobFactoryBase
{
    public SmtTransferHangfireBuildJobFactory(IBackgroundJobClient jobClient)
        : base(jobClient) { }

    public override TranslationEngineType EngineType => TranslationEngineType.SmtTransfer;

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
            "train" => CreateJob<SmtTransferEngineBuildJob>(engineId, buildId, data),
            _ => throw new ArgumentException("Unknown build stage.", nameof(stage)),
        };
        return Task.FromResult(jobId);
    }
}
