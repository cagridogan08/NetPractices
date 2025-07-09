
using System.Net.Http;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;

namespace LinkDatabaseTest.VM
{
    internal class MainViewModel:BaseViewModel
    {
        private string _ipAddress="0.0.0.0";
        private string _password = "Iotech+2015";
        private string _userName = "operator";

        public string IpAddress
        {
            get => _ipAddress;
            set => SetField(ref _ipAddress, value);
        }

        public string UserName
        {
            get => _userName;
            set => SetField(ref _userName, value);
        }

        public string Password
        {
            get => _password;
            set => SetField(ref _password, value);
        }

        //public LoginResponse

        public ICommand ConnectCommand => new AsyncRelayCommand(async () =>
        {
            var client = new HttpClient(new HttpClientHandler()
            {
                ServerCertificateCustomValidationCallback =
                    HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
            });
        });
    }
}
