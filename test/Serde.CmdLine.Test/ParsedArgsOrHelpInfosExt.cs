using static Serde.CmdLine.CmdLine;

namespace Serde.CmdLine.Test;

public static class ParsedArgsOrHelpInfosExt
{
    public static T Unwrap<T>(this ParsedArgsOrHelpInfos<T> @this)
    {
        return ((ParsedArgsOrHelpInfos<T>.Parsed)@this).Args;
    }
}