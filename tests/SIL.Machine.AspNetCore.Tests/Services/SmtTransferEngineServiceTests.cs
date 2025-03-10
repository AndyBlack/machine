﻿using SIL.Machine.Tokenization;

namespace SIL.Machine.AspNetCore.Services;

[TestFixture]
public class SmtTransferEngineServiceTests
{
    [Test]
    public async Task StartBuildAsync()
    {
        using var env = new TestEnvironment();
        TranslationEngine engine = env.Engines.Get("engine1");
        Assert.That(engine.BuildRevision, Is.EqualTo(1)); //For testing purposes BuildRevision is set to 1 (i.e., an already built engine)
        // ensure that the SMT model was loaded before training
        await env.Service.TranslateAsync("engine1", n: 1, "esto es una prueba.");
        await env.Service.StartBuildAsync("engine1", "build1", Array.Empty<Corpus>());
        await env.WaitForBuildToFinishAsync();
        await env.SmtBatchTrainer
            .Received()
            .TrainAsync(Arg.Any<IProgress<ProgressStatus>>(), Arg.Any<CancellationToken>());
        await env.TruecaserTrainer
            .Received()
            .TrainAsync(Arg.Any<IProgress<ProgressStatus>>(), Arg.Any<CancellationToken>());
        await env.SmtBatchTrainer.Received().SaveAsync(Arg.Any<CancellationToken>());
        await env.TruecaserTrainer.Received().SaveAsync(Arg.Any<CancellationToken>());
        engine = env.Engines.Get("engine1");
        Assert.That(engine.BuildState, Is.EqualTo(BuildState.None));
        Assert.That(engine.BuildRevision, Is.EqualTo(2)); //For testing purposes BuildRevision was initially set to 1 (i.e., an already built engine), so now it ought to be 2
        // check if SMT model was reloaded upon first use after training
        env.SmtModel.ClearReceivedCalls();
        await env.Service.TranslateAsync("engine1", n: 1, "esto es una prueba.");
        env.SmtModel.Received().Dispose();
        await env.SmtModel.DidNotReceive().SaveAsync();
        await env.Truecaser.DidNotReceive().SaveAsync();
    }

    [Test]
    public async Task CancelBuildAsync()
    {
        using var env = new TestEnvironment();
        await env.SmtBatchTrainer.TrainAsync(
            Arg.Any<IProgress<ProgressStatus>>(),
            Arg.Do<CancellationToken>(ct =>
            {
                while (true)
                    ct.ThrowIfCancellationRequested();
            })
        );
        await env.Service.StartBuildAsync("engine1", "build1", Array.Empty<Corpus>());
        await env.WaitForBuildToStartAsync();
        TranslationEngine engine = env.Engines.Get("engine1");
        Assert.That(engine.BuildState, Is.EqualTo(BuildState.Active));
        await env.Service.CancelBuildAsync("engine1");
        await env.WaitForBuildToFinishAsync();
        await env.SmtBatchTrainer.DidNotReceive().SaveAsync();
        await env.TruecaserTrainer.DidNotReceive().SaveAsync();
        engine = env.Engines.Get("engine1");
        Assert.That(engine.BuildState, Is.EqualTo(BuildState.None));
    }

    [Test]
    public async Task StartBuildAsync_RestartUnfinishedBuild()
    {
        using var env = new TestEnvironment();

        env.SmtBatchTrainer
            .WhenForAnyArgs(t => t.TrainAsync(null, default))
            .Do(ci =>
            {
                CancellationToken ct = ci.ArgAt<CancellationToken>(1);
                while (true)
                {
                    ct.ThrowIfCancellationRequested();
                    Thread.Sleep(100);
                }
            });
        await env.Service.StartBuildAsync("engine1", "build1", Array.Empty<Corpus>());
        await env.WaitForBuildToStartAsync();
        TranslationEngine engine = env.Engines.Get("engine1");
        Assert.That(engine.BuildState, Is.EqualTo(BuildState.Active));
        env.StopServer();
        engine = env.Engines.Get("engine1");
        Assert.That(engine.BuildState, Is.EqualTo(BuildState.Pending));
        await env.PlatformService.Received().BuildRestartingAsync("build1");
        env.SmtBatchTrainer.ClearSubstitute(ClearOptions.CallActions);
        env.StartServer();
        await env.WaitForBuildToFinishAsync();
        engine = env.Engines.Get("engine1");
        Assert.That(engine.BuildState, Is.EqualTo(BuildState.None));
    }

