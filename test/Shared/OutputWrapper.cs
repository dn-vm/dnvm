
using System.Text;
using Xunit;

namespace Dnvm.Test;

public sealed class OutputWrapper : TextWriter
{
    private readonly ITestOutputHelper _output;
    public OutputWrapper(ITestOutputHelper output)
    {
        _output = output;
    }

    public override Encoding Encoding => Encoding.Default;

    public override void WriteLine(string? value)
    {
        _output.WriteLine(value);
    }
}