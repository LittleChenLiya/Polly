namespace Polly.Fallback;

#pragma warning disable CA1031 // Do not catch general exception types

/// <summary>
/// Provides an independent, ordered fallback execution entry point that tries a small set of
/// fallback candidates one at a time, in order, after the primary service call fails.
/// </summary>
/// <remarks>
/// This entry point is intentionally standalone: it does not build or modify any
/// <see cref="ResiliencePipeline"/> and it does not change the behavior of the existing
/// fallback resilience strategy (<see cref="FallbackStrategyOptions{TResult}"/>).
/// <para>
/// The candidates are captured into a fixed snapshot before the execution starts, so mutating
/// the original sequence while the execution is in progress cannot add or remove candidates.
/// Each candidate is invoked at most once and the first successful candidate ends the execution.
/// Only errors that the caller-provided predicate considers fallbackable move the execution to
/// the next candidate; any other error stops the execution immediately and is rethrown as-is.
/// Cancellation requested through <see cref="ResilienceContext.CancellationToken"/> also stops
/// the execution immediately. If every candidate fails, an <see cref="AggregateException"/> that
/// preserves the errors in the exact order they occurred is thrown.
/// </para>
/// </remarks>
public static class OrderedFallback
{
    /// <summary>
    /// Executes the fallback <paramref name="candidates"/> sequentially, in order, and returns the
    /// result of the first candidate that succeeds.
    /// </summary>
    /// <typeparam name="TResult">The type of the result produced by the candidates.</typeparam>
    /// <param name="context">The resilience context associated with the execution. Its cancellation token is honored before each candidate and while awaiting it.</param>
    /// <param name="candidates">The ordered fallback candidates. The sequence is snapshotted before the execution starts; each candidate is invoked at most once.</param>
    /// <param name="isFallbackable">A caller-provided predicate that decides whether an error thrown by a candidate allows trying the next candidate.</param>
    /// <returns>The result of the first successful candidate.</returns>
    /// <exception cref="ArgumentNullException">One of the arguments is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">The <paramref name="candidates"/> sequence is empty.</exception>
    /// <exception cref="OperationCanceledException">The <see cref="ResilienceContext.CancellationToken"/> is canceled before or during the execution.</exception>
    /// <exception cref="AggregateException">All candidates failed with fallbackable errors. The inner exceptions preserve the order in which they occurred.</exception>
    public static ValueTask<TResult> ExecuteAsync<TResult>(
        ResilienceContext context,
        IEnumerable<Func<ResilienceContext, ValueTask<TResult>>> candidates,
        Func<Exception, bool> isFallbackable)
    {
        Guard.NotNull(context);
        Guard.NotNull(candidates);
        Guard.NotNull(isFallbackable);

        // Capture a fixed snapshot of the candidates before the execution starts so that
        // external changes to the original sequence cannot add or skip candidates mid-execution.
        var snapshot = candidates.ToArray();
        if (snapshot.Length == 0)
        {
            throw new ArgumentException("At least one fallback candidate must be provided.", nameof(candidates));
        }

        return ExecuteCore(context, snapshot, isFallbackable);
    }

    private static async ValueTask<TResult> ExecuteCore<TResult>(
        ResilienceContext context,
        Func<ResilienceContext, ValueTask<TResult>>[] snapshot,
        Func<Exception, bool> isFallbackable)
    {
        List<Exception>? errors = null;

        foreach (var candidate in snapshot)
        {
            context.CancellationToken.ThrowIfCancellationRequested();

            try
            {
                return await candidate(context).ConfigureAwait(context.ContinueOnCapturedContext);
            }
            catch (OperationCanceledException) when (context.CancellationToken.IsCancellationRequested)
            {
                // Cancellation stops the execution immediately and is never treated as a fallbackable error.
                throw;
            }
            catch (Exception e)
            {
                if (!isFallbackable(e))
                {
                    // Errors that the caller does not consider fallbackable stop the execution immediately.
                    throw;
                }

                (errors ??= new List<Exception>(snapshot.Length)).Add(e);
            }
        }

        // All candidates failed; preserve the errors in the exact order they occurred.
        throw new AggregateException("Every fallback candidate failed.", errors!);
    }
}
