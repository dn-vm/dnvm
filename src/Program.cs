﻿using System.CommandLine;
using System.CommandLine.Builder;
using System.CommandLine.Parsing;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

[assembly: InternalsVisibleTo("dnvm.Tests")]
namespace Dnvm;

public static class Program
{
    internal static readonly HttpClient DefaultClient = new HttpClient();
    internal static Logger Logger { get; private set; } = new Logger();

    static Task<int> Run(string[] args, Logger? logger = null)
    {
        if (logger is not null)
            Logger = logger;

        var root = new RootCommand("dnvm");
        root.Add(Install.Command);
        root.Add(Init.Command);
        root.Add(Update.Command);
        root.Add(Activate.Command);

        new CommandLineBuilder(root).Build().Invoke(args);
        return Task.FromResult(0);
    }

    static async Task<int> Main(string[] args)
    {
        return await Run(args);
    }
}
