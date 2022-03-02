﻿namespace SIL.Machine.WebApi.Services;

public class BuildProgress : IProgress<ProgressStatus>
{
	private readonly IRepository<Build> _buildRepo;
	private Build _build;

	public BuildProgress(IRepository<Build> buildRepo, Build build)
	{
		_buildRepo = buildRepo;
		_build = build;
	}

	public void Report(ProgressStatus value)
	{
		if (_build.State != BuildState.Active
			|| (_build.PercentCompleted == value.PercentCompleted && _build.Message == value.Message))
		{
			return;
		}

		_build = _buildRepo.UpdateAsync(_build, u => u
			.Set(b => b.PercentCompleted, value.PercentCompleted)
			.Set(b => b.Message, value.Message)).WaitAndUnwrapException()!;
	}
}
