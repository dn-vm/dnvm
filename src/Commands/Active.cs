using System;
using System.CommandLine;
using System.Linq;
using System.Text;

namespace Dnvm;

internal partial class Active : Command
{
	Program _dnvm;
	public Active(Program dnvm) : base("active")
	{
		_dnvm = dnvm;
		var set = new Set(_dnvm);
		this.Add(set);
		var get = new Get(_dnvm).Command;
		this.Add(get);
	}

	static string PathFlag = "#<!- DNVM_DIR ->";

	public static Command GetSetPath(ILogger logger, Manifest manifest)
	{
		Command setPath = new("get-path");

		setPath.SetHandler(() =>
		{
			string oldPath = Environment.GetEnvironmentVariable("PATH")!;
			char pathEntryDelimiter = OperatingSystem.IsWindows() ? ';' : ':';
			bool skipNext = false;
			StringBuilder outPath = new();
			var newPath = string.Join(pathEntryDelimiter, oldPath.Split(pathEntryDelimiter, StringSplitOptions.RemoveEmptyEntries).Where(entry =>
			{
				if (skipNext == true)
				{
					skipNext = false;
					return false;
				}
				if (entry == PathFlag)
				{
					skipNext = true;
					return false;
				}
				return true;
			}));
			logger.Log(newPath);
		});

		return setPath;
	}
}
