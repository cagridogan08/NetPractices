using MessagingApp.WPF.ViewModels;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using Messaging.ModelLibrary;

namespace Messaging.Wpf
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
        }

        private void MessageTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && !Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
            {
                e.Handled = true;
                var viewModel = DataContext as MainViewModel;
                if (viewModel?.SendMessageCommand.CanExecute(null) == true)
                {
                    viewModel.SendMessageCommand.Execute(null);
                }
            }
        }
    }

    public class MainViewModel : INotifyPropertyChanged
    {
        private readonly IMessagingService _messagingService;
        private string _messageText = string.Empty;
        private string _clientName = Environment.UserName;
        private string _statusText = "Disconnected";
        private bool _isConnected;
        private string _pipeName = "GenericMessagingApp";

        public MainViewModel()
        {
            _messagingService = new MessagingService();
            _messagingService.MessageReceived += OnMessageReceived;
            _messagingService.Connected += OnConnected;
            _messagingService.Disconnected += OnDisconnected;
            _messagingService.ErrorOccurred += OnErrorOccurred;

            Messages = new ObservableCollection<Message>();
            Connections = new ObservableCollection<ConnectionInfo>();

            ConnectCommand = new RelayCommand(async () => await ConnectAsync(), () => !IsConnected);
            DisconnectCommand = new RelayCommand(async () => await DisconnectAsync(), () => IsConnected);
            SendMessageCommand = new RelayCommand(async () => await SendMessageAsync(), () => IsConnected && !string.IsNullOrWhiteSpace(MessageText));
            StartServerCommand = new RelayCommand(async () => await StartServerAsync(), () => !IsConnected);
            ClearMessagesCommand = new RelayCommand(() => Messages.Clear());
        }

        public ObservableCollection<Message> Messages { get; }
        public ObservableCollection<ConnectionInfo> Connections { get; }

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

        public string PipeName
        {
            get => _pipeName;
            set
            {
                _pipeName = value;
                OnPropertyChanged();
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

        public ICommand ConnectCommand { get; }
        public ICommand DisconnectCommand { get; }
        public ICommand SendMessageCommand { get; }
        public ICommand StartServerCommand { get; }
        public ICommand ClearMessagesCommand { get; }

        private async Task ConnectAsync()
        {
            StatusText = "Connecting...";

            var client = new NamedPipeClient();
            var configuration = new Dictionary<string, object>
            {
                ["ServerName"] = ".",
                ["PipeName"] = PipeName,
                ["Timeout"] = 5000,
                ["ClientName"] = ClientName
            };

            var success = await _messagingService.ConnectAsClientAsync(client, configuration);
            if (!success)
            {
                StatusText = "Connection failed";
            }
        }

        private async Task DisconnectAsync()
        {
            StatusText = "Disconnecting...";
            await _messagingService.StopAsync();
        }

        private async Task SendMessageAsync()
        {
            if (!string.IsNullOrWhiteSpace(MessageText))
            {
                await _messagingService.SendMessageAsync(MessageText);
                MessageText = string.Empty;
            }
        }

        private async Task StartServerAsync()
        {
            StatusText = "Starting server...";

            var transport = new NamedPipeTransport(PipeName);
            var configuration = new Dictionary<string, object>
            {
                ["PipeName"] = PipeName
            };

            var success = await _messagingService.StartServerAsync(transport, configuration);
            if (success)
            {
                StatusText = "Server started, waiting for connections...";
            }
            else
            {
                StatusText = "Failed to start server";
            }
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
                StatusText = _messagingService.Mode == MessagingMode.Server ?
                    "Client connected" : "Connected to server";

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
                if (_messagingService.Mode == MessagingMode.None)
                {
                    IsConnected = false;
                    StatusText = "Disconnected";
                    Connections.Clear();
                }

                if (e.Connection != null && Connections.Contains(e.Connection))
                {
                    Connections.Remove(e.Connection);
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
}


namespace MessagingApp.WPF.ViewModels
{
    public class RelayCommand : ICommand
    {
        private readonly Action _execute;
        private readonly Func<bool> _canExecute;

        public RelayCommand(Action execute, Func<bool> canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        public bool CanExecute(object parameter) => _canExecute?.Invoke() ?? true;

        public void Execute(object parameter) => _execute();

        public event EventHandler CanExecuteChanged;

        public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }
}
