
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

    public static async Task<int> Main(string[] args)
    {
        var console = AnsiConsole.Console;
        console.WriteLine("dnvm " + SemVer + " " + GitVersionInformation.Sha);
        console.WriteLine();
        var logger = new Logger(console);
        var options = CommandLineArguments.Parse(args);
        // Self-install is special, since we don't know the DNVM_HOME yet.
        if (options.Command is CommandArguments.SelfInstallArguments selfInstallArgs)
        {
            return (int)await SelfInstallCommand.Run(logger, selfInstallArgs);
        }

        using var env = DnvmEnv.CreateDefault();
        return await Dnvm(env, logger, options);
    }

    internal static async Task<int> Dnvm(DnvmEnv env, Logger logger, CommandLineArguments options)
    {
        return options.Command switch
        {
            CommandArguments.InstallArguments o => (int)await InstallCommand.Run(env, logger, o),
            CommandArguments.UpdateArguments o => (int)await UpdateCommand.Run(env, logger, o),
            CommandArguments.ListArguments => (int)await ListCommand.Run(logger, env),
            CommandArguments.SelectArguments o => (int)await SelectCommand.Run(env, logger, o),
            CommandArguments.UntrackArguments o => await UntrackCommand.Run(env, logger, o),
            _ => throw ExceptionUtilities.Unreachable
        };
    }
}