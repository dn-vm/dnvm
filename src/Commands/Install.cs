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

internal sealed partial class Install : Command
{
	private Program _dnvm;
	private Options? _options;

	public new sealed record Options(Channel? Channel, Version? Version, string Path, bool Force, bool Global, bool Verbose, bool Daily);

	public Install(Program dnvm) : base("install")
	{
		_dnvm = dnvm;

		this.Add(Self.Command);

		Option<Channel> channel = new("--channel", Channel.Parse);
		channel.Description = $"""
								The channel to this from.  
								Available options: 'lts', 'current', 'preview', A.B semver (e.g. 5.0), A.B.Cxx semver (e.g. 6.0.4xx)
								""";
		channel.SetDefaultValue(new Channel(Channel.ChannelKind.LTS));
		channel.AddAlias("-c");
		this.Add(channel);

		Option<bool> verbose = new("--verbose");
		verbose.AddAlias("-v");
		this.AddOption(verbose);


		Option<bool> global = new("--global");
		global.AddAlias("-g");
		this.AddOption(global);

		Option<bool> force = new("--force");
		force.AddAlias("-f");
		this.AddOption(force);

		Option<bool> installer = new("--installer");
		installer.AddAlias("-i");
		//this.AddOption(thiser);

		Option<Version> version = new("--version", Version.Parse);
		this.Add(version);

		Option<string?> path = new("--path");
		path.AddAlias("-p");
		this.Add(path);

		Option<bool> setActive = new("--set-default");
		setActive.AddAlias("-d");
		//this.Add(setActive);

		Option<bool> daily = new(new[] { "--daily", "-d" }, "Use the latest daily build in a channel");
		this.Add(daily);

		this.AddValidator(Utilities.ValidateOneOf(channel, version));
		this.AddValidator(Utilities.ValidateXOnlyIfY(daily, channel));

		this.SetHandler(Handle, channel, version, path, force, global, verbose, daily);

		Task<int> Handle(Channel? channel, Version? version, string? path, bool force, bool global, bool verbose, bool daily)
		{
			_options = new Options(channel, version, path ?? Utilities.LocalInstallLocation, force, global, verbose, daily);
			return this.Handle();
		}
	}

	public static string[] Feeds { get; } = new[] {
			"https://dotnetcli.azureedge.net/dotnet",
			"https://dotnetbuilds.azureedge.net/public"
		};

	bool VersionIsAlreadyInstalled()
	{
		return _dnvm.Manifest.Workloads.Contains(new Workload(_options.Version!.ToString()));
	}

	public async Task EnsureExactVersion()
	{
		Version? exactVersion = _options.Version;
		if (exactVersion is not Version)
		{
			_dnvm.Logger.Info($"Version not provided, getting version from channel {_options.Channel}");
			if (await GetVersion(_options.Channel!, _options.Daily) is not Version newVersion)
				throw new DnvmException("Invalid channel - cannot determine version number");
			exactVersion = newVersion;
		}
		_options = _options with
		{
			Version = exactVersion,
			Path = Path.Combine(_options.Path, Utilities.EscapeFilename(exactVersion.ToString()))
		};
		_dnvm.Logger.Log($"Latest version in channel {_options.Channel} {(_options.Daily ? "(daily)" : "")} is {_options.Version}");
	}

	public async Task<int> Handle()
	{
		if (_options.Verbose)
		{
			_dnvm.Logger.LogLevel = LogLevel.Info;
		}
		_dnvm.Logger.Info("Install Directory: " + _options.Path);

		await EnsureExactVersion();


		_dnvm.Logger.Info("Existing manifest: " + JsonSerializer.Serialize(_dnvm.Manifest));
		if (!_options.Force && VersionIsAlreadyInstalled())
		{
			throw new DnvmException($"Version {_options.Version} is already installed. Use --force to reinstall.");
		}

		_dnvm.Logger.Log($"Installing version {_options.Version}.");

		string[] downloadPaths = _options.Version!.UrlPaths;
		if (await GetCorrectDownloadLink(Feeds, downloadPaths) is not Uri link)
			throw new DnvmException($"Couldn't find download link for Version {_options.Version} and RID {Utilities.CurrentRID}");
		_dnvm.Logger.Info("Download link: " + link);

		await _dnvm.Client.DownloadArchiveAndExtractAsync(link, _options.Path);

		var newWorkload = new Workload(_options.Version.ToString(), _options.Path);
		if (!_dnvm.Manifest.Workloads.Contains(newWorkload))
		{
			_dnvm.Logger.Info($"Adding workload {newWorkload} to manifest.");
			_dnvm.Manifest = _dnvm.Manifest with { Workloads = _dnvm.Manifest.Workloads.Add(newWorkload) };
			_dnvm.Manifest = _dnvm.Manifest with { Active = newWorkload };
			_dnvm.Manifest.WriteOut();
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
				responseTasks.Add(_dnvm.Client.GetHeadersAsync(link));
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
			_dnvm.Logger.Info("Fetching latest version from URL " + versionFileUrl);
			try
			{
				string latestVersion = await _dnvm.Client.GetStringAsync(new Uri(versionFileUrl));
				return Version.From(latestVersion);
			}
			catch (HttpRequestException)
			{
				_dnvm.Logger.Info($"Couldn't find version for channel {channel} in at {versionFileUrl}");
			}
		}
		return null;
	}

	async Task<Version?> GetVersionFromAkaUrl(Uri akaMsUrl)
	{
		_dnvm.Logger.Info("aka.ms URL: " + akaMsUrl);

		if (await _dnvm.Client.GetHeadersAsync(akaMsUrl, true) is not HttpResponseMessage response)
			return null;

		if (response.StatusCode != HttpStatusCode.OK)
			return null;

		Uri redirectedUrl = response.RequestMessage!.RequestUri!;

		if (redirectedUrl.Segments is not { Length: 5 } segments)
			return null;

		_dnvm.Logger.Info($"aka.ms redirects to {redirectedUrl}");

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
