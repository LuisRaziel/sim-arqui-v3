set -euo pipefail

echo "== Build local .NET =="
dotnet restore api/src worker/src
dotnet build api/src -c Release
dotnet build worker/src -c Release

echo "== Compose limpio =="
docker compose down -v --remove-orphans
docker compose build --no-cache
docker compose up -d

echo "== Health API =="
for i in {1..30}; do curl -sf http://localhost:8080/health && break || sleep 1; done

echo "== POST /orders =="
OID=$(uuidgen 2>/dev/null || echo 11111111-1111-1111-1111-111111111111)
curl -i -X POST http://localhost:8080/orders \
  -H "Content-Type: application/json" \
  -d "{\"orderId\":\"$OID\",\"amount\":123.45}"

echo "== Worker logs (tail) =="
docker compose logs worker --tail=50

echo "== OK =="