using System;
using System.Collections.Generic;
using System.Text;
using Tracer.Shared.Models;

namespace Tracer.Shared.Attributes
{
    public class StateInfoAttribute: Attribute
    {
        public string Label { get; }
        public StateCategory Category { get; }

        public StateInfoAttribute(string label, StateCategory category)
        {
            Label = label;
            Category = category;
        }
    }
}
