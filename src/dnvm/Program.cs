using System.Net.Http;
using System.Threading.Tasks;
using Semver;
using Spectre.Console;
using StaticCs;

namespace Dnvm;

public static class Program
{
    public static readonly SemVersion SemVer = SemVersion.Parse(GitVersionInformation.SemVer, SemVersionStyles.Strict);

    public static readonly SemVersion BackupMuxerVersion = new SemVersion(9, 0, 3);

    public static async Task<int> Main(string[] args)
    {
        var console = AnsiConsole.Console;
        console.WriteLine("dnvm " + SemVer + " " + GitVersionInformation.ShortSha);
        console.WriteLine();
        var logger = new Logger(console);

        var options = CommandLineArguments.TryParse(console, args);
        if (options is null)
        {
            // Error was already printed, exit with failure.
            return 1;
        }

        // Self-install is special, since we don't know the DNVM_HOME yet.
        if (options.Command is CommandArguments.SelfInstallArguments selfInstallArgs)
        {
            return (int)await SelfInstallCommand.Run(logger, selfInstallArgs);
        }

        using var env = DnvmEnv.CreateDefault();
        if (options.Command is null)
        {
            if (options.EnableDnvmPreviews)
            {
                return await EnableDnvmPreviews(env);
            }
            else
            {
                // Help was requested, exit with success.
                return 0;
            }
        }

        return await Dnvm(env, logger, options);
    }

    public static async Task<int> EnableDnvmPreviews(DnvmEnv env)
    {
        var manifest = await ManifestUtils.ReadOrCreateManifest(env);
        manifest = manifest with { PreviewsEnabled = true };
        env.WriteManifest(manifest);
        return 0;
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