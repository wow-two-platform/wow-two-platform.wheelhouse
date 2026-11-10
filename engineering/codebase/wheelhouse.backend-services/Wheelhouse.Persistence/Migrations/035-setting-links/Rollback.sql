-- Reverse 035-setting-links — dev/test recovery only. Every link is lost: each linking key renders its own stored value
-- or secret again, and the sources keep theirs. Idempotent.

DROP TABLE IF EXISTS setting_links;
