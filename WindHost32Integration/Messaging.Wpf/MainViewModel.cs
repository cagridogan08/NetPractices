using Messaging.ModelLibrary;
using Messaging.ModelLibrary.Abstract;
using Messaging.ModelLibrary.Pipe;
using Messaging.ModelLibrary.Rtp;
using Messaging.ModelLibrary.RTP;
using Messaging.ModelLibrary.SignalR;
using Messaging.ModelLibrary.Tcp;
using Messaging.ModelLibrary.Udp;
using Messaging.ModelLibrary.WebSocket;
using Messaging.ModelLibrary.Grpc;
using MessagingApp.WPF.ViewModels;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using System.IO;
using System.Windows.Threading;
using Messaging.ModelLibrary.Mqtt;
using ClientInfo = Messaging.ModelLibrary.ClientInfo;
using ErrorEventArgs = Messaging.ModelLibrary.ErrorEventArgs;
using MessagingService = Messaging.ModelLibrary.Abstract;

namespace Messaging.Wpf;

public class MainViewModel : INotifyPropertyChanged
{
    private readonly IMessagingService _messagingService;
    private string _messageText = string.Empty;
    private string _clientName = Environment.UserName;
    private string _statusText = "Disconnected";
    private bool _isConnected;
    private string _selectedRecipient = "Everyone";
    private string _groupName = string.Empty;
    private bool _requiresAcknowledgment;
    private MessagePriority _selectedPriority = MessagePriority.Normal;
    private string _searchText = string.Empty;

    // Transport type
    private TransportType _selectedTransportType = TransportType.NamedPipe;

    // Transport settings (keeping existing ones)
    private string _pipeName = "GenericMessagingApp";
    private string _serverName = ".";
    private string _tcpHost = "localhost";
    private int _tcpPort = 8080;
    private string _udpHost = "localhost";
    private int _udpPort = 11000;
    private int _udpLocalPort;
    private string _webSocketHost = "localhost";
    private int _webSocketPort = 8080;
    private string _webSocketPath = "/";
    private bool _webSocketUseSSL;
    private string _signalRHost = "localhost";
    private int _signalRPort = 5003;
    private string _signalRHubPath = "/messagingHub";
    private bool _signalRUseHttps;
    private bool _signalREnableAutoReconnect = true;
    private string _grpcHost = "localhost";
    private int _grpcPort = 5002;
    private bool _grpcUseHttps;
    private string _rtpHost = "localhost";
    private int _rtpPort = 5004;
    private int _rtpLocalPort;
    private bool _rtpEnableMulticast;
    private string _rtpMulticastAddress = "224.1.1.1";

    public MainViewModel()
    {
        _messagingService = new MessagingService.MessagingService();
        _messagingService.MessageReceived += OnMessageReceived;
        _messagingService.Connected += OnConnected;
        _messagingService.Disconnected += OnDisconnected;
        _messagingService.ErrorOccurred += OnErrorOccurred;

        Messages = new ObservableCollection<MessageDisplayItem>();
        Connections = new ObservableCollection<ConnectionInfo>();
        OnlineClients = new ObservableCollection<ClientInfo>();
        Groups = new ObservableCollection<string>();
        FilteredMessages = new ObservableCollection<MessageDisplayItem>();

        TransportTypes = new ObservableCollection<TransportType>
        {
            TransportType.NamedPipe,
            TransportType.Tcp,
            TransportType.Udp,
            TransportType.WebSocket,
            TransportType.SignalR,
            TransportType.gRPC,
            TransportType.Rtp,
            TransportType.Mqtt
        };

        MessagePriorities = new ObservableCollection<MessagePriority>
        {
            MessagePriority.Low,
            MessagePriority.Normal,
            MessagePriority.High,
            MessagePriority.Critical
        };

        Recipients = new ObservableCollection<string> { "Everyone" };

        // Commands
        ConnectCommand = new RelayCommand(async () => await ConnectAsync(), () => !IsConnected);
        DisconnectCommand = new RelayCommand(async () => await DisconnectAsync(), () => IsConnected);
        SendMessageCommand = new RelayCommand(async () => await SendMessageAsync(), () => IsConnected && !string.IsNullOrWhiteSpace(MessageText));
        StartServerCommand = new RelayCommand(async () => await StartServerAsync(), () => !IsConnected);
        ClearMessagesCommand = new RelayCommand(() => ClearMessages());
        RefreshClientsCommand = new RelayCommand(async () => await RefreshOnlineClientsAsync(), () => IsConnected);
        CreateGroupCommand = new RelayCommand(async () => await CreateGroupAsync(), () => IsConnected && !string.IsNullOrWhiteSpace(GroupName));
        JoinGroupCommand = new RelayCommand(async () => await JoinGroupAsync(), () => IsConnected && !string.IsNullOrWhiteSpace(GroupName));
        LeaveGroupCommand = new RelayCommand(async () => await LeaveGroupAsync(), () => IsConnected && !string.IsNullOrWhiteSpace(GroupName));
        SendFileCommand = new RelayCommand(async () => await SendFileAsync(), () => IsConnected);
        SearchMessagesCommand = new RelayCommand(() => FilterMessages());

        // Initialize filtered messages
        FilterMessages();
    }

