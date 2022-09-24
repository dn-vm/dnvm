using System.CommandLine;
using System.Threading.Tasks;

namespace Dnvm;

internal partial class Active
{
	public class Get
	{
		Program _dnvm;

		public Get(Program dnvm)
		{
			_dnvm = dnvm;
		}

		public Command Command
		{
			get
			{
				var get = new Command("get");
				get.SetHandler(Handle);
				return get;
			}
		}
		public Task<int> Handle()
		{
			if (_dnvm.Manifest.Active is Workload active)
				_dnvm.Logger.Log($"Version {active.Version} at {active.Path}");
			else
				_dnvm.Logger.Log($"No version active");
			return Task.FromResult(0);
		}

	}

}
