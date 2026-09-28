namespace Polly.Core.Tests.Helpers;

/// <summary>
/// Represents a fake resource (for example an HTTP response or a stream) that counts how many times it was cleaned up.
/// </summary>
internal sealed class CountedResource(int id)
{
    public int Id { get; } = id;

    public int CleanupCount { get; private set; }

    public void Release() => CleanupCount++;

    public override string ToString() => $"resource-{Id}";
}
