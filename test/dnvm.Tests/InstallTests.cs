using System.Collections.Immutable;
using System.CommandLine;
using System.CommandLine.Builder;
using System.CommandLine.Parsing;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xunit.Abstractions;

namespace Dnvm.Tests.InstallTests;

public record InstallLTS(ITestOutputHelper Output, string CommandLine = "--channel lts") : InstallSuccess(Output);

public record InstallCurrent(ITestOutputHelper Output, string CommandLine = "--channel current") : InstallSuccess(Output);

public record InstallPreview(ITestOutputHelper Output, string CommandLine = "--channel preview") : InstallSuccess(Output);

public record Install7_0(ITestOutputHelper Output, string CommandLine = "--channel 7.0") : InstallSuccess(Output);

public record Install6_0(ITestOutputHelper Output, string CommandLine = "--channel 6.0") : InstallSuccess(Output);

public record Install5_0(ITestOutputHelper Output, string CommandLine = "--channel 5.0") : InstallSuccess(Output);

public record InstallAsdf(ITestOutputHelper Output, string CommandLine = "--channel asdf") : InstallFailure(Output);

public record Install6_0_4xx(ITestOutputHelper Output, string CommandLine = "--channel 6.0.4xx") : InstallSuccess(Output);

public record Install6_0_4xxDaily(ITestOutputHelper Output, string CommandLine = "--channel 6.0.4xx --daily") : InstallSuccess(Output);

public record Install6_0Daily(ITestOutputHelper Output, string CommandLine = "--channel 6.0 --daily") : InstallSuccess(Output);

public record Install7_0Daily(ITestOutputHelper Output, string CommandLine = "--channel 7.0 --daily") : InstallSuccess(Output);

public record Install7_0_100_rc_1_22431_12(ITestOutputHelper Output, string CommandLine = "--version 7.0.100-rc.1.22431.12") : InstallSuccess(Output);

public record ChannelAndVersionFails(ITestOutputHelper Output, string CommandLine = "--version 8.0.1 --channel 7.0") : InstallFailure(Output);

public record DailyAndVersionFails(ITestOutputHelper Output, string CommandLine = "--version 7.0.1 --daily") : InstallFailure(Output);





public abstract record InstallSuccess(ITestOutputHelper Output) : InstallTest(Output)
{
	public override abstract string CommandLine { get; init; }
	[Fact]
	public override Task Test()
	{
		return InstallSdk();
	}
}

public abstract record InstallFailure(ITestOutputHelper Output) : InstallTest(Output)
{
	public override abstract string CommandLine { get; init; }
	[Fact]
	public override Task Test()
	{
		return Assert.ThrowsAsync<DnvmException>(InstallSdk);
	}
}

public abstract record InstallTest
{
	private InstallTest()
	{
		Output = null!;
	}
	public InstallTest(ITestOutputHelper output)
	{
		Output = output;
	}
	ITestOutputHelper Output;
	public abstract string CommandLine { get; init; }
	static string ArtifactsPath => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(typeof(Program).Assembly.Location)!, "..", "..", "..", ".."));
	Manifest NewTestManifest => new Manifest(ImmutableArray<Workload>.Empty, null, TestManifestPath);
	string TestManifestPath => Path.Combine(TestArtifactsBaseDirectory, "dnvmManifest.json");
	Manifest? CreatedManifest => ManifestHelpers.TryGetManifest(out var m, TestManifestPath) ? m : null;
	string InstalledDotnetExePath => Path.Combine(Directory.GetDirectories(TestArtifactsBaseDirectory).Single(), "dotnet" + Utilities.ExeFileExtension);
	string RunningTestName => this.GetType().Name;
	string TestArtifactsBaseDirectory => Directory.CreateDirectory(Path.Combine(ArtifactsPath, "testcases", "InstallTest", RunningTestName)).FullName;

	[Fact]
	public abstract Task Test();
	protected async Task InstallSdk()
	{
		Directory.Delete(TestArtifactsBaseDirectory, true);
		Directory.CreateDirectory(TestArtifactsBaseDirectory);
		var logger = new TestLogger(Output);
		var exitCode = await new CommandLineBuilder(Install.GetCommand(logger, NewTestManifest, TestArtifactsBaseDirectory)).Build().InvokeAsync($"--path {TestArtifactsBaseDirectory} " + CommandLine);
		ValidateInstall(exitCode);
	}
	void ValidateInstall(int exitCode)
	{
		Assert.Equal(0, exitCode);
		Assert.True(File.Exists(InstalledDotnetExePath));
		Assert.True(CreatedManifest?.Workloads.Any(w => w.Path.Contains(TestArtifactsBaseDirectory)) == true);
		var p = Process.Start(InstalledDotnetExePath, "--version");
		p.WaitForExit();
		Assert.Equal(0, p.ExitCode);
		return;
	}
}
