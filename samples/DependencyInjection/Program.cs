using Ben.Http;
using DependencyInjection;
using System;

(HttpServer server, HttpApp app) = (new HttpServer($"http://+:8080"), new HttpApp());

// Assign routes
app.Get("/plaintext", () => "Hello, World!");

app.Get("/di", () =>
{
    IService service = new MyServiceProvider().GetService<IService>();
    return service.Message();
});

Console.Write($"{server} {app}"); // Display listening info

// Start the server
await server.RunAsync(app);