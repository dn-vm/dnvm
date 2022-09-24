using Dnvm.Tests.InstallTests;
using System.Collections.Immutable;
using System.CommandLine.Parsing;
using System.IO;
using System.Threading.Tasks;
using Xunit.Abstractions;

namespace Dnvm.Tests.ActiveTests;

public record SetSeven0(ITestOutputHelper Output, string CommandLine = "set 7.0.0") : SetActiveTests(Output)
{
	internal override Workload? ExpectedActive => Six301;
}

public record SetSix401(ITestOutputHelper Output, string CommandLine = "set 6.0.401") : SetActiveTests(Output)
{
	internal override Workload? ExpectedActive => Six301;
}

public record SetSix301(ITestOutputHelper Output, string CommandLine = "set 6.0.301") : SetActiveTests(Output)
{
	internal override Workload? ExpectedActive => Six301;
}

public record SetNone(ITestOutputHelper Output, string CommandLine = "set none") : SetActiveTests(Output)
{
	internal override Workload? ExpectedActive => null;
}

public abstract record SetActiveTests(ITestOutputHelper Output) : ActiveTests(Output)
{
	internal abstract Workload? ExpectedActive { get; }
	internal override Task Validate(int exitCode)
	{
		Assert.Equal(0, exitCode);
		return Task.CompletedTask;
	}
}

public abstract record ActiveTests(ITestOutputHelper Output) : TestBase(Output)
{
	public override string TestSuiteName => "ActiveTests";
	internal Workload Seven0 => new Workload("7.0.0", Path.Combine(TestSuiteArtifactsPath, "7.0.0"));
	internal Workload Six301 => new Workload("6.0.301", Path.Combine(TestSuiteArtifactsPath, "6.0.301"));
	internal Workload Six401 => new Workload("6.0.401", Path.Combine(TestSuiteArtifactsPath, "6.0.401"));
	internal Workload Five0 => new Workload("5.0.0", Path.Combine(TestSuiteArtifactsPath, "5.0.0"));
	internal override Manifest NewTestManifest
	{
		get
		{
			ImmutableArray<Workload> workloads = ImmutableArray<Workload>.Empty;
			workloads = workloads.Add(Seven0);
			workloads = workloads.Add(Six401);
			workloads = workloads.Add(Six301);
			workloads = workloads.Add(Five0);
			return new Manifest(workloads, Five0, TestManifestPath);
		}
	}
	internal abstract Task Validate(int exitCode);
	public override async Task Test()
	{
		int exitCode = await Program.Command.InvokeAsync($"active {CommandLine}");
		await Validate(exitCode);
	}

}
