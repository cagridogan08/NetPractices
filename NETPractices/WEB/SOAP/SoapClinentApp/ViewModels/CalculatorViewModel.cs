using System.ServiceModel;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using ModelLibrary;

namespace SoapClinentApp.ViewModels;

internal class CalculatorViewModel : BaseViewModel
{

    private int _firstNumber;
    private int _secondNumber;
    private double _result;
    public int FirstNumber
    {
        get => _firstNumber;
        set => SetField(ref _firstNumber, value);
    }
    public int SecondNumber
    {
        get => _secondNumber;
        set => SetField(ref _secondNumber, value);
    }
    public double Result
    {
        get => _result;
        set => SetField(ref _result, value);
    }

    #region Commands

    private readonly ICalculatorService _calculatorService;
    public CalculatorViewModel()
    {
        var binding = new BasicHttpBinding
        {
            Security =
            {
                Mode = BasicHttpSecurityMode.None // No security for local testing
            }
        };
        var endpoint = new EndpointAddress("http://localhost:5273/CalculatorService.asmx");

        var factory = new ChannelFactory<ICalculatorService>(binding, endpoint);

        // Create a proxy to communicate with the SOAP service
        _calculatorService = factory.CreateChannel();
    }
    public ICommand AddCommand => new RelayCommand(() =>
    {
        Result = _calculatorService.Add(FirstNumber, SecondNumber);
    });

    public ICommand SubstractCommand => new RelayCommand(() => Result = _calculatorService.Subtract(FirstNumber, SecondNumber));

    public ICommand MultiplyCommand => new RelayCommand(() => Result = _calculatorService.Multiply(FirstNumber, SecondNumber));

    public ICommand DivideCommand => new RelayCommand(() => Result = _calculatorService.Divide(FirstNumber, SecondNumber));

    #endregion
}