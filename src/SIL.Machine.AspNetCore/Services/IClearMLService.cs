﻿namespace SIL.Machine.AspNetCore.Services;

public interface IClearMLService
{
    Task<string> CreateProjectAsync(
        string name,
        string? description = null,
        CancellationToken cancellationToken = default
    );
    Task<bool> DeleteProjectAsync(string id, CancellationToken cancellationToken = default);
    Task<string?> GetProjectIdAsync(string name, CancellationToken cancellationToken = default);

    Task<string> CreateTaskAsync(
        string buildId,
        string projectId,
        string engineId,
        string sourceLanguageTag,
        string targetLanguageTag,
        string sharedFileUri,
        CancellationToken cancellationToken = default
    );
    Task<bool> EnqueueTaskAsync(string id, CancellationToken cancellationToken = default);
    Task<bool> DequeueTaskAsync(string id, CancellationToken cancellationToken = default);
    Task<bool> StopTaskAsync(string id, CancellationToken cancellationToken = default);
    Task<ClearMLTask?> GetTaskAsync(string name, string projectId, CancellationToken cancellationToken = default);
    Task<ClearMLTask?> GetTaskAsync(string id, CancellationToken cancellationToken = default);
    Task<IReadOnlyDictionary<string, double>> GetTaskMetricsAsync(
        string id,
        CancellationToken cancellationToken = default
    );
    Task<IReadOnlyList<string>?> GetTasksAheadInQueueAsync(
        string buildId,
        CancellationToken cancellationToken = default
    );

    Task<float> GetInferencePercentCompleteAsync(string id, CancellationToken cancellationToken = default);
}
