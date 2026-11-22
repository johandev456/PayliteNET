#!/usr/bin/env bash
# Prueba de humo de PayLite contra los servicios levantados (docker compose o IDE).
# Requiere: curl, jq. Uso: ./scripts/smoke.sh
# Variables opcionales: KC, ACC, PAY (URLs base).
set -euo pipefail

KC=${KC:-http://localhost:8081}
ACC=${ACC:-http://localhost:5001}
PAY=${PAY:-http://localhost:5002}
JSON='Content-Type: application/json'

ok=0; fallos=0
verde() { printf '\033[32m✔ %s\033[0m\n' "$1"; ok=$((ok + 1)); }
rojo()  { printf '\033[31m✘ %s\033[0m\n' "$1"; fallos=$((fallos + 1)); }
esperar() { # esperar <descripción> <esperado> <obtenido>
  if [[ "$2" == "$3" ]]; then verde "$1 ($3)"; else rojo "$1: esperado $2, obtenido $3"; fi
}

token() {
  curl -sf "$KC/realms/paylite/protocol/openid-connect/token" \
    -d grant_type=password -d client_id=paylite-test -d "username=$1" -d "password=$2" | jq -r .access_token
}

# curl que deja el cuerpo en $BODY y el status en $CODE
llamar() {
  local salida
  salida=$(curl -s -w '\n%{http_code}' "$@")
  CODE=${salida##*$'\n'}
  BODY=${salida%$'\n'*}
}

echo "== Tokens"
C1=$(token cliente1 cliente1-dev)
C2=$(token cliente2 cliente2-dev)
AD=$(token admin admin-dev)
[[ -n "$C1" && "$C1" != null ]] && verde "token de cliente1" || { rojo "no hay token (¿Keycloak arriba?)"; exit 1; }

echo "== Seguridad"
llamar "$ACC/api/cuentas";                                         esperar "sin token → 401" 401 "$CODE"
llamar -H "Authorization: Bearer x.y.z" "$PAY/api/pagos";          esperar "token inválido → 401" 401 "$CODE"
llamar -H "Authorization: Bearer $C1" "$ACC/api/admin/cuentas";    esperar "CLIENTE en /api/admin → 403" 403 "$CODE"
llamar -H "Authorization: Bearer $C1" "$PAY/api/admin/pagos";      esperar "CLIENTE en /api/admin/pagos → 403" 403 "$CODE"
llamar -H "Authorization: Bearer $AD" "$ACC/api/admin/cuentas";    esperar "ADMIN en /api/admin → 200" 200 "$CODE"

echo "== Cuentas"
llamar -H "Authorization: Bearer $C1" -H "$JSON" -d '{"moneda":"DOP"}' "$ACC/api/cuentas"
esperar "crear cuenta → 201" 201 "$CODE"
CUENTA=$(jq -r .id <<<"$BODY")
llamar -H "Authorization: Bearer $C2" -H "$JSON" -d '{"moneda":"USD"}' "$ACC/api/cuentas"
CUENTA2=$(jq -r .id <<<"$BODY")
llamar -H "Authorization: Bearer $C1" -H "$JSON" -d '{"moneda":"dop"}' "$ACC/api/cuentas"
esperar "moneda mal formada → 400" 400 "$CODE"
llamar -H "Authorization: Bearer $C2" "$ACC/api/cuentas/$CUENTA"
esperar "cuenta ajena → 404" 404 "$CODE"
llamar -H "Authorization: Bearer $AD" -H "$JSON" -d '{"monto":10000}' "$ACC/api/admin/cuentas/$CUENTA/credito"
esperar "crédito ADMIN → 200" 200 "$CODE"

saldo() { curl -s -H "Authorization: Bearer $C1" "$ACC/api/cuentas/$CUENTA" | jq -r .saldo; }
esperar "saldo inicial" 10000.00 "$(saldo)"

echo "== Pagos"
KEY="smoke-$(date +%s%N)"
pagar() { # pagar <token> <key> <json>
  llamar -H "Authorization: Bearer $1" -H "$JSON" -H "Idempotency-Key: $2" -d "$3" "$PAY/api/pagos"
}

pagar "$C1" "$KEY-1" "{\"cuentaId\":\"$CUENTA\",\"monto\":100.00,\"metodo\":\"TARJETA\"}"
esperar "pago TARJETA → 201" 201 "$CODE"
esperar "  estado" APROBADO "$(jq -r .estado <<<"$BODY")"
esperar "  comisión 2.9 %" 2.90 "$(jq -r .comision <<<"$BODY")"
PAGO=$(jq -r .id <<<"$BODY")
esperar "  saldo = 10000 - 102.90" 9897.10 "$(saldo)"

pagar "$C1" "$KEY-1" "{\"cuentaId\":\"$CUENTA\",\"monto\":100.00,\"metodo\":\"TARJETA\"}"
esperar "mismo Idempotency-Key → 200" 200 "$CODE"
esperar "  mismo pago" "$PAGO" "$(jq -r .id <<<"$BODY")"
esperar "  sin doble cobro" 9897.10 "$(saldo)"

pagar "$C1" "$KEY-1" "{\"cuentaId\":\"$CUENTA\",\"monto\":999.00,\"metodo\":\"TARJETA\"}"
esperar "misma clave, otro monto → 409" 409 "$CODE"

pagar "$C1" "$KEY-2" "{\"cuentaId\":\"$CUENTA\",\"monto\":1000.00,\"metodo\":\"TRANSFERENCIA\"}"
esperar "TRANSFERENCIA comisión fija 25" 25.00 "$(jq -r .comision <<<"$BODY")"

pagar "$C1" "$KEY-3" "{\"cuentaId\":\"$CUENTA\",\"monto\":20000.01,\"metodo\":\"BILLETERA\"}"
esperar "BILLETERA sobre el límite → 422" 422 "$CODE"

pagar "$C1" "$KEY-4" "{\"cuentaId\":\"$CUENTA\",\"monto\":10.00,\"metodo\":\"CHEQUE\"}"
esperar "método desconocido → 400" 400 "$CODE"

pagar "$C1" "$KEY-5" "{\"cuentaId\":\"$CUENTA\",\"monto\":0,\"metodo\":\"TARJETA\"}"
esperar "monto 0 → 400" 400 "$CODE"

llamar -H "Authorization: Bearer $C1" -H "$JSON" -d "{\"cuentaId\":\"$CUENTA\",\"monto\":1,\"metodo\":\"TARJETA\"}" "$PAY/api/pagos"
esperar "sin Idempotency-Key → 400" 400 "$CODE"

pagar "$C1" "$KEY-6" "{\"cuentaId\":\"$CUENTA\",\"monto\":40000.00,\"metodo\":\"TARJETA\"}"
esperar "saldo insuficiente → 201" 201 "$CODE"
esperar "  estado" RECHAZADO "$(jq -r .estado <<<"$BODY")"

pagar "$C1" "$KEY-7" "{\"cuentaId\":\"$CUENTA2\",\"monto\":10.00,\"metodo\":\"TARJETA\"}"
esperar "pagar desde cuenta ajena → RECHAZADO" RECHAZADO "$(jq -r .estado <<<"$BODY")"

llamar -H "Authorization: Bearer $C2" "$PAY/api/pagos/$PAGO"
esperar "pago ajeno → 404" 404 "$CODE"
llamar -H "Authorization: Bearer $C1" "$PAY/api/pagos?tamano=5"
esperar "listar mis pagos → 200" 200 "$CODE"
llamar -H "Authorization: Bearer $AD" "$PAY/api/admin/pagos?estado=APROBADO"
esperar "ADMIN lista pagos → 200" 200 "$CODE"

echo
echo "Resultado: $ok ok, $fallos fallos"
echo "Recuperación de PENDIENTE: ver README (detener account-service, pagar, levantarlo)."
[[ $fallos -eq 0 ]]
