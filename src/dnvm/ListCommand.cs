
using System;
using System.Threading.Tasks;
using Spectre.Console;

namespace Dnvm;

public static class ListCommand
{
    /// <summary>
    /// Prints a list of installed SDK versions and their locations.
    public static async Task<int> Run(Logger logger, DnvmEnv home)
    {
        Manifest manifest;
        try
        {
            manifest = await home.ReadManifest();
        }
        catch (Exception e)
        {
            logger.Error("Error reading manifest: " + e.Message);
            return 1;
        }

        PrintSdks(logger, manifest);

        return 0;
    }

    public static void PrintSdks(Logger logger, Manifest manifest)
    {
        logger.Log("Installed SDKs:");
        logger.Log();
        var table = new Table();
        table.AddColumn(new TableColumn(" "));
        table.AddColumn("Version");
        table.AddColumn("Channel");
        table.AddColumn("Location");
        foreach (var sdk in manifest.InstalledSdkVersions)
        {
            string selected = manifest.CurrentSdkDir == sdk.SdkDirName ? "*" : " ";
            var channel = sdk.Channel?.GetLowerName() ?? "";
            table.AddRow(selected, sdk.SdkVersion.ToString(), channel, sdk.SdkDirName.Name);
        }
        logger.Console.Write(table);
    }
}