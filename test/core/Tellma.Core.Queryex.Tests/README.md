# Tellma.Core.Queryex.Tests

The offline half of the Queryex suite: everything that can be proven without a database. Hermetic
and cross-platform.

| Folder | What it proves |
|---|---|
| `Syntax/` | Scanning and parsing: what the grammar accepts, what it refuses, and where it says the problem is. |
| `Binding/` | Type checking, coercion, overload resolution, the mode rules, and what discovery reports to an authoring tool. |
| `Functions/` | The registry's own consistency: no two overloads can tie, every pattern uses only the arguments its signature declares, every result that promises presence comes from an emission that tolerates absence. |
| `Corpus/` | The conformance corpus: every case binds to the type and nullity it must, or produces the diagnostic it must — and every whole query emits the SQL its snapshot holds. |
| `Emit/` | Whole-query compilation, clause by clause, with the emitted SQL asserted directly. |
| `Semantics/` | What absence means, checked against the reference implementation over rows that really are missing values; and the nullity analysis checked against it in both directions. |
| `Coverage/` | That the suites cover what they claim: every diagnostic code is provoked somewhere, every function has a reading in the reference implementation, and every fixture string stays inside the collation's repertoire. |
| `Properties/` | Round-trip, determinism, no-duplication, and robustness, over generated input. |
| `Goldens/` | The recorded SQL of every query the corpus compiles. |

## Golden SQL snapshots

Emission is deterministic, so a change in emitted SQL is a reviewable diff rather than a flake. Each
query case has one snapshot under `Goldens/<name>.sql`, carrying the SQL, the parameter slots, and
the result columns. Snapshots are read from and written to the **source tree**, not the build
output, so an update run edits the file a reviewer will see.

To regenerate them after a deliberate emitter change:

```bash
TELLMA_UPDATE_GOLDENS=1 dotnet test test/core/Tellma.Core.Queryex.Tests
```

That run **always fails on purpose** — a run that rewrites its own expectations proves nothing, and
the failure is what stops a stray environment variable in CI from being read as a pass. Review the
diff, unset the variable, and run again.

A snapshot diff is a semantic change. It belongs in the pull request description with a reason.

## The nullity sweep

`Semantics/NullitySoundnessTests` is the check the whole nullity analysis is held to. Guards are
omitted from the emitted SQL wherever the analysis says a value is always present, so it evaluates
every node of every expression against every row of the fixture and reports any node whose claim
did not hold — in both directions, because folding a subexpression away that should have been
evaluated loses rows just as quietly as omitting a guard.

## Property tests

Generators are hand-rolled and seeded, and each seed is a theory row, so a failure is named for its
seed and reproduces byte-identically forever. Robustness cases carry a wall-clock budget, because a
hang is a failure mode that "it did not throw" cannot detect.

## Running

```bash
dotnet test test/core/Tellma.Core.Queryex.Tests
```
