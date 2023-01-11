
using System;
using System.Net.Http;
using System.Threading.Tasks;

namespace Dnvm;

public static class Program
{
    public static readonly HttpClient HttpClient = new();

    static async Task<int> Main(string[] args)
    {
        Console.WriteLine("dnvm " + GitVersionInformation.SemVer + " " + GitVersionInformation.Sha);
        var options = CommandLineArguments.Parse(args);
        var logger = new Logger();
        var dnvmHome = GetDnvmHome();
        return options.Command switch
        {
            CommandArguments.InstallArguments o => (int)await Install.Run(dnvmHome, logger, o),
            CommandArguments.UpdateArguments o => (int)await Update.Run(dnvmHome, logger, o),
            _ => throw new InvalidOperationException("Should be unreachable")
        };
    }

    /// <summary>
    /// Get the path to DNVM_HOME, which is the location of the dnvm manifest
    /// and the installed SDKs. If the environment variable is not set, uses
    /// <see cref="DefaultConfig.DnvmHome" /> as the default.
    /// </summar>
    private static string GetDnvmHome()
    {
        var home = Environment.GetEnvironmentVariable("DNVM_HOME");
        if (!string.IsNullOrWhiteSpace(home))
        {
            return home;
        }
        return DefaultConfig.DnvmHome;
    }
}