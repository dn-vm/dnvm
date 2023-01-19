
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace Dnvm;

public static class ProcUtil
{
    public static async Task<ProcResult> RunWithOutput(string cmd, string args, Dictionary<string, string>? envVars = null)
    {
        var psi = new ProcessStartInfo {
            FileName = cmd,
            Arguments = args,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        if (envVars is not null)
        {
            foreach (var (k, v) in envVars)
            {
                psi.Environment[k] = v;
            }
        }
        var proc = Process.Start(psi);
        if (proc is null)
        {
            throw new IOException($"Could not start process: " + cmd);
        }

        var @out = proc.StandardOutput.ReadToEnd();
        var error = proc.StandardError.ReadToEnd();
        await proc.WaitForExitAsync().ConfigureAwait(false);
        return new ProcResult(proc.ExitCode, @out, error);
    }

    public readonly record struct ProcResult(int ExitCode, string Out, string Error);
}