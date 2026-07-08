using System;
using System.Collections.Generic;
using System.Text;
using ActiMetrics.Shared.Attributes;
using ActiMetrics.Shared.Models;

namespace ActiMetrics.Shared.Models
{
    // Los valores numéricos deben coincidir con el campo "code" que devuelve /states.
    public enum WorkState
    {
        [StateInfoAttribute("Trabajando", StateCategory.Active)]
        Working = 0,

        [StateInfoAttribute("Horas Extras", StateCategory.Active)]
        Overtime = 1,

        [StateInfoAttribute("Break", StateCategory.Neutral)]
        Break = 2,

        [StateInfoAttribute("Baño", StateCategory.Neutral)]
        WC = 3,

        [StateInfoAttribute("Almuerzo", StateCategory.Neutral)]
        Lunch = 4,

        [StateInfoAttribute("Idle", StateCategory.Inactive)]
        Idle = 5,

        [StateInfoAttribute("Fuera de linea", StateCategory.Inactive)]
        Offline = 6
    }
}
