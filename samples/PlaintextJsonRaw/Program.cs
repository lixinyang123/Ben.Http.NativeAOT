using Ben.Http;
using Microsoft.Net.Http.Headers;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using static System.Console;

internal class Program
{
    [RequiresDynamicCode("Calls System.Text.Json.JsonSerializer.SerializeAsync<TValue>(Stream, TValue, JsonSerializerOptions, CancellationToken)")]
    [RequiresUnreferencedCode("Calls System.Text.Json.JsonSerializer.SerializeAsync<TValue>(Stream, TValue, JsonSerializerOptions, CancellationToken)")]
    private static async Task Main(string[] args)
    {
        int port = 8080;

        HttpServer server = new($"http://+:{port}");
        HttpApp app = new();

        // Assign routes
        app.Get("/plaintext", (request, response) =>
        {
            byte[] payload = Settings.HelloWorld;

            Microsoft.AspNetCore.Http.IHeaderDictionary headers = response.Headers;

            headers.ContentLength = payload.Length;
            headers[HeaderNames.ContentType] = "text/plain";

            return response.Writer.WriteAsync(payload).AsTask();
        });


        app.Get("/json", (request, response) =>
        {
            Microsoft.AspNetCore.Http.IHeaderDictionary headers = response.Headers;

            headers.ContentLength = 27;
            headers[HeaderNames.ContentType] = "application/json";

            return JsonSerializer.SerializeAsync(
                response.Stream,
                new JsonMessage { message = "Hello, World!" },
                Settings.SerializerOptions);
        });

        Write($"{server} {app}"); // Display listening info

        await server.RunAsync(app);
    }
}

// Settings and datastructures
internal struct JsonMessage { public string message { get; set; } }

internal static class Settings
{
    public static readonly byte[] HelloWorld = Encoding.UTF8.GetBytes("Hello, World!");
    public static readonly JsonSerializerOptions SerializerOptions = new(new JsonSerializerOptions { });
}