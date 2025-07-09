using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ProjectExporterMvvm.Models
{
    public class RunningProjectInfo
    {
        public int ProcessId { get; set; }
        public string ProjectName { get; set; }
        public string ProjectPath { get; set; }
        public DateTime StartedAt { get; set; }
        public TimeSpan RunningTime => DateTime.UtcNow - StartedAt;
        public string FormattedMemory { get; set; }
        public string WindowTitle { get; set; }
        public bool ProcessExists { get; set; }
    }
}
