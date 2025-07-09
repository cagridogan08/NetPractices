using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Messaging.ModelLibrary;
using Messaging.ModelLibrary.InMemory;
using Messaging.ModelLibrary.Pipe;
using Messaging.ModelLibrary.Tcp;
using Messaging.ModelLibrary.Udp;
using Messaging.ModelLibrary.WebSocket;
using MessagingApp.WPF.ViewModels;

namespace Messaging.Wpf;

public class MainViewModel : INotifyPropertyChanged
{
    private readonly IMessagingService _messagingService;
    private string _messageText = string.Empty;
    private string _clientName = Environment.UserName;
    private string _statusText = "Disconnected";
    private bool _isConnected;

    // Transport type
    private TransportType _selectedTransportType = TransportType.NamedPipe;

    // Named Pipe settings
    private string _pipeName = "GenericMessagingApp";
    private string _serverName = ".";

    // TCP settings
    private string _tcpHost = "localhost";
    private int _tcpPort = 8080;

    // UDP settings
    private string _udpHost = "localhost";
    private int _udpPort = 11000;
    private int _udpLocalPort = 0;

    // WebSocket settings
    private string _webSocketHost = "localhost";
    private int _webSocketPort = 8080;
    private string _webSocketPath = "/";
    private bool _webSocketUseSSL = false;

    // InMemory settings
    private string _inMemoryServerName = "default";

    public MainViewModel()
    {
        _messagingService = new MessagingService();
        _messagingService.MessageReceived += OnMessageReceived;
        _messagingService.Connected += OnConnected;
        _messagingService.Disconnected += OnDisconnected;
        _messagingService.ErrorOccurred += OnErrorOccurred;

        Messages = new ObservableCollection<Message>();
        Connections = new ObservableCollection<ConnectionInfo>();

        TransportTypes = new ObservableCollection<TransportType>
            {
                TransportType.InMemory,
                TransportType.NamedPipe,
                TransportType.Tcp,
                TransportType.Udp,
                TransportType.WebSocket
            };

        ConnectCommand = new RelayCommand(async () => await ConnectAsync(), () => !IsConnected);
        DisconnectCommand = new RelayCommand(async () => await DisconnectAsync(), () => IsConnected);
        SendMessageCommand = new RelayCommand(async () => await SendMessageAsync(), () => IsConnected && !string.IsNullOrWhiteSpace(MessageText));
        StartServerCommand = new RelayCommand(async () => await StartServerAsync(), () => !IsConnected);
        ClearMessagesCommand = new RelayCommand(() => Messages.Clear());
    }

    public ObservableCollection<Message> Messages { get; }
    public ObservableCollection<ConnectionInfo> Connections { get; }
    public ObservableCollection<TransportType> TransportTypes { get; }
    private ConnectionInfo? _selectedConnectionInfo;

    public ConnectionInfo? SelectedConnectionInfo
    {
        get => _selectedConnectionInfo;
        set
        {
            _selectedConnectionInfo = value;
            OnPropertyChanged(nameof(SelectedConnectionInfo));
        }
    }

    public string MessageText
    {
        get => _messageText;
        set
        {
            _messageText = value;
            OnPropertyChanged();
            ((RelayCommand)SendMessageCommand).RaiseCanExecuteChanged();
        }
    }

    public string ClientName
    {
        get => _clientName;
        set
        {
            _clientName = value;
            OnPropertyChanged();
            _messagingService.ClientName = value;
        }
    }

    public string StatusText
    {
        get => _statusText;
        set
        {
            _statusText = value;
            OnPropertyChanged();
        }
    }

    public bool IsConnected
    {
        get => _isConnected;
        set
        {
            _isConnected = value;
            OnPropertyChanged();
            ((RelayCommand)ConnectCommand).RaiseCanExecuteChanged();
            ((RelayCommand)DisconnectCommand).RaiseCanExecuteChanged();
            ((RelayCommand)SendMessageCommand).RaiseCanExecuteChanged();
            ((RelayCommand)StartServerCommand).RaiseCanExecuteChanged();
        }
    }

