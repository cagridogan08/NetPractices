
namespace Messaging.ModelLibrary.ServerSentEvents;

/// <summary>
/// Simple EventSource implementation for .NET
/// </summary>
public class EventSource(string url, HttpClient httpClient) : IDisposable
{
    private Stream? _stream;
    private StreamReader? _reader;
    private CancellationTokenSource? _cancellationTokenSource;

    public event EventHandler<EventSourceMessageEventArgs>? MessageReceived;
    public event EventHandler? ConnectionOpened;
    public event EventHandler? ConnectionClosed;
    public event EventHandler<EventSourceErrorEventArgs>? ErrorOccurred;

    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("Accept", "text/event-stream");
            request.Headers.Add("Cache-Control", "no-cache");

            var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, _cancellationTokenSource.Token);
            response.EnsureSuccessStatusCode();

            _stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            _reader = new StreamReader(_stream);

            ConnectionOpened?.Invoke(this, EventArgs.Empty);

            _ = Task.Run(ReadLoop, _cancellationTokenSource.Token);
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, new EventSourceErrorEventArgs(ex));
        }
    }

    private async Task ReadLoop()
    {
        try
        {
            while (_reader != null && !_cancellationTokenSource?.Token.IsCancellationRequested == true)
            {
                var line = await _reader.ReadLineAsync();
                if (line == null) break;

                if (line.StartsWith("data: "))
                {
                    var data = line.Substring(6);
                    MessageReceived?.Invoke(this, new EventSourceMessageEventArgs(data));
                }
            }
        }
        catch (Exception ex)
        {
            if (!_cancellationTokenSource?.Token.IsCancellationRequested == true)
            {
                ErrorOccurred?.Invoke(this, new EventSourceErrorEventArgs(ex));
            }
        }
        finally
        {
            ConnectionClosed?.Invoke(this, EventArgs.Empty);
        }
    }

    public void Dispose()
    {
        _cancellationTokenSource?.Cancel();
        _reader?.Dispose();
        _stream?.Dispose();
        _cancellationTokenSource?.Dispose();
    }
}

public class EventSourceMessageEventArgs(string data) : EventArgs
{
    public string Data { get; } = data;
}

public class EventSourceErrorEventArgs(Exception exception) : EventArgs
{
    public Exception Exception { get; } = exception;
}

/// <summary>
/// Extension method for WaitHandle
/// </summary>
public static class WaitHandleExtensions
{
    public static async Task<bool> WaitOneAsync(this WaitHandle waitHandle, TimeSpan timeout)
    {
        return await Task.Run(() => waitHandle.WaitOne(timeout));
    }
}