
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

        using var env = DnvmEnv.CreateDefault();
        return options.Command switch
        {
            CommandArguments.InstallArguments o => (int)await InstallCommand.Run(env, logger, o),
            CommandArguments.UpdateArguments o => (int)await UpdateCommand.Run(env, logger, o),
            CommandArguments.ListArguments => (int)await ListCommand.Run(logger, env),
            CommandArguments.SelectArguments o => (int)await SelectCommand.Run(env, logger, o),
            _ => throw ExceptionUtilities.Unreachable
        };
    }
}