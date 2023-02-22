using Microsoft.Net.Http.Headers;
using System;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using System.Threading.Tasks;

namespace Ben.Http
{
    public static class ResponseExtensions
    {
        public static Task Text(this Response response, ReadOnlySpan<byte> utf8String)
        {
            Microsoft.AspNetCore.Http.IHeaderDictionary headers = response.Headers;

            headers.ContentLength = utf8String.Length;
            headers[HeaderNames.ContentType] = "text/plain";

            System.IO.Pipelines.PipeWriter writer = response.Writer;
            Span<byte> output = writer.GetSpan(utf8String.Length);

            utf8String.CopyTo(output);
            writer.Advance(utf8String.Length);

            return Task.CompletedTask;
        }

        public static Task Json(this Response response, object value, JsonTypeInfo inputType)
        {
            response.Headers[HeaderNames.ContentType] = "application/json";

            JsonSerializer.Serialize(GetJsonWriter(response), value, inputType);

            return Task.CompletedTask;
        }

        public static Task NotFound(this Response response)
        {
            response.StatusCode = 404;

            return Task.CompletedTask;
        }

        private static Utf8JsonWriter GetJsonWriter(Response response)
        {
            Utf8JsonWriter utf8JsonWriter = new(response.Writer, new JsonWriterOptions { SkipValidation = true });
            utf8JsonWriter.Reset(response.Writer);
            return utf8JsonWriter;
        }
    }
}
