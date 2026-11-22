# Guía de pruebas de PayLite

Pasos para comprobar a mano cada aspecto del sistema. Todos los comandos se ejecutaron contra el stack de
`docker compose` y dieron el resultado que se indica en **Esperado**.

Necesitas: Docker con Compose, `curl`, `jq` y, para los tests automáticos, el SDK de .NET 10.
Los comandos son de bash (Linux, macOS o WSL/Git Bash en Windows).

---

## 0. Levantar el sistema

```bash
docker compose --profile apps up -d --build
```

Si no puedes descargar de quay.io (red corporativa), usa la misma imagen oficial desde Docker Hub:

```bash
KEYCLOAK_IMAGE=keycloak/keycloak:26.8.2 docker compose --profile apps up -d --build
```

```bash
docker compose --profile apps ps
```

**Esperado:** los 6 contenedores en `Up`, y `keycloak` y las tres bases en `(healthy)`. La primera vez Keycloak tarda unos 30 s.

```bash
curl -s localhost:5001/health; echo; curl -s localhost:5002/health; echo
```

**Esperado:** `Healthy` dos veces.

### Variables para el resto de la guía

Ejecútalas en la misma terminal. Los tokens duran 5 minutos: si un paso da `401` sin razón aparente, vuelve a pedirlos.

```bash
KC=http://localhost:8081/realms/paylite/protocol/openid-connect/token
ACC=http://localhost:5001
PAY=http://localhost:5002
JSON='Content-Type: application/json'
tok() { curl -s $KC -d grant_type=password -d client_id=paylite-test -d username=$1 -d password=$2 | jq -r .access_token; }
C1=$(tok cliente1 cliente1-dev); C2=$(tok cliente2 cliente2-dev); AD=$(tok admin admin-dev)
```

---

## 1. Tests automáticos

```bash
dotnet test
```

**Esperado:** 4 proyectos en `Passed!` (unidad e integración de cada servicio). Con Docker en marcha, los tests de
integración levantan su propio PostgreSQL con Testcontainers. Sin Docker, apunta a un PostgreSQL tuyo con
permiso `CREATEDB`:

```bash
PAYLITE_TEST_POSTGRES="Host=localhost;Port=5433;Username=paylite;Password=paylite_dev;Database=postgres" dotnet test
```

## 2. Prueba de humo (todo el contrato en 1 minuto)

```bash
./scripts/smoke.sh
```

**Esperado:** `Resultado: 30 ok, 0 fallos`. Las secciones siguientes repiten esos casos uno a uno, para verlos en detalle.

---

## 3. Autenticación (401)

Sin token:

```bash
curl -s -w '\n%{http_code}\n' $ACC/api/cuentas
```

**Esperado:** `401` con un cuerpo `application/problem+json`.

Token con la firma alterada (cambia el último carácter):

```bash
curl -s -o /dev/null -w '%{http_code}\n' -H "Authorization: Bearer ${C1%?}x" $ACC/api/cuentas
```

**Esperado:** `401`.

Mira qué trae un token (emisor, audiencia, roles, `sub`):

```bash
echo $C1 | cut -d. -f2 | tr '_-' '/+' | base64 -d 2>/dev/null | jq '{iss, aud, sub, realm_access, exp}'
```

**Esperado:** `iss` = `http://localhost:8081/realms/paylite`, `aud` = `paylite-api`, `realm_access.roles` = `["CLIENTE"]`.
(Si `base64` se queja del relleno, el JSON sale igual.)

Token vencido: espera 5 minutos sin renovar `C1` y repite el primer comando con `-H "Authorization: Bearer $C1"`.
**Esperado:** `401`.

## 4. Autorización por roles (403)

```bash
curl -s -o /dev/null -w 'CLIENTE en admin cuentas: %{http_code}\n' -H "Authorization: Bearer $C1" $ACC/api/admin/cuentas
curl -s -o /dev/null -w 'CLIENTE en admin pagos:   %{http_code}\n' -H "Authorization: Bearer $C1" $PAY/api/admin/pagos
curl -s -o /dev/null -w 'ADMIN en admin cuentas:   %{http_code}\n' -H "Authorization: Bearer $AD" $ACC/api/admin/cuentas
```

**Esperado:** `403`, `403`, `200`.

El endpoint interno solo acepta el rol `SERVICIO` (ni siquiera ADMIN):

