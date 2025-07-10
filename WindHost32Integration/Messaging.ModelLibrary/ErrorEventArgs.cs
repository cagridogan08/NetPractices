
namespace Messaging.ModelLibrary;

public class ErrorEventArgs(string error, Exception? exception = null, ConnectionInfo? connection = null)
    : EventArgs
{
    public string Error { get; } = error;
    public Exception Exception { get; } = exception;
    public ConnectionInfo Connection { get; } = connection;
}