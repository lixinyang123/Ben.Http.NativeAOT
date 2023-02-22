using Ben.Http;
using System;
using System.Text.Json.Serialization;

(HttpServer server, HttpApp app) = (new HttpServer($"http://+:8080"), new HttpApp());

// Assign routes
app.Get("/plaintext", () => "Hello, World!");

app.Get("/json", (req, res) =>
{
    return res.Json(new Note { Message = "Hello, World!" }, JsonContext.Default.Note);
});

Console.Write($"{server} {app}"); // Display listening info

// Start the server
await server.RunAsync(app);

// Datastructures
internal struct Note { public string Message { get; set; } }

[JsonSerializable(typeof(Note))]
internal partial class JsonContext : JsonSerializerContext { }