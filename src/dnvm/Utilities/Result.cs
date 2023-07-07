
using System;

namespace Dnvm;

public abstract record Result<TOk, TErr>
{
    private Result() { }

    public sealed record Ok(TOk Value) : Result<TOk, TErr>;
    public sealed record Err(TErr Value) : Result<TOk, TErr>;

    public static implicit operator Result<TOk, TErr>(TOk success) => new Ok(success);
    public static implicit operator Result<TOk, TErr>(TErr error) => new Err(error);

    public TOk Unwrap() => this switch
    {
        Ok(var ok) => ok,
        _ => throw new InvalidOperationException(),
    };
}