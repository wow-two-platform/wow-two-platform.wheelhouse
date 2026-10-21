-- Reverse 031-product-components — dev/test recovery only. A product's further repositories go; each product keeps its
-- primary repository and release source.

ALTER TABLE products DROP COLUMN IF EXISTS components;
