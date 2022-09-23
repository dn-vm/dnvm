using System;
using System.Collections.Immutable;
using System.CommandLine;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Threading.Tasks;
using static Dnvm.Init;
using static System.Environment;

namespace Dnvm;

sealed partial class Install
{
	internal class SelfInstall
	{
		public SelfInstall(ILogger logger, Options options)
		{
			_logger = logger;
			_options = options;
		}

		public record struct Options(bool Force);

		ILogger _logger;
		Options _options;

		static Task<int> Handle(bool force)
		{
			var selfInstall = new SelfInstall(Logger.Default, new Options(force));
			return selfInstall.Handle();
		}

		async Task<int> Handle()
		{
			if (!Utilities.IsAOT)
			{
				Console.WriteLine("Cannot self-install into target location: the current executable is not deployed as a single file.");
				return 1;
			}

			var procPath = Utilities.ProcessPath;
			_logger.Info("Location of running exe" + procPath);

			var targetPath = Path.Combine(Utilities.LocalInstallLocation, Utilities.DnvmExeName);
			if (!_options.Force && File.Exists(targetPath))
			{
				_logger.Log("dnvm is already installed at: " + targetPath);
				_logger.Log("Did you mean to run `dnvm update`? Otherwise, the '--force' flag is required to overwrite the existing file.");
			}
			else
			{
				try
				{
					_logger.Info($"Copying file from '{procPath}' to '{targetPath}'");
					File.Copy(procPath, targetPath, overwrite: _options.Force);
				}
				catch (Exception e)
				{
					Console.WriteLine($"Could not copy file from '{procPath}' to '{targetPath}': {e.Message}");
					return 1;
				}
			}

			await AddInitToShellProfile(targetPath);

			return 0;
		}

		public static Command Command
		{
			get
			{
				Command self = new("self");
				self.Description = $"""
								Installs dnvm to ~/.dnvm/ and adds hook to profile to add dnvm and the active sdk to path
								""";

				Option<bool> force = new("--force");
				force.AddAlias("-f");
				self.Add(force);

				self.SetHandler(Handle, force);
				return self;
			}
		}

		private async Task AddInitToShellProfile(string installedDnvmPath)
		{
			_logger.Info("Setting environment variables in shell files");
			// Scan shell files for shell suffix and add it if it doesn't exist
			_logger.Log("Scanning for shell files to update");
			bool written = false;
			foreach (var profileShellInfo in ProfileShellFiles)
			{
				foreach (var profileFile in profileShellInfo.ProfileNames)
				{

					var shellPath = Path.Combine(Environment.GetFolderPath(SpecialFolder.UserProfile), profileFile);
					_logger.Info("Checking for file: " + shellPath);
					if (File.Exists(shellPath))
						_logger.Log("Found " + shellPath);
					else
						continue;
					try
					{
						string profileText = Init.ProfileText(profileShellInfo.Shell, installedDnvmPath);
						if (!await FileContainsLine(shellPath, profileText))
						{
							_logger.Log($"Appending shell init `{profileText}` to {shellPath}");
							await File.AppendAllTextAsync(shellPath, profileText);
							written = true;
						}
						else
						{
							written = true;
							_logger.Log($"Init script already found in {shellPath}");
						}
						break;
					}
					catch (IOException e)
					{
						// Ignore if the file can't be accessed
						_logger.Info($"Couldn't write to file {shellPath}: {e.Message}");
					}
				}
			}
			if (!written)
				_logger.Log("Couldn't add init script to any profiles. Run `dnvm init <shell> --copyable` and copy output to your shell profile.");
		}

		private static ImmutableArray<(Shell Shell, string[] ProfileNames)> ProfileShellFiles
			=> ImmutableArray.Create(
				(Shell.Bash, BashProfiles),
				(Shell.Zsh, ZshProfiles),
				(Shell.Zsh, PowershellProfiles)
			);

		protected static string[] ZshProfiles => new string[] {
			Path.Combine(Environment.GetFolderPath(SpecialFolder.UserProfile), ".zprofile"),
			Path.Combine(Environment.GetFolderPath(SpecialFolder.UserProfile), ".zshrc")
		};

		protected static string[] BashProfiles => new string[] {
			Path.Combine(Environment.GetFolderPath(SpecialFolder.UserProfile), ".profile"),
			Path.Combine(Environment.GetFolderPath(SpecialFolder.UserProfile), ".bashrc")
		};

		//Windows - $PSHOME\Profile.ps1
		//Linux - /usr/local/microsoft/powershell/7/profile.ps1
		//macOS - /usr/local/microsoft/powershell/7/profile.ps1
		//All Users, Current Host
		//Windows - $PSHOME\Microsoft.PowerShell_profile.ps1
		//Linux - /usr/local/microsoft/powershell/7/Microsoft.Powershell_profile.ps1
		//macOS - /usr/local/microsoft/powershell/7/Microsoft.Powershell_profile.ps1
		//Current User, All Hosts
		//Windows - $Home\Documents\PowerShell\Profile.ps1
		//Linux - ~/.config/powershell/profile.ps1
		//macOS - ~/.config/powershell/profile.ps1
		//Current user, Current Host
		//Windows - $Home\Documents\PowerShell\Microsoft.PowerShell_profile.ps1
		//Linux - ~/.config/powershell/Microsoft.Powershell_profile.ps1
		//macOS - ~/.config/powershell/Microsoft.Powershell_profile.ps1
		protected static string[] PowershellProfiles
		{
			get
			{
				if (OperatingSystem.IsWindows())
				{
					return new string[] {
					Path.Combine(Environment.GetFolderPath(SpecialFolder.UserProfile), "Documents", "Powershell", "Profile.ps1"),
					Path.Combine(Environment.GetFolderPath(SpecialFolder.UserProfile), "Documents", "Powershell", "Microsoft.Powershell_profile.ps1"),
				};
				}
				else
				{
					return new string[] {
					Path.Combine(Environment.GetFolderPath(SpecialFolder.UserProfile), ".config", "powershell", "profile.ps1"),
					Path.Combine(Environment.GetFolderPath(SpecialFolder.UserProfile), ".config", "powershell", "Microsoft.Powershell_profile.ps1"),
				};
				}
			}
		}


		private static async Task<bool> FileContainsLine(string filePath, string contents)
		{
			using var file = MemoryMappedFile.CreateFromFile(filePath, FileMode.Open);
			var stream = file.CreateViewStream();
			using var reader = new StreamReader(stream);
			string? line;
			while ((line = await reader.ReadLineAsync()) is not null)
			{
				if (line.Contains(contents))
				{
					return true;
				}
			}
			return false;
		}
	}
}
