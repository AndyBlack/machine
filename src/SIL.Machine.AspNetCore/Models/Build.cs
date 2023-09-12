namespace SIL.Machine.AspNetCore.Models;

public enum BuildJobState
{
    None,
    Pending,
    Active,
    Canceling
}

public enum BuildJobType
{
    Cpu,
    Gpu
}

public class Build
{
    public string Id { get; set; } = default!;
    public BuildJobState JobState { get; set; }
    public string JobId { get; set; } = default!;
    public BuildJobType JobType { get; set; }
    public string Stage { get; set; } = default!;
}