    #region Properties

    public ObservableCollection<MessageDisplayItem> Messages { get; }
    public ObservableCollection<MessageDisplayItem> FilteredMessages { get; }
    public ObservableCollection<ConnectionInfo> Connections { get; }
    public ObservableCollection<ClientInfo> OnlineClients { get; }
    public ObservableCollection<string> Groups { get; }
    public ObservableCollection<string> Recipients { get; }
    public ObservableCollection<TransportType> TransportTypes { get; }
    public ObservableCollection<MessagePriority> MessagePriorities { get; }

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
            UpdateCommandStates();
        }
    }

    public string SelectedRecipient
    {
        get => _selectedRecipient ?? "Everyone";
        set
        {
            _selectedRecipient = value;
            OnPropertyChanged();
        }
    }

    public string GroupName
    {
        get => _groupName;
        set
        {
            _groupName = value;
            OnPropertyChanged();
            ((RelayCommand)CreateGroupCommand).RaiseCanExecuteChanged();
            ((RelayCommand)JoinGroupCommand).RaiseCanExecuteChanged();
            ((RelayCommand)LeaveGroupCommand).RaiseCanExecuteChanged();
        }
    }

    public bool RequiresAcknowledgment
    {
        get => _requiresAcknowledgment;
        set
        {
            _requiresAcknowledgment = value;
            OnPropertyChanged();
        }
    }

    public MessagePriority SelectedPriority
    {
        get => _selectedPriority;
        set
        {
            _selectedPriority = value;
            OnPropertyChanged();
        }
    }

    public string SearchText
    {
        get => _searchText;
        set
        {
            _searchText = value;
            OnPropertyChanged();
            FilterMessages();
        }
    }

    public TransportType SelectedTransportType
    {
        get => _selectedTransportType;
        set
        {
            _selectedTransportType = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsNamedPipeSelected));
            OnPropertyChanged(nameof(IsTcpSelected));
            OnPropertyChanged(nameof(IsUdpSelected));
            OnPropertyChanged(nameof(IsWebSocketSelected));
            OnPropertyChanged(nameof(IsSignalRSelected));
            OnPropertyChanged(nameof(IsGrpcSelected));
            OnPropertyChanged(nameof(IsRtpSelected));
        }
    }

    // Transport configuration properties (keeping existing ones)
    public bool IsNamedPipeSelected => SelectedTransportType == TransportType.NamedPipe;
    public bool IsTcpSelected => SelectedTransportType == TransportType.Tcp;
    public bool IsUdpSelected => SelectedTransportType == TransportType.Udp;
    public bool IsWebSocketSelected => SelectedTransportType == TransportType.WebSocket;
    public bool IsSignalRSelected => SelectedTransportType == TransportType.SignalR;
    public bool IsGrpcSelected => SelectedTransportType == TransportType.gRPC;
    public bool IsRtpSelected => SelectedTransportType == TransportType.Rtp;

    public string PipeName { get => _pipeName; set { _pipeName = value; OnPropertyChanged(); } }
    public string ServerName { get => _serverName; set { _serverName = value; OnPropertyChanged(); } }
    public string TcpHost { get => _tcpHost; set { _tcpHost = value; OnPropertyChanged(); } }
    public int TcpPort { get => _tcpPort; set { _tcpPort = value; OnPropertyChanged(); } }
    public string UdpHost { get => _udpHost; set { _udpHost = value; OnPropertyChanged(); } }
    public int UdpPort { get => _udpPort; set { _udpPort = value; OnPropertyChanged(); } }
    public int UdpLocalPort { get => _udpLocalPort; set { _udpLocalPort = value; OnPropertyChanged(); } }
    public string WebSocketHost { get => _webSocketHost; set { _webSocketHost = value; OnPropertyChanged(); } }
    public int WebSocketPort { get => _webSocketPort; set { _webSocketPort = value; OnPropertyChanged(); } }
    public string WebSocketPath { get => _webSocketPath; set { _webSocketPath = value; OnPropertyChanged(); } }
    public bool WebSocketUseSSL { get => _webSocketUseSSL; set { _webSocketUseSSL = value; OnPropertyChanged(); } }
    public string SignalRHost { get => _signalRHost; set { _signalRHost = value; OnPropertyChanged(); } }
    public int SignalRPort { get => _signalRPort; set { _signalRPort = value; OnPropertyChanged(); } }
    public string SignalRHubPath { get => _signalRHubPath; set { _signalRHubPath = value; OnPropertyChanged(); } }
    public bool SignalRUseHttps { get => _signalRUseHttps; set { _signalRUseHttps = value; OnPropertyChanged(); } }
    public bool SignalREnableAutoReconnect { get => _signalREnableAutoReconnect; set { _signalREnableAutoReconnect = value; OnPropertyChanged(); } }
    public string GrpcHost { get => _grpcHost; set { _grpcHost = value; OnPropertyChanged(); } }
    public int GrpcPort { get => _grpcPort; set { _grpcPort = value; OnPropertyChanged(); } }
    public bool GrpcUseHttps { get => _grpcUseHttps; set { _grpcUseHttps = value; OnPropertyChanged(); } }
    public string RtpHost { get => _rtpHost; set { _rtpHost = value; OnPropertyChanged(); } }
    public int RtpPort { get => _rtpPort; set { _rtpPort = value; OnPropertyChanged(); } }
    public int RtpLocalPort { get => _rtpLocalPort; set { _rtpLocalPort = value; OnPropertyChanged(); } }
    public bool RtpEnableMulticast { get => _rtpEnableMulticast; set { _rtpEnableMulticast = value; OnPropertyChanged(); } }
    public string RtpMulticastAddress { get => _rtpMulticastAddress; set { _rtpMulticastAddress = value; OnPropertyChanged(); } }

    #endregion

    #region Commands

    public ICommand ConnectCommand { get; }
    public ICommand DisconnectCommand { get; }
    public ICommand SendMessageCommand { get; }
    public ICommand StartServerCommand { get; }
    public ICommand ClearMessagesCommand { get; }
    public ICommand RefreshClientsCommand { get; }
    public ICommand CreateGroupCommand { get; }
    public ICommand JoinGroupCommand { get; }
    public ICommand LeaveGroupCommand { get; }
    public ICommand SendFileCommand { get; }
    public ICommand SearchMessagesCommand { get; }

    #endregion

    #region Connection Methods

    private async Task ConnectAsync()
    {
        StatusText = "Connecting...";

        try
        {
            IMessageClient client = SelectedTransportType switch
            {
                TransportType.NamedPipe => new NamedPipeClient(),
                TransportType.Tcp => new TcpMessageClient(),
                TransportType.Udp => new UdpMessageClient(),
                TransportType.WebSocket => new WebSocketMessageClient(),
                TransportType.SignalR => new SignalRClient(),
                TransportType.gRPC => new GrpcClient(),
                TransportType.Rtp => new RtpClient(),
                TransportType.Mqtt => new MqttMessageClient(),
                _ => throw new ArgumentException($"Unknown transport type: {SelectedTransportType}")
            };

            var configuration = GetClientConfiguration();
            var success = await _messagingService.ConnectAsClientAsync(client, configuration);

            if (success)
            {
                // Add debug message
                AddMessageToDisplay(new Message
                {
                    Content = $"✅ Connected as client using {SelectedTransportType}. Mode: {_messagingService.Mode}",
                    Sender = "System",
                    Type = MessageType.System,
                    Timestamp = DateTime.UtcNow
                });

                // Start periodic refresh of online clients
                _ = Task.Run(async () =>
                {
                    while (IsConnected)
                    {
                        await RefreshOnlineClientsAsync();
                        await Task.Delay(5000); // Refresh every 5 seconds
                    }
                });
            }
            else
            {
                IsConnected = false;
                StatusText = "Connection failed";

                AddMessageToDisplay(new Message
                {
                    Content = "❌ Connection failed",
                    Sender = "System",
                    Type = MessageType.Error,
                    Timestamp = DateTime.UtcNow
                });
            }
        }
        catch (Exception ex)
        {
            IsConnected = false;
            StatusText = $"Connection error: {ex.Message}";

            AddMessageToDisplay(new Message
            {
                Content = $"❌ Connection error: {ex.Message}",
                Sender = "System",
                Type = MessageType.Error,
                Timestamp = DateTime.UtcNow
            });
        }
    }

    private async Task StartServerAsync()
    {
        StatusText = "Starting server...";

        try
        {
            IMessageTransport transport = SelectedTransportType switch
            {
                TransportType.NamedPipe => new NamedPipeTransport(PipeName),
                TransportType.Tcp => new TcpTransport(),
                TransportType.Udp => new UdpTransport(),
                TransportType.WebSocket => new WebSocketTransport(),
                TransportType.SignalR => new SignalRTransport(SignalRHost, SignalRPort, SignalRHubPath),
                TransportType.gRPC => new GrpcTransport(GrpcHost, GrpcPort),
                TransportType.Rtp => new RtpTransport(RtpPort, System.Net.IPAddress.Parse(RtpHost)),
                TransportType.Mqtt => new MqttTransport(),
                _ => throw new ArgumentException($"Unknown transport type: {SelectedTransportType}")
            };

            var configuration = GetServerConfiguration();
            var success = await _messagingService.StartServerAsync(transport, configuration);

            if (success)
            {
                IsConnected = true;
                StatusText = GetServerStatusText();

                AddMessageToDisplay(new Message
                {
                    Content = $"🖥️ Server started using {SelectedTransportType}. Mode: {_messagingService.Mode}",
                    Sender = "System",
                    Type = MessageType.System,
                    Timestamp = DateTime.UtcNow
                });
            }
            else
            {
                IsConnected = false;
                StatusText = "Failed to start server";

                AddMessageToDisplay(new Message
                {
                    Content = "❌ Failed to start server",
                    Sender = "System",
                    Type = MessageType.Error,
                    Timestamp = DateTime.UtcNow
                });
            }
        }
        catch (Exception ex)
        {
            IsConnected = false;
            StatusText = $"Server start error: {ex.Message}";

            AddMessageToDisplay(new Message
            {
                Content = $"❌ Server start error: {ex.Message}",
                Sender = "System",
                Type = MessageType.Error,
                Timestamp = DateTime.UtcNow
            });
        }
    }

    private async Task DisconnectAsync()
    {
        StatusText = "Disconnecting...";
        try
        {
            await _messagingService.StopAsync();
        }
        catch (Exception ex)
        {
            StatusText = $"Disconnect error: {ex.Message}";
            AddMessageToDisplay(new Message
            {
                Content = $"⚠️ Disconnect error: {ex.Message}",
                Sender = "System",
                Type = MessageType.Error,
                Timestamp = DateTime.UtcNow
            });
        }
        finally
        {
            IsConnected = false;
            StatusText = "Disconnected";
            Connections.Clear();
            OnlineClients.Clear();
            Groups.Clear();
            Recipients.Clear();
            Recipients.Add("Everyone");

            AddMessageToDisplay(new Message
            {
                Content = "🔌 Disconnected",
                Sender = "System",
                Type = MessageType.System,
                Timestamp = DateTime.UtcNow
            });
        }
    }

    #endregion

    #region Messaging Methods

    private async Task SendMessageAsync()
    {
        if (string.IsNullOrWhiteSpace(MessageText)) return;

        try
        {
            // Add debug information
            var debugInfo = $"Sending message. Mode: {_messagingService.Mode}, To: {SelectedRecipient}";
            System.Diagnostics.Debug.WriteLine(debugInfo);

            var message = new Message
            {
                Content = MessageText,
                Sender = ClientName,
                Type = MessageType.Text,
                Priority = SelectedPriority,
                RequiresAcknowledgment = RequiresAcknowledgment,
                Metadata = new Dictionary<string, object>
                {
                    ["ClientVersion"] = "WPF-Enhanced-1.0",
                    ["SentFrom"] = "WPF Client",
                    ["Mode"] = _messagingService.Mode.ToString()
                }
            };

            bool success;
            if (SelectedRecipient == "Everyone")
            {
                System.Diagnostics.Debug.WriteLine("Sending broadcast message");
                success = await _messagingService.BroadcastMessageAsync(MessageText, MessageType.Text);
            }
            else if (SelectedRecipient.StartsWith("Group: "))
            {
                var groupName = SelectedRecipient.Substring(7);
                System.Diagnostics.Debug.WriteLine($"Sending group message to: {groupName}");
                success = await _messagingService.SendGroupMessageAsync(groupName, MessageText);
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"Sending direct message to: {SelectedRecipient}");
                success = await _messagingService.SendDirectMessageAsync(SelectedRecipient, MessageText, MessageType.Text);
            }

            if (success)
            {
                MessageText = string.Empty;
                AddMessageToDisplay(message, true);
                System.Diagnostics.Debug.WriteLine("Message sent successfully");
            }
            else
            {
                StatusText = "Failed to send message";
                System.Diagnostics.Debug.WriteLine("Message send failed");

                AddMessageToDisplay(new Message
                {
                    Content = $"❌ Failed to send message to {SelectedRecipient}",
                    Sender = "System",
                    Type = MessageType.Error,
                    Timestamp = DateTime.UtcNow
                });
            }
        }
        catch (Exception ex)
        {
            StatusText = $"Send error: {ex.Message}";
            System.Diagnostics.Debug.WriteLine($"Send error: {ex.Message}");

            AddMessageToDisplay(new Message
            {
                Content = $"❌ Send error: {ex.Message}",
                Sender = "System",
                Type = MessageType.Error,
                Timestamp = DateTime.UtcNow
            });
        }
    }

    private async Task SendFileAsync()
    {
        try
        {
            var openFileDialog = new OpenFileDialog
            {
                Title = "Select file to send",
                Filter = "All files (*.*)|*.*"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                var filePath = openFileDialog.FileName;
                var fileName = Path.GetFileName(filePath);
                var fileData = await File.ReadAllBytesAsync(filePath);

                // For file sending, we'll encode as base64 in message content
                var message = new Message
                {
                    Content = Convert.ToBase64String(fileData),
                    Sender = ClientName,
                    Receiver = SelectedRecipient == "Everyone" ? null : SelectedRecipient,
                    Type = MessageType.Text,
                    Metadata = new Dictionary<string, object>
                    {
                        ["IsFile"] = true,
                        ["FileName"] = fileName,
                        ["MimeType"] = GetMimeType(fileName),
                        ["FileSize"] = fileData.Length
                    }
                };

                var success = await _messagingService.SendMessageAsync(message);
                if (success)
                {
                    AddMessageToDisplay(new Message
                    {
                        Content = $"📎 Sent file: {fileName} ({FormatFileSize(fileData.Length)})",
                        Sender = ClientName,
                        Type = MessageType.Text,
                        Timestamp = DateTime.UtcNow
                    }, true);
                }
                else
                {
                    AddMessageToDisplay(new Message
                    {
                        Content = $"❌ Failed to send file: {fileName}",
                        Sender = "System",
                        Type = MessageType.Error,
                        Timestamp = DateTime.UtcNow
                    });
                }
            }
        }
        catch (Exception ex)
        {
            StatusText = $"File send error: {ex.Message}";
            AddMessageToDisplay(new Message
            {
                Content = $"❌ File send error: {ex.Message}",
                Sender = "System",
                Type = MessageType.Error,
                Timestamp = DateTime.UtcNow
            });
        }
    }

    #endregion

    #region Group Management

    private async Task CreateGroupAsync()
    {
        if (string.IsNullOrWhiteSpace(GroupName)) return;

        try
        {
            var success = await _messagingService.CreateGroupAsync(GroupName);
            if (success)
            {
                Groups.Add(GroupName);
                Recipients.Add($"Group: {GroupName}");
                StatusText = $"Group '{GroupName}' created successfully";
                GroupName = string.Empty;

                AddMessageToDisplay(new Message
                {
                    Content = $"👥 Created group: {GroupName}",
                    Sender = "System",
                    Type = MessageType.System,
                    Timestamp = DateTime.UtcNow
                });
            }
            else
            {
                StatusText = $"Failed to create group '{GroupName}'";
                AddMessageToDisplay(new Message
                {
                    Content = $"❌ Failed to create group: {GroupName}",
                    Sender = "System",
                    Type = MessageType.Error,
                    Timestamp = DateTime.UtcNow
                });
            }
        }
        catch (Exception ex)
        {
            StatusText = $"Group creation error: {ex.Message}";
            AddMessageToDisplay(new Message
            {
                Content = $"❌ Group creation error: {ex.Message}",
                Sender = "System",
                Type = MessageType.Error,
                Timestamp = DateTime.UtcNow
            });
        }
    }

    private async Task JoinGroupAsync()
    {
        if (string.IsNullOrWhiteSpace(GroupName)) return;

        try
        {
            var success = await _messagingService.JoinGroupAsync(GroupName);
            if (success)
            {
                if (!Groups.Contains(GroupName))
                    Groups.Add(GroupName);
                if (!Recipients.Contains($"Group: {GroupName}"))
                    Recipients.Add($"Group: {GroupName}");
                StatusText = $"Joined group '{GroupName}' successfully";
                GroupName = string.Empty;

                AddMessageToDisplay(new Message
                {
                    Content = $"📥 Joined group: {GroupName}",
                    Sender = "System",
                    Type = MessageType.System,
                    Timestamp = DateTime.UtcNow
                });
            }
            else
            {
                StatusText = $"Failed to join group '{GroupName}'";
                AddMessageToDisplay(new Message
                {
                    Content = $"❌ Failed to join group: {GroupName}",
                    Sender = "System",
                    Type = MessageType.Error,
                    Timestamp = DateTime.UtcNow
                });
            }
        }
        catch (Exception ex)
        {
            StatusText = $"Group join error: {ex.Message}";
            AddMessageToDisplay(new Message
            {
                Content = $"❌ Group join error: {ex.Message}",
                Sender = "System",
                Type = MessageType.Error,
                Timestamp = DateTime.UtcNow
            });
        }
    }

    private async Task LeaveGroupAsync()
    {
        if (string.IsNullOrWhiteSpace(GroupName)) return;

        try
        {
            var success = await _messagingService.LeaveGroupAsync(GroupName);
            if (success)
            {
                Groups.Remove(GroupName);
                Recipients.Remove($"Group: {GroupName}");
                StatusText = $"Left group '{GroupName}' successfully";
                GroupName = string.Empty;

                AddMessageToDisplay(new Message
                {
                    Content = $"📤 Left group: {GroupName}",
                    Sender = "System",
                    Type = MessageType.System,
                    Timestamp = DateTime.UtcNow
                });
            }
            else
            {
                StatusText = $"Failed to leave group '{GroupName}'";
                AddMessageToDisplay(new Message
                {
                    Content = $"❌ Failed to leave group: {GroupName}",
                    Sender = "System",
                    Type = MessageType.Error,
                    Timestamp = DateTime.UtcNow
                });
            }
        }
        catch (Exception ex)
        {
            StatusText = $"Group leave error: {ex.Message}";
            AddMessageToDisplay(new Message
            {
                Content = $"❌ Group leave error: {ex.Message}",
                Sender = "System",
                Type = MessageType.Error,
                Timestamp = DateTime.UtcNow
            });
        }
    }

    #endregion

    #region Client Discovery

    private async Task RefreshOnlineClientsAsync()
    {
        try
        {
            var clients = await _messagingService.GetOnlineClientsAsync();

            Application.Current.Dispatcher.Invoke(() =>
            {
                OnlineClients.Clear();
                Recipients.Clear();
                Recipients.Add("Everyone");

                foreach (var client in clients.Where(c => c.Name != ClientName))
                {
                    OnlineClients.Add(client);
                    if (!Recipients.Contains(client.Name))
                        Recipients.Add(client.Name);
                }

                // Re-add groups
                foreach (var group in Groups)
                {
                    if (!Recipients.Contains($"Group: {group}"))
                        Recipients.Add($"Group: {group}");
                }
            });
        }
        catch (Exception ex)
        {
            // Silently handle refresh errors to avoid spamming the UI
            System.Diagnostics.Debug.WriteLine($"Client refresh error: {ex.Message}");
        }
    }

    #endregion

    #region Message Display

    private void AddMessageToDisplay(Message message, bool isSent = false)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            var displayItem = new MessageDisplayItem
            {
                Message = message,
                IsSent = isSent,
                IsFile = message.Metadata.ContainsKey("IsFile") && (bool)message.Metadata["IsFile"],
                FormattedContent = FormatMessageContent(message)
            };

            Messages.Add(displayItem);
            FilterMessages();
        });
    }

    private string FormatMessageContent(Message message)
    {
        if (message.Metadata.ContainsKey("IsFile") && (bool)message.Metadata["IsFile"])
        {
            var fileName = message.Metadata.GetValueOrDefault("FileName", "Unknown file").ToString();
            var fileSize = message.Metadata.ContainsKey("FileSize")
                ? FormatFileSize((int)message.Metadata["FileSize"])
                : "Unknown size";
            return $"📎 File: {fileName} ({fileSize})";
        }

        return message.Content;
    }

    private void FilterMessages()
    {
        FilteredMessages.Clear();

        var filtered = Messages.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            filtered = filtered.Where(m =>
                m.Message.Content.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
                m.Message.Sender.Contains(SearchText, StringComparison.OrdinalIgnoreCase));
        }

        foreach (var item in filtered.TakeLast(1000)) // Limit to last 1000 messages
        {
            FilteredMessages.Add(item);
        }
    }

    private void ClearMessages()
    {
        Messages.Clear();
        FilteredMessages.Clear();
    }

    #endregion

    #region Event Handlers

    private void OnMessageReceived(object? sender, MessageEventArgs e)
    {
        // Handle file messages
        if (e.Message.Metadata.ContainsKey("IsFile") && (bool)e.Message.Metadata["IsFile"])
        {
            HandleFileMessage(e.Message);
        }
        else
        {
            AddMessageToDisplay(e.Message);
        }
    }

    private void HandleFileMessage(Message message)
    {
        try
        {
            var fileName = message.Metadata.GetValueOrDefault("FileName", "Unknown file").ToString();
            var fileSize = message.Metadata.ContainsKey("FileSize")
                ? (int)message.Metadata["FileSize"]
                : 0;

            // Create a display message for the file
            var displayMessage = new Message
            {
                Content = $"📎 Received file: {fileName} ({FormatFileSize(fileSize)}) - Click to save",
                Sender = message.Sender,
                Type = MessageType.Text,
                Timestamp = message.Timestamp,
                Metadata = message.Metadata
            };

            AddMessageToDisplay(displayMessage);
        }
        catch (Exception ex)
        {
            StatusText = $"File receive error: {ex.Message}";
        }
    }

    private void OnConnected(object sender, ConnectionEventArgs e)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            IsConnected = true;
            StatusText = GetConnectedStatusText();
            if (e.Connection != null && !Connections.Contains(e.Connection))
            {
                Connections.Add(e.Connection);
            }
        });
    }

    private void OnDisconnected(object sender, ConnectionEventArgs e)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            if (e.Connection != null && Connections.Contains(e.Connection))
            {
                Connections.Remove(e.Connection);
            }

            if (_messagingService.Mode == MessagingMode.None || Connections.Count == 0)
            {
                IsConnected = false;
                StatusText = "Disconnected";
                OnlineClients.Clear();
                Groups.Clear();
                Recipients.Clear();
                Recipients.Add("Everyone");
            }
        });
    }

    private void OnErrorOccurred(object sender, ErrorEventArgs e)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            AddMessageToDisplay(new Message
            {
                Content = $"ERROR: {e.Error}",
                Sender = "System",
                Type = MessageType.Error,
                Timestamp = DateTime.UtcNow
            });
        });
    }

    #endregion

    #region Helper Methods

    private Dictionary<string, object> GetClientConfiguration()
    {
        return SelectedTransportType switch
        {
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
            TransportType.SignalR => new Dictionary<string, object>
            {
                ["ServerUrl"] = $"{(SignalRUseHttps ? "https" : "http")}://{SignalRHost}:{SignalRPort}{SignalRHubPath}",
                ["ClientName"] = ClientName,
                ["EnableAutoReconnect"] = SignalREnableAutoReconnect,
                ["Transport"] = "WebSockets"
            },
            TransportType.gRPC => new Dictionary<string, object>
            {
                ["ServerAddress"] = $"{(GrpcUseHttps ? "https" : "http")}://{GrpcHost}:{GrpcPort}",
                ["ClientName"] = ClientName,
                ["MaxReceiveMessageSize"] = 4 * 1024 * 1024,
                ["DisableCertificateValidation"] = !GrpcUseHttps
            },
            TransportType.Rtp => new Dictionary<string, object>
            {
                ["ServerAddress"] = RtpHost,
                ["ServerPort"] = RtpPort,
                ["LocalPort"] = RtpLocalPort,
                ["ClientName"] = ClientName
            },
            TransportType.Mqtt => new Dictionary<string, object>
            {
                ["Host"] = TcpHost,
                ["Port"] = TcpPort,
                ["ClientId"] = ClientName,
                ["Timeout"] = 5000
            },
            _ => new Dictionary<string, object>()
        };
    }

    private Dictionary<string, object> GetServerConfiguration()
    {
        return SelectedTransportType switch
        {
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
            TransportType.SignalR => new Dictionary<string, object>
            {
                ["Address"] = SignalRHost,
                ["Port"] = SignalRPort,
                ["HubPath"] = SignalRHubPath,
                ["EnableHttps"] = SignalRUseHttps,
                ["EnableCors"] = true,
                ["CorsOrigins"] = new[] { "*" },
                ["EnableDetailedErrors"] = true
            },
            TransportType.gRPC => new Dictionary<string, object>
            {
                ["Address"] = GrpcHost,
                ["Port"] = GrpcPort,
                ["EnableHttps"] = GrpcUseHttps
            },
            TransportType.Rtp => new Dictionary<string, object>
            {
                ["Port"] = RtpPort,
                ["BindAddress"] = System.Net.IPAddress.Parse(RtpHost),
                ["EnableMulticast"] = RtpEnableMulticast,
                ["MulticastAddress"] = RtpMulticastAddress
            },
            TransportType.Mqtt => new Dictionary<string, object>
            {
                ["Host"] = TcpHost,
                ["Port"] = TcpPort,
                ["ClientId"] = ClientName,
                ["EnableTls"] = false,
                ["AutoReconnectDelay"] = TimeSpan.FromSeconds(5)
            },
            _ => new Dictionary<string, object>()
        };
    }

    private string GetConnectedStatusText()
    {
        return _messagingService.Mode == MessagingMode.Server
            ? GetServerStatusText() + $" - {Connections.Count} client(s)"
            : $"Connected as client - {OnlineClients.Count} other client(s) online";
    }

    private string GetServerStatusText()
    {
        return SelectedTransportType switch
        {
            TransportType.SignalR => $"SignalR server on {(SignalRUseHttps ? "https" : "http")}://{SignalRHost}:{SignalRPort}{SignalRHubPath}",
            TransportType.gRPC => $"gRPC server on {(GrpcUseHttps ? "https" : "http")}://{GrpcHost}:{GrpcPort}",
            TransportType.Rtp => $"RTP server on {RtpHost}:{RtpPort}" + (RtpEnableMulticast ? $" (MC: {RtpMulticastAddress})" : ""),
            _ => $"Server running on {SelectedTransportType}"
        };
    }

    private void UpdateCommandStates()
    {
        ((RelayCommand)ConnectCommand).RaiseCanExecuteChanged();
        ((RelayCommand)DisconnectCommand).RaiseCanExecuteChanged();
        ((RelayCommand)SendMessageCommand).RaiseCanExecuteChanged();
        ((RelayCommand)StartServerCommand).RaiseCanExecuteChanged();
        ((RelayCommand)RefreshClientsCommand).RaiseCanExecuteChanged();
        ((RelayCommand)CreateGroupCommand).RaiseCanExecuteChanged();
        ((RelayCommand)JoinGroupCommand).RaiseCanExecuteChanged();
        ((RelayCommand)LeaveGroupCommand).RaiseCanExecuteChanged();
        ((RelayCommand)SendFileCommand).RaiseCanExecuteChanged();
    }

    private string GetMimeType(string fileName)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        return extension switch
        {
            ".txt" => "text/plain",
            ".pdf" => "application/pdf",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".gif" => "image/gif",
            ".zip" => "application/zip",
            ".doc" => "application/msword",
            ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            _ => "application/octet-stream"
        };
    }

    private string FormatFileSize(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB" };
        double len = bytes;
        int order = 0;
        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len = len / 1024;
        }
        return $"{len:0.##} {sizes[order]}";
    }

    #endregion

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

// Helper class for message display
public class MessageDisplayItem
{
    public Message Message { get; set; } = new();
    public bool IsSent { get; set; }
    public bool IsFile { get; set; }
    public string FormattedContent { get; set; } = string.Empty;
}