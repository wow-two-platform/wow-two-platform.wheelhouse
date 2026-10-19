-- Reverse 022-fleet-rollouts — dev/test recovery only. Every fleet record goes; the deployments they submitted stay in
-- the runner's history.

DROP TABLE IF EXISTS fleet_rollout_items;
DROP TABLE IF EXISTS fleet_rollouts;
