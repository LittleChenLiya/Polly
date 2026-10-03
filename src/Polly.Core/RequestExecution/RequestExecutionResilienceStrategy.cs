#pragma warning disable CA1031 // Do not catch general exception types

using Polly.Utils;

namespace Polly.RequestExecution;

/// <summary>
/// An execution adapter that creates an explicit copy of the original request for every attempt.
/// </summary>
/// <typeparam name="TRequest">The type of the request that is executed by the pipeline.</typeparam>
/// <typeparam name="TResult">The type of the result produced by an execution attempt.</typeparam>
/// <remarks>
/// <para>
/// This adapter is intended to be added as the innermost strategy of a retry (or hedging) pipeline.
/// The surrounding strategies re-invoke this adapter for every attempt they perform, and the adapter
/// in turn invokes the copy factory exactly once per attempt before calling the execution callback
/// with the newly created copy.
/// </para>
/// <para>
/// If the copy factory throws, the exception escapes the adapter immediately and terminates the
/// execution; the execution callback is not called and no additional copies are created. If
/// execution is canceled while a retry is waiting for its next attempt, no new copy is created.
/// </para>
/// <para>
/// The original request always stays owned by the caller and is never released by the adapter.
/// </para>
/// </remarks>
internal sealed class RequestExecutionResilienceStrategy<TRequest, TResult> : ResilienceStrategy<TResult>
    where TRequest : class
{
    private readonly Func<TRequest, CancellationToken, ValueTask<TRequest>> _copyFactory;
    private readonly Func<TRequest, ResilienceContext, ValueTask<Outcome<TResult>>> _executeCallback;
    private readonly Func<TRequest, ValueTask>? _releaseCallback;
    private readonly bool _adapterOwnsCopy;

    public RequestExecutionResilienceStrategy(RequestExecutionStrategyOptions<TRequest, TResult> options)
    {
        OriginalRequest = options.OriginalRequest
            ?? throw new ArgumentNullException(nameof(options), $"The {nameof(options.OriginalRequest)} property must not be null.");
        _copyFactory = options.CopyFactory
            ?? throw new ArgumentNullException(nameof(options), $"The {nameof(options.CopyFactory)} property must not be null.");
        _executeCallback = options.ExecuteCallback
            ?? throw new ArgumentNullException(nameof(options), $"The {nameof(options.ExecuteCallback)} property must not be null.");
        _releaseCallback = options.ReleaseCallback;
        _adapterOwnsCopy = options.Ownership == RequestCopyOwnership.Adapter;
    }

    public TRequest OriginalRequest { get; }

    protected internal override async ValueTask<Outcome<TResult>> ExecuteCore<TState>(
        Func<ResilienceContext, TState, ValueTask<Outcome<TResult>>> callback,
        ResilienceContext context,
        TState state)
    {
        context.CancellationToken.ThrowIfCancellationRequested();

        TRequest? copy = null;
        try
        {
            // A fresh copy is created exactly once for this attempt, immediately before the attempt runs.
            // Attempts that are canceled while a retry is waiting never reach this point, so no surplus
            // copy is created. If the factory throws, the execution terminates immediately and the
            // execution callback is never invoked.
            copy = await _copyFactory(OriginalRequest, context.CancellationToken).ConfigureAwait(context.ContinueOnCapturedContext);

            if (copy is null)
            {
                throw new InvalidOperationException("The request copy factory returned a null request copy.");
            }

            Outcome<TResult> outcome;
            try
            {
                outcome = await _executeCallback(copy, context).ConfigureAwait(context.ContinueOnCapturedContext);
            }
            catch (Exception ex)
            {
                outcome = new Outcome<TResult>(ex);
            }

            if (_adapterOwnsCopy && !ReturnsCopy(copy, outcome))
            {
                await ReleaseAsync(copy, context).ConfigureAwait(context.ContinueOnCapturedContext);
            }

            return outcome;
        }
        catch
        {
            // Factory failure (or a null copy): release the copy the factory may have produced,
            // then rethrow so the execution terminates immediately without invoking the callback.
            if (copy is not null && _adapterOwnsCopy)
            {
                await ReleaseAsync(copy, context).ConfigureAwait(context.ContinueOnCapturedContext);
            }

            throw;
        }
    }

    private static bool ReturnsCopy(TRequest copy, Outcome<TResult> outcome)
    {
        // Only skip the release when a successful attempt hands the copy itself back to the caller
        // as its result, so a successful result is never disposed underneath the caller during cleanup.
        return outcome.TryGetResult(out var result) && result is TRequest requestResult && ReferenceEquals(copy, requestResult);
    }

    private async ValueTask ReleaseAsync(TRequest copy, ResilienceContext context)
    {
        try
        {
            if (_releaseCallback is not null)
            {
                await _releaseCallback(copy).ConfigureAwait(context.ContinueOnCapturedContext);
            }
            else
            {
                await DisposeHelper.TryDisposeSafeAsync(copy, context.IsSynchronous).ConfigureAwait(context.ContinueOnCapturedContext);
            }
        }
        catch (Exception)
        {
            // Releasing a copy must never mask the outcome of the attempt.
        }
    }
}
