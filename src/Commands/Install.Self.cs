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
	internal class Self : Command
	{
		Options _options;
		Program _dnvm;

		public Self(Program dnvm) : base("self", "Installs the dnvm executable to ~/.dnvm and add init script to profile files")
		{
			_dnvm = dnvm;

			Option<bool> force = new("--force");
			force.AddAlias("-f");
			this.Add(force);

			this.SetHandler(Handle, force);
		}

		public new record struct Options(bool Force);

		Task<int> Handle(bool force)
		{
			_options = new Options(force);
			return this.Handle();
		}

		async Task<int> Handle()
		{
			if (!Utilities.IsAOT)
			{
				Console.WriteLine("Cannot self-install into target location: the current executable is not deployed as a single file.");
				return 1;
			}

			var procPath = Utilities.ProcessPath;
			_dnvm.Logger.Info("Location of running exe" + procPath);

			var targetPath = Path.Combine(Utilities.LocalInstallLocation, Utilities.DnvmExeName);
			if (!_options.Force && File.Exists(targetPath))
			{
				_dnvm.Logger.Log("dnvm is already installed at: " + targetPath);
				_dnvm.Logger.Log("Did you mean to run `dnvm update`? Otherwise, the '--force' flag is required to overwrite the existing file.");
			}
			else
			{
				try
				{
					_dnvm.Logger.Info($"Copying file from '{procPath}' to '{targetPath}'");
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
				return self;
			}
		}

		private async Task AddInitToShellProfile(string installedDnvmPath)
		{
			_dnvm.Logger.Info("Setting environment variables in shell files");
			// Scan shell files for shell suffix and add it if it doesn't exist
			_dnvm.Logger.Log("Scanning for shell files to update");
			bool written = false;
			foreach (var profileShellInfo in ProfileShellFiles)
			{
				foreach (var profileFile in profileShellInfo.ProfileNames)
				{

					var shellPath = Path.Combine(Environment.GetFolderPath(SpecialFolder.UserProfile), profileFile);
					_dnvm.Logger.Info("Checking for file: " + shellPath);
					if (File.Exists(shellPath))
						_dnvm.Logger.Log("Found " + shellPath);
					else
						continue;
					try
					{
						string profileText = Init.ProfileText(profileShellInfo.Shell, installedDnvmPath);
						if (!await FileContainsLine(shellPath, profileText))
						{
							_dnvm.Logger.Log($"Appending shell init `{profileText}` to {shellPath}");
							await File.AppendAllTextAsync(shellPath, Environment.NewLine + profileText + Environment.NewLine);
							written = true;
						}
						else
						{
							written = true;
							_dnvm.Logger.Log($"Init script already found in {shellPath}");
						}
						break;
					}
					catch (IOException e)
					{
						// Ignore if the file can't be accessed
						_dnvm.Logger.Info($"Couldn't write to file {shellPath}: {e.Message}");
					}
				}
			}
			if (!written)
				_dnvm.Logger.Log("Couldn't add init script to any profiles. Run `dnvm init <shell> --copyable` and copy output to your shell profile.");
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
