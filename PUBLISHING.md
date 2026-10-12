# Publishing

How the Docker image is built and published, and how to cut a release. To run the image, see
[Quick start](README.md#quick-start-docker-compose) and [Deployment](README.md#deployment) in the
README.

## The pipelines

Two Jenkins jobs, and only one of them ever pushes an image.

**CI — [Jenkinsfile](Jenkinsfile).** Polls `main` every two minutes and checks each new commit. It
never pushes anything.

1. **Checkout** — a fresh clone of `main`, noting which files changed since the last successful
   build.
2. **Backend** — `dotnet build -c Release` and the whole test suite (`run-tests.sh`); the JUnit
   reports appear on the build page. Skipped when every change since the last successful build is
   documentation (`*.md` files and `docs/`).
3. **Frontend** — `npm ci`, `npm run lint`, `npm run build` (which also type-checks) and
   `npm run test`.

**Release — [Jenkinsfile.publish](Jenkinsfile.publish).** Started only by hand, with a `VERSION`.
It has no trigger: pushing to Docker Hub is a release, and a release is a decision.

1. **Checkout** — a fresh clone of `main`. `VERSION` must be `X.Y.Z` and equal the version in
   `frontend/package.json`, or the job stops.
2. **Backend** and **Frontend** — the same checks as CI, none skipped, on the tree the image is
   built from.
3. **Publish** — builds the image for `linux/amd64` and `linux/arm64` and pushes it to
   `vahac/media-acquisition-manager` as `X.Y.Z`, `X.Y` and `latest`. A version that is already on
   Docker Hub is rebuilt, and its tags move to the new image.

The API inside the image is stamped with the version and the commit, for example `0.1.0+abc1234`.

## One-time setup

1. **Docker Hub.** Create an access token with *Read & Write* scope (Account settings → Personal
   access tokens). The first push creates the repository `vahac/media-acquisition-manager`; make it
   public or private there.
2. **Jenkins credentials** (Manage Jenkins → Credentials), both of the kind *Username with password*:
   - `dockerhub-creds` — the Docker Hub user and the access token from step 1 (the release job
     only);
   - `github-pat` — a GitHub user and a personal access token that can read this repository.
3. **The Jenkins host** needs the Docker CLI with the buildx plugin and access to the Docker daemon,
   and the Docker Pipeline and JUnit plugins (Backend and Frontend run inside the
   `mcr.microsoft.com/dotnet/sdk:10.0` and `node:24-alpine` images). The release job creates a
   buildx builder named `mam-builder` (the `docker-container` driver, which brings its own emulation
   for the arm64 half of the image) for each run and removes it afterwards, so no BuildKit container
   stays running between releases.
4. **The jobs.** Two items of the kind *Pipeline*, each with *Pipeline script from SCM*: Git,
   repository `https://github.com/VahaC/Media-Acquisition-Manager.git`, credentials `github-pat`,
   branch `*/main`.
   - `Media-Acquisition-Manager-ci` with script path `Jenkinsfile`. Its polling trigger is defined
     in the Jenkinsfile and takes effect after the first run, so start that one with *Build Now*.
   - `Media-Acquisition-Manager-publish` with script path `Jenkinsfile.publish`, and no build
     trigger of any kind. Its `VERSION` parameter appears after the first run; that run has no
     version and stops at once.

## Cutting a release

1. Make sure `frontend/package.json` on `main` has the version you are releasing, that its
   CHANGELOG section and its release note (`frontend/src/release-notes/<version>.md`) describe the
   release, and that CI is green for that commit.
2. Build the image from that commit and check whom it runs as (MAM-39, ADR 0036) — the default
   user; `PUID` and `PGID`, with its data handed over, modes kept, the app left no capability and
   only its `group_add` group, and back; a `user:` line; the ids it refuses, and root without
   capabilities (`cap_drop: ALL`); a start with nothing to hand over; a media folder that keeps its
   owner; and a quick `docker stop` —, about two minutes:

   ```bash
   docker build -t vahac/media-acquisition-manager:local .
   bash tools/check-image-user.sh vahac/media-acquisition-manager:local
   ```

   It pushes nothing and removes every container and volume it made; it must end with "All 8
   scenarios passed." Neither Jenkins job runs it.
3. Open `Media-Acquisition-Manager-publish` → *Build with Parameters*, enter the version (e.g.
   `0.1.0`) and start it. When it finishes, `0.1.0`, `0.1` and `latest` point at the new image.
4. To rebuild a release that is already out — a fix, or newer base images for security updates —
   without a new version number, start the job again with the same version, as often as you need.
   Installations that run that tag pick up the rebuild with their next `bash deploy.sh`.

## Building by hand

A local image to try, tagged `:local` and never pushed:

```bash
docker compose -f docker-compose.build.yml up -d --build
```

A multi-architecture push without Jenkins. The default builder of Docker Desktop and of a plain
Docker Engine (the `docker` driver) can't build for two platforms at once, so create a
`docker-container` builder first; it brings its own arm64 emulation. Once is enough — skip the
`create` when `docker buildx inspect mam-builder` already finds it:

```bash
docker buildx create --name mam-builder --driver docker-container --bootstrap
docker login
docker buildx build --builder mam-builder --platform linux/amd64,linux/arm64 \
  --build-arg APP_REVISION="$(git rev-parse --short=7 HEAD)" \
  --tag vahac/media-acquisition-manager:0.1.0 --push .
```

Without `--build-arg APP_VERSION=<version>` the API is stamped with the version in
`frontend/package.json`; without `APP_REVISION` no commit is appended.
