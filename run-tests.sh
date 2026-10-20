#!/usr/bin/env bash
#
# Runs the .NET test suite as several concurrent `dotnet test` processes ("shards").
#
# Why processes and not just threads: xunit parallelises test classes inside one
# process too (see each project's xunit.runner.json), but concurrent tests in a
# single test host contend badly — measured on the Api suite, per-test time grew
# 4x at 8 threads, for only a 1.8x wall-clock win. The same work split across
# separate processes inflates ~1.4x and lands at 3.5x. So: a few processes, each
# with a few threads.
#
# Isolation: every test class owns its database (MediaAcquisitionManager.TestInfrastructure's
# TestDatabase keys files by process id + class, MediaAcquisitionManagerApiFactory copies its
# own), so a class may land in any shard.
#
# MAM-47 (ADR 0042) — two passes: "normal", the suite as it always ran, and "pseudo", the
# whole suite again with MAM_TEST_LANGUAGE=pseudo, where every request asks for the
# pseudo-language and the server wraps each of its sentences in ⟦…⟧. The harness checks the
# wrapping and strips it, so the English asserts stay as they are and prove that every
# sentence came from the locale files; it also fails a database write or a log line that
# holds a rendered sentence. The passes run one after the other (one full run at a time on
# the box); the run fails if either fails. Both run by default; PSEUDO=0 skips the second for
# a quick local run.
#
# Usage:
#   ./run-tests.sh                 # Release, shard count from nproc, both passes
#   CONFIG=Debug ./run-tests.sh    # override configuration
#   SHARDS=4 ./run-tests.sh        # override shard count
#   NO_BUILD=1 ./run-tests.sh      # skip the build (assumes it is current)
#   PSEUDO=0 ./run-tests.sh        # the normal pass only
#
# Each shard writes TRX artifacts and JUnit XML reports under the project's
# TestResults directory, named by pass (test-results-<project>-<pass>-<shard>.xml). CI
# publishes the XML reports of both passes.
set -uo pipefail

CONFIG=${CONFIG:-Release}
PSEUDO=${PSEUDO:-1}
PROJECTS=(
  tests/MediaAcquisitionManager.Api.Tests
  tests/MediaAcquisitionManager.Infrastructure.Tests
  tests/MediaAcquisitionManager.Core.Tests
)

# Each shard runs with maxParallelThreads=4 (xunit.runner.json), so aim for
# cores/4 shards and never fewer than one.
cores=$(nproc 2>/dev/null || echo 4)
SHARDS=${SHARDS:-$(( cores / 4 ))}
[ "$SHARDS" -lt 1 ] && SHARDS=1

cd "$(dirname "$0")"
repo_root=$(pwd)

if [ "${NO_BUILD:-0}" != "1" ]; then
  echo "==> Building ($CONFIG)"
  dotnet build MediaAcquisitionManager.slnx -c "$CONFIG" || exit 1
fi

# Test classes are discovered once per project, by the first pass that needs them (so the
# first project's shards already run while the next project is discovered), and the second
# pass deals the same classes. Lines look like "    Namespace.Class.Method(arg: 1)": drop
# theory arguments, then the method, leaving the class. One file per project, as bash has no
# arrays of arrays.
class_dir=$(mktemp -d)
trap 'rm -rf "$class_dir"' EXIT
for project in "${PROJECTS[@]}"; do
  mkdir -p "$project/TestResults"
  # A shard log of an earlier run would be counted in this run's summary.
  rm -f "$project"/TestResults/shard-*.log
done

discover() {
  local project=$1 name
  name=$(basename "$project")
  [ -f "$class_dir/$name" ] && return 0
  dotnet test "$project" -c "$CONFIG" --no-build --list-tests 2>/dev/null |
    sed -n 's/^ \{4,\}\([A-Za-z_][A-Za-z0-9_.<>+]*\).*$/\1/p' |
    sed 's/\.[^.]*$//' |
    sort -u > "$class_dir/$name"
}

# run_pass <label> <MAM_TEST_LANGUAGE value>: every project's shards, concurrently; the
# summary and, on failure, the failing shards' logs. Returns non-zero if any shard failed.
run_pass() {
  local label=$1 language=$2
  local pids=() labels=() start status=0

  echo "==> Pass \"$label\": ${#PROJECTS[@]} test projects in $SHARDS shard(s) each, config $CONFIG"
  start=$(date +%s)

  for project in "${PROJECTS[@]}"; do
    local name classes shards
    name=$(basename "$project")
    discover "$project"
    mapfile -t classes < "$class_dir/$name"
    if [ ${#classes[@]} -eq 0 ]; then
      echo "!! $name: no test classes discovered" >&2
      exit 1
    fi

    shards=$SHARDS
    [ "$shards" -gt "${#classes[@]}" ] && shards=${#classes[@]}
    echo "    $name: ${#classes[@]} classes -> $shards shard(s)"

    for ((i = 0; i < shards; i++)); do
      local filter=""
      for ((j = i; j < ${#classes[@]}; j += shards)); do
        # A trailing dot anchors the match at the method separator, so one class
        # name being a prefix of another cannot pull in the wrong tests.
        filter+="${filter:+|}FullyQualifiedName~${classes[j]}."
      done

      MAM_TEST_LANGUAGE="$language" dotnet test "$project" -c "$CONFIG" --no-build \
        --filter "$filter" \
        --logger "trx;LogFileName=test-results-$name-$label-$i.trx" \
        --logger "junit;LogFilePath=$repo_root/$project/TestResults/test-results-$name-$label-$i.xml" \
        > "$project/TestResults/shard-$label-$i.log" 2>&1 &
      pids+=($!)
      labels+=("$name $label shard $i")
    done
  done

  for idx in "${!pids[@]}"; do
    if ! wait "${pids[idx]}"; then
      status=1
      echo "!! FAILED: ${labels[idx]}"
    fi
  done

  echo "==> Summary of pass \"$label\" ($(( $(date +%s) - start ))s wall)"
  for project in "${PROJECTS[@]}"; do
    grep -h -E "^(Passed!|Failed!)" "$project"/TestResults/shard-"$label"-*.log 2>/dev/null |
      sed "s|^|  $(basename "$project"): |"
  done

  # Failing shards print their own output; surface it so CI logs are self-contained.
  if [ $status -ne 0 ]; then
    for project in "${PROJECTS[@]}"; do
      for log in "$project"/TestResults/shard-"$label"-*.log; do
        [ -f "$log" ] || continue
        grep -q "^Failed!" "$log" && { echo "===== $log"; cat "$log"; }
      done
    done
  fi

  return $status
}

status=0
run_pass normal "" || status=1
# Only PSEUDO=0 skips the second pass: a mistyped value (PSEUDO=true) runs it rather than silently proving less.
if [ "$PSEUDO" != "0" ]; then
  run_pass pseudo pseudo || status=1
fi

exit $status
