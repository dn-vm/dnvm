
using Serde.Json;
using System;
using System.Collections.Generic;
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
	public static Command Command
	{
		get
		{
			Command install = new Command("install", "Install a dotnet sdk");

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
			//install.AddOption(installer);

			Option<Version> version = new("--version", Version.Parse);
			install.Add(version);

			Option<string?> path = new("--path");
			path.AddAlias("-p");
			install.Add(path);

			Option<bool> setActive = new("--set-default");
			setActive.AddAlias("-d");
			//install.Add(setActive);

			Option<bool> daily = new(new[] { "--daily", "-d" }, "Use the latest daily build in a channel");
			install.Add(daily);

			install.AddValidator(Utilities.ValidateOneOf(channel, version));
			install.AddValidator(Utilities.ValidateXOnlyIfY(daily, channel));

			install.SetHandler(Handler, channel, version, path, force, global, verbose, daily);

			return install;
		}
	}

	public sealed record Options(Channel? Channel, Version? Version, string Path, bool Force, bool Global, bool Verbose, bool Daily);

	private static async Task<int> Handler(Channel? channel, Version? version, string? path, bool force, bool global, bool verbose, bool daily)
	{
		var install = new Install(Logger.Default, ManifestHelpers.Instance, new Options(channel, version, path ?? Utilities.LocalInstallLocation, force, global, verbose, daily));
#if DEBUG
		var @catch = false;
#else
		var @catch = true;
#endif

		return await install.Handle(@catch);
	}

	private readonly ILogger _logger;
	private Manifest _manifest;
	private Options _options;

	public Install(ILogger logger, Manifest manifest, Options options)
	{
		_logger = logger;
		_manifest = manifest;
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
		return _manifest.Workloads.Contains(new Workload(_options.Version!.ToString()));
	}

	public async Task EnsureExactVersion()
	{
		if (_options.Version is not null)
			return;
		_logger.Info($"Version not provided, getting version from channel {_options.Channel}");
		if (await GetVersion(_options.Channel!, _options.Daily) is not Version newVersion)
		{
			throw new DnvmException("Invalid channel - cannot determine version number");
		}
		_options = _options with
		{
			Version = newVersion,
			Path = Path.Combine(_options.Path, Utilities.EscapeFilename(newVersion.ToString()))
		};
		_logger.Log($"Latest version in channel {_options.Channel} {(_options.Daily ? "(daily)" : "")} is {_options.Version}");
	}

	public async Task<int> Handle(bool @catch = false)
	{
		try
		{
			return await Handle();
		}
		catch (DnvmException e) when (@catch)
		{
			_logger.Error(e.Message);
			return 1;
		}
	}

	public async Task<int> Handle()
	{
		_logger.Info("Install Directory: " + _options.Path);

		await EnsureExactVersion();


		_logger.Info("Existing manifest: " + JsonSerializer.Serialize(_manifest));
		if (!_options.Force && VersionIsAlreadyInstalled())
		{
			throw new DnvmException($"Version {_options.Version} is already installed. Use --force to reinstall.");
		}

		_logger.Log($"Installing version {_options.Version}.");

		string archiveName = ConstructArchiveName(_options.Version);
		string archivePath = Path.Combine(Path.GetTempPath(), archiveName);
		_logger.Info("Archive path: " + archivePath);

		string[] downloadPaths = _options.Version!.DownloadPaths;


		if (await GetCorrectDownloadLink(Feeds, downloadPaths) is not Uri link)
			throw new DnvmException($"Couldn't find download link for Version {_options.Version} and RID {Utilities.CurrentRID}");
		_logger.Info("Download link: " + link);
		using (var archiveHttpStream = await Program.DefaultClient.GetStreamAsync(link))
		using (var tempArchiveFile = File.Create(archivePath, 64 * 1024 /* 64kB */, FileOptions.WriteThrough))
		{
			await archiveHttpStream.CopyToAsync(tempArchiveFile);
			await tempArchiveFile.FlushAsync();
			_logger.Info($"Installing to {_options.Path}");
			tempArchiveFile.Close();
		}

		_logger.Info($"Extracting downloaded archive at {archivePath} to directory at {_options.Path}");
		if (await ExtractArchiveToDir(archivePath, _options.Path) != 0)
		{
			File.Delete(archivePath);
			return 1;
		}
		File.Delete(archivePath);


		var newWorkload = new Workload(_options.Version.ToString(), _options.Path);
		if (!_manifest.Workloads.Contains(newWorkload))
		{
			_logger.Info($"Adding workload {newWorkload} to manifest.");
			_manifest = _manifest with { Workloads = _manifest.Workloads.Add(newWorkload) };
			_manifest = _manifest with { Active = newWorkload };
			_manifest.WriteOut();
		}

		return 0;
	}

	static async Task<Uri?> GetCorrectDownloadLink(string[] feeds, string[] downloadPaths)
	{
		int capacity = feeds.Length * downloadPaths.Length;
		List<Task<HttpResponseMessage>> responseTasks = new(capacity);
		List<Uri> links = new(capacity);
		for (int i = 0; i < feeds.Length; i++)
		{
			for (int j = 0; j < downloadPaths.Length; j++)
			{
				Uri link = new Uri(feeds[i] + downloadPaths[j]);
				links.Add(link);
				var requestMessage = new HttpRequestMessage(
					HttpMethod.Head,
					link);
				responseTasks.Add(s_noRedirectClient.SendAsync(requestMessage, HttpCompletionOption.ResponseHeadersRead));
			}
		}
		var responses = await Task.WhenAll(responseTasks);
		for (int i = 0; i < responses.Length; i++)
		{
			if (responses[i].StatusCode == HttpStatusCode.OK)
				return links[i];
		}
		return null;
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
		else
		{
			ZipFile.ExtractToDirectory(archivePath, dirPath, overwriteFiles: true);
		}
		return 0;
	}

	static string ConstructArchiveName(Version? specificVersion = null)
	{
		return specificVersion is null
			? $"dotnet-sdk-{Utilities.CurrentRID}.{Utilities.ZipSuffix}"
			: $"dotnet-sdk-{specificVersion}-{Utilities.CurrentRID}.{Utilities.ZipSuffix}";
	}

	private static readonly HttpClient s_noRedirectClient = new HttpClient(new HttpClientHandler() { AllowAutoRedirect = false });

	async Task<Version?> GetVersionFromAkaMs(Channel channel)
	{
		var versionlessArchiveName = ConstructArchiveName();
		Uri? akaMsUrl = new Uri($"https://aka.ms/dotnet/{channel}/{versionlessArchiveName}");
		return await GetVersionFromAkaUrl(akaMsUrl);
	}

	async Task<Version?> GetVersionFromLegacyUrl(Channel channel)
	{
		foreach (var feed in Feeds)
		{
			string versionFileUrl = $"{feed}/Sdk/{channel}/latest.version";
			_logger.Info("Fetching latest version from URL " + versionFileUrl);
			try
			{
				string latestVersion = await Program.DefaultClient.GetStringAsync(versionFileUrl);
				return Version.From(latestVersion);
			}
			catch (HttpRequestException)
			{
				_logger.Info($"Couldn't find version for channel {channel} in at {versionFileUrl}");
			}
		}
		return null;
	}

	async Task<Version?> GetVersionFromAkaUrl(Uri akaMsUrl)
	{
		_logger.Info("aka.ms URL: " + akaMsUrl);
		HttpResponseMessage? response = null;
		for (int i = 0; i < 10; i++)
		{
			var requestMessage = new HttpRequestMessage(
				HttpMethod.Head,
				akaMsUrl);
			response = await s_noRedirectClient.SendAsync(requestMessage, HttpCompletionOption.ResponseHeadersRead);
			if (response.StatusCode != HttpStatusCode.MovedPermanently)
				break;
			akaMsUrl = response.Headers.Location!;
		}

		if (response!.StatusCode != HttpStatusCode.OK)
			return null;

		if (akaMsUrl!.Segments is not { Length: 5 } segments)
			return null;

		_logger.Info($"aka.ms redirects to {akaMsUrl}");

		return Version.From(segments[3].TrimEnd('/'));
	}

	async Task<Version?> GetDailyVersion(Channel channel)
	{
		var versionlessArchiveName = ConstructArchiveName();
		Uri akaMsUrl = new Uri($"https://aka.ms/dotnet/{channel}/daily/{versionlessArchiveName}");
		return await GetVersionFromAkaUrl(akaMsUrl);
	}

	private async Task<Version?> GetVersion(Channel channel, bool daily)
	{
		if (daily)
			return await GetDailyVersion(channel);
		var akaVersion = GetVersionFromAkaMs(channel);
		var legacyVersion = GetVersionFromLegacyUrl(channel);
		return (await Task.WhenAll(akaVersion, legacyVersion)) switch
		{
			[Version aka, _] => aka,
			[null, var legacy] => legacy,
			_ => throw new NotImplementedException("This should never happen"),
		};
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
	sealed class SelfInstall
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

		Task<int> Handle()
		{
			if (!Utilities.IsAOT)
			{
				Console.WriteLine("Cannot self-install into target location: the current executable is not deployed as a single file.");
				return Task.FromResult(1);
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
}
