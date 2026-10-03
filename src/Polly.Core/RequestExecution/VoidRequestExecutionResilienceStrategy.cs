#pragma warning disable CA1031 // Do not catch general exception types

using Polly.Utils;

namespace Polly.RequestExecution;

/// <summary>
/// A request execution adapter for non-generic, void-returning pipelines.
/// </summary>
/// <typeparam name="TRequest">The type of the request that is executed by the pipeline.</typeparam>
internal sealed class VoidRequestExecutionResilienceStrategy<TRequest> : ResilienceStrategy
    where TRequest : class
{
    private readonly TRequest _originalRequest;
    private readonly Func<TRequest, CancellationToken, ValueTask<TRequest>> _copyFactory;
    private readonly Func<TRequest, ResilienceContext, ValueTask> _executeCallback;
    private readonly Func<TRequest, ValueTask>? _releaseCallback;
    private readonly bool _adapterOwnsCopy;

    public VoidRequestExecutionResilienceStrategy(VoidRequestExecutionStrategyOptions<TRequest> options)
    {
        _originalRequest = options.OriginalRequest
            ?? throw new ArgumentNullException(nameof(options), $"The {nameof(options.OriginalRequest)} property must not be null.");
        _copyFactory = options.CopyFactory
            ?? throw new ArgumentNullException(nameof(options), $"The {nameof(options.CopyFactory)} property must not be null.");
        _executeCallback = options.ExecuteCallback
            ?? throw new ArgumentNullException(nameof(options), $"The {nameof(options.ExecuteCallback)} property must not be null.");
        _releaseCallback = options.ReleaseCallback;
        _adapterOwnsCopy = options.Ownership == RequestCopyOwnership.Adapter;
    }

    protected internal override async ValueTask<Outcome<TResult>> ExecuteCore<TResult, TState>(
        Func<ResilienceContext, TState, ValueTask<Outcome<TResult>>> callback,
        ResilienceContext context,
        TState state)
    {
        context.CancellationToken.ThrowIfCancellationRequested();

        TRequest? copy = null;
        try
        {
            copy = await _copyFactory(_originalRequest, context.CancellationToken).ConfigureAwait(context.ContinueOnCapturedContext);

            if (copy is null)
            {
                throw new InvalidOperationException("The request copy factory returned a null request copy.");
            }

            Exception? executionException = null;
            try
            {
                await _executeCallback(copy, context).ConfigureAwait(context.ContinueOnCapturedContext);
            }
            catch (Exception ex)
            {
                executionException = ex;
            }

            if (_adapterOwnsCopy)
            {
                await ReleaseAsync(copy, context).ConfigureAwait(context.ContinueOnCapturedContext);
            }

            if (executionException is not null)
            {
                return new Outcome<TResult>(executionException);
            }

            // Non-generic pipelines execute as VoidResult; the terminal callback produces the
            // void outcome of the pipeline and is simply propagated unchanged.
            return await callback(context, state).ConfigureAwait(context.ContinueOnCapturedContext);
        }
        catch
        {
            if (copy is not null && _adapterOwnsCopy)
            {
                await ReleaseAsync(copy, context).ConfigureAwait(context.ContinueOnCapturedContext);
            }

            throw;
        }
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
