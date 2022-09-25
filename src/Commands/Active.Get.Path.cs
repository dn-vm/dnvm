using System;
using System.CommandLine;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Dnvm;

internal partial class Active
{
	public partial class Get
	{
		public class Path : Command
		{
			Program _dnvm;
			public Path(Program dnvm) : base("path", "Get the current path updated with the active sdk")
			{
				_dnvm = dnvm;
				this.SetHandler(Handle);
			}
			public Task<int> Handle()
			{
				_dnvm.Logger.Log(NewPath);
				return Task.FromResult(0);
			}

			static string PathFlag = "*!--DNVM_DIR--*";

			public string NewPath
			{
				get
				{
					string oldPath = _dnvm.EnvPath;
					char pathEntryDelimiter = OperatingSystem.IsWindows() ? ';' : ':';
					bool skipNext = false;
					StringBuilder outPath = new();
					var newPath = string.Join(
						pathEntryDelimiter,
						oldPath.Split(pathEntryDelimiter, StringSplitOptions.RemoveEmptyEntries)
							.Where(entry =>
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
							}

							));
					if (_dnvm.Manifest.Active is Workload active)
						return string.Join(pathEntryDelimiter, PathFlag, active.Path, newPath);
					else
						return newPath;
				}
			}

		}
	}
}
