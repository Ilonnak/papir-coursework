-- =============================================================================
-- Ідемпотентне доповнення схеми coursework_web (Міграція 001)
-- Зберігає наявні 7 таблиць і дані без руйнівних операцій (без DROP TABLE / TRUNCATE).
-- =============================================================================

BEGIN;

-- 1. Таблиця історії застосованих схемних оновлень
CREATE TABLE IF NOT EXISTS "schema_migrations" (
    "version" varchar(100) PRIMARY KEY,
    "applied_at" timestamp NOT NULL DEFAULT (now())
);

-- 2. Перевірка відсутності конфліктів email за регістром у таблиці users
DO $$
DECLARE
    dup_count int;
BEGIN
    SELECT COUNT(*) INTO dup_count
    FROM (
        SELECT LOWER(TRIM("email"))
        FROM "users"
        GROUP BY LOWER(TRIM("email"))
        HAVING COUNT(*) > 1
    ) t;

    IF dup_count > 0 THEN
        RAISE EXCEPTION 'Виявлено конфлікти email без урахування регістру в таблиці users (%). Усуньте їх перед створенням індексу.', dup_count;
    END IF;
END $$;

-- Нечутливий до регістру унікальний індекс для email
CREATE UNIQUE INDEX IF NOT EXISTS "ix_users_email_lower" ON "users" (LOWER("email"));

-- Нечутливий до регістру унікальний індекс для назви категорії
CREATE UNIQUE INDEX IF NOT EXISTS "ix_categories_name_lower" ON "categories" (LOWER("name"));

-- 3. Зміна зовнішнього ключа products -> categories з ON DELETE CASCADE на ON DELETE RESTRICT,
--    щоб непорожню категорію не можна було видалити разом із товарами каскадом.
DO $$
BEGIN
    IF EXISTS (
        SELECT 1 FROM information_schema.table_constraints
        WHERE table_schema = 'public'
          AND table_name = 'products'
          AND constraint_name = 'products_category_id_fkey'
    ) THEN
        ALTER TABLE "products" DROP CONSTRAINT "products_category_id_fkey";
    END IF;

    ALTER TABLE "products"
        ADD CONSTRAINT "products_category_id_fkey"
        FOREIGN KEY ("category_id") REFERENCES "categories" ("id")
        ON DELETE RESTRICT DEFERRABLE INITIALLY IMMEDIATE;
END $$;

-- 4. Доповнення таблиці products окремим полем характеристик (specifications) та обмеженнями CHECK
ALTER TABLE "products"
    ADD COLUMN IF NOT EXISTS "specifications" text NOT NULL DEFAULT '';

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint WHERE conname = 'chk_products_price_nonnegative'
    ) THEN
        ALTER TABLE "products" ADD CONSTRAINT "chk_products_price_nonnegative" CHECK ("price" >= 0);
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint WHERE conname = 'chk_products_stock_nonnegative'
    ) THEN
        ALTER TABLE "products" ADD CONSTRAINT "chk_products_stock_nonnegative" CHECK ("stock_quantity" >= 0);
    END IF;
END $$;

-- 5. Обмеження додатної кількості у cart_items
DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint WHERE conname = 'chk_cart_items_quantity_positive'
    ) THEN
        ALTER TABLE "cart_items" ADD CONSTRAINT "chk_cart_items_quantity_positive" CHECK ("quantity" > 0);
    END IF;
END $$;

-- 6. Доповнення таблиці orders:
--    контакти отримувача як знімок на момент оформлення,
--    спосіб отримання (самовивіз), спосіб оплати (при отриманні),
--    стан оплати (is_paid, paid_at) та ключ ідемпотентності (idempotency_key) для захисту від повторного оформлення.
ALTER TABLE "orders"
    ADD COLUMN IF NOT EXISTS "recipient_first_name" varchar(100) NOT NULL DEFAULT '',
    ADD COLUMN IF NOT EXISTS "recipient_last_name" varchar(100) NOT NULL DEFAULT '',
    ADD COLUMN IF NOT EXISTS "recipient_phone" varchar(30) NOT NULL DEFAULT '',
    ADD COLUMN IF NOT EXISTS "recipient_email" varchar(255) NOT NULL DEFAULT '',
    ADD COLUMN IF NOT EXISTS "comment" varchar(500),
    ADD COLUMN IF NOT EXISTS "delivery_method" varchar(50) NOT NULL DEFAULT 'Самовивіз',
    ADD COLUMN IF NOT EXISTS "pickup_location" varchar(255) NOT NULL DEFAULT '',
    ADD COLUMN IF NOT EXISTS "payment_method" varchar(50) NOT NULL DEFAULT 'Оплата при отриманні',
    ADD COLUMN IF NOT EXISTS "is_paid" boolean NOT NULL DEFAULT false,
    ADD COLUMN IF NOT EXISTS "paid_at" timestamp,
    ADD COLUMN IF NOT EXISTS "idempotency_key" varchar(100);

CREATE UNIQUE INDEX IF NOT EXISTS "ix_orders_user_idempotency_key"
    ON "orders" ("user_id", "idempotency_key")
    WHERE "idempotency_key" IS NOT NULL;

-- 7. Доповнення таблиці order_items назвою товару на момент замовлення (product_name_snapshot) та CHECK
ALTER TABLE "order_items"
    ADD COLUMN IF NOT EXISTS "product_name_snapshot" varchar(255) NOT NULL DEFAULT '';

-- Якщо в order_items вже були записи з порожнім знімком назви, заповнюємо з таблиці products
UPDATE "order_items" oi
SET "product_name_snapshot" = p."name"
FROM "products" p
WHERE oi."product_id" = p."id" AND oi."product_name_snapshot" = '';

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint WHERE conname = 'chk_order_items_price_nonnegative'
    ) THEN
        ALTER TABLE "order_items" ADD CONSTRAINT "chk_order_items_price_nonnegative" CHECK ("price_at_purchase" >= 0);
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint WHERE conname = 'chk_order_items_quantity_positive'
    ) THEN
        ALTER TABLE "order_items" ADD CONSTRAINT "chk_order_items_quantity_positive" CHECK ("quantity" > 0);
    END IF;
END $$;

-- 8. Початкові статуси замовлень (без дублікатів)
INSERT INTO "order_statuses" ("name")
SELECT s.name
FROM (VALUES ('Нове'), ('В обробці'), ('Виконане'), ('Скасоване')) AS s(name)
WHERE NOT EXISTS (
    SELECT 1 FROM "order_statuses" os WHERE os."name" = s.name
);

-- Фіксація виконання міграції
INSERT INTO "schema_migrations" ("version", "applied_at")
VALUES ('001_extend_existing_schema', now())
ON CONFLICT ("version") DO NOTHING;

COMMIT;
