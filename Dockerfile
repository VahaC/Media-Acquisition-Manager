# syntax=docker/dockerfile:1.7

# Media Acquisition Manager in one image: the API with the built web app in its wwwroot (MAM-3).
#
#   docker buildx build -t vahac/media-acquisition-manager:local .
#
# Both build arguments are optional; see the api-build stage.

ARG DOTNET_VERSION=10.0
# 24 because frontend/package.json requires it ("engines").
ARG NODE_VERSION=24

# ── frontend-build ────────────────────────────────────────────────────────────────────────────────
# The web app is JavaScript and CSS, the same bytes for every architecture. Built once on the
# builder's own platform instead of again under arm64 emulation, where npm ci is several times slower.
FROM --platform=$BUILDPLATFORM node:${NODE_VERSION}-alpine AS frontend-build
WORKDIR /src/frontend
COPY frontend/package.json frontend/package-lock.json ./
RUN --mount=type=cache,id=mam-npm,target=/root/.npm,sharing=locked \
    npm ci
COPY frontend/ ./
RUN npm run build

# ── api-build ─────────────────────────────────────────────────────────────────────────────────────
# Also on the builder's platform: without a runtime identifier, `dotnet publish` produces portable IL
# plus the native SQLite library of every platform (runtimes/<rid>/native), so one publish serves
# amd64 and arm64 alike and nothing here runs under emulation.
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:${DOTNET_VERSION} AS api-build
# APP_VERSION: the version stamped into the API assembly. Unset, it is read from
# frontend/package.json, the version of record, so a plain `docker buildx build` stamps the right
# number without a flag to remember. The release job passes the VERSION it was started with.
# APP_REVISION: the commit, appended as build metadata (0.1.0+abc1234). .git is not in the build
# context, so without the argument there is no revision and none is appended.
ARG APP_VERSION
ARG APP_REVISION
WORKDIR /src
# Project files first, so the restore layer is reused until a package reference changes.
COPY src/MediaAcquisitionManager.Core/MediaAcquisitionManager.Core.csproj src/MediaAcquisitionManager.Core/
COPY src/MediaAcquisitionManager.Infrastructure/MediaAcquisitionManager.Infrastructure.csproj src/MediaAcquisitionManager.Infrastructure/
COPY src/MediaAcquisitionManager.Api/MediaAcquisitionManager.Api.csproj src/MediaAcquisitionManager.Api/
RUN --mount=type=cache,id=mam-nuget,target=/root/.nuget/packages,sharing=locked \
    dotnet restore src/MediaAcquisitionManager.Api/MediaAcquisitionManager.Api.csproj
COPY src/ src/
COPY frontend/package.json frontend/package.json
# An image that can't name its version must not be built: it would claim the csproj's 0.0.0-dev.
# No --no-restore: the packages live only in the cache mount, which BuildKit prunes on its own
# schedule, apart from the cached restore layer above. Publish's own restore is a no-op while the
# mount is warm and fetches the packages again after a prune, instead of failing with NETSDK1064.
RUN --mount=type=cache,id=mam-nuget,target=/root/.nuget/packages,sharing=locked \
    set -eu; \
    VERSION="${APP_VERSION:-$(grep -m1 '"version"' frontend/package.json | cut -d'"' -f4)}"; \
    if [ -z "$VERSION" ]; then echo "Could not read the version from frontend/package.json." >&2; exit 1; fi; \
    echo "Stamping the API with ${VERSION}${APP_REVISION:++$APP_REVISION}"; \
    dotnet publish src/MediaAcquisitionManager.Api/MediaAcquisitionManager.Api.csproj \
      -c Release -o /app/publish \
      -p:UseAppHost=false \
      -p:Version="$VERSION" \
      -p:InformationalVersion="${VERSION}${APP_REVISION:++$APP_REVISION}"

# ── runtime ───────────────────────────────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:${DOTNET_VERSION} AS runtime

