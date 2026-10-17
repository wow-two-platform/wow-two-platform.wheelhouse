-- Reverse 013-drop-test-environment — forward-only, so a no-op. The deleted test targets and their vitals readings
-- cannot be recreated, and the environment enum has no `test` value to read them with any more. Rolling back only
-- returns 013 to pending; applying it again deletes nothing that is not a test target.

SELECT 1;
