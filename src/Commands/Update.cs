using Serde;
using Serde.Json;
using System;
using System.Collections.Generic;
using System.CommandLine;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace Dnvm;

sealed partial class Update : Command
{
    Program _dnvm;
    Options? _options;
    public new sealed record Options(bool Verbose);

    public async Task<int> Handle(bool verbose)
    {
        _options = new Options(verbose);
        var exit = await this.Handle();
        return exit;
    }

    public Update(Program dnvm) : base("update", "Update the dnvm executable with the latest released version")
    {
        _dnvm = dnvm;
        System.CommandLine.Option<bool> verbose = new(new[] { "--verbose", "-v" });
        this.Add(verbose);

        this.SetHandler(Handle, verbose);
    }

    public async Task<int> Handle()
    {
        if (!Utilities.IsAOT)
        {
            Console.WriteLine("Cannot self-update: the current executable is not deployed as a single file.");
            return 1;
        }

        string artifactDownloadLink = await GetReleaseLink();

        string tempArchiveDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Action<string> handleDownload = tempDownloadPath =>
        {
            _dnvm.Logger.Info("Extraction directory: " + tempArchiveDir);
            ZipFile.ExtractToDirectory(tempDownloadPath, tempArchiveDir);
        };

        await DownloadBinaryToTempAndDelete(new Uri(artifactDownloadLink), tempArchiveDir);
        _dnvm.Logger.Info($"Downloaded binary to {tempArchiveDir}");

        string dnvmTmpPath = Path.Combine(tempArchiveDir, Utilities.DnvmExeName);
        bool success =
            await ValidateBinary(dnvmTmpPath) &&
            SwapWithRunningFile(dnvmTmpPath);
        return success ? 0 : 1;
    }


    [GenerateDeserialize]
    [SerdeTypeOptions(MemberFormat = MemberFormat.CamelCase)]
    partial struct Releases
    {
        public Release LatestVersion { get; init; }
    }

    [GenerateDeserialize]
    [SerdeTypeOptions(MemberFormat = MemberFormat.CamelCase)]
    partial struct Release
    {
        public string Version { get; init; }

        public Dictionary<string, string> Artifacts { get; init; }
    }

    private async Task<string> GetReleaseLink()
    {
        string releasesJson = await new DefaultClient().GetStringAsync(new Uri("https://commentout.com/dnvm/releases.json"));
        _dnvm.Logger.Info("Releases JSON: " + releasesJson);
        var releases = JsonSerializer.Deserialize<Releases>(releasesJson);
        var rid = Utilities.CurrentRID.ToString();
        var artifactDownloadLink = releases.LatestVersion.Artifacts[rid];
        _dnvm.Logger.Info("Artifact download link: " + artifactDownloadLink);
        return artifactDownloadLink;
    }

    private async Task DownloadBinaryToTempAndDelete(Uri uri, string action)
    {
        await _dnvm.Client.DownloadArchiveAndExtractAsync(uri, action);
    }

    public async Task<bool> ValidateBinary(string fileName)
    {
        // Replace with File.SetUnixFileMode when available
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var chmod = Process.Start("chmod", $"+x \"{fileName}\"");
            await chmod.WaitForExitAsync();
            _dnvm.Logger.Info("chmod return: " + chmod.ExitCode);
        }

        // Run exe and make sure it's OK
        var testProc = Process.Start(new ProcessStartInfo
        {
            FileName = fileName,
            ArgumentList = { "--help" },
            RedirectStandardOutput = true,
            RedirectStandardError = true
        });
        if (testProc is Process ps)
        {
            await testProc.WaitForExitAsync();
            var output = await ps.StandardOutput.ReadToEndAsync();
            if (ps.ExitCode != 0 || !output.Contains("usage: "))
            {
                _dnvm.Logger.Error("Could not run downloaded dnvm");
                return false;
            }
        }
        return true;
    }

    public bool SwapWithRunningFile(string newFileName)
    {
        try
        {
            string backupPath = Utilities.ProcessPath + ".bak";
            _dnvm.Logger.Info($"Swapping {Utilities.ProcessPath} with downloaded version at {newFileName}");
            File.Move(Utilities.ProcessPath, backupPath);
            File.Move(newFileName, Utilities.ProcessPath, overwrite: false);
            _dnvm.Logger.Log("Process successfully upgraded");
            File.Delete(backupPath);
            return true;
        }
        catch (Exception e)
        {
            _dnvm.Logger.Error("Couldn't replace existing binary: " + e.Message);
            return false;
        }
    }
}