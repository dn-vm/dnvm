
using Serde.Json;
using System;
using System.Collections.Immutable;
using System.CommandLine;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.IO.MemoryMappedFiles;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using static System.Environment;

namespace Dnvm;

sealed class Install
{
	sealed class SelfInstall
	{
		public record struct Options(bool Global);
		static Logger _logger => Program.Logger;
		static Task<int> Handle(bool force)
		{
			if (!Utilities.IsAOT)
			{
				Console.WriteLine("Cannot self-install into target location: the current executable is not deployed as a single file.");
				return Task.FromResult(1);
			}

			var procPath = Environment.ProcessPath;
			_logger.Info("Location of running exe" + procPath);

			var targetPath = Path.Combine(Utilities.LocalInstallLocation, Utilities.ExeName);
			if (!force && File.Exists(targetPath))
			{
				_logger.Log("dnvm is already installed at: " + targetPath);
				_logger.Log("Did you mean to run `dnvm update`? Otherwise, the '--force' flag is required to overwrite the existing file.");
			}
			else
			{
				try
				{
					_logger.Info($"Copying file from '{procPath}' to '{targetPath}'");
					File.Copy(Utilities.ProcessPath, targetPath, overwrite: force);
				}
				catch (Exception e)
				{
					Console.WriteLine($"Could not copy file from '{procPath}' to '{targetPath}': {e.Message}");
					return Task.FromResult(1);
				}
			}

			// Set up path -- probably add eval(dnvm init)
			throw new NotImplementedException();

			//return Task.FromResult(0);
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
	}
	public static Command Command
	{
		get
		{
			Command install = new Command("install");

			install.Add(SelfInstall.Command);

			Option<Channel> channel = new("--channel", Channel.Parse);
			channel.Description = $"""
									The channel to install from.  
									Available options: 'lts', 'current', 'preview', A.B semver (e.g. 5.0), A.B.Cxx semver (e.g. 6.0.4xx)
									""";
			channel.SetDefaultValue(new Channel(Channel.ChannelKind.LTS));
			channel.AddAlias("-c");
			install.Add(channel);

			Option<bool> verbose = new("--verbose");
			verbose.AddAlias("-v");
			install.AddOption(verbose);


			Option<bool> global = new("--global");
			global.AddAlias("-g");
			install.AddOption(global);

			Option<bool> force = new("--force");
			force.AddAlias("-f");
			install.AddOption(force);

			Option<bool> installer = new("--installer");
			installer.AddAlias("-i");
			install.AddOption(installer);

			Option<Version> version = new("--version", Version.Parse);
			install.Add(version);

			Option<string?> path = new("--path");
			path.AddAlias("-p");
			install.Add(path);

			Option<bool> setActive = new("--set-default");
			setActive.AddAlias("-d");
			install.Add(setActive);

			install.SetHandler(Handler, channel, version, path, force, global, verbose, setActive);

			return install;
		}
	}

	public sealed record Options(Channel Channel, Version Version, string Path, bool Force, bool Global, bool Verbose, bool setActive);

	private static async Task<int> Handler(Channel channel, Version version, string? path, bool force, bool global, bool verbose, bool setActive)
	{
		path ??= Path.Combine(Utilities.LocalInstallLocation, Utilities.EscapeFilename(version.ToString()));
		var install = new Install(new Logger(), new Options(channel, version, path ?? Utilities.LocalInstallLocation, force, global, verbose, setActive));

		return await install.Handle();
	}

	private readonly Logger _logger;

	private Options _options;

	private async Task<int> LinuxAddToPath(string pathToAdd, bool global = false)
	{
		string addToPath = $"PATH=$PATH:{pathToAdd}";
		if (global)
		{
			_logger.Info($"Adding {pathToAdd} to the global PATH in /etc/profile.d/dnvm.sh");
			try
			{
				using (var f = File.OpenWrite("/etc/profile.d/dnvm.sh"))
				{
					await f.WriteAsync(System.Text.Encoding.UTF8.GetBytes(addToPath).AsMemory());
				}
				return 0;
			}
			catch (UnauthorizedAccessException)
			{
				_logger.Error("Unable to write to /etc/profile.d/dnvm.sh, attempting to write to local environment");
			}
		}
		return await UnixAddToPathInShellFiles(pathToAdd);
	}

	private async Task<int> MacAddToPath(string pathToAdd)
	{
		//if (_options.Global) {
		//    _logger.Info($"Adding {pathToAdd} to the global PATH in /etc/paths.d/dnvm");
		//    try
		//    {
		//        using (var f = File.OpenWrite("/etc/paths.d/dnvm"))
		//        {
		//            await f.WriteAsync(System.Text.Encoding.UTF8.GetBytes(pathToAdd).AsMemory());
		//        }
		//        return 0;
		//    }
		//    catch (UnauthorizedAccessException)
		//    {
		//        _logger.Error("Unable to write path to /etc/paths.d/dnvm, attempting to write to local environment");
		//    }
		//}
		return await UnixAddToPathInShellFiles(pathToAdd);
	}

