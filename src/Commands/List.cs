using System.CommandLine;
using System.Threading.Tasks;

namespace Dnvm;

internal class List : Command
{
	Program _dnvm;

	public List(Program dnvm) : base("list", "List versions of the dotnet sdk installed by dnvm")
	{
		this.SetHandler(Handle);
		_dnvm = dnvm;
	}

	Task<int> Handle(Version? version)
	{
		return this.Handle();
	}

	public Task<int> Handle()
	{
		foreach (var workload in _dnvm.Manifest.Workloads)
		{
			_dnvm.Logger.Log($"Version {workload.Version} installed at {workload.Path})");
		}
		return Task.FromResult(0);
	}

}
