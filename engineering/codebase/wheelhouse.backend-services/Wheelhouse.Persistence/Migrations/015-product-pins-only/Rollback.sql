-- Reverse 015-product-pins-only — forward-only, so a no-op. The deleted workspace and service pins cannot be
-- recreated, and a product pin made from a service pin cannot be told apart from one the owner made. Rolling back only
-- returns 015 to pending; applying it again finds nothing to convert or delete.

SELECT 1;