	public Install(Logger logger, Options options)
	{
		_logger = logger;
		_options = options;
		if (_options.Verbose)
		{
			_logger.LogLevel = LogLevel.Info;
		}
	}
	public static string[] Feeds { get; } = new[] {
			"https://dotnetcli.azureedge.net/dotnet",
			"https://dotnetbuilds.azureedge.net/public"
		};

	bool VersionIsAlreadyInstalled()
	{
		return ManifestHelpers.Instance.Workloads.Contains(new Workload(_options.Version.ToString()));
	}

	public async Task SetVersion()
	{
		_logger.Info($"Precise version not provided, getting version from channel {_options.Channel}");
		_options = _options with { Version = await GetVersion(_options.Channel) };
		_logger.Info($"Version set to {_options.Version}");
	}

	public async Task<int> Handle()
	{
		_logger.Info("Install Directory: " + _options.Path);

		if (_options.Version.Kind is not Version.VersionKind.Exact)
			await SetVersion();

		if (!_options.Force && VersionIsAlreadyInstalled())
		{
			Console.WriteLine($"Version {_options.Version} is already installed. Use --force to reinstall.");
			return 0;
		}

		string archiveName = ConstructArchiveName(_options.Version);
		string archivePath = Path.Combine(Path.GetTempPath(), archiveName);
		_logger.Info("Archive path: " + archivePath);

		string downloadPath = _options.Version.DownloadPath;
		string link = Feeds[0] + downloadPath;
		_logger.Info("Download link: " + link);

		var result = JsonSerializer.Serialize(ManifestHelpers.Instance);
		_logger.Info("Existing manifest: " + result);

		using (var tempArchiveFile = File.Create(archivePath, 64 * 1024 /* 64kB */, FileOptions.WriteThrough | FileOptions.DeleteOnClose))
		using (var archiveHttpStream = await Program.DefaultClient.GetStreamAsync(link))
		{
			await archiveHttpStream.CopyToAsync(tempArchiveFile);
			await tempArchiveFile.FlushAsync();
			_logger.Info($"Installing to {_options.Path}");
			if (await ExtractArchiveToDir(archivePath, _options.Path) != 0)
			{
				return 1;
			}
		}


		var newWorkload = new Workload(_options.Version.ToString(), _options.Path);
		if (!ManifestHelpers.Instance.Workloads.Contains(newWorkload))
		{
			_logger.Info($"Adding workload {newWorkload} to manifest.");
			ManifestHelpers.Instance = ManifestHelpers.Instance with { Workloads = ManifestHelpers.Instance.Workloads.Add(newWorkload) };
			ManifestHelpers.Instance = ManifestHelpers.Instance with { Active = newWorkload };
			ManifestHelpers.Instance.WriteOut();
		}

		// Add to path / update default;
		//throw new NotImplementedException();

		return 0;
	}

	private async Task<Version> GetVersion(Channel channel)
	{
		foreach (var feed in Feeds)
		{
			if (await GetLatestVersion(feed, channel) is Version v)
				return v;
		}
		throw new NotImplementedException("IDK what to do here really");
	}

	static async Task<int> ExtractArchiveToDir(string archivePath, string dirPath)
	{
		Directory.CreateDirectory(dirPath);
		if (!(Utilities.CurrentRID.OS == OSPlatform.Windows))
		{
			var psi = new ProcessStartInfo()
			{
				FileName = "tar",
				ArgumentList = { "-xzf", $"{archivePath}", "-C", $"{dirPath}" },
			};

			var p = Process.Start(psi);
			if (p is not null)
			{
				await p.WaitForExitAsync();
				return p.ExitCode;
			}
			return 1;
		}
		ZipFile.ExtractToDirectory(archivePath, dirPath);
		return 0;
	}

	static string ConstructArchiveName(Version? specificVersion = null)
	{
		return specificVersion is null
			? $"dotnet-sdk-{Utilities.CurrentRID}.{Utilities.ZipSuffix}"
			: $"dotnet-sdk-{specificVersion}-{Utilities.CurrentRID}.{Utilities.ZipSuffix}";
	}

	private static readonly HttpClient s_noRedirectClient = new HttpClient(new HttpClientHandler() { AllowAutoRedirect = false });

