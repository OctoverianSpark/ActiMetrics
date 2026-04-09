using System;
using ActiMetrics.Shared.Models;

namespace ActiMetrics.Shared.Extensions
{


  public static class DaysExtensions
  {
    public static string ToFullString(this Days day)
    {
      return day switch
      {
        Days.L => "Lunes",
        Days.M => "Martes",
        Days.X => "Miércoles",
        Days.J => "Jueves",
        Days.V => "Viernes",
        Days.S => "Sábado",
        Days.D => "Domingo",
        _ => throw new ArgumentOutOfRangeException(nameof(day), $"Not expected day value: {day}")
      };
    }
  }

}