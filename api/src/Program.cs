using Microsoft.OpenApi.Models;
using RabbitMQ.Client;
using System.Text;
using System.Text.Json;
using Api.Contracts.Requests;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.IdentityModel.JsonWebTokens;
using System.Security.Claims;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c => c.SwaggerDoc("v1", new OpenApiInfo { Title = "Orders API", Version = "v1" }));

var jwtKey = builder.Configuration["JWT__KEY"] ?? "dev-local-change-me";
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
  .AddJwtBearer(o =>
  {
      o.RequireHttpsMetadata = false;
      o.TokenValidationParameters = new TokenValidationParameters
      {
          ValidateIssuer = false,
          ValidateAudience = false,
          ValidateIssuerSigningKey = true,
          IssuerSigningKey = new SymmetricSecurityKey(
              Encoding.UTF8.GetBytes(jwtKey.Length >= 32 ? jwtKey : jwtKey.PadRight(32, '_')))
      };
  });
builder.Services.AddAuthorization();

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();
app.UseAuthentication();
app.UseAuthorization();

app.MapMethods("/token", new[] { "GET", "POST" }, () =>
{
    var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(
        jwtKey.Length >= 32 ? jwtKey : jwtKey.PadRight(32, '_')));
    var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

    var claims = new[]
    {
        new Claim(JwtRegisteredClaimNames.Sub, "demo-user"),
        new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N"))
    };

    var desc = new SecurityTokenDescriptor
    {
        Subject = new ClaimsIdentity(claims),
        Expires = DateTime.UtcNow.AddHours(1),
        SigningCredentials = creds
    };

    var handler = new JsonWebTokenHandler();
    var jwt = handler.CreateToken(desc);
    return Results.Json(new { token = jwt });
});

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.MapPost("/orders",
    [Microsoft.AspNetCore.Authorization.Authorize] (CreateOrderRequest request) =>
{
    if (request.OrderId == Guid.Empty || request.Amount <= 0)
    return Results.BadRequest(new { message = "Invalid payload" });

    var host = Environment.GetEnvironmentVariable("RABBITMQ__HOST") ?? "localhost";
    var user = Environment.GetEnvironmentVariable("RABBITMQ__USER") ?? "guest";
    var pass = Environment.GetEnvironmentVariable("RABBITMQ__PASS") ?? "guest";

    const string exchange = "orders.exchange";
    const string routingKey = "orders.created";

    var envelope = new { messageId = Guid.NewGuid(), request.OrderId, request.Amount, createdAt = DateTime.UtcNow };
    var body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(envelope));

    try
    {
        var factory = new ConnectionFactory { HostName = host, UserName = user, Password = pass };
        using var connection = factory.CreateConnection();
        using var channel = connection.CreateModel();

        channel.ExchangeDeclare(exchange, type: "topic", durable: true, autoDelete: false);

        var props = channel.CreateBasicProperties();
        props.ContentType = "application/json";
        props.DeliveryMode = 2; // persistente
        props.MessageId = envelope.messageId.ToString();

        channel.BasicPublish(exchange: exchange, routingKey: routingKey, basicProperties: props, body: body);
        return Results.Accepted($"/orders/{request.OrderId}", new { status = "queued", request.OrderId });
    }
    catch
    {
        // Fallback: permite probar la API aunque no haya broker
        return Results.Accepted($"/orders/{request.OrderId}", new { status = "simulated", request.OrderId });
    }
})
.WithName("CreateOrder")
.Produces(StatusCodes.Status202Accepted)
.Produces(StatusCodes.Status400BadRequest)
.Produces(StatusCodes.Status401Unauthorized);

app.Run();