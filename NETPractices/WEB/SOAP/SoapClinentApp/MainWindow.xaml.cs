using System.Windows.Controls;
using System.Windows.Data;
using SoapClinentApp.ViewModels;

namespace SoapClinentApp;
/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = new CalculatorViewModel();
        UsersListView.SetBinding(ItemsControl.ItemsSourceProperty, new Binding("Users")
        {
            Source = new UsersViewModel()
        });
    }


}
