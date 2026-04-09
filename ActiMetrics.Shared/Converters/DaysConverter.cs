using System.Text.Json;
using System.Text.Json.Serialization;
using ActiMetrics.Shared.Models;


namespace ActiMetrics.Shared.Converters
{



  public class DaysConverter : JsonConverter<Days>
  {
    public override Days Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
      var value = reader.GetString();
      return value switch
      {
        "L" => Days.L,
        "M" => Days.M,
        "X" => Days.X,
        "J" => Days.J,
        "V" => Days.V,
        "S" => Days.S,
        "D" => Days.D,
        _ => throw new JsonException($"Valor de día de semana no válido: {value}")
      };
    }


    public override void Write(Utf8JsonWriter writer, Days value, JsonSerializerOptions options)
    {
      writer.WriteStringValue(value.ToString());
    }
  }


}