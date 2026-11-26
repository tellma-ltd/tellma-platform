#!/usr/bin/env bash
#
# Runs `dotnet build` under a time limit and keeps the diagnostics that a build stopped from outside
# would otherwise lose, so a build that stalls fails at a known time and leaves enough behind to
# find where it stopped. README.md beside this script explains what each file shows.
#
# Usage:
#   build-with-evidence.sh --timeout <limit> --evidence <directory> -- <dotnet build arguments>...
#
# <limit> is a whole number of seconds, minutes or hours: 90s, 12m, 1h. <directory> must be absent
# or empty; the script creates it and leaves the diagnostics there. For example:
#   build-with-evidence.sh --timeout 12m --evidence "$RUNNER_TEMP/build-evidence" -- Tellma.slnx -c Release --no-restore

set -uo pipefail

# --- Tunables -----------------------------------------------------------------------------------
# A build past its limit is first asked to stop, then killed if it is still running this long
# afterwards.
readonly stop_grace=30s
# How much of the text log's tail the step log repeats when the build is stopped.
readonly tail_lines=40

# Prints the usage to stderr.
usage() {
  echo "Usage: $0 --timeout <limit: 90s, 12m, 1h> --evidence <empty or absent directory> -- <dotnet build arguments>..." >&2
}

# --- Arguments: the options, then `--`, then the arguments for dotnet build ---------------------
limit=''
evidence=''
while (($# > 0)) && [[ $1 != -- ]]; do
  case $1 in
    --timeout | --evidence)
      if (($# < 2)); then
        usage
        exit 2
      fi
      if [[ $1 == --timeout ]]; then limit=$2; else evidence=$2; fi
      shift 2
      ;;
    *)
      echo "::error::Unknown option '$1'." >&2
      usage
      exit 2
      ;;
  esac
done
if [[ -z $limit || -z $evidence ]] || (($# == 0)); then
  usage
  exit 2
fi
shift # the `--`

# The limit in seconds, to tell a build the limit stopped from one killed by something else.
if [[ ! $limit =~ ^([1-9][0-9]*)([smh])$ ]]; then
  echo "::error::The limit '$limit' is not a whole number of seconds, minutes or hours, such as 90s, 12m or 1h." >&2
  exit 2
fi
case ${BASH_REMATCH[2]} in
  s) limit_seconds=${BASH_REMATCH[1]} ;;
  m) limit_seconds=$((BASH_REMATCH[1] * 60)) ;;
  h) limit_seconds=$((BASH_REMATCH[1] * 3600)) ;;
esac

# --- The time limit: GNU timeout ----------------------------------------------------------------
# Git Bash on Windows carries GNU coreutils in /usr/bin, but its PATH can put Windows' own
# timeout.exe, a different program, first. The `--version` probe refuses anything but GNU timeout.
timeout_tool=/usr/bin/timeout
if [[ ! -x $timeout_tool ]]; then
  timeout_tool=$(command -v timeout || true)
fi
if [[ -z $timeout_tool ]] || ! "$timeout_tool" --version 2>/dev/null | grep -q 'GNU coreutils'; then
  echo "::error::GNU timeout (coreutils) was not found." >&2
  exit 2
fi

# How the build is asked to stop. On Linux, SIGINT is a Ctrl+C: MSBuild cancels the build, logs each
# task it is still waiting for (warning MSB4220), and writes its binary log out in full once they
# have stopped. Git Bash on Windows cannot deliver a Ctrl+C to a native program (SIGINT does nothing
# there), so the script sends SIGTERM, which ends the build's processes outright. A build stalled on
# a tool it started can lose only that tool and then finish on its own, with the tool's failure and
# a complete binary log.
case $OSTYPE in
  msys* | cygwin*) stop_signal=TERM ;;
  *) stop_signal=INT ;;
esac

# --- The evidence folder ------------------------------------------------------------------------
# A fresh folder, so that no earlier build's logs mix into this one's. Its absolute path goes to
# MSBuild and to every process the build starts, whatever their working folder; on Windows it is
# given in the mixed form (C:/...), which .NET reads and Git Bash leaves unconverted.
if [[ -e $evidence && ( ! -d $evidence || -n $(ls -A "$evidence") ) ]]; then
  echo "::error::The evidence folder '$evidence' is not an empty directory." >&2
  exit 2
