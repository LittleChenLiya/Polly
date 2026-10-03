namespace Polly.Core.Tests.RequestExecution;

/// <summary>
/// A request-like fixture that tracks whether it was released.
/// </summary>
internal sealed class DisposableRequest : IDisposable
{
    public DisposableRequest()
        : this(0)
    {
    }

    public DisposableRequest(int id) => Id = id;

    public int Id { get; }

    public string Name => $"request-{Id}";

    public bool IsDisposed { get; private set; }

    public DisposableRequest Clone(int id) => new(id) { OriginalId = Id };

    public int OriginalId { get; private set; }

    public void Dispose() => IsDisposed = true;
}
