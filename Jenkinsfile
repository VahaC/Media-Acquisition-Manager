// CI for Media Acquisition Manager (MAM-3): every commit to main runs the .NET build and tests and
// the frontend lint, build and tests. It never pushes an image: a push to Docker Hub is a release,
// and a release is started by hand from Jenkinsfile.publish. PUBLISHING.md has the Jenkins setup.
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

        // Backend and Frontend share nothing but the stashed source, so they run side by side: the
        // build takes as long as the slower of the two, not their sum. Neither stops the other on a
        // failure, so one build reports both.
        //
        // Package caches: each container starts empty, so without a cache every build downloaded
        // every NuGet and npm package again. The caches live in the workspace's @tmp directory, which
        // the Docker agent mounts into the container at the same path and which belongs to the
        // Jenkins user (a named Docker volume would be root's, and the container doesn't run as root).
        // deleteDir() clears the workspace itself, never its @tmp, so the caches outlive the build.
        stage('Checks') {
            parallel {
                stage('Backend') {
                    agent {
                        docker {
                            image 'mcr.microsoft.com/dotnet/sdk:10.0'
                            // A RAM disk for the tests' temporary files, which are mostly SQLite
                            // databases: every commit a test makes is flushed to disk, and thousands
                            // of tests waiting on the agent's disk were most of the stage. Only
                            // run-tests.sh points TMPDIR here; the build keeps the normal /tmp.
                            args '--tmpfs /mam-test-tmp:rw,exec,mode=1777'
                        }
                    }
                    when {
                        // Decided before the agent starts, so a skipped stage doesn't pull or start
                        // the SDK container just to skip.
                        beforeAgent true
                        expression { env.RUN_BACKEND == 'true' }
                    }
                    steps {
                        // Parallel stages on one agent get separate workspaces (job, job@2), and
                        // only Checkout's was cleaned: test reports an earlier build left in this one
                        // must not be published with this build's.
                        deleteDir()
                        unstash 'source'
                        withEnv(["NUGET_PACKAGES=${env.WORKSPACE_TMP}/nuget"]) {
                            sh 'dotnet restore MediaAcquisitionManager.slnx'
                            sh 'dotnet build MediaAcquisitionManager.slnx -c Release --no-restore'
                            // Every test project, as concurrent shards; see run-tests.sh. Each
                            // project's .runsettings excludes Category=RequiresDocker, and there are
                            // no such tests yet.
                            sh 'TMPDIR=/mam-test-tmp NO_BUILD=1 bash run-tests.sh'
                        }
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
                        // The same separate-workspace reason as in Backend.
                        deleteDir()
                        unstash 'source'
                        withEnv(["npm_config_cache=${env.WORKSPACE_TMP}/npm"]) {
                            dir('frontend') {
                                // --prefer-offline takes a cached package as it is instead of asking
                                // the registry whether it changed; package-lock.json pins the exact
                                // versions and their hashes anyway.
                                sh 'npm ci --prefer-offline'
                                // Lint and the type check are separate from the tests: Vitest strips
                                // types without checking them, and `npm run build` is what runs `tsc -b`.
                                sh 'npm run lint'
                                sh 'npm run build'
                                sh 'npm run test'
                            }
                        }
                    }
                }
            }
        }
    }
}
