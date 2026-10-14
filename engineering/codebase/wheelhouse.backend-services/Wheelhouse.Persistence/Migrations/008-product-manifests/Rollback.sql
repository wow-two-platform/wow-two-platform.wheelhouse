-- Reverse 008-product-manifests — drop the cached manifests. Dev/test recovery only: nothing is lost, since every row is
-- a copy of a file in a repository; the next read of each product fills its row again.

DROP TABLE IF EXISTS product_manifests;
