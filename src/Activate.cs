using System;
using System.CommandLine;
using System.Linq;
using System.Threading.Tasks;

namespace Dnvm;

internal class Activate
{
	public record struct Options(Version Version, bool None);
	public static Command Command
	{
		get
		{
			Command activate = new("activate", "Set an installed sdk as the active version");

			Option<Version> version = new(new[] { "--version", "-v" }, Version.Parse, false, "The installed version to activate");
			activate.Add(version);

			Option<bool> none = new(new[] { "--none", "-n" }, "Remove active sdk an use whatever else is on the path");
			activate.Add(none);

			activate.SetHandler(Handle, version, none);

			return activate;
		}
	}

	static Task<int> Handle(Version version, bool none)
	{
		var activate = new Activate(Program.Logger, new Options(version, none));
		return activate.Handle();
	}

	Options _options;
	Logger _logger;
	Manifest _manifest;

	public Activate(Logger logger, Options options)
	{
		_logger = logger;
		_options = options;
		_manifest = ManifestHelpers.Instance;
	}

	public Task<int> Handle()
	{
		Workload newActive;
		string versionToFind = _options.Version.ToString();
		try
		{
			newActive = _manifest.Workloads.First(w => w.Version == versionToFind);
		}
		catch (InvalidOperationException)
		{
			_logger.Error("Version {_options.Version} is not installed.");
			return Task.FromResult(1);
		}
		_logger.Info($"Updating active sdk to {newActive.Version} at {newActive.Path}");
		_manifest = _manifest with { Active = newActive };
		_manifest.WriteOut();
		return Task.FromResult(0);
	}
}
