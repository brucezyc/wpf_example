using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace WpfPlotMvp.Protocols;

public static class MessageSerializer
{
    private static readonly JsonSerializerSettings Settings = new()
    {
        Converters = { new StringEnumConverter() },
        Formatting = Formatting.None
    };

    public static string Serialize<T>(T message) =>
        JsonConvert.SerializeObject(message, Settings);

    public static T? Deserialize<T>(string json) =>
        JsonConvert.DeserializeObject<T>(json, Settings);
}