    [Test]
    public async Task CommitAsync_LoadedInactive()
    {
        using var env = new TestEnvironment();
        await env.Service.TrainSegmentPairAsync("engine1", "esto es una prueba.", "this is a test.", true);
        await Task.Delay(10);
        await env.CommitAsync(TimeSpan.Zero);
        await env.SmtModel.Received().SaveAsync();
        Assert.That(env.StateService.Get("engine1").IsLoaded, Is.False);
    }

    [Test]
    public async Task CommitAsync_LoadedActive()
    {
        using var env = new TestEnvironment();
        await env.Service.TrainSegmentPairAsync("engine1", "esto es una prueba.", "this is a test.", true);
        await env.CommitAsync(TimeSpan.FromHours(1));
        await env.SmtModel.Received().SaveAsync();
        Assert.That(env.StateService.Get("engine1").IsLoaded, Is.True);
    }

    [Test]
    public async Task TranslateAsync()
    {
        using var env = new TestEnvironment();
        TranslationResult result = (await env.Service.TranslateAsync("engine1", n: 1, "esto es una prueba."))[0];
        Assert.That(result.Translation, Is.EqualTo("this is a TEST."));
    }

    private class TestEnvironment : DisposableBase
    {
        private readonly MemoryStorage _memoryStorage;
        private readonly BackgroundJobClient _jobClient;
        private BackgroundJobServer _jobServer;
        private readonly ISmtModelFactory _smtModelFactory;
        private readonly ITransferEngineFactory _transferEngineFactory;
        private readonly ITruecaserFactory _truecaserFactory;
        private readonly IDistributedReaderWriterLockFactory _lockFactory;

        public TestEnvironment()
        {
            Engines = new MemoryRepository<TranslationEngine>();
            Engines.Add(
                new TranslationEngine
                {
                    Id = "engine1",
                    EngineId = "engine1",
                    SourceLanguage = "es",
                    TargetLanguage = "en",
                    BuildRevision = 1,
                    BuildState = BuildState.None,
                }
            );
            TrainSegmentPairs = new MemoryRepository<TrainSegmentPair>();
            _memoryStorage = new MemoryStorage();
            _jobClient = new BackgroundJobClient(_memoryStorage);
            PlatformService = Substitute.For<IPlatformService>();
            SmtModel = Substitute.For<IInteractiveTranslationModel>();
            SmtBatchTrainer = Substitute.For<ITrainer>();
            SmtBatchTrainer.Stats.Returns(new TrainStats { Metrics = { { "bleu", 0.0 }, { "perplexity", 0.0 } } });
            Truecaser = Substitute.For<ITruecaser>();
            TruecaserTrainer = Substitute.For<ITrainer>();
            TruecaserTrainer.SaveAsync().Returns(Task.CompletedTask);
            _smtModelFactory = CreateSmtModelFactory();
            _transferEngineFactory = CreateTransferEngineFactory();
            _truecaserFactory = CreateTruecaserFactory();
            _lockFactory = new DistributedReaderWriterLockFactory(
                new OptionsWrapper<ServiceOptions>(new ServiceOptions { ServiceId = "host" }),
                new MemoryRepository<RWLock>(),
                new ObjectIdGenerator()
            );
            _jobServer = CreateJobServer();
            StateService = CreateStateService();
            Service = CreateService();
        }

        public SmtTransferEngineService Service { get; private set; }
        public SmtTransferEngineStateService StateService { get; private set; }
        public MemoryRepository<TranslationEngine> Engines { get; }
        public MemoryRepository<TrainSegmentPair> TrainSegmentPairs { get; }
        public ITrainer SmtBatchTrainer { get; }
        public IInteractiveTranslationModel SmtModel { get; }
        public ITruecaser Truecaser { get; }
        public ITrainer TruecaserTrainer { get; }
        public IPlatformService PlatformService { get; }

