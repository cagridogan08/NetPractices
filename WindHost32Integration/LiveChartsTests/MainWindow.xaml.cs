using LiveCharts.Wpf;
using LiveCharts;
using System.ComponentModel;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System;
using System.Data;
using System.Windows.Threading;
using ScottPlot;
using ScottPlot.Plottables;

namespace LiveChartsTests
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow
    {
        public MainWindow()
        {
            InitializeComponent();
            DateTime[] xs = Generate.ConsecutiveHours(100);
            double[] ys = Generate.RandomWalk(100);
            LinePlot.Plot.Add.Scatter(xs, ys);
            // setup the bottom axis to use DateTime ticks
            var axis = LinePlot.Plot.Axes.DateTimeTicksBottom();

            // create a custom formatter to return a string with
            // date only when zoomed out and time only when zoomed in
            static string CustomFormatter(DateTime dt)
            {
                bool isMidnight = dt is { Hour: 0, Minute: 0, Second: 0 };
                return isMidnight
                    ? DateOnly.FromDateTime(dt).ToString()
                    : TimeOnly.FromDateTime(dt).ToString();
            }

            // apply our custom tick formatter
            var tickGen = (ScottPlot.TickGenerators.DateTimeAutomatic)axis.TickGenerator;
            tickGen.LabelFormatter = CustomFormatter;

            LinePlot.Refresh();

        }
    }
}