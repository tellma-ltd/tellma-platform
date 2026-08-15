# Tellma.Core.Testing.Tests

Tests for the shipped test package. Untested test infrastructure produces false green everywhere
downstream, which is the whole reason this project exists.

| Folder | Covers |
|---|---|
| `Email/` | The capturing sender's thread safety, its lifetime-ordinal scripting, its event-driven wait (including the timeout), and the fact that it rejects an invalid message like any other transport. Both `AddCapturingEmail` modes, with the transport mode proving the router and its sandbox marking stay in the loop. The delivery-event builder's determinism and its verbatim redelivery. |

## Running

```bash
dotnet test test/core/Tellma.Core.Testing.Tests
```
