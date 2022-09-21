using StaticCs;
using System;
using System.CommandLine;
using System.Threading.Tasks;

namespace Dnvm
{
	internal class Init
	{
		[Closed]
		internal enum Shell
		{
			Powershell,
			Bash,
			Zsh
		}
		public static Command Command
		{
			get
			{
				Command init = new("init");

				Argument<Shell> shell = new Argument<Shell>("shell");
				init.Add(shell);

				init.SetHandler(Handle, shell);

				return init;
			}
		}
		internal static Task<int> Handle(Shell shell)
		{
			var output = shell switch
			{
				Shell.Powershell => PowershellInit(),
				Shell.Bash => BashInit(),
				Shell.Zsh => ZshInit()
			};
			Program.Logger.Log(output);
			return Task.FromResult(0);
		}

		static string BashInit()
		{
			throw new NotImplementedException();
		}

		static string ZshInit()
		{
			throw new NotImplementedException();
		}

		static string PowershellInit()
		{
			string output = "";
			var manifest = ManifestHelpers.Instance;
			var activeWorkload = manifest.Active;
			output += PowershellAddToPathText(Utilities.ProcessPath);
			if (!string.IsNullOrEmpty(activeWorkload.Path))
				output += PowershellAddToPathText(activeWorkload.Path);
			WindowsSetActive(manifest.Active);
			return output;
		}

		static void WindowsSetActive(Workload workload)
		{
			var path = Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.User)!;
			path = ReplaceOrAddActiveWorkloadInPath(path, workload.Path);

			Environment.SetEnvironmentVariable("PATH", path, EnvironmentVariableTarget.User);
		}

		static string ReplaceOrAddActiveWorkloadInPath(string path, string newActivePath)
		{
			string flag = "#dnvm-active;";
			int startReplace = path.IndexOf(flag);
			if (startReplace == -1)
			{
				path = flag + newActivePath + ";" + flag + path;
			}
			else
			{
				startReplace += flag.Length;
				int endReplace = path.LastIndexOf(flag);
				path = path.Remove(startReplace, endReplace - startReplace);
				path = path.Insert(startReplace, newActivePath + ';');
			}
			return path;
		}

		static string PowershellAddToPathText(string pathToAdd)
			=> $"$env:PATH = {pathToAdd};$env:PATH" + Environment.NewLine;
	}
}
