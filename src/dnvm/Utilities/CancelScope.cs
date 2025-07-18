
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace Dnvm;

public sealed partial class CancelScope
{
    private readonly CancellationTokenSource _cts;

    private CancelScope(CancelScope? parent)
    {
        _cts = parent is null
            ? new()
            : CancellationTokenSource.CreateLinkedTokenSource(parent._cts.Token);
    }
}

partial class CancelScope
{
    private static readonly AsyncLocal<CancelScope> _current = new AsyncLocal<CancelScope>();

    public static CancelScope Current
    {
        get
        {
            if (_current.Value is null)
            {
                _current.Value = new CancelScope(null);
            }
            return _current.Value;
        }
        private set
        {
            if (value is null)
            {
                throw new ArgumentNullException(nameof(value));
            }
            _current.Value = value;
        }
    }

    public static async Task WithCancelAfter(
        TimeSpan delay,
        Func<CancelScope, Task> func,
        Action<OperationCanceledException>? onCanceled = null)
    {
        var parent = Current;
        Debug.Assert(parent is not null);
        var scope = new CancelScope(Current);
        Current = scope;
        try
        {
            scope._cts.CancelAfter(delay);
            await func(scope);
        }
        catch (OperationCanceledException e) when (e.CancellationToken == scope._cts.Token)
        {
            onCanceled?.Invoke(e);
        }
        finally
        {
            scope._cts.Dispose();
            Debug.Assert(parent is not null);
            Current = parent;
        }
    }

    public static void WithCancelAfter(
        TimeSpan delay,
        Action<CancelScope> action,
        Action<OperationCanceledException>? onCanceled = null)
    {
        WithCancelAfter(
            delay,
            scope => { action(scope); return Task.CompletedTask; },
            onCanceled).GetAwaiter().GetResult();
    }

    public static T WithTimeoutAfter<T>(
        TimeSpan delay,
        Func<CancelScope, T> func,
        Action<OperationCanceledException>? onCanceled = null)
    {
        return WithTimeoutAfter(
            delay,
            scope => Task.FromResult(func(scope)),
            onCanceled).GetAwaiter().GetResult();
    }

    /// <summary>
    /// Executes the provided function with a timeout. If the function does not complete within the specified delay,
    /// a <see cref="TimeoutException"/> is thrown.
    /// </summary>
    /// <remarks>
    /// Identical to <see cref="WithCancelAfter(TimeSpan, Func{CancelScope, Task},
    /// Action{OperationCanceledException}?)"/> except that it throws a <see cref="TimeoutException"/> on timeout instead
    /// of continuing silently.
    /// </remarks>
    public static async Task<T> WithTimeoutAfter<T>(
        TimeSpan delay,
        Func<CancelScope, Task<T>> func,
        Action<OperationCanceledException>? onCanceled = null)
    {
        var parent = Current;
        Debug.Assert(parent is not null);
        var scope = new CancelScope(Current);
        Current = scope;
        try
        {
            scope._cts.CancelAfter(delay);
            return await func(scope);
        }
        catch (OperationCanceledException e) when (e.CancellationToken == scope._cts.Token)
        {
            onCanceled?.Invoke(e);
            throw new TimeoutException("Operation timed out", e);
        }
        finally
        {
            scope._cts.Dispose();
            Debug.Assert(parent is not null);
            Current = parent;
        }
    }

    public CancellationToken Token => _cts.Token;


    public async Task Delay(TimeSpan delay)
    {
        await Current.Delay(delay);
    }

    public void Cancel()
    {
        _cts.Cancel();
        _cts.Token.ThrowIfCancellationRequested();
    }
}

public static class CancelScopeExtensions
{
    public static async Task Delay(this CancelScope scope, TimeSpan delay)
    {
        await Task.Delay(delay, scope.Token);
    }
}