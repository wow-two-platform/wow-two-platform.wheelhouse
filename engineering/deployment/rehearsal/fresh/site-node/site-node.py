import json
import os
import signal
import sys
import time
import urllib.request

signal.signal(signal.SIGTERM, lambda *_: sys.exit(0))
state = os.path.join(os.environ.get("TS_STATE_DIR", "/var/lib/tailscale"), "tailscaled.state")
if not os.path.exists(state):
    tags = [argument.split("=", 1)[1] for argument in os.environ.get("TS_EXTRA_ARGS", "").split()
            if argument.startswith("--advertise-tags=")]
    body = json.dumps({"authKey": os.environ.get("TS_AUTHKEY", ""), "hostname": os.environ["TS_HOSTNAME"],
                       "tags": tags[0].split(",") if tags else []}).encode()
    request = urllib.request.Request(os.environ.get("RIG_TAILSCALE_API", "http://172.31.250.5:8080") + "/rig/join",
                                     data=body, method="POST", headers={"Content-Type": "application/json"})
    answer = json.loads(urllib.request.urlopen(request, timeout=20).read())
    with open(state, "w") as stream:
        json.dump(answer, stream)
while True:
    time.sleep(3600)
