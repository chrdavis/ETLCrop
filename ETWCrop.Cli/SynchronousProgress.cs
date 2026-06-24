namespace ETWCrop.Cli;

/// <summary>
/// An <see cref="IProgress{T}"/> that invokes its callback synchronously on the calling thread,
/// avoiding the thread-pool marshalling of <see cref="Progress{T}"/> so console output stays ordered.
/// </summary>
/// <typeparam name="T">The progress payload type.</typeparam>
public sealed class SynchronousProgress<T>(Action<T> handler) : IProgress<T>
{
    private readonly Action<T> _handler = handler ?? throw new ArgumentNullException(nameof(handler));

    /// <inheritdoc />
    public void Report(T value) => _handler(value);
}
