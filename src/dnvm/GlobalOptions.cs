using System;
using System.IO;
using static System.Environment;

namespace Dnvm;

/// <summary>
/// GlobalConfig contains options used by all of dnvm, like the DNVM_HOME path,
/// the SDK install path, and the location of the user's home directory.
/// </summary>
public sealed class GlobalOptions : IDisposable
{
    /// <summary>
    /// Default DNVM_HOME is
    ///  ~/.local/share/dnvm on Linux
    ///  %LocalAppData%/dnvm on Windows
    ///  ~/Library/Application Support/dnvm on Mac
    /// </summary>
    public static readonly string DefaultDnvmHome = Path.Combine(
        GetFolderPath(SpecialFolder.LocalApplicationData, SpecialFolderOption.DoNotVerify),
        "dnvm");

    /// <summary>
    /// The location of the SDK install directory, relative to <see cref="DnvmHome" />
    /// </summary>
    public static readonly SdkDirName DefaultSdkDirName = new("dn");


    public string UserHome { get; }
    public DnvmEnv DnvmEnv { get; }

    public GlobalOptions(
        string userHome,
        DnvmEnv dnvmEnv)
    {
        UserHome = userHome;
        DnvmEnv = dnvmEnv;
    }

    public void Dispose()
    {
        DnvmEnv.Dispose();
    }
}