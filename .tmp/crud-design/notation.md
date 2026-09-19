# Contract notation for specs 0010–0021: C# sketches

Specs document public contracts and entity shapes as **C# sketches**: real C# syntax, so a .NET
reader parses them at a glance, stripped of everything that would make them pasteable. An earlier
generation of specs shipped full C# with XML documentation, and implementers copied those blocks
into the code verbatim, XML docs included; a later generation used a language-neutral notation,
which turned out harder to read than the C# it replaced. The sketch is the middle: the shape in
the language the code will use, and nothing a compiler or a copy-paste would accept as finished.

## Rules

1. **Fenced `csharp`.** Every contract block is tagged `csharp`. SQL stays in `sql` fences, JSON in
   `jsonc`; column tables stay tables.
2. **No `using` directives, no `namespace` declaration, no XML documentation (`///`), no
   `#nullable`, no method bodies, no `[AttributeUsage]`.** A leading `// Tellma.Core.Abstractions.Tenancy`
   comment names the namespace. Members on classes are declared without bodies, as in an
   interface (`public TellmaBuilder AddFeature<T>() where T : ITellmaFeature, new();`); the block
   is a sketch and the spec says so once (rule 9).
3. **Short `//` remarks only** — one line, roughly eighty characters, to state a default, an
   ownership, a store type or a cross-reference. Anything longer lives in prose or in a Members
   table under the block.
4. **Shapes.**
   - `public interface IFoo` for services (DI-resolved, platform-implemented) and for contracts a
     distribution or pack implements.
   - `public sealed record Foo(int A, string? B)` for immutable data; `public sealed record`
     with `{ get; init; }` members when positional would be unreadable.
   - `public sealed class Foo` with `{ get; set; }` members for mutable data (options, requests,
     builders); `public abstract class` for entity bases; `public static class` for constants.
   - `public enum Kind { A, B }`.
   - Attributes as `public sealed class TemporalAttribute(bool enabled = true) : Attribute` with
     a remark naming the target (`// on type; inherited`); usage is written `[Temporal]`.
   - Exceptions as `public sealed class TenantNotFoundException(int tenantId) : TellmaException`.
5. **Async.** `Task`/`Task<T>` return types with the `Async` suffix. Every async member also takes
   a trailing `CancellationToken cancellationToken`, which the blocks omit; the spec states that
   once (rule 9). A member without `Task` is synchronous. Prose and Members tables may name an async member without
   its suffix (`Evaluate` for `EvaluateAsync`); the sketch carries the suffix.
6. **Types.** `IReadOnlyList<T>` for sequences, `IReadOnlyDictionary<TKey, TValue>` for maps,
   `IReadOnlySet<T>` for sets, `T?` for optional, `Func<…>`/`Action<…>` for delegates, `Type` for
   type tokens, `object` for untyped keys. Entity properties use CLR types (`DateTime` for
   `datetime2`, `DateOnly`, `Guid`, `decimal`) with the SQL type and ownership in a trailing remark
   (`// datetime2(7); server-owned`) — the column table remains the authoritative storage shape.
7. **Defaults** as initializers (`public int Take { get; set; } = 50;`) or optional parameters.
   Constants as `public const int NotFound = 50404;`.
8. **Generics and constraints** in C# form: `where TLeaf : TDefault`, `where T : ITellmaFeature, new()`.
9. **The placement sentence.** Section 1 of every spec states once: "Contract blocks are C#
   sketches: names and shapes are normative; `using` directives, XML documentation,
   cancellation-token parameters, method bodies and accessibility details are omitted, so a block
   is never pasted into code."
10. **Illustrations** of a distribution author's call site stay `csharp`, one to five lines,
    preceded by the word *Illustration*.

## Conversion table (from the language-neutral notation)

| Notation | C# sketch |
|---|---|
| `service IFoo` / `contract IFoo` | `public interface IFoo` |
| `record Foo(A: int, B: string?)` | `public sealed record Foo(int A, string? B);` |
| `record FooException(X: int)   // exception` | `public sealed class FooException(int X) : TellmaException;` |
| `data Foo` with members | `public sealed class Foo { public T Member { get; set; } … }` |
| `data Foo // constants` | `public static class Foo { public const int X = 1; … }` |
| `base Foo<TKey> : Bar<TKey>` | `public abstract class Foo<TKey> : Bar<TKey>` |
| `enum K = A \| B` | `public enum K { A, B }` |
| `annotation [Foo(x: int = 1)]   on property` | `public sealed class FooAttribute(int x = 1) : Attribute   // on property` |
| `Name(p: T) -> R` | `Task<R> NameAsync(T p);` |
| `Name(p: T) -> R   sync` | `R Name(T p);` |
| `Name(p: T)   sync` | `void Name(T p);` |
| `Name: T` (property) | `T Name { get; }` on interfaces; `public T Name { get; set; }` on classes; positional on records |
| `Name: T = v` | `public T Name { get; set; } = v;` |
| `list<T>` / `map<K, V>` / `set<T>` | `IReadOnlyList<T>` / `IReadOnlyDictionary<K, V>` / `IReadOnlySet<T>` |
| `(A, B) -> C` | `Func<A, B, C>`; `(A) -> void` is `Action<A>` |
| `where T: X` | `where T : X` |
| `Foo: RequestContext   // static` | `public static RequestContext Foo { get; }` |
| trailing `server-owned`, `write-once`, `FK -> core.Users` | a `//` remark at the end of the member line |
| `X: datetime2(7)` | `public DateTime X { get; set; }   // datetime2(7)` |
| `X: nvarchar(255)` | `public string X { get; set; }   // nvarchar(255)` |
| `X: bit` | `public bool X { get; set; }` |

## Worked example

Before:

```contract
// Tellma.Core.Abstractions.Tenancy
service ITenantRegistry                       // singleton; snapshot reads, no I/O
  Tenants: list<TenantInfo>   SnapshotVersion: Guid
  Find(tenantId: int) -> TenantInfo?   sync
  RefreshAsync(force: bool)                   // single-flight
record TenantLocation(Server: string, Database: string, CredentialProfile: string)
enum TenantState = Provisioning | Active | ReadOnly | Suspended | Retired
annotation [Temporal(enabled: bool = true)]   on type; inherited
base TopLevelEntity<TKey> : Entity<TKey>
  ModifiedAt: datetime2(7)                    server-owned, concurrency token
```

After:

```csharp
// Tellma.Core.Abstractions.Tenancy
public interface ITenantRegistry                        // singleton; snapshot reads, no I/O
{
    IReadOnlyList<TenantInfo> Tenants { get; }
    Guid SnapshotVersion { get; }
    TenantInfo? Find(int tenantId);
    Task RefreshAsync(bool force);                      // single-flight
}

public sealed record TenantLocation(string Server, string Database, string CredentialProfile);

public enum TenantState { Provisioning, Active, ReadOnly, Suspended, Retired }

public sealed class TemporalAttribute(bool enabled = true) : Attribute;   // on type; inherited

public abstract class TopLevelEntity<TKey> : Entity<TKey>
{
    public DateTime ModifiedAt { get; set; }            // datetime2(7); server-owned; concurrency token
}
```

## Entities and tables

Every persisted entity keeps its column table; the C# sketch of an entity base or a pack's
default leaf shows the members with their store type and ownership as remarks. Members tables
under a block carry the semantics the remarks cannot.
