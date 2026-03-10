using System;
using System.Collections.Generic;
using System.Text;

namespace Tracer.Shared.Models
{
    public class Screenshot
    {
        public int Id { get; set; }
        public string WorkerId { get; set; }
        public string FilePath { get; set; }
        public bool Synced { get; set; } 
    }
}
