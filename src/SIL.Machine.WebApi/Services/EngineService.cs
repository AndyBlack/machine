﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using Autofac.Features.OwnedInstances;
using Microsoft.Extensions.Options;
using SIL.Machine.Translation;
using SIL.Machine.WebApi.Configuration;
using SIL.Machine.WebApi.DataAccess;
using SIL.Machine.WebApi.Models;
using SIL.Machine.WebApi.Utils;

namespace SIL.Machine.WebApi.Services
{
	internal class EngineService : AsyncDisposableBase, IEngineServiceInternal
	{
		private readonly IOptions<EngineOptions> _engineOptions;
		private readonly ConcurrentDictionary<string, Owned<EngineRuntime>> _runtimes;
		private readonly IEngineRepository _engines;
		private readonly IBuildRepository _builds;
		private readonly Func<string, Owned<EngineRuntime>> _engineRunnerFactory;
		private readonly AsyncTimer _commitTimer;

		public EngineService(IOptions<EngineOptions> engineOptions, IEngineRepository engines,
			IBuildRepository builds, Func<string, Owned<EngineRuntime>> engineRuntimeFactory)
		{
			_engineOptions = engineOptions;
			_engines = engines;
			_builds = builds;
			_engineRunnerFactory = engineRuntimeFactory;
			_runtimes = new ConcurrentDictionary<string, Owned<EngineRuntime>>();
			_commitTimer = new AsyncTimer(EngineCommitAsync);
		}

		public void Init()
		{
			_commitTimer.Start(_engineOptions.Value.EngineCommitFrequency);
		}

		private async Task EngineCommitAsync()
		{
			foreach (Owned<EngineRuntime> runner in _runtimes.Values)
				await runner.Value.CommitAsync();
		}

		public async Task<TranslationResult> TranslateAsync(string engineId, IReadOnlyList<string> segment)
		{
			CheckDisposed();

			if (!await _engines.ExistsAsync(engineId))
				return null;
			EngineRuntime runtime = GetOrCreateRuntime(engineId);
			return await runtime.TranslateAsync(segment);
		}

		public async Task<IEnumerable<TranslationResult>> TranslateAsync(string engineId, int n,
			IReadOnlyList<string> segment)
		{
			CheckDisposed();

			if (!await _engines.ExistsAsync(engineId))
				return null;
			EngineRuntime runtime = GetOrCreateRuntime(engineId);
			return await runtime.TranslateAsync(n, segment);
		}

		public async Task<WordGraph> GetWordGraphAsync(string engineId, IReadOnlyList<string> segment)
		{
			CheckDisposed();

			if (!await _engines.ExistsAsync(engineId))
				return null;
			EngineRuntime runtime = GetOrCreateRuntime(engineId);
			return await runtime.GetWordGraph(segment);
		}

		public async Task<bool> TrainSegmentAsync(string engineId, IReadOnlyList<string> sourceSegment,
			IReadOnlyList<string> targetSegment, bool sentenceStart)
		{
			CheckDisposed();

			if (!await _engines.ExistsAsync(engineId))
				return false;
			EngineRuntime runtime = GetOrCreateRuntime(engineId);
			await runtime.TrainSegmentPairAsync(sourceSegment, targetSegment, sentenceStart);
			return true;
		}

		public async Task<bool> AddAsync(Engine engine)
		{
			CheckDisposed();

			try
			{
				await _engines.InsertAsync(engine);
				EngineRuntime runtime = CreateRuntime(engine.Id);
				await runtime.InitNewAsync();
			}
			catch (KeyAlreadyExistsException)
			{
				// a project with the same id already exists
				return false;
			}
			return true;
		}

		public async Task<bool> RemoveAsync(string engineId)
		{
			CheckDisposed();

			if (!await _engines.ExistsAsync(engineId))
				return false;

			await _engines.DeleteAsync(engineId);
			await _builds.DeleteAllByEngineIdAsync(engineId);

			EngineRuntime runtime = GetOrCreateRuntime(engineId);
			// the engine will have no associated projects, so remove it
			_runtimes.TryRemove(engineId, out _);
			await runtime.DeleteDataAsync();
			await runtime.DisposeAsync();
			return true;
		}

		public async Task<Build> StartBuildAsync(string engineId)
		{
			CheckDisposed();

			if (!await _engines.ExistsAsync(engineId))
				return null;
			EngineRuntime runtime = GetOrCreateRuntime(engineId);
			return await runtime.StartBuildAsync();
		}

		public async Task CancelBuildAsync(string engineId)
		{
			CheckDisposed();

			if (TryGetRuntime(engineId, out EngineRuntime runtime))
				await runtime.CancelBuildAsync();
		}

		public async Task<(Engine Engine, EngineRuntime Runtime)> GetEngineAsync(string engineId)
		{
			CheckDisposed();

			Engine engine = await _engines.GetAsync(engineId);
			if (engine == null)
				return (null, null);
			return (engine, GetOrCreateRuntime(engineId));
		}

		internal EngineRuntime GetOrCreateRuntime(string engineId)
		{
			return _runtimes.GetOrAdd(engineId, _engineRunnerFactory).Value;
		}

		private EngineRuntime CreateRuntime(string engineId)
		{
			Owned<EngineRuntime> runtime = _engineRunnerFactory(engineId);
			_runtimes.TryAdd(engineId, runtime);
			return runtime.Value;
		}

		private bool TryGetRuntime(string engineId, out EngineRuntime runtime)
		{
			if (_runtimes.TryGetValue(engineId, out Owned<EngineRuntime> ownedRuntime))
			{
				runtime = ownedRuntime.Value;
				return true;
			}

			runtime = null;
			return false;
		}

		protected override async ValueTask DisposeAsyncCore()
		{
			await _commitTimer.DisposeAsync();
			foreach (Owned<EngineRuntime> runtime in _runtimes.Values)
				await runtime.DisposeAsync();
			_runtimes.Clear();
		}
	}
}
