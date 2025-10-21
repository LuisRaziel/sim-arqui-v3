# Sistema de Órdenes - Arquitectura de Microservicios

## Arquitectura
┌──────────────┐ ┌──────────────┐ ┌──────────────┐
│ Cliente │─────▶│ API │─────▶│ RabbitMQ │
│ │ │ (REST) │ │ (Broker) │
└──────────────┘ └──────────────┘ └──────────────┘
│ │
▼ ▼
┌──────────────┐ ┌──────────────┐
│ Prometheus │ │ Worker │
│ (Métricas) │ │ (Consumidor) │
└──────────────┘ └──────────────┘


## Funcionalidades implementadas

### ✅ Seguridad
- JWT Bearer authentication
- Rate limiting configurable (20 req/10s por defecto)

### ✅ Observabilidad
- Logs estructurados JSON (Serilog)
- CorrelationId end-to-end
- Métricas Prometheus (/metrics)
- Health checks (/health, /live)

### ✅ Resiliencia
- Dead Letter Queue (DLQ)
- Reintentos configurables (3 por defecto)
- Idempotencia (MemoryCache con TTL 10min)
- Reconnection automática a RabbitMQ

## Ejecución local

### Requisitos
- .NET 8 SDK
- RabbitMQ 3.x (o Docker)

### API
```bash
export ASPNETCORE_URLS=http://localhost:5047
export JWT__KEY=dev-local-change-me
export RATELIMIT__PERMIT_LIMIT=20
export RATELIMIT__WINDOW_SECONDS=10
dotnet run --project api/src/Api.csproj

### Worker
export RABBITMQ__HOST=localhost
export WORKER__PREFETCH=10
export WORKER__RETRYCOUNT=3
dotnet run --project worker/src/Worker.csproj

# Obtener token
TOKEN=$(curl -s http://localhost:5047/token | jq -r .token)

# Enviar orden
curl -X POST http://localhost:5047/orders \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"orderId":"12345678-1234-1234-1234-123456789012","amount":100}'

Endpoints
API (puerto 5047 o 8080)
GET /health - Health check
GET /token - Obtener JWT
POST /orders - Crear orden (requiere JWT)
GET /metrics - Métricas Prometheus
Worker (puerto 8081)
GET /live - Liveness probe
GET /health - Readiness probe (verifica RabbitMQ)
GET /metrics - Métricas Prometheus