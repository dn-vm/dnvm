
using System;

namespace Serde.CmdLine;

public sealed class HelpRequestedException(string helpText) : Exception
{
    public string HelpText { get; } = helpText;
}