using System.Text.Json;
using Xunit;

namespace Pusula.IntegrationTests.Support;

internal static class HttpExtensions
{
    public static Task<HttpResponseMessage> GetResponseAsync(this HttpClient client, string url) =>
        client.GetAsync(url, TestContext.Current.CancellationToken);

    public static async Task<JsonDocument> ReadJsonAsync(this HttpResponseMessage response)
    {
        string text = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        return JsonDocument.Parse(text);
    }

    public static async Task<JsonDocument> GetJsonAsync(this HttpClient client, string url)
    {
        using HttpResponseMessage response = await client.GetResponseAsync(url);
        response.EnsureSuccessStatusCode();
        return await response.ReadJsonAsync();
    }

    public static string[] PropertyNames(this JsonElement element) => [.. element.EnumerateObject().Select(property => property.Name)];
}
