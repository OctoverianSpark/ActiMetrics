using System;
using System.Collections.Generic;
using System.Text;

namespace Tracer.Shared.Models
{
    public class AppUsageLog
    {

        public int Id { get; set; }
        public string WorkerId { get; set; }
        public string IntervalStart { get; set; }
        public string IntervalEnd { get; set; }
        public string Apps { get; set; } // JSON: [{"app":"Chrome","seconds":142.5}]
        public bool Synced { get; set; }
    }
}
