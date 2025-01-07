using System.Net.Http;
using System.Threading.Tasks;
using Semver;
using Spectre.Console;

namespace Dnvm;

public static class Program
{
    public static readonly SemVersion SemVer = SemVersion.Parse(GitVersionInformation.MajorMinorPatch, SemVersionStyles.Strict);

    public static async Task<int> Main(string[] args)
    {
        var console = AnsiConsole.Console;
        console.WriteLine("dnvm " + SemVer + " " + GitVersionInformation.Sha);
        console.WriteLine();
        var logger = new Logger(console);

        var options = CommandLineArguments.TryParse(console, args);
        if (options is null)
        {
            // Error was already printed, exit with failure.
            return 1;
        }
        if (options.Command is null)
        {
            // Help was requested, exit with success.
            return 0;
        }

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
            CommandArguments.TrackArguments o => (int)await TrackCommand.Run(env, logger, o),
            CommandArguments.InstallArguments o => (int)await InstallCommand.Run(env, logger, o),
            CommandArguments.UpdateArguments o => (int)await UpdateCommand.Run(env, logger, o),
            CommandArguments.ListArguments => (int)await ListCommand.Run(logger, env),
            CommandArguments.SelectArguments o => (int)await SelectCommand.Run(env, logger, o),
            CommandArguments.UntrackArguments o => await UntrackCommand.Run(env, logger, o),
            CommandArguments.UninstallArguments o => await UninstallCommand.Run(env, logger, o),
            CommandArguments.PruneArguments args => await PruneCommand.Run(env, logger, args),
            CommandArguments.RestoreArguments => await RestoreCommand.Run(env, logger) switch {
                Result<SemVersion, RestoreCommand.Error>.Ok => 0,
                Result<SemVersion, RestoreCommand.Error>.Err x => (int)x.Value,
            },

            CommandArguments.SelfInstallArguments => throw ExceptionUtilities.Unreachable,
        };
    }
}