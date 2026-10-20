using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace Client.Helpers;

public static class HttpClientHelper
{
    public static async Task<T?> GetModelAsync<T>(this HttpResponseMessage response)
    {
        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized) throw new UnauthorizedAccessException("The session expired.");
        response.EnsureSuccessStatusCode();
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };
        return await response.Content.ReadFromJsonAsync<T>(options);
    }

    public static HttpRequestMessage AddContent<T>(this HttpRequestMessage request, T content)
    {
        var serializeObject = JsonSerializer.Serialize(content);
        request.Content = new StringContent(serializeObject, Encoding.UTF8, "application/json");
        return request;
    }
}
