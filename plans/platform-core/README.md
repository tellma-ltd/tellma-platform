# plans/platform-core

Working folder for the autonomous implementation of specs 0010–0035 on the `platform-core` branch.

- [PLAN.md](PLAN.md) covers what is built, in which order, the tiers and the forecast.
- [SETUP.md](SETUP.md) and [KICKOFF.md](KICKOFF.md) are for the person who starts the run.
- [PROCESS.md](PROCESS.md) is the operating manual for the orchestrator and its agents.
- [CHECKPOINTS.md](CHECKPOINTS.md), [DEPS.md](DEPS.md) and [WATCHLIST.md](WATCHLIST.md) describe
  the work items.
- [LEDGER.md](LEDGER.md) and [DECISIONS.md](DECISIONS.md) hold the live state. `reviews/` holds
  one file per checkpoint.
- `tools/check_deps.py` validates the gating graph and lists ready checkpoints.
  `tools/usage_window.py` meters cost units from session transcripts. Both run with `python -I`
  on Windows and Linux.

The folder is living and is removed or archived before `platform-core` merges into `main`.