# curl runs the HEALTHCHECK below. ffmpeg brings ffprobe, which reads the streams of library files
# (MAM-9). Ubuntu's package rather than a pinned static build: ffprobe parses files downloaded from
# the internet, and Ubuntu's security fixes for it arrive whenever the image is rebuilt on a newer
# base, without anyone having to bump a pin.
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl ffmpeg \
    && rm -rf /var/lib/apt/lists/*

# A fixed uid and gid, the ones the app runs as unless PUID and PGID name others (MAM-39), so a
# bind-mounted data directory can be given the right owner and mode up front
# (`sudo install -d -o 10001 -g 10001 -m 700 ./data`). The base image already has ubuntu (1000) and
# app (1654); 10001 is clear of both and of the ids a host usually hands out to people.
#
# /data holds the database and the generated secrets, so it is the owner's alone (0700). An empty
# named volume mounted there copies this owner and mode on first use; with PUID and PGID set, the
# entrypoint below hands it over to them.
# No --system: it only picks an id from the system range, and with an explicit one above that range
# useradd warns on every build.
RUN groupadd --gid 10001 mam \
    && useradd --uid 10001 --gid mam --no-create-home --home-dir /nonexistent --shell /usr/sbin/nologin mam \
    && mkdir /data \
    && chown mam:mam /data \
    && chmod 0700 /data

WORKDIR /app
# Owned by root, not by the app's user: a compromised process can't rewrite its own code or the web
# app it serves. The only persistent place the app writes is /data (besides the usual scratch /tmp).
COPY --from=api-build /app/publish ./
COPY --from=frontend-build /src/frontend/dist ./wwwroot/

# No USER: the container starts as root, and only so that this script can hand the data directory
# over to PUID and PGID, the media folders' owner, whose ids replacement (0.3.0) needs to rename
# files there (MAM-39). It changes the owner of /data and of nothing else, then drops to those ids —
# the mam user above when they are unset — with every capability gone, no supplementary group but
# those Docker gave the container (group_add), and no way to gain more back, and only then starts
# the app. A container given a user (compose's
# user:, docker run --user) starts as that user instead, and the script just starts the app.
# ADR 0036 says why. Owned by root like the app, so the app can't rewrite it either.
COPY --chmod=0755 docker-entrypoint.sh /usr/local/bin/docker-entrypoint.sh

# 8118 rather than the base image's 8080, which qBittorrent's web UI and many other self-hosted
# services already use; the same number inside and outside the container keeps `docker ps` and the
# logs unambiguous. Set through ASPNETCORE_HTTP_PORTS, the variable the base image uses, rather than
# ASPNETCORE_URLS: URLS on top of it makes Kestrel warn on every start that it overrides the ports.
ENV ASPNETCORE_ENVIRONMENT=Production \
    ASPNETCORE_HTTP_PORTS=8118 \
    MAM_Storage__DataDirectory=/data
EXPOSE 8118

# Readiness, not liveness: "healthy" should mean the app can serve, and readiness is the endpoint
# that checks the database opens and is fully migrated. Docker only reports the status, it doesn't
# restart an unhealthy container, so the restart-loop concern that keeps liveness check-free doesn't
# apply here. --fail-with-body keeps the JSON of a failing check in `docker inspect`, where it names
# the reason. Probes run every 2 s while starting (--start-interval, Docker 25+), so deploy.sh sees
# "healthy" within seconds rather than after the first 30 s interval. The start period is 150 s
# because a start can legitimately take over two minutes: after a container was killed mid-migration,
# the app waits until the migration lock it left is two minutes old before it removes it
# (DatabaseMigrationExtensions), and Kestrel doesn't listen until then.
HEALTHCHECK --interval=30s --timeout=5s --start-period=150s --start-interval=2s --retries=3 \
    CMD curl -sS --fail-with-body http://localhost:8118/api/health/ready || exit 1

# The app's command line is part of ENTRYPOINT, not a CMD, so arguments after the image name
# (docker run, compose's command:) still reach the app as configuration, as they did before the
# script (MAM-39).
ENTRYPOINT ["/usr/local/bin/docker-entrypoint.sh", "dotnet", "MediaAcquisitionManager.Api.dll"]
