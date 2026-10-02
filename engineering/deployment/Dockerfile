# syntax=docker/dockerfile:1
FROM node:24.11.0-alpine AS spa
RUN npm install --global pnpm@10.33.2
WORKDIR /src/wheelhouse.frontend-services
COPY wheelhouse.frontend-services/package.json wheelhouse.frontend-services/pnpm-lock.yaml wheelhouse.frontend-services/pnpm-workspace.yaml ./
COPY wheelhouse.frontend-services/apps/web/package.json ./apps/web/package.json
RUN pnpm install --frozen-lockfile
COPY wheelhouse.frontend-services/ ./
# The build's version from the shared publish workflow (X.Y.Z or a pre-release); unset in a local build. Declared
# after the dependency layers, since a changed build argument misses the cache of every RUN after it.
ARG APP_VERSION
RUN pnpm build

# Stays in global.json's 10.0.3xx band; the floating 10.0 tag moved to 10.0.4xx, which the pin refuses.
FROM mcr.microsoft.com/dotnet/sdk:10.0.302 AS build
ARG TARGETARCH
WORKDIR /src
COPY wheelhouse.backend-services/ ./
COPY --from=spa /src/wheelhouse.frontend-services/apps/web/dist ./Wheelhouse.Api/wwwroot
# MSBuild reads APP_VERSION from the environment; Directory.Build.props turns it into the product version.
ARG APP_VERSION
RUN case "$TARGETARCH" in amd64) rid=linux-x64 ;; arm64) rid=linux-arm64 ;; *) exit 1 ;; esac \
    && dotnet publish Wheelhouse.Api/Wheelhouse.Api.csproj -c Release -o /app -r "$rid" --self-contained false -p:BuildSpa=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
RUN apt-get update && apt-get install -y --no-install-recommends curl python3 openssh-client \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /app ./
COPY wheelhouse.runner-services/runner.py wheelhouse.runner-services/transport.py wheelhouse.runner-services/fleet.py \
     wheelhouse.runner-services/artifacts.py wheelhouse.runner-services/catalog.py wheelhouse.runner-services/inventory.py /app/runner/
RUN mkdir -p /data/keys /data/deployments && chown -R app:app /data
ENV ASPNETCORE_HTTP_PORTS=8080 DataProtection__KeyPath=/data/keys
USER app
EXPOSE 8080
HEALTHCHECK --interval=15s --timeout=5s --start-period=60s --retries=4 \
    CMD curl --fail --silent http://localhost:8080/api/system/ready || exit 1
ENTRYPOINT ["dotnet", "Wheelhouse.Api.dll"]