	private async Task<Version?> GetLatestVersion(
		string feed,
		Channel channel)
	{
		string latestVersion;
		// The dotnet service provides an endpoint for fetching the latest LTS and Current versions,
		// but not preview. We'll have to construct that ourselves from aka.ms.
		if (channel.Kind != Channel.ChannelKind.Preview)
		{
			string versionFileUrl = $"{feed}/Sdk/{channel}/latest.version";
			_logger.Info("Fetching latest version from URL " + versionFileUrl);
			latestVersion = await Program.DefaultClient.GetStringAsync(versionFileUrl);
			return Version.From(latestVersion);
		}
		else
		{
			const string PreviewMajorVersion = "8.0";
			var versionlessArchiveName = ConstructArchiveName();
			string akaMsUrl = $"https://aka.ms/dotnet/{PreviewMajorVersion}/preview/{versionlessArchiveName}";
			_logger.Info("aka.ms URL: " + akaMsUrl);
			var requestMessage = new HttpRequestMessage(
				HttpMethod.Head,
				akaMsUrl);
			var response = await s_noRedirectClient.SendAsync(requestMessage);

			if (response.StatusCode != HttpStatusCode.MovedPermanently)
			{
				return null;
			}

			if (response.Headers.Location?.Segments is not { Length: 5 } segments)
			{
				return null;
			}

			latestVersion = segments[3].TrimEnd('/');
		}
		_logger.Info(latestVersion);
		return Version.From(latestVersion);
	}

	private const string s_envShContent = """
#!/bin/sh
# Prepend dotnet dir to the path, unless it's already there.
# Steal rustup trick of matching with ':' on both sides
case ":${PATH}:" in
    *:{install_loc}:*)
        ;;
    *)
        export PATH="{install_loc}:$PATH"
        ;;
esac
""";


	private async Task<int> AddToPath(string path, bool global = false)
	{
		throw new NotImplementedException();
		if (Utilities.CurrentRID.OS == OSPlatform.Windows)
		{
			Console.WriteLine("Adding install directory to user path: " + _options.Path);
			//WindowsAddToPath(_options.Path);
		}
		else if (Utilities.CurrentRID.OS == OSPlatform.OSX)
		{
			int result = await MacAddToPath(_options.Path);
			if (result != 0)
			{
				_logger.Error("Failed to add to path");
			}
		}
		else
		{
			int result = await LinuxAddToPath(_options.Path, global);
			if (result != 0)
			{
				_logger.Error("Failed to add to path");
			}
		}
		return 0;
	}

	private async Task<int> UnixAddToPathInShellFiles(string pathToAdd)
	{
		_logger.Info("Setting environment variables in shell files");
		string resolvedEnvPath = Path.Combine(pathToAdd, "env");
		// Using the full path to the install directory is usually fine, but on Unix systems
		// people often copy their dotfiles from one machine to another and fully resolved paths present a problem
		// there. Instead, we'll try to replace instances of the user's home directory with the $HOME
		// variable, which should be the most common case of machine-dependence.
		var portableEnvPath = resolvedEnvPath.Replace(Environment.GetFolderPath(SpecialFolder.UserProfile), "$HOME");
		string userShSuffix = $"""

if [ -f "{portableEnvPath}" ]; then
    . "{portableEnvPath}"
fi
""";
		FileStream? envFile;
		try
		{
			envFile = File.Open(resolvedEnvPath, FileMode.CreateNew);
		}
		catch
		{
			_logger.Info("env file already exists, skipping installation");
			envFile = null;
		}

		if (envFile is not null)
		{
			_logger.Info("Writing env sh file");
			using (envFile)
			using (var writer = new StreamWriter(envFile))
			{
				await writer.WriteAsync(s_envShContent.Replace("{install_loc}", pathToAdd));
				await envFile.FlushAsync();
			}

			// Scan shell files for shell suffix and add it if it doesn't exist
			_logger.Log("Scanning for shell files to update");
			foreach (var shellFileName in ProfileShellFiles)
			{
				var shellPath = Path.Combine(Environment.GetFolderPath(SpecialFolder.UserProfile), shellFileName);
				_logger.Info("Checking for file: " + shellPath);
				if (File.Exists(shellPath))
				{
					_logger.Log("Found " + shellPath);
				}
				else
				{
					continue;
				}
				try
				{
					if (!(await FileContainsLine(shellPath, $". \"{portableEnvPath}\"")))
					{
						_logger.Log("Adding env import to: " + shellPath);
						await File.AppendAllTextAsync(shellPath, userShSuffix);
					}
				}
				catch (Exception e)
				{
					// Ignore if the file can't be accessed
					_logger.Info($"Couldn't write to file {shellPath}: {e.Message}");
				}
			}
		}
		return 0;
	}

	private static ImmutableArray<string> ProfileShellFiles => ImmutableArray.Create<string>(
		".profile",
		".bashrc",
		".zshrc"
	);

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
