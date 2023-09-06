using Google.Protobuf.WellKnownTypes;
using Machine.V1;

namespace SIL.Machine.AspNetCore.Services;

public class BuildNotificationServiceV1 : BuildNotificationApi.BuildNotificationApiBase
{
    private static readonly Empty Empty = new();

    private readonly Dictionary<TranslationEngineType, ITranslationEngineService> _engineServices;

    public BuildNotificationServiceV1(IEnumerable<ITranslationEngineService> engineServices)
    {
        _engineServices = engineServices.ToDictionary(es => es.Type);
    }

    public override async Task<StartedResponse> Started(StartedRequest request, ServerCallContext context)
    {
        ITranslationEngineService engineService = GetEngineService(request.EngineType);
        bool canceled = !await engineService.BuildStartedAsync(
            request.EngineId,
            request.BuildId,
            context.CancellationToken
        );
        return new StartedResponse { Canceled = canceled };
    }

    public override async Task<Empty> Completed(CompletedRequest request, ServerCallContext context)
    {
        ITranslationEngineService engineService = GetEngineService(request.EngineType);
        await engineService.BuildCompletedAsync(
            request.EngineId,
            request.BuildId,
            request.CorpusSize,
            request.Confidence,
            context.CancellationToken
        );
        return Empty;
    }

    public override async Task<Empty> Canceled(CanceledRequest request, ServerCallContext context)
    {
        ITranslationEngineService engineService = GetEngineService(request.EngineType);
        await engineService.BuildCanceledAsync(request.EngineId, request.BuildId, context.CancellationToken);
        return Empty;
    }

    public override async Task<Empty> Faulted(FaultedRequest request, ServerCallContext context)
    {
        ITranslationEngineService engineService = GetEngineService(request.EngineType);
        await engineService.BuildFaultedAsync(
            request.EngineId,
            request.BuildId,
            request.Message,
            context.CancellationToken
        );
        return Empty;
    }

    public override async Task<Empty> UpdateStatus(UpdateStatusRequest request, ServerCallContext context)
    {
        ITranslationEngineService engineService = GetEngineService(request.EngineType);
        await engineService.UpdateBuildStatus(
            request.EngineId,
            request.BuildId,
            new ProgressStatus(
                request.Step,
                request.HasPercentCompleted ? request.PercentCompleted : null,
                request.HasMessage ? request.Message : null
            ),
            context.CancellationToken
        );
        return Empty;
    }

    private ITranslationEngineService GetEngineService(string engineTypeStr)
    {
        if (_engineServices.TryGetValue(GetEngineType(engineTypeStr), out ITranslationEngineService? service))
            return service;
        throw new RpcException(new Status(StatusCode.InvalidArgument, "The engine type is invalid."));
    }

    private static TranslationEngineType GetEngineType(string engineTypeStr)
    {
        engineTypeStr = engineTypeStr[0].ToString().ToUpperInvariant() + engineTypeStr[1..];
        if (System.Enum.TryParse(engineTypeStr, out TranslationEngineType engineType))
            return engineType;
        throw new RpcException(new Status(StatusCode.InvalidArgument, "The engine type is invalid."));
    }
}
