CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory" (
    "MigrationId" character varying(150) NOT NULL,
    "ProductVersion" character varying(32) NOT NULL,
    CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY ("MigrationId")
);

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261122174452_V1_CrearPago') THEN
    CREATE TABLE pago (
        id uuid NOT NULL,
        owner_id text NOT NULL,
        cuenta_id uuid NOT NULL,
        monto numeric(19,2) NOT NULL,
        comision numeric(19,2) NOT NULL,
        metodo character varying(20) NOT NULL,
        estado character varying(20) NOT NULL,
        idempotency_key character varying(100) NOT NULL,
        motivo_rechazo character varying(500),
        intentos integer NOT NULL DEFAULT 0,
        created_at timestamp with time zone NOT NULL,
        updated_at timestamp with time zone NOT NULL,
        CONSTRAINT "PK_pago" PRIMARY KEY (id),
        CONSTRAINT ck_pago_comision_no_negativa CHECK (comision >= 0),
        CONSTRAINT ck_pago_estado CHECK (estado IN ('PENDIENTE','APROBADO','RECHAZADO','REVISION_MANUAL')),
        CONSTRAINT ck_pago_monto_positivo CHECK (monto > 0)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261122174452_V1_CrearPago') THEN
    CREATE INDEX ix_pago_owner_created ON pago (owner_id, created_at);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261122174452_V1_CrearPago') THEN
    CREATE INDEX ix_pago_pendientes ON pago (created_at) WHERE estado = 'PENDIENTE';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261122174452_V1_CrearPago') THEN
    CREATE UNIQUE INDEX ux_pago_owner_idempotency_key ON pago (owner_id, idempotency_key);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261122174452_V1_CrearPago') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20261122174452_V1_CrearPago', '10.0.15');
    END IF;
END $EF$;
COMMIT;

