using Messaging.ModelLibrary.Abstract;
using System.Text;
using System.Text.Json;
namespace Messaging.ModelLibrary.ServerSentEvents;
public class ServerSentEventClient : MessageClientBase
{
    #region Fields

    private HttpClient? _httpClient;

    private EventSource? _eventSource;

    private CancellationTokenSource? _cancellationTokenSource;
    private SseConfig? _config;

    #endregion

    #region Properties

    public override bool IsConnected => _httpClient != null && _eventSource is not null;
    public override ConnectionInfo? ConnectionInfo { get; protected set; }
    public override TransportType TransportType => TransportType.ServerSentEvents;

    #endregion

    #region Methods

    public override async Task<bool> ConnectAsync(Dictionary<string, object> configuration)
    {
        try
        {
            await DisconnectAsync();

            _config = SseConfig.FromDictionary(configuration);

            _httpClient = new();
            _cancellationTokenSource = new();
            var sseUrl = $"http://{_config.Host}:{_config.Port}{_config.EventsPath}?client={_config.ClientName}";
            _eventSource = new EventSource(sseUrl, _httpClient);
            _eventSource.MessageReceived += OnEventSourceMessage;
            _eventSource.ConnectionOpened += OnEventSourceOpened;
            _eventSource.ConnectionClosed += OnEventSourceClosed;
            _eventSource.ErrorOccurred += OnEventSourceError;
            await _eventSource.ConnectAsync(_cancellationTokenSource.Token);
            ConnectionInfo = new ConnectionInfo
            {
                Id = _config.ClientName,
                Name = _config.ClientName,
                Address = $"{_config.Host}:{_config.Port}",
                ConnectedAt = DateTime.UtcNow,
                IsActive = true
            };

            OnConnected(new ConnectionEventArgs(ConnectionInfo));
            return true;
        }
        catch (Exception ex)
        {
            OnErrorOccurred(new ErrorEventArgs($"SSE connection failed: {ex.Message}", ex));
            return false;
        }
    }


    public override Task DisconnectAsync()
    {
        try
        {
            _cancellationTokenSource?.Cancel();
            if (_eventSource is not null)
            {
                _eventSource.MessageReceived -= OnEventSourceMessage;
                _eventSource.ConnectionOpened -= OnEventSourceOpened;
                _eventSource.ConnectionClosed -= OnEventSourceClosed;
                _eventSource.ErrorOccurred -= OnEventSourceError;
                _eventSource.Dispose();
                _eventSource = null;
            }
            _httpClient?.Dispose();
            _httpClient = null;
            if (ConnectionInfo is not null)
            {
                OnDisconnected(new ConnectionEventArgs(ConnectionInfo));
                ConnectionInfo = null;
            }
        }
        catch (Exception ex)
        {
            OnErrorOccurred(new ErrorEventArgs($"SSE disconnection error: {ex.Message}", ex));
        }
        return Task.CompletedTask;
    }

    public override async Task<bool> SendMessageAsync(Message message)
    {
        try
        {
            if (!IsConnected || _httpClient == null || _config == null) return false;

            if (string.IsNullOrEmpty(message.Sender))
                message.Sender = ConnectionInfo?.Name ?? "Unknown";

            var json = JsonSerializer.Serialize(message);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var url = $"http://{_config.Host}:{_config.Port}{_config.MessagesPath}";
            var response = await _httpClient.PostAsync(url, content);

            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            OnErrorOccurred(new ErrorEventArgs($"SSE send error: {ex.Message}", ex, ConnectionInfo));
            return false;
        }
    }

    #region EventSourceHandlers

    private void OnEventSourceMessage(object? sender, EventSourceMessageEventArgs e)
    {
        try
        {
            var message = JsonSerializer.Deserialize<Message>(e.Data);
            if (message != null && ConnectionInfo != null)
            {
                base.OnMessageReceived(new MessageEventArgs(message, ConnectionInfo));
            }
        }
        catch (Exception ex)
        {
            OnErrorOccurred(new ErrorEventArgs($"Error processing SSE message: {ex.Message}", ex, ConnectionInfo));
        }
    }

    private void OnEventSourceOpened(object? sender, EventArgs e)
    {
        // Connection opened - already handled in ConnectAsync
    }

    private void OnEventSourceClosed(object? sender, EventArgs e)
    {
        if (ConnectionInfo != null && !_disposed)
        {
            OnDisconnected(new ConnectionEventArgs(ConnectionInfo));
        }
    }

    private void OnEventSourceError(object? sender, EventSourceErrorEventArgs e)
    {
        OnErrorOccurred(new ErrorEventArgs($"SSE error: {e.Exception.Message}", e.Exception, ConnectionInfo));
    }

    #endregion

    #endregion

}
