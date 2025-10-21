#!/usr/bin/env bash
set -euo pipefail

echo "== 0) Contexto =="
pwd
dotnet --info | head -n 8 || true
echo

echo "== 1) Build local .NET (sin Docker) =="
# == 1) Build local .NET (sin Docker) ==
set -x
dotnet restore api/src/Api.csproj
dotnet restore worker/src/Worker.csproj
dotnet build api/src/Api.csproj -c Release --no-restore
dotnet build worker/src/Worker.csproj -c Release --no-restore
set +x
echo

echo "== 2) Compose: rebuild limpio =="
set -x
docker compose down -v --remove-orphans
docker compose build --no-cache --progress=plain
docker compose up -d
set +x
echo

echo "== 3) Estado de contenedores =="
docker compose ps
echo

echo "== 4) Logs iniciales (api/rabbitmq/worker) =="
docker compose logs api --tail=50 || true
docker compose logs rabbitmq --tail=50 || true
docker compose logs worker --tail=50 || true
echo

echo "== 5) Esperando /health de API =="
for i in {1..40}; do
  if curl -sf http://localhost:8080/health >/dev/null; then
    echo "API saludable ✅"
    break
  fi
  sleep 1
  if [[ $i -eq 40 ]]; then
    echo "❌ Timeout esperando /health"
    docker compose logs api --tail=200
    exit 1
  fi
done
echo

echo "== 6) Probar POST /orders =="
OID=$(uuidgen 2>/dev/null || echo 11111111-1111-1111-1111-111111111111)
set -x
curl -i -X POST http://localhost:8080/orders \
  -H "Content-Type: application/json" \
  -d "{\"orderId\":\"$OID\",\"amount\":123.45}"
set +x
echo

echo "== 7) Logs de worker (consumo) =="
docker compose logs worker --tail=100 || true
echo

echo "== ✅ E2E OK (si /orders fue 202 y el worker mostró consumo) =="