        public async Task CommitAsync(TimeSpan inactiveTimeout)
        {
            await StateService.CommitAsync(_lockFactory, Engines, inactiveTimeout);
        }

        public void StopServer()
        {
            StateService.Dispose();
            _jobServer.Dispose();
        }

        public void StartServer()
        {
            _jobServer = CreateJobServer();
            StateService = CreateStateService();
            Service = CreateService();
        }

        private BackgroundJobServer CreateJobServer()
        {
            var jobServerOptions = new BackgroundJobServerOptions
            {
                Activator = new EnvActivator(this),
                Queues = new[] { "smt_transfer" },
                CancellationCheckInterval = TimeSpan.FromMilliseconds(50),
            };
            return new BackgroundJobServer(jobServerOptions, _memoryStorage);
        }

        private SmtTransferEngineStateService CreateStateService()
        {
            return new SmtTransferEngineStateService(_smtModelFactory, _transferEngineFactory, _truecaserFactory);
        }

        private SmtTransferEngineService CreateService()
        {
            return new SmtTransferEngineService(
                _jobClient,
                _lockFactory,
                PlatformService,
                new MemoryDataAccessContext(),
                Engines,
                TrainSegmentPairs,
                StateService
            );
        }

        private ISmtModelFactory CreateSmtModelFactory()
        {
            var factory = Substitute.For<ISmtModelFactory>();

            var translationResult = new TranslationResult(
                "this is a TEST.",
                "esto es una prueba .".Split(),
                "this is a TEST .".Split(),
                new[] { 1.0, 1.0, 1.0, 1.0, 1.0 },
                new[]
                {
                    TranslationSources.Smt,
                    TranslationSources.Smt,
                    TranslationSources.Smt,
                    TranslationSources.Smt,
                    TranslationSources.Smt
                },
                new WordAlignmentMatrix(5, 5)
                {
                    [0, 0] = true,
                    [1, 1] = true,
                    [2, 2] = true,
                    [3, 3] = true,
                    [4, 4] = true
                },
                new[] { new Phrase(Range<int>.Create(0, 5), 5) }
            );
            SmtModel
                .TranslateAsync(1, Arg.Any<string>())
                .Returns(Task.FromResult<IReadOnlyList<TranslationResult>>(new[] { translationResult }));
            SmtModel
                .GetWordGraphAsync(Arg.Any<string>())
                .Returns(
                    Task.FromResult(
                        new WordGraph(
                            "esto es una prueba .".Split(),
                            new[]
                            {
                                new WordGraphArc(
                                    0,
                                    1,
                                    1.0,
                                    "this is".Split(),
                                    new WordAlignmentMatrix(2, 2) { [0, 0] = true, [1, 1] = true },
                                    Range<int>.Create(0, 2),
                                    GetSources(2, false),
                                    new[] { 1.0, 1.0 }
                                ),
                                new WordGraphArc(
                                    1,
                                    2,
                                    1.0,
                                    "a test".Split(),
                                    new WordAlignmentMatrix(2, 2) { [0, 0] = true, [1, 1] = true },
                                    Range<int>.Create(2, 4),
                                    GetSources(2, false),
                                    new[] { 1.0, 1.0 }
                                ),
                                new WordGraphArc(
                                    2,
                                    3,
                                    1.0,
                                    new[] { "." },
                                    new WordAlignmentMatrix(1, 1) { [0, 0] = true },
                                    Range<int>.Create(4, 5),
                                    GetSources(1, false),
                                    new[] { 1.0 }
                                )
                            },
                            new[] { 3 }
                        )
                    )
                );

            factory
                .Create(
                    Arg.Any<string>(),
                    Arg.Any<IRangeTokenizer<string, int, string>>(),
                    Arg.Any<IDetokenizer<string, string>>(),
                    Arg.Any<ITruecaser>()
                )
                .Returns(SmtModel);
            factory
                .CreateTrainer(
                    Arg.Any<string>(),
                    Arg.Any<IRangeTokenizer<string, int, string>>(),
                    Arg.Any<IParallelTextCorpus>()
                )
                .Returns(SmtBatchTrainer);
            return factory;
        }

