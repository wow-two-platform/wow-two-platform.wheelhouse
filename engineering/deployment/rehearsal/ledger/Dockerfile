# syntax=docker/dockerfile:1
# Ledger's identity service, the local rig's stand-in for Ocharo's (`rehearse.py set-reset`). Never a production image.
# It is WoW2's Identity image as its repository builds it locally (identity:local), so its migrate verbs (migrate, sets,
# check, rollback) are the be sdk's own tree over the real `identity` set. Its serving process only sleeps: the proof
# never signs anyone in, and Identity itself would boot only with its keys and sites.
FROM identity:local
ENTRYPOINT ["sleep", "infinity"]
