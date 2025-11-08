
using Zio;

namespace Dnvm.Test;

internal static class DnvmRunner
{
    public static async Task<ProcUtil.ProcResult> RunAndRestoreEnv(
        DnvmEnv env,
        string dnvmPath,
        string dnvmArgs,
        Action? envChecker = null,
        string? testConfigDir = null)
    {
        var savedVars = new Dictionary<string, string?>();
        const string PATH = "PATH";
        const string DOTNET_ROOT = "DOTNET_ROOT";
        const string DNVM_HOME = "DNVM_HOME";
        if (OperatingSystem.IsWindows())
        {
            SaveVars(savedVars);
        }
        try
        {
            var envVars = new Dictionary<string, string>
            {
                ["HOME"] = env.UserHome,
                ["DNVM_HOME"] = env.RealPath(UPath.Root)
            };
            if (testConfigDir is not null)
            {
                envVars["DNVM_TEST_CONFIG_DIR"] = testConfigDir;
            }
            var procResult = await ProcUtil.RunWithOutput(
                dnvmPath,
                dnvmArgs,
                envVars
            );
            // Allow the test to check the environment variables before they are restored
            envChecker?.Invoke();
            return procResult;
        }
        finally
        {
            RestoreVars(savedVars);
        }

        static void SaveVars(Dictionary<string, string?> savedVars)
        {
            savedVars[PATH] = Environment.GetEnvironmentVariable(PATH, EnvironmentVariableTarget.User);
            savedVars[DOTNET_ROOT] = Environment.GetEnvironmentVariable(DOTNET_ROOT, EnvironmentVariableTarget.User);
            savedVars[DNVM_HOME] = Environment.GetEnvironmentVariable(DNVM_HOME, EnvironmentVariableTarget.User);
        }

        static void RestoreVars(Dictionary<string, string?> savedVars)
        {
            foreach (var kvp in savedVars)
            {
                Environment.SetEnvironmentVariable(kvp.Key, kvp.Value, EnvironmentVariableTarget.User);
            }
        }
    }
}