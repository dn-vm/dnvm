
using System;
using Spectre.Console;

namespace Dnvm;

internal static class IAnsiConsoleExt
{
    public static void Error(this IAnsiConsole console, string message)
    {
        console.MarkupLineInterpolated($"{Environment.NewLine}[default on red]Error[/]: {message}{Environment.NewLine}");
    }

    public static void Warn(this IAnsiConsole console, string message)
    {
        console.MarkupLineInterpolated($"{Environment.NewLine}[default on yellow]Warning[/]: {message}{Environment.NewLine}");
    }
}