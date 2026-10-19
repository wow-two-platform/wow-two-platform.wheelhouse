# The rig's stand-in for tailscale/tailscale on a fresh host: a site node that spends its single-use key at the rig's
# fake Tailscale API once, keeps its state like TS_AUTH_ONCE, and stays up. Tagged as the image the kit names.
FROM python:3.12-alpine
COPY site-node.py /site-node.py
ENTRYPOINT ["python3", "/site-node.py"]
