using Microsoft.AspNetCore.OpenApi;

namespace Pusula.Startup;

/// <summary>
/// Keeps an enum of the API document a list of strings when the API only ever uses it as a nullable (<c>FolderKind?</c>, the kind
/// of a folder, which is left out when a folder has none). The schema that the framework makes of a nullable enum has <c>null</c>
/// among the values, and when the nullable is the only use of the enum, that schema is the one the document keeps for the enum
/// itself, with the <c>null</c> in it.
/// </summary>
internal static class OpenApiEnums
{
    /// <summary>Takes <c>null</c> out of the values of the schema of a nullable enum: the property that is nullable says so by itself.</summary>
    /// <param name="options">The options of the OpenAPI document.</param>
    public static OpenApiOptions AddNullableEnumSchemas(this OpenApiOptions options) =>
        options.AddSchemaTransformer(static (schema, context, _) =>
        {
            if (Nullable.GetUnderlyingType(context.JsonTypeInfo.Type)?.IsEnum == true && schema.Enum is { } values)
            {
                schema.Enum = [.. values.Where(value => value is not null)];
            }

            return Task.CompletedTask;
        });
}
