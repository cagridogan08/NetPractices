using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.ServiceModel;
using System.Text;
using System.Threading.Tasks;
using ModelLibrary;
using ModelLibrary.Models;

namespace SoapClinentApp.ViewModels;

internal class UsersViewModel : BaseViewModel
{
    private readonly IUserService _userService;

    public UsersViewModel()
    {
        var binding = new BasicHttpBinding
        {
            Security =
            {
                Mode = BasicHttpSecurityMode.None // No security for local testing
            }
        };
        var endpoint = new EndpointAddress("http://localhost:5273/UserService.asmx");

        var factory = new ChannelFactory<IUserService>(binding, endpoint);

        // Create a proxy to communicate with the SOAP service
        _userService = factory.CreateChannel();
        _users = new(_userService.GetUsers());
    }

    #region Properties

    private ObservableCollection<User> _users;

    public ObservableCollection<User> Users
    {
        get => _users;
        set => SetField(ref _users, value);
    }

    #endregion
}