```bash
curl -s -o /dev/null -w '%{http_code}\n' -H "Authorization: Bearer $AD" -H "$JSON" -H 'Idempotency-Key: x' \
  -d '{"monto":1,"ownerId":"x"}' $ACC/api/internal/cuentas/00000000-0000-0000-0000-000000000001/debito
```

**Esperado:** `403`.

## 5. Cuentas

```bash
CUENTA=$(curl -s -H "Authorization: Bearer $C1" -H "$JSON" -d '{"moneda":"DOP"}' $ACC/api/cuentas | tee /dev/stderr | jq -r .id)
```

**Esperado:** `{"id": ..., "saldo": 0.00, "moneda": "DOP", "createdAt": ...}`, sin `ownerId`.

Moneda mal formada:

```bash
curl -s -o /dev/null -w '%{http_code}\n' -H "Authorization: Bearer $C1" -H "$JSON" -d '{"moneda":"dop"}' $ACC/api/cuentas
```

**Esperado:** `400`.

Una cuenta nueva no tiene saldo, así que el primer débito es una regla de negocio fallida, no una entrada inválida:

```bash
curl -s -w '\n%{http_code}\n' -H "Authorization: Bearer $C1" -H "$JSON" -H 'Idempotency-Key: d-0' -d '{"monto":10}' $ACC/api/cuentas/$CUENTA/debito
```

**Esperado:** `422` "Saldo insuficiente".

Crédito como ADMIN:

```bash
curl -s -H "Authorization: Bearer $AD" -H "$JSON" -d '{"monto":1000}' $ACC/api/admin/cuentas/$CUENTA/credito | jq '{tipo, monto, saldo}'
```

**Esperado:** `"CREDITO"`, `1000`, saldo `1000.00`.

## 6. Propiedad de los recursos (IDOR → 404)

```bash
curl -s -o /dev/null -w 'cliente2 lee la cuenta de cliente1: %{http_code}\n' -H "Authorization: Bearer $C2" $ACC/api/cuentas/$CUENTA
curl -s -o /dev/null -w 'cuenta inexistente:                  %{http_code}\n' -H "Authorization: Bearer $C1" $ACC/api/cuentas/00000000-0000-0000-0000-000000000001
curl -s -o /dev/null -w 'ADMIN por la ruta normal:            %{http_code}\n' -H "Authorization: Bearer $AD" $ACC/api/cuentas/$CUENTA
```

**Esperado:** `404` las tres veces. Una cuenta ajena es indistinguible de una que no existe, y el rol ADMIN solo
abre `/api/admin/**`: no quita el filtro por dueño.

```bash
curl -s -H "Authorization: Bearer $AD" "$ACC/api/admin/cuentas?pagina=1&tamano=5" | jq '{total, primero: .items[0] | {id, ownerId}}'
```

**Esperado:** la vista de administración sí muestra `ownerId`.

## 7. Débito idempotente (AccountService)

```bash
for i in 1 2; do curl -s -D - -o /tmp/r.json -H "Authorization: Bearer $C1" -H "$JSON" -H 'Idempotency-Key: debito-1' \
  -d '{"monto":100}' $ACC/api/cuentas/$CUENTA/debito | grep -iE '^HTTP|replayed'; jq -c '{movimientoId, saldo}' /tmp/r.json; done
```

**Esperado:**
- Primera vez: `200` y saldo `900.00`.
- Segunda vez: `200` con `Idempotent-Replayed: true`, el **mismo** `movimientoId` y el saldo sigue en `900.00`.

Misma clave con otro monto, y débito sin clave:

```bash
curl -s -o /dev/null -w 'otro monto: %{http_code}\n' -H "Authorization: Bearer $C1" -H "$JSON" -H 'Idempotency-Key: debito-1' -d '{"monto":5}' $ACC/api/cuentas/$CUENTA/debito
curl -s -o /dev/null -w 'sin clave:  %{http_code}\n' -H "Authorization: Bearer $C1" -H "$JSON" -d '{"monto":5}' $ACC/api/cuentas/$CUENTA/debito
```

**Esperado:** `409` y `400`.

## 8. Pagos, comisiones y límites (Strategy + Factory)

`pagar <clave> <monto> <metodo> [token]` muestra el código y lo esencial del pago:

