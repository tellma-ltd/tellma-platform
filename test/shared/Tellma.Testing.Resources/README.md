# Tellma.Testing.Resources

The resource audit every feature's suite runs over its own shared string resources. Not a test
project (it carries no test SDK, so `dotnet test` never tries to run it) and not a shipped package:
it is referenced only by projects under `test/`. It reads `.resx` files and source text alone and
references nothing beyond the base class library, so a suite of Core, of a pack or of a distribution
takes it without a second copy of anything.

## What the audit checks

`ResourceAudit.Run` reads each neutral file (`Strings.resx`, `ServerStrings.resx`) and its satellites
(`Strings.ar.resx`, …) and reports:

| Finding | When |
|---|---|
| `Orphan` | a neutral key no convention derives, no source literal names and no pending entry spares |
| `SatelliteOnly` | a satellite key its neutral file does not declare |
| `StalePending` | a pending entry whose key is used after all, or is no longer declared |

A key is used when it is one of the request's `DerivedKeys` (the suite passes the composed model's,
`ConventionalStringKeys.Of(services)` in `Tellma.Core`) or when a rule turns one of the `Literals`
into it. The platform's rules (`LiteralKeyRules.Platform`) take a literal verbatim, with `.` written
`_` (`Settings.TimeZoneUnknown` names `Settings_TimeZoneUnknown`), as a problem code
(`concurrency-conflict` names `Problem_concurrency_conflict` and its `_Detail`), and as a query
diagnostic (`QX3001` names `Queryex_QX3001`). A suite passes its own rules when its feature builds
keys another way.

`Pending` maps a key shipped ahead of the code that will look it up to the reason it waits. The
audit fails once the key is used, so the list shrinks as the features land.

## Usage

```csharp
IReadOnlyList<ResourceAuditFinding> findings = ResourceAudit.Run(new ResourceAuditRequest
{
    NeutralFiles = [RepositoryRoot.Combine("src", "acme", "Acme", "Resources", "Strings.resx")],
    DerivedKeys = ConventionalStringKeys.Of(provider),
    Literals = SourceLiterals.Read([RepositoryRoot.Combine("src", "acme")]),
    Pending = new Dictionary<string, string> { ["Acme_Order_Archive"] = "the archive action" },
});

Assert.True(findings.Count == 0, ResourceAudit.Report(findings));
```

| Type | Contents |
|---|---|
| [ResourceAudit](ResourceAudit.cs) | `Run`, `Report`, the request, the findings and their kinds. |
| [LiteralKeyRules](LiteralKeyRules.cs) | The rules that turn a source literal into the keys it names, and the platform's list. |
| [SourceLiterals](SourceLiterals.cs) | Every string literal of the C# and TypeScript files under the given folders, skipping build output (`bin`, `obj`, `dist`, `out-tsc`, `coverage`), `node_modules`, folders whose name starts with `.`, and TypeScript tests (`*.spec.ts`); a C# literal's interpolation holes and a template's `${…}` are read as source in their turn, and comments, character literals and regular expressions are skipped. |
| [ResxFile](ResxFile.cs) | A `.resx` file's string entries, and the satellites beside a neutral file. |
| [RepositoryRoot](RepositoryRoot.cs) | The repository root, the nearest folder above the build output that holds `Tellma.slnx`, and paths under it. |

The tokenizers read literals, not meaning: a literal that only happens to spell a key keeps the key
alive. That errs toward keeping a resource, never toward failing a sound one.
