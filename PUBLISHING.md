# Publishing

How the Docker image is built and published, and how to cut a release. To run the image, see
[Quick start](README.md#quick-start-docker-compose) and [Deployment](README.md#deployment) in the
README.

## The pipeline

The [Jenkinsfile](Jenkinsfile) polls `main` every two minutes and builds each new commit:

1. **Checkout** — a fresh clone of `main`. It notes the release tag on the commit, if there is one,
   and which files changed since the last successful build.
2. **Backend** — `dotnet build -c Release` and the whole test suite (`run-tests.sh`); the JUnit
   reports appear on the build page. Skipped when every change since the last successful build is
   documentation (`*.md` files and `docs/`).
3. **Frontend** — `npm ci`, `npm run lint`, `npm run build` (which also type-checks) and
   `npm run test`.
4. **Publish** — builds the image for `linux/amd64` and `linux/arm64` and pushes it to Docker Hub.
   It runs only when every stage above passed or was skipped.

| Built commit | Tags pushed to `vahac/media-acquisition-manager` |
| --- | --- |
| Any commit on `main` | `latest` and `sha-<first 7 characters of the commit>` |
| A commit tagged `vX.Y.Z` | The same, plus `X.Y.Z` and `X.Y` |

The API inside the image is stamped with the version from `frontend/package.json` and the commit, for
example `0.1.0+abc1234`. A `vX.Y.Z` tag that differs from the version in `frontend/package.json`
stops the build before anything is pushed, so an image's tag and the version the app shows always
agree.

## One-time setup

1. **Docker Hub.** Create an access token with *Read & Write* scope (Account settings → Personal
   access tokens). The first push creates the repository `vahac/media-acquisition-manager`; make it
   public or private there.
2. **Jenkins credentials** (Manage Jenkins → Credentials), both of the kind *Username with password*:
   - `dockerhub-creds` — the Docker Hub user and the access token from step 1;
   - `github-pat` — a GitHub user and a personal access token that can read this repository.
3. **The Jenkins host** needs the Docker CLI with the buildx plugin and access to the Docker daemon,
   and the Docker Pipeline and JUnit plugins (Backend and Frontend run inside the
   `mcr.microsoft.com/dotnet/sdk:10.0` and `node:24-alpine` images). The Publish stage creates a
   buildx builder named `mam-builder` (the `docker-container` driver) on its first run and keeps it:
   it brings its own emulation for the arm64 half of the image, and its cache makes later builds
   faster. `docker buildx rm mam-builder` drops it and its cache.
4. **The job.** New Item → *Pipeline*. Under *Pipeline*, choose *Pipeline script from SCM*: Git,
   repository `https://github.com/VahaC/Media-Acquisition-Manager.git`, credentials `github-pat`,
   branch `*/main`, script path `Jenkinsfile`. The polling trigger is defined in the Jenkinsfile and
   takes effect after the first run, so start that one with *Build Now*.

## Cutting a release

1. Make sure `frontend/package.json` has the version you are releasing, and that its CHANGELOG
   section and its release note (`frontend/src/release-notes/<version>.md`) describe the release.
2. Tag the commit and push the tag with it:

   ```bash
   git tag v0.1.0
   git push origin main v0.1.0
   ```

3. When the tagged commit was already built before the tag existed, polling sees no new commit:
   start the job with *Build Now*. That build pushes `0.1.0` and `0.1` as well as `latest`.

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
