using System;
using System.Collections.Generic;
using System.Text;

namespace ActiMetrics.Shared.Models
{
    public class User
    {
        public string Token { get; set; }
        public string Email { get; set; } = string.Empty;
    }
}
