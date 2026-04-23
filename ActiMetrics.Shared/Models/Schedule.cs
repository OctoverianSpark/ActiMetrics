using System.Text.Json.Serialization;
using ActiMetrics.Shared.Converters;

namespace ActiMetrics.Shared.Models
{
  public class Schedule
  {
    public int Id { get; set; }

    public int Appuser_Id { get; set; }
    public int Programation_Id { get; set; }


    [property: JsonConverter(typeof(DaysConverter))]
    public Days Day_Of_Week { get; set; }
  }
}