fi
if ! mkdir -p "$evidence/msbuild" "$evidence/roslyn"; then
  echo "::error::Could not create the evidence folder '$evidence'." >&2
  exit 2
fi
evidence=$(cd "$evidence" && pwd)
if command -v cygpath >/dev/null 2>&1; then
  evidence=$(cygpath -m "$evidence")
fi
readonly binary_log="$evidence/build.binlog"
readonly text_log="$evidence/build.log"

# MSBuild writes a report into MSBUILDDEBUGPATH when one of its nodes fails with an exception (by
# default into one of its temporary folders, where nothing collects it), and, with
# MSBUILDDEBUGCOMM, a trace of how each process connected to its worker nodes. The compiler client
# in each MSBuild node, and a compiler server the build starts, log every compilation request and
# its answer into a file per process in the folder RoslynCommandLineLogFile names. All three write
# as they go, so they survive the build being killed. A worker node or compiler server that an
# earlier build left running, and this one reuses, keeps the settings it started with; only the
# compiler client inside a reused node reads its variable afresh on every build.
export MSBUILDDEBUGPATH="$evidence/msbuild"
export MSBUILDDEBUGCOMM=1
export RoslynCommandLineLogFile="$evidence/roslyn"

# --- The build ----------------------------------------------------------------------------------
# The binary log is complete only when the build ends on its own or cancels: it is buffered and
# compressed, so a build killed mid-way leaves it truncated, or empty. The text log is flushed line
# by line and keeps everything up to the moment the build stopped, each line with its time.
echo "==> dotnet build $* (time limit $limit; evidence in $evidence)"
started=$SECONDS
started_at=$(date +%s)
"$timeout_tool" --signal="$stop_signal" --kill-after="$stop_grace" "$limit" dotnet build "$@" \
  "-bl:$binary_log" "-flp:logfile=$text_log;verbosity=normal;ShowTimestamp;Encoding=UTF-8"
status=$?
elapsed=$((SECONDS - started))

