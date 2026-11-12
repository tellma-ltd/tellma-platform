"""Check the platform-core gating graph and list ready checkpoints.

Usage (from the repository root, Windows or Linux):
    python -I plans/platform-core/tools/check_deps.py           # validate DEPS.md
    python -I plans/platform-core/tools/check_deps.py --ready   # also list ready checkpoints

Validation: every gate exists, the graph is acyclic, and no checkpoint is gated on a higher tier.
Readiness: a checkpoint is ready when its LEDGER.md status is `todo` and every gate is `done`.
"""
import re
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent.parent
LINE = re.compile(r"^(PC-\d{3}(?:\.\d)?) T(\d) (\S+):(.*)$")


def load_graph():
    """Parse DEPS.md into {id: (tier, analysis id, [gates])}, preserving priority order."""
    graph = {}
    for raw in (HERE / "DEPS.md").read_text(encoding="utf-8").splitlines():
        m = LINE.match(raw.strip())
        if m:
            graph[m.group(1)] = (int(m.group(2)), m.group(3), m.group(4).split())
    return graph


def load_status():
    """Parse the LEDGER.md status table into {id: status}."""
    status = {}
    for raw in (HERE / "LEDGER.md").read_text(encoding="utf-8").splitlines():
        cells = [c.strip() for c in raw.strip().strip("|").split("|")]
        if len(cells) >= 4 and re.fullmatch(r"PC-\d{3}(?:\.\d)?", cells[0]):
            status[cells[0]] = cells[3]
    return status


def is_done(status):
    """True when a LEDGER.md status cell records a finished checkpoint.

    Status cells carry a trailing note after the keyword, e.g. "done (CI 123 green; ...)",
    so only the leading word counts.
    """
    return bool(status) and (status == "done" or status.startswith("done "))


def validate(graph):
    """Return a list of problems: unknown gates, tier inversions and cycles."""
    problems = []
    idx = {p: i for i, p in enumerate(graph)}
    for pc, (tier, _, gates) in graph.items():
        for g in gates:
            if g not in graph:
                problems.append(f"{pc}: unknown gate {g}")
                continue
            if graph[g][0] > tier:
                problems.append(f"{pc} (T{tier}) is gated on {g} (T{graph[g][0]})")
            # Gates must point to an earlier checkpoint in priority order.
            if idx[g] > idx[pc]:
                problems.append(f"{pc}: gate {g} points forward")
    # Depth-first search for cycles.
    state = {}

    def visit(node, path):
        state[node] = 1
        for g in graph.get(node, (0, "", []))[2]:
            if state.get(g) == 1:
                problems.append("cycle: " + " -> ".join(path + [node, g]))
            elif g in graph and g not in state:
                visit(g, path + [node])
        state[node] = 2

    for pc in graph:
        if pc not in state:
            visit(pc, [])
    return problems


def main():
    graph = load_graph()
    problems = validate(graph)
    for p in problems:
        print("PROBLEM", p)
    print(f"{len(graph)} checkpoints, {len(problems)} problems")
    if "--ready" in sys.argv:
        status = load_status()
        ready = [pc for pc, (tier, aid, gates) in graph.items()
                 if status.get(pc, "todo") == "todo" and all(is_done(status.get(g)) for g in gates)]
        for pc in ready:
            # The MVP gate also needs the Tier-1 batch reviews, which DEPS.md does not list.
            extra = " (also needs PC-B01…B07 integrated)" if graph[pc][1] == "MVP" else ""
            print(f"READY {pc} T{graph[pc][0]} {graph[pc][1]}{extra}")
    sys.exit(1 if problems else 0)


if __name__ == "__main__":
    main()
