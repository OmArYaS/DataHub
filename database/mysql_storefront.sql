-- ═══════════════════════════════════════════════════════════════════════════
-- MySQL 8 storefront replica — attached in CI via a service container
-- (DATAHUB_MYSQL_CONN). DataHub only ever runs READ queries here; this file
-- ships the schema + deterministic seed the pipeline expects.
-- Run as: mysql -h <host> -u datahub_demo -p < mysql_storefront.sql
-- ═══════════════════════════════════════════════════════════════════════════

CREATE DATABASE IF NOT EXISTS datahub_retail;
USE datahub_retail;

CREATE TABLE IF NOT EXISTS storefront_orders (
    order_id     INT AUTO_INCREMENT PRIMARY KEY,
    order_ref    VARCHAR(32)   NOT NULL UNIQUE,
    customer_ref VARCHAR(32)   NOT NULL,
    channel      VARCHAR(32)   NOT NULL,
    order_state  VARCHAR(16)   NOT NULL,
    order_total  DECIMAL(10,2) NOT NULL,
    placed_at    DATETIME      NOT NULL,
    note         VARCHAR(200)  NULL,
    INDEX ux_storefront_order_ref (order_ref)
);

-- Re-seed deterministically (idempotent): the CI suite asserts the table
-- shape ("state, n" grouped by state → exactly one distinct state).
DELETE FROM storefront_orders;

INSERT INTO storefront_orders (order_ref, customer_ref, channel, order_state, order_total, placed_at, note) VALUES
    ('ORD-10001', 'cust_alfa',   'web',      'placed', 250.50, '2026-08-03 09:15:00', NULL),
    ('ORD-10002', 'cust_bravo',  'web',      'placed', 180.00, '2026-08-03 10:00:00', NULL),
    ('ORD-10003', 'cust_charlie','store',    'placed', 95.75,  '2026-08-05 11:30:00', 'gift wrap'),
    ('ORD-10004', 'cust_alfa',   'mobile',   'placed', 400.00, '2026-08-07 08:45:00', NULL),
    ('ORD-10005', 'cust_echo',   'web',      'placed', 60.25,  '2026-08-10 16:20:00', NULL),
    ('ORD-10006', 'cust_hotel',  'store',    'placed', 310.00, '2026-08-12 14:05:00', 'bulk order'),
    ('ORD-10007', 'cust_bravo',  'mobile',   'placed', 75.40,  '2026-08-15 19:55:00', NULL),
    ('ORD-10008', 'cust_charlie','web',      'placed', 480.90, '2026-08-18 12:35:00', NULL);

-- The catalog report reads:
--   SELECT s.order_ref, s.customer_ref, s.channel, s.order_total, s.placed_at
--   FROM storefront_orders s WHERE s.order_state = 'placed'
--   ORDER BY s.placed_at DESC
-- The integration suite consumes the LIMIT/OFFSET wrapper and a COUNT wrap.
