
using System;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Serde.Json;
using static System.Environment;

namespace Dnvm;

public static class Program
{
    static Task<int> Main(string[] args)
    {
        var options = CommandLineOptions.Parse(args);
        var logger = new Logger();
        return options.Command switch
        {
            Command.InstallOptions o => new Install(logger).Handle(o),
            _ => throw new InvalidOperationException("Should be unreachable")
        };
    }
}