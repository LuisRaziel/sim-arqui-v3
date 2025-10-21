using Microsoft.AspNetCore.Http;
using Serilog.Context;

namespace Api.Middleware
{
    public class CorrelationIdMiddleware
    {
        private readonly RequestDelegate _next;
        private const string HeaderName = "X-Correlation-Id";
        public CorrelationIdMiddleware(RequestDelegate next) => _next = next;

        public async Task InvokeAsync(HttpContext ctx)
        {
            var corr = ctx.Request.Headers.TryGetValue(HeaderName, out var values) && !string.IsNullOrWhiteSpace(values)
                ? values.ToString()
                : Guid.NewGuid().ToString("N");

            if (!ctx.Response.Headers.ContainsKey(HeaderName))
                ctx.Response.Headers[HeaderName] = corr;

            ctx.Items["CorrelationId"] = corr;

            using (LogContext.PushProperty("CorrelationId", corr))
            {
                await _next(ctx);
            }
        }
    }
}