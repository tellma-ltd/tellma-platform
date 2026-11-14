#!/usr/bin/env bash
#
# Builds an analyzer probe and checks that its build fails with exactly the six platform analyzer
# errors, TELLMA0001 to TELLMA0006, and no other error. Any other error code (a compiler error
# after a contract rename, a restore or MSBuild error) fails the check, because the six alone
# prove the rules ran.
#
# Usage: bash verify-probe.sh <probe project directory> <log file> [extra dotnet build arguments]
set -u

project="$1"
log="$2"
shift 2

# --- The build must fail ---
if dotnet build "$project" "$@" > "$log" 2>&1; then
  cat "$log"
  echo "The analyzer probe $project built, so the platform analyzers did not run."
  exit 1
fi

# --- With exactly the six rule ids among its error codes ---
# An error line reads "<file>(<line>,<col>): error <CODE>: <message>"; a code-less MSBuild error
# reads "error : <message>" and is caught as the empty code.
expected="TELLMA0001 TELLMA0002 TELLMA0003 TELLMA0004 TELLMA0005 TELLMA0006"
actual="$(grep -oE "(^|[[:space:]:])error [A-Za-z]*[0-9]*:" "$log" | sed -E 's/.*error ([A-Za-z]*[0-9]*):/\1/' | sort -u | tr '\n' ' ' | sed 's/ $//')"
if [ "$actual" != "$expected" ]; then
  cat "$log"
  echo "The analyzer probe $project failed with the error codes [$actual], not exactly [$expected]."
  exit 1
fi

echo "The analyzer probe $project failed with exactly $expected."
