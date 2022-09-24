using System;
using System.CommandLine;
using System.CommandLine.Builder;
using System.CommandLine.Parsing;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

[assembly: InternalsVisibleTo("dnvm.Tests")]
namespace Dnvm;

public class Program
{
    internal IClient Client { get; init; } = new DefaultClient();

    internal ILogger Logger { get; init; } = new Logger();

    internal Manifest Manifest { get; set; } = ManifestHelpers.DefaultManifest;

    private string? _envPath = null;
    internal string EnvPath
    {
        get => _envPath is null ? _envPath = Environment.GetEnvironmentVariable("PATH")! : _envPath;
        init => _envPath = value;
    }

    private string? _homeDir = null;
    internal string HomeDir
    {
        get => _homeDir is null ? _homeDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) : _homeDir;
        init => _homeDir = value;
    }

    public Parser Command
    {
        get
        {
            var root = new RootCommand("dnvm");
            root.Add(new Install(this));
            root.Add(new Init(this));
            root.Add(new Update(this));
            root.Add(new Active(this));
            root.Add(new List(this));
            return new CommandLineBuilder(root).UseHelp().UseTypoCorrections(2).UseSuggestDirective().RegisterWithDotnetSuggest().Build();
        }
    }

    static async Task<int> Main(string[] args)
    {
#if Debug
        var @catch = true;
#else
        var @catch = false;
#endif
        try
        {
            return await new Program().Command.InvokeAsync(args);
        }
        catch (DnvmException e) when (@catch)
        {
            Console.Error.WriteLine(e.Message);
            return 1;
        }
    }
}
