# 🧩 TODO - Simulación Arquitectura

## ✅ FASE 1: BASE
- [x] Estructura base (api/src, worker/src)
- [x] API + Worker .NET 8
- [x] RabbitMQ.Client en ambos
- [x] Dockerfiles multi-stage
- [x] docker-compose con RabbitMQ + healthcheck
- [x] Endpoint /orders (publica evento)
- [x] Worker consume (idempotencia + DLQ)
- [x] Script scripts/e2e.sh
- [x] Commit + tag v1.0.0-base

---

## 🔒 FASE 2: AUTENTICACIÓN (JWT)
- [x] Rama `feat/jwt`
- [x] Agregar paquetes `JwtBearer`, `IdentityModel.*`
- [x] Endpoint `/token` (demo) que genere JWT
- [x] Proteger `/orders` con `[Authorize]`
- [x] Variables de entorno en `compose` (`JWT__KEY`)
- [x] Prueba E2E completa con token
- [x] Refactorizar generación de JWT en clase separada

---

## 📋 FASE 3: LOGS (SERILOG JSON)
- [x] Rama `feat/logs`
- [x] Configurar Serilog en API y Worker
- [x] Reemplazar `Console.WriteLine` por `Serilog.Log.*`
- [x] Usar `CompactJsonFormatter` para compatibilidad con ELK
- [x] Agregar contexto `CorrelationId` en logs

---

## 📈 FASE 4: MÉTRICAS (PROMETHEUS)
- [x] Rama `feat/metrics`
- [x] Agregar paquete `prometheus-net.AspNetCore`
- [x] Exponer `/metrics` y habilitar `UseHttpMetrics()`
- [ ] Métricas personalizadas (p. ej. pedidos procesados)
- [ ] Documentar endpoints de observabilidad

---

## 🚦 FASE 5: RATE LIMITING
- [x] Rama `feat/ratelimit`
- [x] Implementar limitador fijo en `/orders`
- [ ] Parametrizar por entorno (`RATELIMIT__WINDOW`, `PERMIT_LIMIT`)

---

## 🧱 FASE 6: CI/CD Y TESTS (opcional)
- [ ] Testcontainers para integración RabbitMQ
- [ ] GitHub Actions con build + e2e
- [ ] Reporte de cobertura o validación de endpoints

---

## 🧠 IDEAS FUTURAS / BACKLOG
- [ ] HealthCheck en Worker para readiness (K8s)
- [ ] OpenTelemetry (tracing)
- [ ] ADR sobre diseño event-driven y DLQ
- [ ] README final con arquitectura y diagrama ASCII