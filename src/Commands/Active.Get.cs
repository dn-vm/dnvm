using System.CommandLine;
using System.Threading.Tasks;

namespace Dnvm;

internal partial class Active
{
	public partial class Get : Command
	{
		Program _dnvm;

		public Get(Program dnvm) : base("get")
		{
			_dnvm = dnvm;
			this.Add(new Path(_dnvm));
			this.SetHandler(Handle);
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
