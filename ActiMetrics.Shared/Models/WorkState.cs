using System;
using System.Collections.Generic;
using System.Text;
using ActiMetrics.Shared.Attributes;
using ActiMetrics.Shared.Models;

namespace ActiMetrics.Shared.Models
{
    public enum WorkState
    {
        [StateInfoAttribute("Trabajando", StateCategory.Active)]
        Working,

        [StateInfoAttribute("Horas Extras", StateCategory.Active)]
        Overtime,

        [StateInfoAttribute("Break", StateCategory.Neutral)]
        Break,

        [StateInfoAttribute("Baño", StateCategory.Neutral)]
        WC,

        [StateInfoAttribute("Almuerzo", StateCategory.Neutral)]
        Lunch,


        [StateInfoAttribute("Idle", StateCategory.Inactive)]
        Idle,
        [StateInfoAttribute("Fuera de linea", StateCategory.Inactive)]
        Offline
    }
}
