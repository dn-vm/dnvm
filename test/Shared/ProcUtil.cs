
using System.Diagnostics;

namespace Dnvm.Test;

public static class ProcUtil
{
    public static async Task<ProcResult> RunWithOutput(string cmd, string args)
    {
        var psi = new ProcessStartInfo {
            FileName = cmd,
            Arguments = args,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        var proc = Process.Start(psi);
        if (proc is null)
        {
            throw new IOException($"Could not start process: " + cmd);
        }

        var outTask = proc.StandardOutput.ReadToEndAsync();
        var errorTask = proc.StandardError.ReadToEndAsync();
        await Task.WhenAll(
            outTask,
            errorTask,
            proc.WaitForExitAsync());
        return new ProcResult(proc.ExitCode, outTask.Result, errorTask.Result);
    }

    public readonly record struct ProcResult(int ExitCode, string Out, string Error);
}