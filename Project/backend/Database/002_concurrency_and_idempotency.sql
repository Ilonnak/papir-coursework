-- =============================================================================
-- Ідемпотентне доповнення схеми coursework_web (Міграція 002)
-- Додає:
-- 1. products.version (int NOT NULL DEFAULT 1) для оптимістичного контролю конкурентних змін товару та залишку.
-- 2. products.seed_key (varchar(100) NULL) з унікальним індексом для незмінної ідентифікації демонстраційних товарів навіть після ручного перейменування.
-- 3. orders.idempotency_payload_hash (varchar(128)) для перевірки незмінності змісту запиту при повторі IdempotencyKey.
-- =============================================================================

BEGIN;

CREATE TABLE IF NOT EXISTS "schema_migrations" (
    "version" varchar(100) PRIMARY KEY,
    "applied_at" timestamp NOT NULL DEFAULT (now())
);

ALTER TABLE "products"
    ADD COLUMN IF NOT EXISTS "version" int NOT NULL DEFAULT 1;

ALTER TABLE "products"
    ADD COLUMN IF NOT EXISTS "seed_key" varchar(100);

CREATE UNIQUE INDEX IF NOT EXISTS "ix_products_seed_key"
    ON "products" ("seed_key")
    WHERE "seed_key" IS NOT NULL;

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint WHERE conname = 'chk_products_version_positive'
    ) THEN
        ALTER TABLE "products" ADD CONSTRAINT "chk_products_version_positive" CHECK ("version" >= 1);
    END IF;
END $$;

ALTER TABLE "orders"
    ADD COLUMN IF NOT EXISTS "idempotency_payload_hash" varchar(128);

INSERT INTO "schema_migrations" ("version", "applied_at")
VALUES ('002_concurrency_and_idempotency', now())
ON CONFLICT ("version") DO NOTHING;

COMMIT;
