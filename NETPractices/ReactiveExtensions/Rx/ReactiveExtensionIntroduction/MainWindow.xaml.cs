using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using DynamicData;

namespace ReactiveExtensionIntroduction;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    private readonly IObservable<EventPattern<RoutedEventArgs>> _buttonClick;

    CompositeDisposable _disposable = new CompositeDisposable();

    public MainWindow()
    {
        InitializeComponent();
        _buttonClick = Observable.FromEventPattern<RoutedEventHandler, RoutedEventArgs>(
            h => Btn.Click += h,
            h => Btn.Click -= h);

        _buttonClick.Subscribe(c =>
        {
            Console.WriteLine("Button Clicked");
        });
        _disposable.Add(_buttonClick.Subscribe(c =>
        {
            Console.WriteLine("Button Clicked");
        }));

        var sourceList = new SourceList<int>();

        var observable = sourceList.Connect().Subscribe(
            changes =>
            {

                Console.WriteLine("Collection changed");
            });

        sourceList.Add(1);
        sourceList.Add(2);
        sourceList.Remove(1);
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        _disposable.Dispose();
    }
}