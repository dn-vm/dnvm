
using System;
using System.Runtime.CompilerServices;

namespace Serde.CmdLine;

internal static class NullableExt
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static U Map<T, U>(this T t, Func<T, U> f) => f(t);
}

internal static class StringExt
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static string Prepend(this string s, string prefix) => prefix + s;
}