```bash
pagar() { curl -s -w '\n%{http_code}' -H "Authorization: Bearer ${4:-$C1}" -H "$JSON" -H "Idempotency-Key: $1" \
  -d "{\"cuentaId\":\"$CUENTA\",\"monto\":$2,\"metodo\":\"$3\"}" $PAY/api/pagos \
  | { read -r body; read -r code; echo "$code $(jq -c '{id, estado, monto, comision, total, motivoRechazo, title} | with_entries(select(.value != null))' <<<"$body")"; }; }
```

```bash
pagar p-1 100.00 TARJETA
pagar p-2 1000.00 TRANSFERENCIA
pagar p-3 2.50 BILLETERA
```

**Esperado:**

| Pago | Código | Comisión | Total debitado |
|---|---|---|---|
| TARJETA 100.00 (2.9 %) | 201 APROBADO | 2.90 | 102.90 |
| TRANSFERENCIA 1000.00 (fija) | 201 APROBADO | 25.00 | 1025.00 → **RECHAZADO** si ya no alcanza el saldo |
| BILLETERA 2.50 (1 %) | 201 APROBADO | 0.03 (redondeo comercial; el bancario daría 0.02) | 2.53 |

> Con saldo 900 tras la sección 7, la TRANSFERENCIA de 1025.00 sale `RECHAZADO` con motivo "Saldo insuficiente".
> También es un resultado correcto (ver sección 10). Si la quieres aprobada, acredita antes.

Límites por método y validaciones:

```bash
pagar p-4 50000.01 TARJETA        # 422 límite 50 000
pagar p-5 20000.01 BILLETERA      # 422 límite 20 000
pagar p-6 500000.01 TRANSFERENCIA # 422 límite 500 000
pagar p-7 10 CHEQUE               # 400 método desconocido
pagar p-8 0 TARJETA               # 400 monto inválido
```

```bash
curl -s -o /dev/null -w 'sin Idempotency-Key: %{http_code}\n' -H "Authorization: Bearer $C1" -H "$JSON" \
  -d "{\"cuentaId\":\"$CUENTA\",\"monto\":1,\"metodo\":\"TARJETA\"}" $PAY/api/pagos
```

**Esperado:** `400`.

## 9. Idempotencia de pagos (no cobrar dos veces)

```bash
saldo() { curl -s -H "Authorization: Bearer $C1" $ACC/api/cuentas/$CUENTA | jq -r .saldo; }
saldo; pagar p-1 100.00 TARJETA; saldo
```

**Esperado:** `200` (no `201`), el **mismo id** que en la sección 8 y el saldo no cambia.

Misma clave con otros datos:

```bash
pagar p-1 999.00 TARJETA
```

**Esperado:** `409`.

La clave es por usuario: cliente2 puede usar `p-1` sin chocar. Su pago sale `RECHAZADO` porque la cuenta no es suya (sección 10).

```bash
pagar p-1 100.00 TARJETA $C2
```

Diez peticiones **simultáneas** con la misma clave:

```bash
seq 1 10 | xargs -P 10 -I{} curl -s -o /dev/null -w '%{http_code}\n' -H "Authorization: Bearer $C1" -H "$JSON" \
  -H 'Idempotency-Key: carrera-1' -d "{\"cuentaId\":\"$CUENTA\",\"monto\":10.00,\"metodo\":\"TARJETA\"}" $PAY/api/pagos | sort | uniq -c
```

**Esperado:** `1 201` y `9 200`. Se crea un solo pago y se cobra una sola vez (compruébalo con `saldo`).

## 10. Rechazos de negocio (201 RECHAZADO)

```bash
pagar p-9 40000.00 TARJETA
```

**Esperado:** `201` con `"estado":"RECHAZADO"` y `"motivoRechazo":"Saldo insuficiente"`. El pago se creó y queda consultable;
el rechazo es su resultado.

Pagar desde una cuenta de otro usuario:

```bash
CUENTA2=$(curl -s -H "Authorization: Bearer $C2" -H "$JSON" -d '{"moneda":"USD"}' $ACC/api/cuentas | jq -r .id)
curl -s -H "Authorization: Bearer $C1" -H "$JSON" -H 'Idempotency-Key: ajena-1' \
  -d "{\"cuentaId\":\"$CUENTA2\",\"monto\":10,\"metodo\":\"TARJETA\"}" $PAY/api/pagos | jq '{estado, motivoRechazo}'
```

