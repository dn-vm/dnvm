using System.CommandLine;
using System.CommandLine.Parsing;
using System.Linq;
using System.Threading.Tasks;

namespace Dnvm;

internal partial class Active
{
	public class Set : Command
	{
		Program _dnvm;
		Options? _options;
		public new sealed record Options(Version? Version);

		public Set(Program dnvm) : base("set")
		{
			_dnvm = dnvm;

			Argument<Version?> version = new("version",
				(ArgumentResult arg) => arg.Tokens.Single()?.Value.ToLower() == "none" ? null : Version.Parse(arg),
				false, "The installed version to make active, or 'none'");
			this.Add(version);

			Option<bool> interactive = new(new[] { "--interactive", "-i" }, "Interactively select from installed sdks");
			//this.Add(interactive);

			//this.AddValidator(Utilities.ValidateOneOf(version, interactive));

			this.SetHandler(Handle, version);// , interactive);
		}
		public Task<int> Handle(Version? version)
		{
			_options = new Options(version);
			return Handle();
		}


		public Task<int> Handle()
		{
			if (_options.Version is null)
			{
				_dnvm.Manifest = _dnvm.Manifest with { Active = null };
				_dnvm.Manifest.WriteOut();
				return Task.FromResult(0);
			}
			var newActive = _dnvm.Manifest.Workloads.FirstOrDefault(w => w.Version == _options.Version.ToString());
			if (newActive == default)
				throw new DnvmException($"Version {_options.Version} not installed");

			_dnvm.Manifest = _dnvm.Manifest with { Active = newActive };
			_dnvm.Manifest.WriteOut();
			_dnvm.Logger.Log($"Active set to version {newActive.Version} at {newActive.Path}");
			return Task.FromResult(0);
		}

	}

}
