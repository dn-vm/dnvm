
using System;
using System.Collections.Immutable;
using System.Net.Http;
using System.Threading.Tasks;
using Semver;

namespace Dnvm;

internal static class ImmutableArrayExt
{
    public static async Task<ImmutableArray<U>> SelectAsArray<T, U>(this ImmutableArray<T> e, Func<T, Task<U>> f)
    {
        var builder = ImmutableArray.CreateBuilder<U>(e.Length);
        foreach (var item in e)
        {
            builder.Add(await f(item));
        }
        return builder.MoveToImmutable();
    }

    public static T? SingleOrNull<T>(this ImmutableArray<T> e, Func<T, bool> func)
        where T : class
    {
        T? result = null;
        foreach (var elem in e)
        {
            if (func(elem))
            {
                if (result is not null)
                {
                    return null;
                }
                result = elem;
            }
        }
        return result;
    }
}

internal static class ImmutableArrayExt2
{
    public static T? SingleOrNull<T>(this ImmutableArray<T> e, Func<T, bool> func)
        where T : struct
    {
        T? result = null;
        foreach (var elem in e)
        {
            if (func(elem))
            {
                if (result is not null)
                {
                    return null;
                }
                result = elem;
            }
        }
        return result;
    }
}

internal static class SemVerExtensions
{
    public static SemVersion WithSuggestedThreeDigitPatch(this SemVersion version) =>
        version.Patch switch
        {
            >= 100 => throw new InvalidOperationException("Patch version is already three digits"),
            0 => version.WithPatch(100),
            < 10 => version.WithPatch(version.Patch * 100),
            < 100 and >= 10 => version.WithPatch(version.Patch * 10)
        };
}