# --- Node failure reports from nodes this build did not start ------------------------------------
# A worker node that an earlier build left running (on CI, the restore's) and this one reused keeps
# the report folder it started with, one of MSBuild's temporary folders (MSBuildTemp* under the
# system's temporary folder). Reports written there since the build started are copied in beside
# the rest.
shopt -s nullglob
for temp_root in "${TMPDIR:-}" "${TMP:-}" "${TEMP:-}" /tmp; do
  if [[ -z $temp_root ]]; then
    continue
  fi
  # Git Bash globs only its own path form; a Windows path's backslashes would read as escapes.
  if command -v cygpath >/dev/null 2>&1; then
    temp_root=$(cygpath -u "$temp_root")
  fi
  for report in "$temp_root"/MSBuildTemp*/MSBuild_*.failure.txt; do
    if (($(stat -c %Y "$report") >= started_at)) && [[ ! -e $evidence/msbuild/${report##*/} ]]; then
      cp "$report" "$evidence/msbuild/"
    fi
  done
done

# Prints, sorted by the time they were requested, the projects the text log shows starting and not
# finishing: the ones a stopped build was still building, or waiting on. A start reads
# `Project "<path>" on node <n> (<targets>).` or, for a project another one builds,
# `Project "<parent>" (<id>) is building "<path>" (<id>) on node <n> (<targets>).`; an end reads
# `Done Building Project "<path>" (<targets>).`, with ` -- FAILED.` when it failed. A project is
# keyed by its path and targets, and counted, since one project can build several times at once
# (once per target framework, say).
unfinished_projects() {
  awk '
    {
      line = $0
      sub(/\r$/, "", line)
      if ((at = index(line, ">Done Building Project \"")) > 0) {
        rest = substr(line, at + 24)
        quote = index(rest, "\"")
        targets = substr(rest, quote + 2)
        sub(/( -- FAILED)?\.$/, "", targets)
        open[substr(rest, 1, quote - 1) " " targets]--
      } else if ((at = index(line, ">Project \"")) > 0 && index(line, " on node ") > 0) {
        rest = substr(line, at + 10)
        if ((building = index(rest, ") is building \"")) > 0) {
          rest = substr(rest, building + 15)
        }
        quote = index(rest, "\"")
        node_and_targets = substr(rest, index(rest, " on node ") + 9)
        space = index(node_and_targets, " ")
        targets = substr(node_and_targets, space + 1)
        sub(/\.$/, "", targets)
        key = substr(rest, 1, quote - 1) " " targets
        open[key]++
        # The time stamp is the last one before the node prefix: the log glues its first project
        # line to the header line before it.
        stamp = ""
        if (match(substr(line, 1, at), /[0-9][0-9]:[0-9][0-9]:[0-9][0-9]\.[0-9]+/)) {
          stamp = substr(line, RSTART, RLENGTH)
        }
        started[key] = stamp " node " substr(node_and_targets, 1, space - 1)
      }
    }
    END {
      for (key in open) {
        if (open[key] > 0) {
          print started[key] "  " key
        }
      }
    }
  ' "$text_log" | sort
}

# Prints the compilations that the compiler logs show requested and never answered. The compiler
# client logs `Compilation request <project> (<framework>), PathToTool=...` when a compile starts,
# and one `CompilerServer: <outcome> - <detail> - <project> (<framework>)` line when it has its
# answer, whether the server compiled it, refused it or the client fell back to compiling itself.
# Each process logs into its own file, so a request is matched with its answer within one file.
unanswered_compilations() {
  awk '
    {
      line = $0
      sub(/\r$/, "", line)
      if ((at = index(line, "Compilation request ")) > 0) {
        rest = substr(line, at + 20)
        comma = index(rest, ", PathToTool=")
        name = comma > 0 ? substr(rest, 1, comma - 1) : rest
        pending[FILENAME SUBSEP name] = substr(line, 1, at + 19) name
      } else if (index(line, "CompilerServer: ") > 0) {
        # The project is what follows the last " - " on the line.
        rest = line
        while ((separator = index(rest, " - ")) > 0) {
          rest = substr(rest, separator + 3)
        }
        delete pending[FILENAME SUBSEP rest]
      }
    }
    END {
      for (key in pending) {
        print pending[key]
      }
    }
  ' "$@" | sort
}

# --- What the step log shows when the build failed -----------------------------------------------
# A failed node's report, whenever there is one: it names the exception that ended the node, which
# MSBuild otherwise reports only as error MSB4166.
for report in "$evidence"/msbuild/MSBuild_*.failure.txt; do
  echo "::warning title=MSBuild node failure::An MSBuild node failed; its report follows in the log: ${report##*/}"
  echo "::group::${report##*/}"
  cat "$report"
  echo "::endgroup::"
done

# A build the limit stopped (124: it exited when asked; 137: it was killed after the grace
# period), or one killed (137) by something else, such as the out-of-memory killer: what it was
# doing at that moment, from the logs that survive a kill.
if ((status == 124 || status == 137)); then
  # MSBuild opens the text log before it starts building; a build stopped earlier has none.
  touch "$text_log"
  # The build's closing summary ends with its elapsed time: when the text log has it, the build
  # ended on its own once stopped, and its binary log is complete.
  if grep -q 'Time Elapsed' "$text_log"; then
    binary_log_state='complete'
  else
    binary_log_state='truncated'
  fi
  if ((status == 124)); then
    echo "::error title=Build stopped::dotnet build did not finish within $limit and was stopped. Its logs are in $evidence; the binary log is $binary_log_state."
  elif ((elapsed >= limit_seconds)); then
    echo "::error title=Build stopped::dotnet build did not finish within $limit, was still running $stop_grace after it was asked to stop, and was killed. Its logs are in $evidence; the binary log is $binary_log_state."
  else
    echo "::error title=Build killed::dotnet build was killed after ${elapsed}s, before its $limit limit, by something other than this script. Its logs are in $evidence; the binary log is $binary_log_state."
  fi
  echo "::group::Projects started and not finished (time requested, node, project, targets)"
  projects=$(unfinished_projects)
  echo "${projects:-(none)}"
  echo "::endgroup::"
  compiler_logs=("$evidence"/roslyn/*.log)
  compilations=''
  if ((${#compiler_logs[@]} > 0)); then
    compilations=$(unanswered_compilations "${compiler_logs[@]}")
  fi
  echo "::group::Compilations requested and not answered"
  echo "${compilations:-(none)}"
  echo "::endgroup::"
  # `awk 1` ends the last line, which a stopped build leaves unterminated.
  echo "::group::The last $tail_lines lines of the text log"
  tail -n "$tail_lines" "$text_log" | awk 1
  echo "::endgroup::"
fi

exit "$status"
