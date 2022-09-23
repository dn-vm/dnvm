using Serde.Json;
using System;
using System.Collections.Generic;
using System.CommandLine;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace Dnvm;

sealed partial class Install
{
	internal static Command GetCommand(ILogger? logger = null, Manifest? manifest = null, string? downloadDir = null, IClient? client = null)
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

		if (true)
			install.SetHandler(Handler, channel, version, path, force, global, verbose, daily);

		return install;
		async Task<int> Handler(Channel? channel, Version? version, string? path, bool force, bool global, bool verbose, bool daily)
		{
			var install = new Install(
				logger ?? Logger.Default, manifest ?? ManifestHelpers.Instance, downloadDir ?? DefaultDownloadDirectory, client ?? new DefaultClient(),
				new Options(channel, version, path ?? Utilities.LocalInstallLocation, force, global, verbose, daily));
#if DEBUG
			var @catch = false;
#else
			var @catch = true;
#endif
			return await install.Handle(@catch);
		}
	}

	public static Command Command => GetCommand();

	public static string DefaultDownloadDirectory = Path.GetTempPath();

	public sealed record Options(Channel? Channel, Version? Version, string Path, bool Force, bool Global, bool Verbose, bool Daily);

	private readonly ILogger _logger;
	private Manifest _manifest;
	private Options _options;
	private readonly string _downloadDir;
	private readonly IClient _client;

	public Install(ILogger logger, Manifest manifest, string downloadDir, IClient client, Options options)
	{
		_logger = logger;
		_manifest = manifest;
		_options = options;
		_downloadDir = downloadDir;
		_client = client;
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
		Version? exactVersion = _options.Version;
		if (exactVersion is not Version)
		{
			_logger.Info($"Version not provided, getting version from channel {_options.Channel}");
			if (await GetVersion(_options.Channel!, _options.Daily) is not Version newVersion)
				throw new DnvmException("Invalid channel - cannot determine version number");
			exactVersion = newVersion;
		}
		_options = _options with
		{
			Version = exactVersion,
			Path = Path.Combine(_options.Path, Utilities.EscapeFilename(exactVersion.ToString()))
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
		string archivePath = Path.Combine(_downloadDir, archiveName);
		_logger.Info("Archive path: " + archivePath);

		string[] downloadPaths = _options.Version!.UrlPaths;


		if (await GetCorrectDownloadLink(Feeds, downloadPaths) is not Uri link)
			throw new DnvmException($"Couldn't find download link for Version {_options.Version} and RID {Utilities.CurrentRID}");
		_logger.Info("Download link: " + link);

		await _client.DownloadArchiveAndExtractAsync(link, _options.Path);

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

	async Task<Uri?> GetCorrectDownloadLink(string[] feeds, string[] downloadPaths)
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
				responseTasks.Add(_client.GetHeadersAsync(link));
			}
		}
		var responses = await Task.WhenAll(responseTasks);
		for (int i = 0; i < responses.Length; i++)
		{
			if (responses[i].StatusCode == HttpStatusCode.OK)
				return responses[i].RequestMessage!.RequestUri;
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
			System.IO.Compression.ZipFile.ExtractToDirectory(archivePath, dirPath, overwriteFiles: true);
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
				string latestVersion = await _client.GetStringAsync(new Uri(versionFileUrl));
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

		if (await _client.GetHeadersAsync(akaMsUrl, true) is not HttpResponseMessage response)
			return null;

		if (response.StatusCode != HttpStatusCode.OK)
			return null;

		Uri redirectedUrl = response.RequestMessage!.RequestUri!;

		if (redirectedUrl.Segments is not { Length: 5 } segments)
			return null;

		_logger.Info($"aka.ms redirects to {redirectedUrl}");

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
}
