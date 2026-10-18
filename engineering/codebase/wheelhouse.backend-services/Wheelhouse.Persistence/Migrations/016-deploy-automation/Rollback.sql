-- Reverse 016-deploy-automation — dev/test recovery only. Every product's automation choices, the release watcher's
-- watermarks and any queued automatic deploy are lost; the defaults apply again on the next apply.

DROP TABLE IF EXISTS deployment_queue;
DROP TABLE IF EXISTS release_watermarks;
DROP TABLE IF EXISTS product_automation;