        private static ITransferEngineFactory CreateTransferEngineFactory()
        {
            var factory = Substitute.For<ITransferEngineFactory>();
            var engine = Substitute.For<ITranslationEngine>();
            engine
                .TranslateAsync(Arg.Any<string>())
                .Returns(
                    Task.FromResult(
                        new TranslationResult(
                            "this is a TEST.",
                            "esto es una prueba .".Split(),
                            "this is a TEST .".Split(),
                            new[] { 1.0, 1.0, 1.0, 1.0, 1.0 },
                            new[]
                            {
                                TranslationSources.Transfer,
                                TranslationSources.Transfer,
                                TranslationSources.Transfer,
                                TranslationSources.Transfer,
                                TranslationSources.Transfer
                            },
                            new WordAlignmentMatrix(5, 5)
                            {
                                [0, 0] = true,
                                [1, 1] = true,
                                [2, 2] = true,
                                [3, 3] = true,
                                [4, 4] = true
                            },
                            new[] { new Phrase(Range<int>.Create(0, 5), 5) }
                        )
                    )
                );
            factory
                .Create(
                    Arg.Any<string>(),
                    Arg.Any<IRangeTokenizer<string, int, string>>(),
                    Arg.Any<IDetokenizer<string, string>>(),
                    Arg.Any<ITruecaser>()
                )
                .Returns(engine);
            return factory;
        }

        private ITruecaserFactory CreateTruecaserFactory()
        {
            var factory = Substitute.For<ITruecaserFactory>();
            factory.CreateAsync(Arg.Any<string>()).Returns(Task.FromResult(Truecaser));
            factory
                .CreateTrainer(Arg.Any<string>(), Arg.Any<ITokenizer<string, int, string>>(), Arg.Any<ITextCorpus>())
                .Returns(TruecaserTrainer);
            return factory;
        }

        private static IEnumerable<TranslationSources> GetSources(int count, bool isUnknown)
        {
            var sources = new TranslationSources[count];
            for (int i = 0; i < count; i++)
                sources[i] = isUnknown ? TranslationSources.None : TranslationSources.Smt;
            return sources;
        }

        public Task WaitForBuildToFinishAsync()
        {
            return WaitForBuildState(e => e.BuildState is BuildState.None);
        }

        public Task WaitForBuildToStartAsync()
        {
            return WaitForBuildState(e => e.BuildState is BuildState.Active);
        }

        private async Task WaitForBuildState(Func<TranslationEngine, bool> predicate)
        {
            using ISubscription<TranslationEngine> subscription = await Engines.SubscribeAsync(
                e => e.EngineId == "engine1"
            );
            while (true)
            {
                TranslationEngine? build = subscription.Change.Entity;
                if (build is not null && predicate(build))
                    break;
                await subscription.WaitForChangeAsync();
            }
        }

        protected override void DisposeManagedResources()
        {
            StateService.Dispose();
            _jobServer.Dispose();
        }

        private class EnvActivator : JobActivator
        {
            private readonly TestEnvironment _env;

            public EnvActivator(TestEnvironment env)
            {
                _env = env;
            }

            public override object ActivateJob(Type jobType)
            {
                if (jobType == typeof(SmtTransferEngineBuildJob))
                {
                    return new SmtTransferEngineBuildJob(
                        _env.PlatformService,
                        _env.Engines,
                        _env.TrainSegmentPairs,
                        _env._lockFactory,
                        _env._truecaserFactory,
                        _env._smtModelFactory,
                        Substitute.For<ICorpusService>(),
                        Substitute.For<ILogger<SmtTransferEngineBuildJob>>()
                    );
                }
                return base.ActivateJob(jobType);
            }
        }
    }
}
