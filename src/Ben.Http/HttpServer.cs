using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.Server.Kestrel.Transport.Sockets;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Ben.Http
{
    public class HttpServer : IDisposable
    {
        private readonly IServerAddressesFeature addresses = null!;
        private readonly TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly ILoggerFactory loggerFactory;
        private readonly IServer server;

        public IFeatureCollection? Features => server.Features;

        public HttpServer(string listenAddress) : this(DefaultLoggerFactories.Empty)
        {
            addresses = Features.Get<IServerAddressesFeature>();
            addresses.Addresses.Add(listenAddress);
        }

        public HttpServer(string listenAddress, ILoggerFactory loggerFactory) : this(loggerFactory)
        {
            addresses = Features.Get<IServerAddressesFeature>();
            addresses.Addresses.Add(listenAddress);
        }

        public HttpServer(IEnumerable<string> listenAddresses, ILoggerFactory loggerFactory) : this(loggerFactory)
        {
            addresses = Features.Get<IServerAddressesFeature>();
            foreach (string uri in listenAddresses)
            {
                addresses.Addresses.Add(uri);
            };
        }

        private HttpServer(ILoggerFactory loggerFactory)
        {
            this.loggerFactory = loggerFactory;
            server = new KestrelServer(
                KestrelOptions.Defaults,
                new SocketTransportFactory(SocketOptions.Defaults, this.loggerFactory),
                this.loggerFactory);
        }

        public override string ToString()
        {
            StringBuilder sb = new();
            _ = sb.AppendLine($"Listening on:");
            foreach (string address in addresses.Addresses)
            {
                _ = sb.AppendLine($"=> {address}");
            }

            return sb.ToString();
        }

        public async Task RunAsync(HttpApp application, CancellationToken cancellationToken = default)
        {
            await server.StartAsync(application, cancellationToken);

            _ = cancellationToken.UnsafeRegister(static (o) => ((HttpServer)o!).completion.TrySetResult(), this);

            await completion.Task;

            await server.StopAsync(default);
        }

        void IDisposable.Dispose()
        {
            server.Dispose();
        }

        private class DefaultLoggerFactories
        {
            public static ILoggerFactory Empty => new LoggerFactory();
        }

        private class KestrelOptions : IOptions<KestrelServerOptions>
        {
            private KestrelOptions()
            {
                Value = new KestrelServerOptions();
            }

            public static KestrelOptions Defaults { get; } = new KestrelOptions();

            public KestrelServerOptions Value { get; init; }
        }

        private class SocketOptions : IOptions<SocketTransportOptions>
        {
            public static SocketOptions Defaults { get; } = new SocketOptions
            {
                Value = new SocketTransportOptions()
                {
                    WaitForDataBeforeAllocatingBuffer = false,
                    UnsafePreferInlineScheduling = RuntimeInformation.IsOSPlatform(OSPlatform.Linux) && Environment.GetEnvironmentVariable("DOTNET_SYSTEM_NET_SOCKETS_INLINE_COMPLETIONS") == "1",
                }
            };

            public SocketTransportOptions Value { get; init; } = new SocketTransportOptions();
        }

        private class LoggerOptions : IOptionsMonitor<ConsoleLoggerOptions>
        {
            public static LoggerOptions Default { get; } = new LoggerOptions();

            public ConsoleLoggerOptions CurrentValue { get; } = new ConsoleLoggerOptions();

            public ConsoleLoggerOptions Get(string? name)
            {
                return CurrentValue;
            }

            public IDisposable OnChange(Action<ConsoleLoggerOptions, string> listener)
            {
                return NullDisposable.Shared;
            }

            private class NullDisposable : IDisposable
            {
                public static NullDisposable Shared { get; } = new NullDisposable();

                public void Dispose() { }
            }
        }
    }
}
