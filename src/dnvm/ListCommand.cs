
using System;
using System.Linq;
using System.Threading.Tasks;
using Spectre.Console;
using Zio;

namespace Dnvm;

public static class ListCommand
{
    /// <summary>
    /// Prints a list of installed SDK versions and their locations.
    public static async Task<int> Run(Logger logger, DnvmEnv env)
    {
        Manifest manifest;
        try
        {
            manifest = await Manifest.ReadManifestUnsafe(env);
        }
        catch (Exception e)
        {
            Environment.FailFast("Error reading manifest: ", e);
            // unreachable
            return 1;
        }

        PrintSdks(env.Console, manifest, env.RealPath(UPath.Root));

        return 0;
    }

    public static void PrintSdks(IAnsiConsole console, Manifest manifest, string homePath)
    {
        console.WriteLine($"DNVM_HOME: {homePath}");
        console.WriteLine();
        console.WriteLine("Installed SDKs:");
        console.WriteLine();
        var table = new Table();
        table.AddColumn(new TableColumn(" "));
        table.AddColumn("Version");
        table.AddColumn("Channel");
        table.AddColumn("Location");
        foreach (var sdk in manifest.InstalledSdks)
        {
            string selected = manifest.CurrentSdkDir == sdk.SdkDirName ? "*" : " ";
            var channels = manifest.RegisteredChannels
                .Where(c => c.InstalledSdkVersions.Contains(sdk.SdkVersion))
                .Select(c => c.ChannelName.GetLowerName());
            table.AddRow(selected, sdk.SdkVersion.ToString(), string.Join(", ", channels), sdk.SdkDirName.Name);
        }
        console.Write(table);

        console.WriteLine();
        console.WriteLine("Tracked channels:");
        console.WriteLine();
        foreach (var c in manifest.TrackedChannels())
        {
            console.WriteLine($" • {c.ChannelName.GetLowerName()}");
        }
    }
}