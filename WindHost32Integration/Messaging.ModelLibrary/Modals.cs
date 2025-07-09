
namespace Messaging.ModelLibrary
{
    public class ErrorEventArgs : EventArgs
    {
        public string Error { get; }
        public Exception Exception { get; }
        public ConnectionInfo Connection { get; }

        public ErrorEventArgs(string error, Exception exception = null, ConnectionInfo connection = null)
        {
            Error = error;
            Exception = exception;
            Connection = connection;
        }
    }
}
