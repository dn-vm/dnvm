
using System;
using System.Threading.Tasks;

namespace Dnvm;

public static class ListCommand
{
    /// <summary>
    /// Prints a list of installed SDK versions and their locations.
    public static Task<int> Run(Logger logger, DnvmHome home)
    {
        Manifest manifest;
        try
        {
            manifest = home.ReadManifest();
        }
        catch (Exception e)
        {
            logger.Error("Error reading manifest: " + e.Message);
            return Task.FromResult(1);
        }

        PrintSdks(logger, manifest);

        return Task.FromResult(0);
    }

    public static void PrintSdks(Logger logger, Manifest manifest)
    {
        logger.Log("Installed SDKs:");
        logger.Log("");
        var header = "  | Channel\tVersion\tLocation";
        logger.Log(header);
        logger.Log(new string('-', header.Length));
        foreach (var channel in manifest.TrackedChannels)
        {
            char selected = manifest.CurrentSdkDir == channel.SdkDirName ? '*' : ' ';
            foreach (var version in channel.InstalledSdkVersions)
            {
                logger.Log($"{selected} | {channel.ChannelName}\t{version}\t{channel.SdkDirName.Name}");
            }
        }
    }
}