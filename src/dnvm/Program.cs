
using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Semver;
using Spectre.Console;
using Zio.FileSystems;
using static System.Environment;

namespace Dnvm;

public static class Program
{
    public static readonly SemVersion SemVer = SemVersion.Parse(GitVersionInformation.MajorMinorPatch, SemVersionStyles.Strict);

    internal static readonly HttpClient HttpClient = new();

    static async Task<int> Main(string[] args)
    {
        Console.WriteLine("dnvm " + SemVer + " " + GitVersionInformation.Sha);
        Console.WriteLine();
        var options = CommandLineArguments.Parse(args);
        var logger = new Logger(AnsiConsole.Console);

        // Self-install is special, since we don't know the DNVM_HOME yet.
        if (options.Command is CommandArguments.SelfInstallArguments selfInstallArgs)
        {
            return (int)await SelfInstallCommand.Run(logger, selfInstallArgs);
        }

        using var globalOptions = GetGlobalConfig();
        return options.Command switch
        {
            CommandArguments.InstallArguments o => (int)await InstallCommand.Run(globalOptions, logger, o),
            CommandArguments.UpdateArguments o => (int)await UpdateCommand.Run(globalOptions, logger, o),
            CommandArguments.ListArguments => (int)await ListCommand.Run(logger, globalOptions.DnvmEnv),
            CommandArguments.SelectArguments o => (int)await SelectCommand.Run(globalOptions, logger, o),
            _ => throw ExceptionUtilities.Unreachable
        };
    }

    /// <summary>
    /// Get the path to DNVM_HOME, which is the location of the dnvm manifest
    /// and the installed SDKs. If the environment variable is not set, uses
    /// <see cref="DefaultConfig.DnvmHome" /> as the default.
    /// </summar>
    internal static GlobalOptions GetGlobalConfig()
    {
        return new GlobalOptions(
            userHome: GetFolderPath(SpecialFolder.UserProfile, SpecialFolderOption.DoNotVerify),
            dnvmEnv: DnvmEnv.CreateDefault()
        );
    }
}