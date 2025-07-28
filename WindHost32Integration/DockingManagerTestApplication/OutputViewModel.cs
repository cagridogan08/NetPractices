using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DockingManagerTestApplication
{
    public class OutputViewModel : ToolViewModel
    {
        public override string Name => "Output";

        public ObservableCollection<string> Messages { get; } = new ObservableCollection<string>();

        public OutputViewModel()
        {
            AddMessage("Output window initialized");
        }

        public void AddMessage(string message)
        {
            var timestamp = DateTime.Now.ToString("HH:mm:ss");
            Messages.Add($"[{timestamp}] {message}");
        }
    }
}
