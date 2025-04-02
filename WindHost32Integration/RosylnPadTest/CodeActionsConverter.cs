using Microsoft.CodeAnalysis.CodeActions;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Data;
using System.Windows.Markup;
using RoslynPad.Roslyn.CodeActions;

namespace RosylnPadTest
{
    internal sealed class CodeActionsConverter : MarkupExtension, IValueConverter
    {
        public override object ProvideValue(IServiceProvider serviceProvider) => this;

        public object? Convert(object value, Type targetType, object? parameter, CultureInfo culture) =>
            (value as CodeAction).GetCodeActions();

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }
}
