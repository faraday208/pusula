using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Pusula.Sources;

/// <summary>Documents what the route group <c>/api/sources/{source}</c> takes from the URL: the handlers do not bind it themselves.</summary>
internal static class SourceOpenApi
{
    private const string GroupPrefix = "api/sources/{source}/";

    /// <summary>Adds the <c>source</c> path parameter to every operation below <c>/api/sources/{source}/</c>.</summary>
    /// <param name="options">The options of the OpenAPI document.</param>
    public static OpenApiOptions AddSourceParameter(this OpenApiOptions options) =>
        options.AddOperationTransformer(static (operation, context, _) =>
        {
            if (context.Description.RelativePath?.StartsWith(GroupPrefix, StringComparison.Ordinal) == true)
            {
                operation.Parameters ??= [];
                operation.Parameters.Insert(0, new OpenApiParameter
                {
                    Name = "source",
                    In = ParameterLocation.Path,
                    Required = true,
                    Description = "The id of the source, as listed by /api/sources.",
                    Schema = new OpenApiSchema { Type = JsonSchemaType.String },
                });
            }

            return Task.CompletedTask;
        });
}
