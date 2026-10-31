# syntax=docker/dockerfile:1
# Keysmith, the local rig's product for generated and linked setting keys (`rehearse.py generated`). Never a production
# image. openssl makes the certificate its generate command prints, curl answers the runner's smoke checks; the image
# runs as its own unprivileged user.
FROM python:3.12-alpine
RUN apk add --no-cache curl openssl && adduser -D -H -u 10001 keysmith
COPY keysmith.py /app/keysmith.py
USER keysmith
EXPOSE 8080
ENTRYPOINT ["python3", "/app/keysmith.py"]
CMD ["serve"]
