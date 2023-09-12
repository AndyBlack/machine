namespace SIL.Machine.AspNetCore.Configuration;

public class BuildJobOptions
{
    public const string Key = "BuildJob";

    public Dictionary<BuildJobType, BuildJobRunnerType> Runners { get; set; } =
        new() { { BuildJobType.Cpu, BuildJobRunnerType.Hangfire }, { BuildJobType.Gpu, BuildJobRunnerType.ClearML } };
}
