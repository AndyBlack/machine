using Hangfire;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services
    .AddMachine(builder.Configuration)
    .AddMongoDataAccess()
    .AddMongoHangfireJobClient()
    .AddServalTranslationEngineService()
    .AddHangfireBuildJobRunner()
    .AddClearMLBuildJobRunner();

var app = builder.Build();

app.UseHttpsRedirection();

app.MapServalTranslationEngineService();
app.MapBuildJobNotificationService();
app.MapGrpcHealthChecksService();
app.MapHangfireDashboard();

app.Run();
