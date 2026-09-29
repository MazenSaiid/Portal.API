using System.Text.Json;
using System.Text.Json.Serialization;

namespace Portal.Tests.Infrastructure;

/// <summary>Matches the API's JSON settings (camelCase, enums as strings).</summary>
public static class Json
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };
}
