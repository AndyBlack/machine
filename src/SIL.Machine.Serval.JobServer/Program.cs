var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddMachine(builder.Configuration)
    .AddMongoDataAccess()
    .AddMongoHangfireJobClient()
    .AddHangfireJobServer()
    .AddServalPlatformService()
    .AddHangfireBuildJobRunner()
    .AddClearMLBuildJobRunner();

var app = builder.Build();

app.MapHealthChecks("/health");

app.Run();
