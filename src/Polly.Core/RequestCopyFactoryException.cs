namespace Polly;

/// <summary>
/// The exception that is thrown when a request copy factory fails to create a copy.
/// </summary>
/// <remarks>
/// This exception is thrown by the request copy execution adapter when the factory's
/// <see cref="RequestCopyFactory{TRequest}.CreateCopy"/> method throws an exception.
/// It is used to terminate the execution immediately, bypassing any retry logic.
/// </remarks>
public class RequestCopyFactoryException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="RequestCopyFactoryException"/> class.
    /// </summary>
    public RequestCopyFactoryException()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="RequestCopyFactoryException"/> class with a specified error message.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    public RequestCopyFactoryException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="RequestCopyFactoryException"/> class with a specified error message
    /// and a reference to the inner exception that is the cause of this exception.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    /// <param name="innerException">The exception that is the cause of the current exception.</param>
    public RequestCopyFactoryException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
