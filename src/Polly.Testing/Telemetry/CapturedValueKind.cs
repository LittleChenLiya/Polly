namespace Polly.Testing;

/// <summary>
/// Describes how a value was captured by <see cref="CapturedValue"/>.
/// </summary>
public enum CapturedValueKind
{
    /// <summary>
    /// The captured value was <see langword="null"/>.
    /// </summary>
    Null = 0,

    /// <summary>
    /// The captured value is an explicitly supported immutable scalar and its value was copied.
    /// </summary>
    Scalar = 1,

    /// <summary>
    /// The captured value is an unsupported object. Only its type name was recorded, the original
    /// object was never stored and no <see cref="object.ToString"/> was called on it.
    /// </summary>
    TypeMarker = 2,
}
