using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xunit.Abstractions;

namespace Dnvm.Tests.InstallTests;

public class InstallLTS : InstallTest, IInstallTest
{
	Install.Options IInstallTest.Options => this.DefaultOptions();

	public InstallLTS(ITestOutputHelper output) : base(output) { }
}

public class InstallCurrent : InstallTest, IInstallTest
{
	Install.Options IInstallTest.Options => this.DefaultOptions() with { Channel = Channel.From("current") };

	public InstallCurrent(ITestOutputHelper output) : base(output) { }
}

internal interface IInstallTest
{
	ITestOutputHelper Output { get; }

	static readonly string artifacts = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(typeof(Program).Assembly.Location)!, "..", "..", "..", ".."));

	string InstallLocation
	{
		get
		{
			var installLocation = Path.Combine(artifacts, "testcases", "InstallTest", RunningTestName);
			Directory.CreateDirectory(installLocation);
			return installLocation;
		}
	}
	internal Install.Options DefaultOptions => new Install.Options(new Channel(Channel.ChannelKind.LTS), null, InstallLocation, false, false, true, false);
	Install.Options Options { get; }


	Manifest NewTestManifest => new Manifest(ImmutableArray<Workload>.Empty, null, TestManifestPath);

	string TestManifestPath => Path.Combine(InstallLocation, "dnvmManifest.json");

	Manifest? CreatedManifest => ManifestHelpers.TryGetManifest(out var m, TestManifestPath) ? m : null;

	string InstalledDotnetExePath => Path.Combine(Directory.GetDirectories(InstallLocation).Single(), "dotnet" + Utilities.ExeFileExtension);

	string RunningTestName => this.GetType().Name;

	async Task<Install> InstallSdk(Install.Options options)
	{
		var logger = new TestLogger(Output);
		var install = new Install(logger, NewTestManifest, options);
		await install.Handle(@catch: false);
		ValidateInstall();
		return install;
	}

	void ValidateInstall()
	{
		Assert.True(File.Exists(InstalledDotnetExePath));
		Assert.True(CreatedManifest?.Workloads.Any(w => w.Path.Contains(InstallLocation)) == true);
		var p = Process.Start(InstalledDotnetExePath, "--version");
		p.WaitForExit();
		Assert.Equal(0, p.ExitCode);
		return;
	}
}

public abstract class InstallTest
{
	[Fact]
	public Task Install()
	{
		var test = (IInstallTest)this;
		return test.InstallSdk(test.Options);
	}
	public ITestOutputHelper Output { get; set; }

	public InstallTest(ITestOutputHelper output)
	{
		Output = output;
	}
}
internal static class InstallTestExtensions
{
	public static Install.Options DefaultOptions(this IInstallTest test) => new Install.Options(new Channel(Channel.ChannelKind.LTS), null, test.InstallLocation, false, false, true, false);
}


//[Fact]
//Task<Install> InstallLTS()
//{
//	RunningTestName = nameof(InstallLTS);
//	return InstallSdk(Options);
//}

//[Fact]
//Task<Install> InstallCurrent()
//{
//	RunningTestName = nameof(InstallCurrent);
//	return InstallSdk(Options with { Channel = new Channel(Channel.ChannelKind.Current) });
//}

//[Fact]
//Task<Install> InstallPreview()
//{
//	//RunningTestName = nameof(InstallPreview);
//	return InstallSdk(Options with { Channel = new Channel(Channel.ChannelKind.Preview) });
//}

//[Fact]
//Task<Install> Install7_0()
//{
//	//RunningTestName = nameof(Install7_0_1xx);
//	return InstallSdk(Options with { Channel = Channel.From("7.0") });
//}

//[Fact]
//Task InstallFails()
//{
//	//RunningTestName = nameof(InstallFails);
//	var options = Options with { Channel = Channel.From("9.0") };
//	return Assert.ThrowsAsync<DnvmException>(() => InstallSdk(options));
//}
//}
