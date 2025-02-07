using System.Diagnostics;
using System.Xml.Linq;
using ModelLibrary;

namespace Api.Services;

public class CalculatorService : ICalculatorService
{
    public void DoWork(XElement xml)
    {
        Trace.WriteLine(xml.ToString());
    }

    public int Add(int a, int b) => a + b;

    public int Subtract(int a, int b) => a - b;

    public int Multiply(int a, int b) => a * b;
    public double Divide(int a, int b) => a / b;
}