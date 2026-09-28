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
# Usage:
#   ./run-tests.sh                 # Release, shard count from nproc
#   CONFIG=Debug ./run-tests.sh    # override configuration
#   SHARDS=4 ./run-tests.sh        # override shard count
#   NO_BUILD=1 ./run-tests.sh      # skip the build (assumes it is current)
#
# Each shard writes TRX artifacts and JUnit XML reports under the project's
# TestResults directory. CI publishes the XML reports.
set -uo pipefail

CONFIG=${CONFIG:-Release}
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

echo "==> Running ${#PROJECTS[@]} test projects in $SHARDS shard(s) each, config $CONFIG"
start=$(date +%s)
pids=()
labels=()

for project in "${PROJECTS[@]}"; do
  name=$(basename "$project")
  mkdir -p "$project/TestResults"

  # Discover the test classes and deal them round-robin across the shards. Lines
  # look like "    Namespace.Class.Method(arg: 1)": drop theory arguments, then
  # the method, leaving the class.
  mapfile -t classes < <(
    dotnet test "$project" -c "$CONFIG" --no-build --list-tests 2>/dev/null |
      sed -n 's/^ \{4,\}\([A-Za-z_][A-Za-z0-9_.<>+]*\).*$/\1/p' |
      sed 's/\.[^.]*$//' |
      sort -u
  )

  if [ ${#classes[@]} -eq 0 ]; then
    echo "!! $name: no test classes discovered" >&2
    exit 1
  fi

  shards=$SHARDS
  [ "$shards" -gt "${#classes[@]}" ] && shards=${#classes[@]}
  echo "    $name: ${#classes[@]} classes -> $shards shard(s)"

  for ((i = 0; i < shards; i++)); do
    filter=""
    for ((j = i; j < ${#classes[@]}; j += shards)); do
      # A trailing dot anchors the match at the method separator, so one class
      # name being a prefix of another cannot pull in the wrong tests.
      filter+="${filter:+|}FullyQualifiedName~${classes[j]}."
    done

    dotnet test "$project" -c "$CONFIG" --no-build \
      --filter "$filter" \
      --logger "trx;LogFileName=test-results-$name-$i.trx" \
      --logger "junit;LogFilePath=$repo_root/$project/TestResults/test-results-$name-$i.xml" \
      > "$project/TestResults/shard-$i.log" 2>&1 &
    pids+=($!)
    labels+=("$name shard $i")
  done
done

status=0
for idx in "${!pids[@]}"; do
  if ! wait "${pids[idx]}"; then
    status=1
    echo "!! FAILED: ${labels[idx]}"
  fi
done

echo "==> Summary ($(( $(date +%s) - start ))s wall)"
for project in "${PROJECTS[@]}"; do
  grep -h -E "^(Passed!|Failed!)" "$project"/TestResults/shard-*.log 2>/dev/null |
    sed "s|^|  $(basename "$project"): |"
done

# Failing shards print their own output; surface it so CI logs are self-contained.
if [ $status -ne 0 ]; then
  for project in "${PROJECTS[@]}"; do
    for log in "$project"/TestResults/shard-*.log; do
      [ -f "$log" ] || continue
      grep -q "^Failed!" "$log" && { echo "===== $log"; cat "$log"; }
    done
  done
fi

exit $status
