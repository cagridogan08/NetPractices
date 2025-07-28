using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DockingManagerTestApplication
{
    public class PropertiesViewModel : ToolViewModel
    {
        public override string Name => "Properties";

        public ObservableCollection<PropertyItem> Properties { get; } = new ObservableCollection<PropertyItem>();

        public void UpdateSelectedItem(object selectedItem)
        {
            Properties.Clear();

            if (selectedItem is CodeEditorViewModel doc)
            {
                Properties.Add(new PropertyItem { Name = "Title", Value = doc.Title });
                Properties.Add(new PropertyItem { Name = "File Path", Value = doc.FilePath ?? "Not saved" });
                Properties.Add(new PropertyItem { Name = "Is Dirty", Value = doc.IsDirty.ToString() });
                Properties.Add(new PropertyItem { Name = "Content Length", Value = doc.Content?.Length.ToString() ?? "0" });
            }
        }
    }

    public class PropertyItem
    {
        public string Name { get; set; }
        public string Value { get; set; }
    }
}
