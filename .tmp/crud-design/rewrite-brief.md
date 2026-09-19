# Brief: rewrite every contract block of one spec as a C# sketch

You are converting one spec file under `docs/specs/` from the language-neutral `contract`
notation to C# sketches. Read `.tmp/crud-design/notation.md` first and follow it exactly; it has
the rules, a conversion table and a worked example. Then read the whole spec, top to bottom, in
pages (the Read tool caps around 25k tokens; page with offset/limit), and convert.

## What to change

1. Every fenced block tagged `contract` becomes a fenced block tagged `csharp` containing the same
   declarations as C# sketches per `notation.md`. Preserve every name, every member, every default,
   every generic constraint, every remark and the order of declarations. A remark that stated
   ownership or a store type (`server-owned`, `write-once`, `datetime2(7)`, `FK -> core.Users`)
   becomes a trailing `//` remark on the member. Multi-member lines (`A: int   B: string`) become
   one member per line.
2. The placement sentence in section 1 — "Contract blocks use the platform's contract notation:
   names are normative; shape is described, not transcribed. SQL statements are the exact shape to
   emit." or close wording — becomes exactly: "Contract blocks are C# sketches: names and shapes are
   normative; `using` directives, XML documentation, cancellation-token parameters, method bodies
   and accessibility details are omitted, so a block is never pasted into code. SQL statements are
   the exact shape to emit."
3. Prose that refers to the old notation's words (`contract`, `record`, `data`, `service`, `base`,
   `annotation` used as notation keywords, "the contract notation", "in the notation") is
   reworded to plain English or the C# term (`interface`, `record`, `class`, `attribute`).
   Ordinary uses of those words in prose stay.
4. Existing `csharp` illustrations stay as they are. SQL, JSON and column tables stay as they are.
   Members tables under blocks stay as they are.

## What not to change

- No content change: no new members, no dropped members, no renamed members, no reordering of
  sections, no edits to prose beyond rule 3 above, no reflow of paragraphs you did not touch.
- No `using` directives, no `namespace { }` wrappers, no XML doc comments (`///`), no method
  bodies (not even `{ }` or `=> throw`), no `[AttributeUsage]`, no `#nullable`, no `required`
  keyword, no `init` unless the notation block already implied immutability without positional
  parameters.
- Do not touch any file other than the one spec you were given.

## Mechanics

- Work with a Python script or the Edit tool; whichever you use, verify at the end.
- Balanced braces in every `csharp` block; every declaration ends with `;` or a `{ … }` body of
  members; enums as `public enum X { A, B }` on one line when short.
- Async members: `Task`/`Task<T>` with the `Async` suffix, no token parameter. Members marked
  `sync` in the notation are plain non-`Task` members without the suffix. A member that already
  ends in `Async` in the notation keeps its name.
- Positional records for `record X(A: int, …)`; keep line length under about 110 characters by
  wrapping parameters.
- `data X` with members → `public sealed class X { public T Member { get; set; } … }` (options,
  requests, builders); `data X // constants` → `public static class X { public const … }`;
  `base X` → `public abstract class X`; `service`/`contract` → `public interface`;
  `annotation [X(…)] on …` → `public sealed class XAttribute(…) : Attribute;   // on …`;
  a record marked as an exception → `public sealed class XException(…) : TellmaException;`.
- Types: `list<T>` → `IReadOnlyList<T>`; `map<K, V>` → `IReadOnlyDictionary<K, V>`;
  `set<T>` → `IReadOnlySet<T>`; `(A) -> B` → `Func<A, B>`; `(A) -> void` → `Action<A>`; `Type`
  stays `Type`; SQL types on entity members become CLR types with the SQL type in the remark
  (`datetime2(7)` → `DateTime`, `nvarchar(n)`/`varchar(n)` → `string`, `bit` → `bool`,
  `uniqueidentifier` → `Guid`, `int`/`bigint` → `int`/`long`, `decimal(p,s)` → `decimal`,
  `varbinary` → `byte[]`, `hierarchyid` → `HierarchyId`, `date` → `DateOnly`, `time` → `TimeOnly`).
- Static members (`X: T // static`) → `public static T X { get; }`.

## Verification before you finish

Run these over the file and fix anything they find:

- No line contains "```contract".
- No line starts with `using ` or contains `namespace ` followed by `{` inside a `csharp` fence.
- No `///` anywhere in the file.
- Every `csharp` fence is closed; braces balance inside each fence.
- The count of `csharp` fences equals the old count of `contract` fences plus the pre-existing
  `csharp` illustrations (report both numbers).
- `grep -n -E "^(service|contract|data|base|annotation|record|enum) " ` finds nothing inside
  fences (the old keywords are gone).

## Report

Return: the file name; the number of blocks converted; the placement sentence's new wording
confirmed present; any notation construct you were unsure how to render and what you chose; any
place where the notation was internally inconsistent (a member referenced in a Members table but
absent from the block, or vice versa) — do not fix those, list them.
