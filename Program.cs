
using dnvm;
using System;
using System.Net.Http;
using System.Threading.Tasks;

namespace Dnvm;

public static class Program
{
    public static readonly HttpClient DefaultClient = new HttpClient();
    internal static readonly RID Rid = RID.GetRid();

    static Task<int> Main(string[] args)
    {
        var options = CommandLineOptions.Parse(args);
        var logger = new Logger();
        return options.Command switch
        {
            Command.InstallOptions o => new Install(logger, o).Handle(),
            Command.UpdateOptions o => new Update(logger, o).Handle(),
            _ => throw new InvalidOperationException("Should be unreachable")
        };
    }
}