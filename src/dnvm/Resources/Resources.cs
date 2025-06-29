
using System.IO;

namespace Dnvm;

internal static class Resources
{
    private static string ReadResource(string resourceName)
    {
        var asm = typeof(Resources).Assembly;
        using var stream = asm.GetManifestResourceStream(resourceName)
            ?? throw new FileNotFoundException($"Resource '{resourceName}' not found.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    public static string GetEnvShContent()
        => ReadResource("dnvm.Resources.env.sh");

    public static string GetRootPubContent()
        => ReadResource("dnvm.Resources.root.pub");
}