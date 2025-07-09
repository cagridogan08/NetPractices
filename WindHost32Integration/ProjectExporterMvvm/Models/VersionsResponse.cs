using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ProjectExporterMvvm.Models
{
    public class VersionsResponse
    {
        public string ProjectName { get; set; }
        public List<ProjectInfo> Versions { get; set; } = new();
        public string LatestVersion { get; set; }
        public int VersionCount { get; set; }
        public DateTime Timestamp { get; set; }
    }
}
