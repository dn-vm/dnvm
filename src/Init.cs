using StaticCs;
using System;
using System.CommandLine;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace Dnvm
{
	internal class Init
	{
		public record struct Options(Shell Shell, bool Copyable);

		public static Command Command
		{
			get
			{
				Command init = new("init", "Prints the shell commands to add dnvm and the active sdk to the PATH.");

				Argument<Shell> shell = new Argument<Shell>("shell");
				init.Add(shell);

				Option<bool> copyable = new(new[] { "--copyable", "-c" }, "Print the line to be copied and pasted into your profile to add the active sdk to the PATH.");
				init.Add(copyable);

				init.SetHandler(Handle, shell, copyable);
				return init;
			}
		}

		Logger _logger;
		Options _options;

		internal static Task<int> Handle(Shell shell, bool copyable)
		{
			var init = new Init(Program.Logger, new Options(shell, copyable));
			return init.Handle();
		}

		public Task<int> Handle()
		{
			if (_options.Copyable)
			{
				_logger.Log(ProfileText(_options.Shell));
				return Task.FromResult(0);
			}
			string output = AddToPathText(_options.Shell, Path.GetDirectoryName(Utilities.ProcessPath)!);

			string activePath = ManifestHelpers.Instance.Active.Path;
			if (!string.IsNullOrEmpty(activePath))
				output += AddToPathText(_options.Shell, activePath);

			_logger.Log(output);
			return Task.FromResult(0);
		}

		public static string ProfileText(Shell shell)
			=> shell switch
			{
				Shell.Powershell => $"Invoke-Expression -Command $({Utilities.ProcessPath} init powershell | out-string)",
				Shell.Bash => $"eval \"$({Utilities.ProcessPath} init bash)\"",
				Shell.Zsh => throw new NotImplementedException()
			};

		public Init(Logger logger, Options options)
		{
			_logger = logger;
			_options = options;
		}

		static string AddToPathText(Shell shell, string path)
			=> shell switch
			{
				Shell.Powershell => PowershellAddToPathText(path),
				Shell.Bash => BashAddToPathText(path),
				Shell.Zsh => ZshAddToPathText(path)
			} + Environment.NewLine;

		static string PowershellAddToPathText(string pathToAdd)
			=> $"$env:PATH=\"{pathToAdd};$env:PATH\"";

		static string BashAddToPathText(string pathToAdd)
			=> Utilities.CurrentOS == OSPlatform.Windows ?
				$"export PATH=\"{ConvertWindowPathToGitBash(pathToAdd)}:$PATH\""
				: $"export PATH=\"{pathToAdd}:$PATH\"";

		static string ConvertWindowPathToGitBash(string path)
			=> $"/{path[0].ToString().ToLower()}{path[2..].Replace('\\', '/')}";

		static string ZshAddToPathText(string pathToAdd)
			=> throw new NotImplementedException();

		public void Activate(Workload workload)
		{
			var envPath = Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.User)!;
			envPath = ReplaceOrAddActiveWorkloadInPath(envPath, workload.Path);
			Environment.SetEnvironmentVariable("PATH", envPath, EnvironmentVariableTarget.User);
		}

		public static string ReplaceOrAddActiveWorkloadInPath(string path, string newActivePath)
		{
			string flag = "#dnvm-active;";
			int firstFlagStart = path.IndexOf(flag);
			// If flag not there, add to front
			if (firstFlagStart == -1)
				return flag + newActivePath + ";" + flag + path;
			int startReplace = firstFlagStart + flag.Length;
			int endReplace = path.LastIndexOf(flag);
			path = path.Remove(startReplace, endReplace - startReplace);
			path = path.Insert(startReplace, newActivePath + ';');
			return path;
		}

		[Closed]
		internal enum Shell
		{
			Powershell,
			Bash,
			Zsh
		}
	}
}