**Esperado:** `RECHAZADO`, "Cuenta no encontrada", y la cuenta de cliente2 no se toca.

Consultas de pagos:

```bash
PAGO=$(curl -s -H "Authorization: Bearer $C1" "$PAY/api/pagos?tamano=1" | jq -r '.items[0].id')
curl -s -o /dev/null -w 'pago ajeno: %{http_code}\n' -H "Authorization: Bearer $C2" $PAY/api/pagos/$PAGO
curl -s -H "Authorization: Bearer $AD" "$PAY/api/admin/pagos?estado=RECHAZADO" | jq '{total}'
curl -s -o /dev/null -w 'pagina fuera de rango: %{http_code}\n' -H "Authorization: Bearer $C1" "$PAY/api/pagos?pagina=2147483647&tamano=100"
```

**Esperado:** `404`, un total mayor que 0, y `400`.

## 11. Caída de AccountService y recuperación de PENDIENTE

```bash
docker compose stop account-service
pagar rec-1 50.00 BILLETERA
```

**Esperado:** `503` "Servicio de cuentas no disponible" con `"pagoId"` y `"estado":"PENDIENTE"`.

```bash
REC=<pegue aquí el pagoId>
docker compose start account-service
```

Espera unos 30 s y consulta (o mira el log):

```bash
curl -s -H "Authorization: Bearer $C1" $PAY/api/pagos/$REC | jq '{estado}'
docker compose logs payment-service | grep -E "PENDIENTE|APROBADO|Recuperación" | tail -4
```

**Esperado:** `APROBADO`, y en el log "queda PENDIENTE…" seguido de "APROBADO" y "Recuperación: 1 pagos PENDIENTE procesados".
La recuperación usó el token de servicio (client credentials), porque el tuyo puede haber vencido.

Comprueba en las bases que se cobró una sola vez, con el id del pago como clave de la operación:

```bash
docker compose exec postgres-payments psql -U paylite -d payments -c "select estado, intentos, monto, comision from pago where id='$REC';"
docker compose exec postgres-accounts psql -U paylite -d accounts -c "select tipo, monto, operacion_id from movimiento where operacion_id='$REC';"
```

**Esperado:** `APROBADO` con `intentos` = 2, y **una sola** fila `DEBITO` con monto 50.50.

### Timeout (504)

`pause` congela el proceso: la conexión se abre pero nadie responde.

```bash
docker compose pause account-service
pagar lento-1 10.00 TARJETA
docker compose unpause account-service
```

**Esperado:** `504` "AccountService no respondió a tiempo" tras unos 6 s. Después lo recupera igual que arriba.

### REVISION_MANUAL (unos 2 minutos)

```bash
docker compose stop account-service
pagar rev-1 10.00 TARJETA            # anota el pagoId
```

Espera unos 100 s:

```bash
docker compose exec postgres-payments psql -U paylite -d payments -c "select estado, intentos, motivo_rechazo from pago where idempotency_key='rev-1';"
docker compose start account-service
```

**Esperado:** `REVISION_MANUAL`, `intentos` = 5, "Sin respuesta de AccountService tras 5 intentos.", sin débito en AccountService.

## 12. Garantías en la base de datos

Ni con SQL directo se puede dejar un saldo negativo:

```bash
docker compose exec postgres-accounts psql -U paylite -d accounts -c "update cuenta set saldo = -1 where id='$CUENTA';"
```

**Esperado:** `ERROR: ... violates check constraint "ck_cuenta_saldo_no_negativo"`.

Esquema y migraciones aplicadas:

```bash
docker compose exec postgres-accounts psql -U paylite -d accounts -c '\d movimiento' -c 'select * from "__EFMigrationsHistory";'
docker compose exec postgres-payments psql -U paylite -d payments -c '\d pago'
```

**Esperado:**
- `ux_movimiento_cuenta_operacion` es UNIQUE y existe la FK a `cuenta`.
- Están las migraciones `V1_CrearCuenta` y `V2_CrearMovimiento`.
- En `pago` están `ux_pago_owner_idempotency_key` y el índice parcial `ix_pago_pendientes_updated_at`.

## 13. Limpieza

Para apagarlo conservando los datos:

```bash
docker compose --profile apps down
```

Para apagarlo y borrar las bases (también reimporta el realm de Keycloak la próxima vez):

```bash
docker compose --profile apps down -v
```
