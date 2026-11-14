"""Sum Claude Code token usage from session transcripts, in price-weighted cost units.

The token figure an agent's completion notice reports is its final context size, not what it
consumed. Calibrate pacing with this script instead (PROCESS.md §6.2).

Cost unit = input + 1.25 x cache write + 0.1 x cache read + 5 x output, weighted by model price
relative to Opus (Fable: 2.5x input, cache write and output, 1.25x cache read). Records are
deduplicated by message id.

Usage (Windows or Linux):
    python -I plans/platform-core/tools/usage_window.py --latest [--from 2026-11-08T00:00Z] [--to ...]
    python -I plans/platform-core/tools/usage_window.py --session <session-id> --agent <agentId>
    python -I plans/platform-core/tools/usage_window.py --latest --by-agent 20
"""
import argparse
import json
import os
import re
from collections import defaultdict
from pathlib import Path

# Price weights relative to Opus 5.x: (input, cache write, cache read, output).
WEIGHTS = {"fable": (2.5, 2.5, 1.25, 2.5), "opus": (1, 1, 1, 1), "sonnet": (0.6, 0.6, 0.6, 0.6), "haiku": (0.2, 0.2, 0.2, 0.2)}


def project_dir():
    """Locate the transcript folder of the current working directory's project."""
    root = Path.home() / ".claude" / "projects"
    want = re.sub(r"[:\\/]", "-", str(Path.cwd())).lower()
    for d in root.iterdir():
        if d.is_dir() and d.name.lower() == want:
            return d
    raise SystemExit(f"no transcript folder for {Path.cwd()} under {root}")


def files_for(session_dir, session_file):
    """Yield the session transcript and every subagent transcript under it."""
    if session_file.exists():
        yield session_file
    if session_dir.exists():
        for dirpath, _, names in os.walk(session_dir):
            for n in names:
                if n.endswith(".jsonl") and n != "journal.jsonl":
                    yield Path(dirpath) / n


def weight(model):
    """Return the price weights for a model id."""
    for key, w in WEIGHTS.items():
        if key in (model or ""):
            return w
    return WEIGHTS["opus"]


def main():
    ap = argparse.ArgumentParser()
    g = ap.add_mutually_exclusive_group(required=True)
    g.add_argument("--session")
    g.add_argument("--latest", action="store_true", help="the most recently written top-level session")
    ap.add_argument("--from", dest="start")
    ap.add_argument("--to", dest="end")
    ap.add_argument("--agent", help="only transcripts whose file name contains this agent id")
    ap.add_argument("--by-agent", type=int, default=0, help="print the N most expensive transcripts")
    a = ap.parse_args()

    proj = project_dir()
    if a.latest:
        session_file = max(proj.glob("*.jsonl"), key=lambda p: p.stat().st_mtime)
    else:
        session_file = proj / f"{a.session}.jsonl"
    session_dir = session_file.with_suffix("")

    seen, total, per_file = set(), 0.0, defaultdict(float)
    for fp in files_for(session_dir, session_file):
        if a.agent and a.agent not in fp.name:
            continue
        with open(fp, encoding="utf-8", errors="replace") as fh:
            for line in fh:
                try:
                    o = json.loads(line)
                except ValueError:
                    continue
                m = o.get("message") if isinstance(o, dict) else None
                if not isinstance(m, dict) or not m.get("usage"):
                    continue
                ts = o.get("timestamp") or ""
                if (a.start and ts < a.start) or (a.end and ts > a.end):
                    continue
                mid = m.get("id") or o.get("uuid")
                if mid in seen:
                    continue
                seen.add(mid)
                u = m["usage"]
                wi, wcw, wcr, wo = weight(m.get("model"))
                cost = (wi * (u.get("input_tokens") or 0) + 1.25 * wcw * (u.get("cache_creation_input_tokens") or 0)
                        + 0.1 * wcr * (u.get("cache_read_input_tokens") or 0) + 5 * wo * (u.get("output_tokens") or 0))
                total += cost
                per_file[fp.name] += cost
    print(f"session {session_file.stem}: {total / 1e6:.2f} M cost units ({len(seen)} messages)")
    for name, cost in sorted(per_file.items(), key=lambda kv: -kv[1])[: a.by_agent]:
        print(f"  {cost / 1e6:8.2f} M  {name}")


if __name__ == "__main__":
    main()
