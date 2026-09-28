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
    }
}
