using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Abstractions;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Net.Http.Headers;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace Ben.Http
{
    public delegate Task RequestHandler(Request req, Response res);

    public class HttpApp : IHttpApplication<Context>
    {
        private readonly Dictionary<string, RequestHandler> _routes = new();

        public void Get(string path, RequestHandler handler)
        {
            _routes[path] = handler;
        }

        public void Get(string path, Func<string> handler)
        {
            byte[] utf8String = Encoding.UTF8.GetBytes(handler());

            _routes[path] = (req, res) =>
            {
                Microsoft.AspNetCore.Http.IHeaderDictionary headers = res.Headers;
                ReadOnlySpan<byte> data = utf8String;

                headers.ContentLength = data.Length;
                headers[HeaderNames.ContentType] = "text/plain";

                System.IO.Pipelines.PipeWriter writer = res.Writer;
                Span<byte> output = writer.GetSpan(data.Length);

                data.CopyTo(output);
                writer.Advance(data.Length);

                return Task.CompletedTask;
            };
        }

        public override string ToString()
        {
            StringBuilder sb = new();
            _ = sb.AppendLine($"Paths:");
            foreach (string path in _routes.Keys)
            {
                _ = sb.AppendLine($"=> {path}");
            }

            return sb.ToString();
        }

        public IEnumerable<string> Paths
            => PathsEnumerable();

        private IEnumerable<string> PathsEnumerable()
        {
            foreach (string path in _routes.Keys)
            {
                yield return path;
            }
        }

        Task IHttpApplication<Context>.ProcessRequestAsync(Context context)
        {
            Request request = context.Request;
            Response response = context.Response;
            response.Headers[HeaderNames.Server] = "Ben";
            if (_routes.TryGetValue(request.Path, out RequestHandler? handler))
            {
                return handler(request, context.Response);
            }

            response.StatusCode = 404;
            return Task.CompletedTask;
        }

        Context IHttpApplication<Context>.CreateContext(IFeatureCollection features)
        {
            Context context = new();
            if (features is IHostContextContainer<Context> container)
            {
                // The server allows us to store the HttpContext on the connection
                // between requests so we don't have to reallocate it each time.
                container.HostContext ??= new Context();
            }

            context.Initialize(features);

            return context;
        }

        void IHttpApplication<Context>.DisposeContext(Context context, Exception? exception)
        {
            // As we may be pooling the HttpContext above; Reset its settings.
            context.Reset();
        }
    }
}
