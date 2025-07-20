
namespace Dnvm;

internal static class Encoding
{
    /// <summary>
    /// UTF-8 encoding without the BOM and with strict error handling.
    /// </summary>
    public static readonly System.Text.Encoding UTF8 = new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
}