// Builds, tests and publishes Media Acquisition Manager (MAM-3).
//
// Every build of main runs the .NET build and tests and the frontend lint, build and tests, then
// pushes a multi-architecture image to Docker Hub as :latest and :sha-<commit>. When the commit
// carries a release tag vX.Y.Z, the same image is also pushed as :X.Y.Z and :X.Y. PUBLISHING.md has
// the one-time Jenkins setup and the release steps.
pipeline {
    agent none

    options {
        disableConcurrentBuilds()
        // Every stage below declares an agent, and a declarative stage with an agent runs its own
        // implicit `checkout scm` first. The source is fetched once, in Checkout, and stashed; an
        // implicit fetch in a later stage would only add a way for a network blip to fail a build
        // whose source is already on disk.
        skipDefaultCheckout(true)
    }

    triggers {
        pollSCM('H/2 * * * *')
    }

    environment {
        IMAGE = 'vahac/media-acquisition-manager'
    }

    stages {
        stage('Checkout') {
            agent any
            steps {
                // A fresh clone each build: files left over from an earlier build (stale test
                // results, a deleted source file) must not reach the tests or the stash.
                deleteDir()
                script {
                    def scmVars = git branch: 'main',
                        credentialsId: 'github-pat',
                        url: 'https://github.com/VahaC/Media-Acquisition-Manager.git'
                    stash name: 'source', includes: '**'

                    env.SHORT_SHA = scmVars.GIT_COMMIT.substring(0, 7)

                    // The highest vX.Y.Z tag on the built commit, if any. The git step fetches tags,
                    // so a tag pushed with its commit is seen by that commit's build; a tag added to
                    // an already built commit needs a manual "Build Now".
                    def releaseVersion = sh(
                        script: '''git tag --points-at HEAD | grep -E '^v[0-9]+[.][0-9]+[.][0-9]+$' | sed 's/^v//' | sort -V | tail -n 1''',
                        returnStdout: true
                    ).trim()
                    if (releaseVersion) {
                        // frontend/package.json is the version of record: the web app shows it and
                        // the API is stamped with it. A tag that disagrees would publish an image
                        // whose tag and "About" say different versions, so it stops the build.
                        def packageVersion = sh(
                            script: '''grep -m1 '"version"' frontend/package.json | cut -d'"' -f4''',
                            returnStdout: true
                        ).trim()
                        if (releaseVersion != packageVersion) {
                            error "Tag v${releaseVersion} does not match the version in frontend/package.json (${packageVersion})."
                        }
                        env.RELEASE_VERSION = releaseVersion
                    }

                    // The backend stage is skipped when every change since the last successful build
                    // is documentation (*.md, docs/). Anything else runs it, frontend files included:
                    // the backend tests read frontend/index.html (the CSP hash of its theme script).
                    // A force-push can drop the last green commit from main, and then the fresh clone
                    // above doesn't have it: git diff would fail every build from then on, so such a
                    // build runs everything, like the first one.
                    def previous = scmVars.GIT_PREVIOUS_SUCCESSFUL_COMMIT
                    def comparable = previous && sh(script: "git cat-file -e ${previous}^{commit}", returnStatus: true) == 0
                    def changedFiles = []
                    if (comparable) {
                        changedFiles = sh(
                            script: "git diff --name-only ${previous} ${scmVars.GIT_COMMIT}",
                            returnStdout: true
                        ).trim().split('\n').findAll { it }
                    }
                    def isDocOnly = { String file -> file.endsWith('.md') || file.startsWith('docs/') }
                    env.RUN_BACKEND = (!comparable || changedFiles.any { !isDocOnly(it) }).toString()
                }
            }
        }

        stage('Backend') {
            agent {
                docker { image 'mcr.microsoft.com/dotnet/sdk:10.0' }
            }
            when {
                // Decided before the agent starts, so a skipped stage doesn't pull or start the SDK
                // container just to skip.
                beforeAgent true
                expression { env.RUN_BACKEND == 'true' }
            }
            steps {
                unstash 'source'
                sh 'dotnet restore MediaAcquisitionManager.slnx'
                sh 'dotnet build MediaAcquisitionManager.slnx -c Release --no-restore'
                // Every test project, as concurrent shards; see run-tests.sh. Each project's
                // .runsettings excludes Category=RequiresDocker, and there are no such tests yet.
                sh 'NO_BUILD=1 bash run-tests.sh'
            }
            post {
                always {
                    junit allowEmptyResults: true, testResults: 'tests/**/TestResults/test-results-*.xml'
                }
            }
        }

        stage('Frontend') {
            agent {
                docker { image 'node:24-alpine' }
            }
            steps {
                unstash 'source'
                dir('frontend') {
                    sh 'npm ci'
                    // Lint and the type check are separate from the tests: Vitest strips types
                    // without checking them, and `npm run build` is what runs `tsc -b`.
                    sh 'npm run lint'
                    sh 'npm run build'
                    sh 'npm run test'
                }
            }
        }

        stage('Publish') {
            // Reached only when every stage above passed or was skipped.
            agent any
            steps {
                unstash 'source'
                withCredentials([usernamePassword(credentialsId: 'dockerhub-creds', usernameVariable: 'DOCKERHUB_USER', passwordVariable: 'DOCKERHUB_TOKEN')]) {
                    sh '''
                        set -eu

                        tags="--tag $IMAGE:latest --tag $IMAGE:sha-$SHORT_SHA"
                        version_arg=""
                        if [ -n "${RELEASE_VERSION:-}" ]; then
                            # X.Y follows the newest patch of its minor; X.Y.Z never moves.
                            tags="$tags --tag $IMAGE:$RELEASE_VERSION --tag $IMAGE:${RELEASE_VERSION%.*}"
                            version_arg="--build-arg APP_VERSION=$RELEASE_VERSION"
                        fi

                        printf '%s' "$DOCKERHUB_TOKEN" | docker login --username "$DOCKERHUB_USER" --password-stdin

                        # A builder that outlives the build keeps BuildKit's layer and package caches,
                        # so an unchanged apt or npm layer isn't rebuilt under arm64 emulation every
                        # time. --pull still checks the base images on every build, and a new base
                        # (Microsoft rebuilds aspnet:10.0 for every .NET patch) invalidates the cached
                        # layers above it, which is how Ubuntu's security fixes reach the image.
                        docker buildx inspect mam-builder > /dev/null 2>&1 \
                            || docker buildx create --name mam-builder --driver docker-container --bootstrap

                        docker buildx build \
                            --builder mam-builder \
                            --platform linux/amd64,linux/arm64 \
                            --pull \
                            --build-arg APP_REVISION="$SHORT_SHA" \
                            $version_arg \
                            $tags \
                            --push \
                            .
                    '''
                }
            }
            post {
                always {
                    sh 'docker logout || true'
                }
            }
        }
    }
}
