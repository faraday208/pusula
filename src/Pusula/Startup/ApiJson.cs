using System.Text.Json;
using System.Text.Json.Serialization;

namespace Pusula.Startup;

/// <summary>The JSON settings of the API.</summary>
internal static class ApiJson
{
    /// <summary>
    /// Strict reading (exact property names, numbers only from JSON numbers, no duplicate properties) and compact
    /// writing: enums as strings, properties that are null left out. The web defaults (camelCase) stay.
    /// </summary>
    /// <param name="options">The options to adjust.</param>
    public static void Configure(JsonSerializerOptions options)
    {
        options.NumberHandling = JsonNumberHandling.Strict;
        options.PropertyNameCaseInsensitive = false;
        options.AllowDuplicateProperties = false;
        options.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
        options.Converters.Add(new JsonStringEnumConverter());
    }
}
