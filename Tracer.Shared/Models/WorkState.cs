using System;
using System.Collections.Generic;
using System.Text;
using Tracer.Shared.Attributes;
using Tracer.Shared.Models;

namespace Tracer.Shared.Models
{
    public enum WorkState
    {
        [StateInfoAttribute("Trabajando",StateCategory.Active)]
        Working,

        [StateInfoAttribute("Break", StateCategory.Neutral)]
        Break,

        [StateInfoAttribute("Baño", StateCategory.Neutral)]
        WC,

        [StateInfoAttribute("Almuerzo", StateCategory.Neutral)]
        Lunch,


        [StateInfoAttribute("Idle",StateCategory.Inactive)]
        Idle,
        [StateInfoAttribute("Fuera de linea", StateCategory.Inactive)]
        Offline
    }
}
