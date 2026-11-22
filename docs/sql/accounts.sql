CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory" (
    "MigrationId" character varying(150) NOT NULL,
    "ProductVersion" character varying(32) NOT NULL,
    CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY ("MigrationId")
);

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261122173850_V1_CrearCuenta') THEN
    CREATE TABLE cuenta (
        id uuid NOT NULL,
        owner_id text NOT NULL,
        saldo numeric(19,2) NOT NULL DEFAULT 0.0,
        moneda character varying(3) NOT NULL,
        created_at timestamp with time zone NOT NULL DEFAULT (now()),
        CONSTRAINT "PK_cuenta" PRIMARY KEY (id),
        CONSTRAINT ck_cuenta_saldo_no_negativo CHECK (saldo >= 0)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261122173850_V1_CrearCuenta') THEN
    CREATE INDEX ix_cuenta_owner_id ON cuenta (owner_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261122173850_V1_CrearCuenta') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20261122173850_V1_CrearCuenta', '10.0.15');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261122174645_V2_CrearMovimiento') THEN
    CREATE TABLE movimiento (
        id uuid NOT NULL,
        cuenta_id uuid NOT NULL,
        tipo character varying(10) NOT NULL,
        monto numeric(19,2) NOT NULL,
        operacion_id character varying(100) NOT NULL,
        created_at timestamp with time zone NOT NULL DEFAULT (now()),
        CONSTRAINT "PK_movimiento" PRIMARY KEY (id),
        CONSTRAINT ck_movimiento_monto_positivo CHECK (monto > 0),
        CONSTRAINT "FK_movimiento_cuenta_cuenta_id" FOREIGN KEY (cuenta_id) REFERENCES cuenta (id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261122174645_V2_CrearMovimiento') THEN
    CREATE UNIQUE INDEX ux_movimiento_cuenta_operacion ON movimiento (cuenta_id, operacion_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261122174645_V2_CrearMovimiento') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20261122174645_V2_CrearMovimiento', '10.0.15');
    END IF;
END $EF$;
COMMIT;

