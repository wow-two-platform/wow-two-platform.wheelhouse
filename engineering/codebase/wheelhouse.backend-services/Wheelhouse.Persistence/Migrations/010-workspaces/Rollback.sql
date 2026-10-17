-- Reverse 010-workspaces — dev/test recovery only. Products leave their workspaces and the workspaces go; which
-- product sat in which workspace, and the workspaces' order, are lost.

ALTER TABLE products DROP CONSTRAINT IF EXISTS fk_products_workspaces_workspace_id;
DROP INDEX IF EXISTS ix_products_workspace_id;
ALTER TABLE products DROP COLUMN IF EXISTS workspace_id;
DROP TABLE IF EXISTS workspaces;
