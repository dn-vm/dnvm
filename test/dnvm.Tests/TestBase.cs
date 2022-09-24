using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xunit.Abstractions;

namespace Dnvm.Tests.InstallTests;

public abstract record TestBase
{
	private TestBase()
	{
		throw new InvalidOperationException();
	}
	public TestBase(ITestOutputHelper output)
	{
		Output = output;
		Directory.Delete(TestSuiteArtifactsPath, true);
		Directory.CreateDirectory(TestSuiteArtifactsPath);
	}

	public ITestOutputHelper Output;
	public abstract string CommandLine { get; init; }
	internal static string ArtifactsPath => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(typeof(Program).Assembly.Location)!, "..", "..", "..", ".."));
	internal virtual Manifest NewTestManifest => new Manifest(ImmutableArray<Workload>.Empty, null, TestManifestPath);
	internal string TestManifestPath => Path.Combine(TestSuiteArtifactsPath, "dnvmManifest.json");
	internal Manifest? CreatedManifest => ManifestHelpers.TryGetManifest(out var m, TestManifestPath) ? m : null;
	internal string InstalledDotnetExePath => Path.Combine(Directory.GetDirectories(TestSuiteArtifactsPath).Single(), "dotnet" + Utilities.ExeFileExtension);
	internal string RunningTestName => this.GetType().Name;
	internal string TestSuiteArtifactsPath => Directory.CreateDirectory(Path.Combine(ArtifactsPath, "testcases", TestSuiteName, RunningTestName)).FullName;
	public abstract string TestSuiteName { get; }
	internal virtual IClient Client => new DefaultClient();
	internal virtual ILogger Logger => new TestLogger(Output);
	public virtual Program Program => new Program() { Logger = Logger, Manifest = NewTestManifest, Client = Client };

	[Fact]
	public abstract Task Test();
}