    public TransportType SelectedTransportType
    {
        get => _selectedTransportType;
        set
        {
            _selectedTransportType = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsInMemorySelected));
            OnPropertyChanged(nameof(IsNamedPipeSelected));
            OnPropertyChanged(nameof(IsTcpSelected));
            OnPropertyChanged(nameof(IsUdpSelected));
            OnPropertyChanged(nameof(IsWebSocketSelected));
        }
    }

    // Transport type visibility properties
    public bool IsInMemorySelected => SelectedTransportType == TransportType.InMemory;
    public bool IsNamedPipeSelected => SelectedTransportType == TransportType.NamedPipe;
    public bool IsTcpSelected => SelectedTransportType == TransportType.Tcp;
    public bool IsUdpSelected => SelectedTransportType == TransportType.Udp;
    public bool IsWebSocketSelected => SelectedTransportType == TransportType.WebSocket;

    // Named Pipe Properties
    public string PipeName
    {
        get => _pipeName;
        set
        {
            _pipeName = value;
            OnPropertyChanged();
        }
    }

    public string ServerName
    {
        get => _serverName;
        set
        {
            _serverName = value;
            OnPropertyChanged();
        }
    }

    // TCP Properties
    public string TcpHost
    {
        get => _tcpHost;
        set
        {
            _tcpHost = value;
            OnPropertyChanged();
        }
    }

    public int TcpPort
    {
        get => _tcpPort;
        set
        {
            _tcpPort = value;
            OnPropertyChanged();
        }
    }

    // UDP Properties
    public string UdpHost
    {
        get => _udpHost;
        set
        {
            _udpHost = value;
            OnPropertyChanged();
        }
    }

    public int UdpPort
    {
        get => _udpPort;
        set
        {
            _udpPort = value;
            OnPropertyChanged();
        }
    }

    public int UdpLocalPort
    {
        get => _udpLocalPort;
        set
        {
            _udpLocalPort = value;
            OnPropertyChanged();
        }
    }

    // WebSocket Properties
    public string WebSocketHost
    {
        get => _webSocketHost;
        set
        {
            _webSocketHost = value;
            OnPropertyChanged();
        }
    }

    public int WebSocketPort
    {
        get => _webSocketPort;
        set
        {
            _webSocketPort = value;
            OnPropertyChanged();
        }
    }

    public string WebSocketPath
    {
        get => _webSocketPath;
        set
        {
            _webSocketPath = value;
            OnPropertyChanged();
        }
    }

    public bool WebSocketUseSSL
    {
        get => _webSocketUseSSL;
        set
        {
            _webSocketUseSSL = value;
            OnPropertyChanged();
        }
    }

    // InMemory Properties
    public string InMemoryServerName
    {
        get => _inMemoryServerName;
        set
        {
            _inMemoryServerName = value;
            OnPropertyChanged();
        }
    }

    public ICommand ConnectCommand { get; }
    public ICommand DisconnectCommand { get; }
    public ICommand SendMessageCommand { get; }
    public ICommand StartServerCommand { get; }
    public ICommand ClearMessagesCommand { get; }

    private async Task ConnectAsync()
    {
        StatusText = "Connecting...";

        try
        {
            IMessageClient client = SelectedTransportType switch
            {
                TransportType.InMemory => new InMemoryClient(ClientName),
                TransportType.NamedPipe => new NamedPipeClient(),
                TransportType.Tcp => new TcpMessageClient(),
                TransportType.Udp => new UdpMessageClient(),
                TransportType.WebSocket => new WebSocketMessageClient(),
                _ => throw new ArgumentException($"Unknown transport type: {SelectedTransportType}")
            };

            var configuration = GetClientConfiguration();
            var success = await _messagingService.ConnectAsClientAsync(client, configuration);

            if (!success)
            {
                IsConnected = false;
                StatusText = "Connection failed";
            }
            // If success, the OnConnected event will update the status
        }
        catch (Exception ex)
        {
            IsConnected = false;
            StatusText = $"Connection error: {ex.Message}";
        }
    }

    private async Task DisconnectAsync()
    {
        StatusText = "Disconnecting...";
        try
        {
            await _messagingService.StopAsync();

            // Force update UI state after disconnect
            IsConnected = false;
            StatusText = "Disconnected";
            Connections.Clear();
        }
        catch (Exception ex)
        {
            StatusText = $"Disconnect error: {ex.Message}";
            // Still update the connection state even if there was an error
            IsConnected = false;
            Connections.Clear();
        }
    }

    private async Task SendMessageAsync()
    {
        if (!string.IsNullOrWhiteSpace(MessageText))
        {
            if (SelectedConnectionInfo != null)
                await _messagingService.SendMessageAsync(MessageText, SelectedConnectionInfo.Id);
            else
            {
                await _messagingService.SendMessageAsync(MessageText);
            }
            MessageText = string.Empty;
        }
    }

    private async Task StartServerAsync()
    {
        StatusText = "Starting server...";

        try
        {
            IMessageTransport transport = SelectedTransportType switch
            {
                TransportType.InMemory => new InMemoryTransport(InMemoryServerName),
                TransportType.NamedPipe => new NamedPipeTransport(PipeName),
                TransportType.Tcp => new TcpTransport(),
                TransportType.Udp => new UdpTransport(),
                TransportType.WebSocket => new WebSocketTransport(),
                _ => throw new ArgumentException($"Unknown transport type: {SelectedTransportType}")
            };

            var configuration = GetServerConfiguration();
            var success = await _messagingService.StartServerAsync(transport, configuration);

            if (success)
            {
                IsConnected = true;
                StatusText = "Server started, waiting for connections...";
            }
            else
            {
                IsConnected = false;
                StatusText = "Failed to start server";
            }
        }
        catch (Exception ex)
        {
            IsConnected = false;
            StatusText = $"Server start error: {ex.Message}";
        }
    }

    private Dictionary<string, object> GetClientConfiguration()
    {
        return SelectedTransportType switch
        {
            TransportType.InMemory => new Dictionary<string, object>
            {
                ["ServerName"] = InMemoryServerName,
                ["ClientName"] = ClientName
            },
            TransportType.NamedPipe => new Dictionary<string, object>
            {
                ["ServerName"] = ServerName,
                ["PipeName"] = PipeName,
                ["Timeout"] = 5000,
                ["ClientName"] = ClientName
            },
            TransportType.Tcp => new Dictionary<string, object>
            {
                ["Host"] = TcpHost,
                ["Port"] = TcpPort,
                ["Timeout"] = 5000,
                ["ClientName"] = ClientName
            },
            TransportType.Udp => new Dictionary<string, object>
            {
                ["Host"] = UdpHost,
                ["Port"] = UdpPort,
                ["LocalPort"] = UdpLocalPort,
                ["ClientName"] = ClientName
            },
            TransportType.WebSocket => new Dictionary<string, object>
            {
                ["Host"] = WebSocketHost,
                ["Port"] = WebSocketPort,
                ["Path"] = WebSocketPath,
                ["UseSSL"] = WebSocketUseSSL,
                ["Timeout"] = 5000,
                ["ClientName"] = ClientName
            },
            _ => new Dictionary<string, object>()
        };
    }

    private Dictionary<string, object> GetServerConfiguration()
    {
        return SelectedTransportType switch
        {
            TransportType.InMemory => new Dictionary<string, object>
            {
                ["ServerName"] = InMemoryServerName
            },
            TransportType.NamedPipe => new Dictionary<string, object>
            {
                ["PipeName"] = PipeName
            },
            TransportType.Tcp => new Dictionary<string, object>
            {
                ["Host"] = TcpHost,
                ["Port"] = TcpPort
            },
            TransportType.Udp => new Dictionary<string, object>
            {
                ["Host"] = UdpHost,
                ["Port"] = UdpPort
            },
            TransportType.WebSocket => new Dictionary<string, object>
            {
                ["Host"] = WebSocketHost,
                ["Port"] = WebSocketPort,
                ["Path"] = WebSocketPath
            },
            _ => new Dictionary<string, object>()
        };
    }

    private void OnMessageReceived(object sender, MessageEventArgs e)
    {
        App.Current.Dispatcher.Invoke(() =>
        {
            Messages.Add(e.Message);
        });
    }

    private void OnConnected(object sender, ConnectionEventArgs e)
    {
        App.Current.Dispatcher.Invoke(() =>
        {
            IsConnected = true;

            // Update status based on mode
            if (_messagingService.Mode == MessagingMode.Server)
            {
                StatusText = Connections.Count == 0 ? "First client connected" : "Client connected";
            }
            else if (_messagingService.Mode == MessagingMode.Client)
            {
                StatusText = "Connected to server";
            }
            else
            {
                StatusText = "Connected";
            }

            // Add connection if not already present
            if (e.Connection != null && !Connections.Contains(e.Connection))
            {
                Connections.Add(e.Connection);
            }
        });
    }

    private void OnDisconnected(object sender, ConnectionEventArgs e)
    {
        App.Current.Dispatcher.Invoke(() =>
        {
            // Remove the specific connection
            if (e.Connection != null && Connections.Contains(e.Connection))
            {
                Connections.Remove(e.Connection);
            }

            // Update overall connection state
            if (_messagingService.Mode == MessagingMode.None || Connections.Count == 0)
            {
                IsConnected = false;
                if (StatusText == "Disconnecting...")
                {
                    StatusText = "Disconnected";
                }
                else if (_messagingService.Mode == MessagingMode.None)
                {
                    StatusText = "Disconnected";
                }
            }
        });
    }

    private void OnErrorOccurred(object sender, ErrorEventArgs e)
    {
        App.Current.Dispatcher.Invoke(() =>
        {
            Messages.Add(new Message
            {
                Content = e.Error,
                Sender = "System",
                Type = MessageType.Error
            });
        });
    }

    public event PropertyChangedEventHandler PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}