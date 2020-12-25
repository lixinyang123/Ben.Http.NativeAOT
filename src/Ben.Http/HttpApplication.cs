﻿using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Abstractions;
using Microsoft.AspNetCore.Http.Features;

namespace Ben.Http
{
    public delegate Task RequestHandler(Request req, Response res);

    public class HttpApplication : IHttpApplication<Context>
    {
        private readonly Dictionary<string, RequestHandler> _routes = new();

        public void Get(string path, RequestHandler handler)
        {
            _routes[path] = handler;
        }

        public IEnumerable<string> Paths
            => PathsEnumerable();

        private IEnumerable<string> PathsEnumerable()
        {
            foreach (var path in _routes.Keys)
            {
                yield return path;
            }
        }

        Task IHttpApplication<Context>.ProcessRequestAsync(Context context)
        {
            var request = context.Request;
            var response = context.Response;
            if (_routes.TryGetValue(request.Path, out var handler))
            {
                return handler(request, context.Response);
            }

            response.StatusCode = 404;
            return Task.CompletedTask;
        }

        Context IHttpApplication<Context>.CreateContext(IFeatureCollection features)
        {
            Context context;
            if (features is IHostContextContainer<Context> container)
            {
                // The server allows us to store the HttpContext on the connection
                // between requests so we don't have to reallocate it each time.
                context = container.HostContext;
                if (context is null)
                {
                    context = new Context();
                    container.HostContext = context;
                }
            }
            else
            {
                // Server doesn't support pooling, so create a new Context
                context = new Context();
            }

            context.Initialize(features);

            return context;
        }

        void IHttpApplication<Context>.DisposeContext(Context context, Exception exception)
        {
            // As we may be pooling the HttpContext above; Reset its settings.
            context.Reset();
        }
    }
}
