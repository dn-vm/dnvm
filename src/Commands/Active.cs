using System.CommandLine;

namespace Dnvm;

internal partial class Active : Command
{
	Program _dnvm;
	public Active(Program dnvm) : base("active")
	{
		_dnvm = dnvm;
		var set = new Set(_dnvm);
		this.Add(set);
		var get = new Get(_dnvm);
		this.Add(get);
	}

	static string PathFlag = "#<!- DNVM_DIR ->";
}
