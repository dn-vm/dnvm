
using System;
using System.Net.Http;
using System.Threading.Tasks;

namespace Dnvm;

public static class Program
{
    public static readonly HttpClient DefaultClient = new HttpClient();

    static Task<int> Main(string[] args)
    {
        var options = CommandLineOptions.Parse(args);
        var logger = new Logger();
        return options.Command switch
        {
            Command.InstallOptions o => RunInstall(logger, o),
            Command.UpdateOptions o => new Update(logger, o).Handle(),
            _ => throw new InvalidOperationException("Should be unreachable")
        };

        static async Task<int> RunInstall(Logger logger, Command.InstallOptions o)
        {
            var result = await (new Install(logger, o).Handle());
            return (int)result;
        }
    }
}