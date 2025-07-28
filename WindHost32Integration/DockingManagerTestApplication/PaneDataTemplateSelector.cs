using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace DockingManagerTestApplication
{
    public class PaneDataTemplateSelector : DataTemplateSelector
    {
        public override DataTemplate SelectTemplate(object item, DependencyObject container)
        {
            if (container is FrameworkElement element)
            {
                return item switch
                {
                    SolutionExplorerViewModel => element.FindResource("SolutionExplorerTemplate") as DataTemplate,
                    PropertiesViewModel => element.FindResource("PropertiesTemplate") as DataTemplate,
                    OutputViewModel => element.FindResource("OutputTemplate") as DataTemplate,
                    CodeEditorViewModel => element.FindResource("CodeEditorTemplate") as DataTemplate,
                    _ => base.SelectTemplate(item, container)
                };
            }
            return base.SelectTemplate(item, container);
        }
    }
    public class PaneStyleSelector : StyleSelector
    {
        public Style DocumentStyle { get; set; }
        public Style ToolStyle { get; set; }

        public override Style SelectStyle(object item, DependencyObject container)
        {
            return item switch
            {
                CodeEditorViewModel => DocumentStyle,
                ToolViewModel => ToolStyle,
                _ => base.SelectStyle(item, container)
            };
        }
    }
}
