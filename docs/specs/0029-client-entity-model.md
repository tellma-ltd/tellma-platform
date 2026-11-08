# Spec: Client Entity Model

- **Author:** Ahmad Akra
- **Date:** 20 October 2026

**Status:** Ready for implementation. Frozen once merged: revised only if implementation forces a
design change, then kept as the historical record of what shipped — never updated thereafter as the
code or its dependencies evolve.

## Context

Every entity page needs the same facts: properties, types, labels, navigations, operations,
actions, limits, and how a row is named. The server emits them with the build. A distribution adds
a short `tmEntity` declaration per entity, and `TmEntities` merges the two per tenant.

On that model this spec ships the parts every entity page composes: display names, navigation
expansion, client permissions, a client Queryex with its `queryex/discover` endpoint, the
expression editor, the path tree picker, typed value and filter controls, and one generic picker
binding. Specs 0030–0032 and 0034 compose these parts. This spec builds on specs 0008, 0012–0016,
0024, 0025 and the component library.

Other specs that carry parts of this design:

- spec 0012 §2.5, §3.1, §3.2 — `[DisplayNameSource]`, the related projection's minimum, `IDisplayNames`.
- spec 0013 §7.6, §10.1 — label chains, key-resolving twins; action, Queryex and display-name keys.
- spec 0013 §7.7 — the value sets (`core.Language`, `core.Calendar`, `core.TimeZone`,
  `core.Region`) that `enums` carries beside the C# enums (§2.2).
- spec 0014 §10, spec 0032 §12.4 — the securables section of the Core chunk and its label chains.
- spec 0015 §2.2, §5.2, §5.2.1, §6.6, §7.3, §11.2 — descriptor `DisplayName`, traversal, finding by display name, the prefix check, `Query.PathNotReachable`, `[EntityRef]`.
- spec 0016 §2.4, §3.4, §3.5 — the `queryex/discover` route, `QueryDiagnostic` and `AccessSummary`, the row set.
- spec 0017 §2.3, spec 0019 §4.1 — blob kind policies; export columns through `IDisplayNames`.
- spec 0024 §3.1, §3.2, §4.4, §7.9, §9.2, §9.4, §10.1, §10.4, Appendix A — the Entities chunk's
  container (`TmEntitiesChunk`), the emit's contributors, the route host's `load()`, text direction
  and `tmStripBidiControls`, the details routes and `TmUrls.target` that the details-page rule gates
  (§7), `TmId`, `TmDecimal` and plain JSON, config problem codes.
- spec 0010 §C.15, §D.4, §D.12, §D.15, §E.1–§E.4, §F.13, §F.14, §F.17, §F.19, §F.21 — popup cell editors, the zone label, `tmButton`'s `disabledReason`, ungrouped numbers, two-line picker rows and label parts, whole-set picker pages, page `context`, label parts in grid cells, `ComparedWith`, discovery's `Measures`, `startOfQuarter`, the `'et'` calendar, discovered conjuncts.
- spec 0023 §6.4 — `tm-date-time-field`, the editor a layout places for an editable instant (§12.2).
- spec 0031 §10.4 — `TmEntityActions` and the generated action dialog, shipped in `/entity`.

## Goals / Non-goals

**Goals**

- One entity model, emitted at build and read from the worker cache: no metadata request, ever.
- Display names, labels and formats that read the same everywhere, server output included.
- Grids, pickers and filters that never send a column the user may not reach, so no page `403`s.
- Queryex authoring with instant feedback: highlighting on every key, server checks a moment later.
- A few declaration lines per entity; every navigation field and cell gets a picker with no code.

**Non-goals (explicitly out of scope)**

- The page hosts that compose these parts (specs 0027, 0030, 0031, 0032, 0034).
- Generic components (spec 0023); library and Queryex engine changes (spec 0010).
- A client Queryex parser or evaluator: `queryex/discover` is the one authority (§8).
- Per-row capabilities in `TmPermissions`. The core `permissions` extra carries them on every stack
  (spec 0015 §5.3), and the details host reads it (spec 0031 §4.1). Pages handle `403` and `404`
  (spec 0015 §9).
- A generated date-time editor: `tm-value-editor` leaves instants read-only, and a layout that edits
  one places `tm-date-time-field` itself (§12.2).

## 1. Placement and vocabulary

The entity registry lives in the eager primary entry point. Everything that edits, renders or
queries through it lives in the lazy `/entity` entry point, which every entity page shares.

Contract blocks are TypeScript and C# sketches: names and shapes are normative; imports,
documentation comments, bodies and cancellation tokens are omitted.

- **`@tellma/core` (primary, ≤ 6 KB gzip).** `tmEntity` and the declaration types, `TmEntities`,
  `TmDisplayNames` and `TmPermissions`, in `client/projects/core/tellma-core/src/entity/`. The
  descriptor code sits here because `TmEntities.get` is primary API; only its data loads lazily.
- **`@tellma/core/entity` (lazy, 28 KB gzip).** §6, §8–§14 and `tmCheckBlobFile` (§16), plus
  `TmEntityActions` and the generated action dialog (contract: spec 0031 §10.4). The picker binding
  `import()`s `@tellma/core/search` and `/details`, as spec 0024 §1's import rule admits.
- **Server (`Tellma.Core`).**
  - `EntityClientAssetContributor` and `ClientEntityModel`, in `Tellma.Core.Client` (§2.1).
  - `QueryexService`, in `Tellma.Core.Api` (§9).
  - `DisplayNames`, which implements spec 0012's `IDisplayNames`.
  - The records, in `Tellma.Core.Abstractions.Client` and `Tellma.Core.Abstractions.Api`.
- **Testing.** `@tellma/core-testing` gains `tmTestEntities()`, `TmFakeDiscover`, and harnesses for
  the editor, path picker, column filter and entity field.
- **Strings and telemetry.** String scopes `tm.entity`, `tm.queryex`, `tm.filter` and `tm.value`;
  telemetry areas `tm.entity` and `tm.queryex`.

**Vocabulary**

- *Emit* — the build step that writes the client metadata as hashed JSON chunks (spec 0024 §3.2).
  The *Core chunk* loads before first render; the *Entities chunk* loads on entity routes.
- *Summary* — the eager facts about one entity, in the Core chunk (§2.2).
- *Descriptor* — an entity's metadata merged with its declaration and gated by the tenant's shape (§4).
- *Shape* — the tenant's number of content languages, 1–3 (spec 0013 §7.4).
- *Slot* — position 1–3 of a multilingual group (`Name`, `Name2`, `Name3`).
- *Group anchor* — the slot-1 name, `Name`; as a column key it stands for the whole group (§6.1).
- *Twin* — the slot 2 or 3 property of a group, `Name2` or `Name3`.
- *Active slot* — the slot the active language reads, `TmLocalization.contentSlot()` (spec 0024 §7.1).
- *Admitted slot* — a slot within the tenant's shape (§4.2).
- *Display name* — the one-line text for a row (§5).
- *Column key* — a path or expression shown as one column (§6.1).
- *Select plan* — the query terms a list of column keys expands to (§6).
- *Restricted entity* — one whose `Read` the user holds with a filter, or not at all: not
  `can(E, 'Read')`, or `filtered(E, 'Read')` (§7).
- *Slash label* — a path's segment labels joined with " / " (§11.4).
- *Value descriptor* — the type facts that choose a value's format and control (§12.1).
- *Hatch* — an escape hatch that replaces generated UI with hand-written code (spec 0024 §2.5).

## 2. The entity section of the client metadata

The emit writes one tenant-independent description of every entity the Queryex model knows. Each
entity gets a summary in the eager Core chunk and its full metadata in the Entities chunk.

### 2.1 Producer

```csharp
// Tellma.Core.Abstractions.Client
public interface IClientEntityModel                          // singleton; built once from the realised composition
{
    IReadOnlyList<ClientEntityMetadata> Entities { get; }     // ordered by Name, ordinal
    ClientEntityMetadata? Find(string entity);                // ordinal-ignore-case
}
// ClientEntityMetadata and its parts mirror §2.2: PascalCase members, camelCase JSON (source-generated), nulls omitted.

// Tellma.Core.Client — registered by CoreFeature; spec 0024 §3.2's contract
internal sealed class EntityClientAssetContributor : IClientAssetContributor
{
    // writes the summaries to the Core chunk's "entities" section, the metadata and the named
    // enums to the Entities chunk's "entities" and "enums" sections, and the .d.ts (§15)
    public void Contribute(ClientAssetContext context);
}
```

- **Sources.** Spec 0012's metadata and `All`-shape schema (spec 0012 §3.1, §11.1), spec 0015's
  `IStackRegistry`, the key-resolving twins of spec 0013 §7.6 (`ValueLabelKey` among them), spec
  0013 §7.7's `IValueSetRegistry`, spec 0017's kind registry, and `JsonSchemaExporter` for action
  `arguments`.
- **Which entities.** Every root of the `All`-shape schema: stacks, child entities, and read-only
  roots such as `core.Blob` and `core.UserStamp`.
- **Same bytes.** Entities are sorted by name and members keep metadata order, so one composition
  always emits the same bytes.

### 2.2 Shapes

```ts
type TmEntityOperation = 'query' | 'details' | 'save' | 'delete' | 'import' | 'export';
type TmDataType = 'string' | 'integer' | 'decimal' | 'float' | 'bool' | 'date' | 'datetime' | 'instant' | 'time' | 'guid' | 'enum' | 'json' | 'binary';
// integer: int, short, byte or long; date: DateOnly; datetime: DateTime (wall clock); instant: DateTimeOffset;
// float, time, json, binary are never queryable

interface TmEntitySummary {                          // Core chunk, eager
  readonly name: string; readonly segment: string | null; // name: 'gl.Center'; segment: 'centers', a child's query segment, null for read-only roots
  readonly labelKey: string; readonly pluralLabelKey: string; readonly newLabelKey: string; readonly labelFallback?: string; // §2.4
  readonly operations: readonly TmEntityOperation[]; readonly capabilities: readonly string[]; // spec 0015 §2.2's vocabulary; a child's operations: query, and export when its owner stack exports
  readonly displayName: TmDisplayNameRule;           // §5: the shell and the bell render names before /entity loads
  readonly kind: 'stack' | 'child' | 'readonly'; readonly icon?: string; // icon: from the merged declaration (§3), never emitted
}
interface TmEntityMetadata extends TmEntitySummary { // Entities chunk "entities" (spec 0024 §3.1's TmEntitiesChunk)
  readonly key: { readonly property: string; readonly type: 'int' | 'long' }; // ids: a number for 'int', a TmDecimal for 'long'
  readonly owner?: { readonly entity: string; readonly navigation: string; readonly collection: string }; // set on child entities
  readonly properties: readonly TmPropertyMetadata[]; readonly navigations: readonly TmNavigationMetadata[];
  readonly children: readonly TmChildMetadata[]; readonly actions: readonly TmActionMetadata[];
  readonly relatedSelect: readonly string[];         // the related projection (spec 0012 §2.5)
  readonly defaultOrderBy: string;                   // StackDescriptor.DefaultOrderBy ?? 'Id desc'
  readonly searchable: readonly string[]; readonly naturalKeys: readonly string[]; // searchable: spec 0015 §5.2.1
  readonly extras: readonly string[];                // details extra names, the core 'permissions' included; all eager (spec 0015 §5.3)
  readonly limits: { readonly maxTake: number; readonly countCap: number; readonly maxSkipWindow: number;
    readonly maxSearchLength: number; readonly maxIds: number; readonly maxExtras: number;
    readonly maxFilterLength: number };              // maxFilterLength: the pipeline's MaxInputLength (spec 0015 §5.2)
}
interface TmPropertyMetadata {
  readonly name: string; readonly json: string;      // name: 'CreditLimit' (Queryex, labels, declarations); json: 'creditLimit' (wire)
  readonly type: TmDataType; readonly queryable: boolean; readonly nullable: boolean; readonly unique?: true;
  readonly long?: true;                              // an integer that is a C# long: its values are TmDecimal (§12.2)
  readonly labelKey: string; readonly labelFallback?: string; readonly excel: boolean; // excel: false under [ExcludeFromExcel]
  readonly ownership: 'editable' | 'write-once' | 'server-owned' | 'derived' | 'database-owned'; // §12.2
  readonly defaulted?: true;                         // [Defaulted]: may be empty in a save, which fills it (spec 0012 §2.2)
  readonly maxLength?: number; readonly precision?: number; readonly scale?: number;
  readonly enum?: string;                            // a named enum in `enums`: 'gl.CenterType', 'core.TimeZone'
  readonly multilingual?: { readonly group: string; readonly slot: 1 | 2 | 3 };
  readonly foreignKeyOf?: string;                    // the navigation this key backs: 'Parent'
  readonly blob?: TmBlobKindPolicy;                  // a [BlobReference] property (§16)
}
interface TmEnumMetadata {                           // Entities chunk "enums"; name: 'gl.CenterType' (a C# enum) or 'core.TimeZone' (a value set)
  readonly name: string;
  readonly values: readonly { readonly value: string; readonly labelKey?: string; readonly label?: string; readonly labelFallback?: string }[];
  readonly aliases?: Readonly<Record<string, string>>; // a value set's Aliases: another spelling → a value; 'Asia/Calcutta' → 'Asia/Kolkata'
}                                                    // exactly one of labelKey and label; label reads the same in every language
interface TmNavigationMetadata {
  readonly name: string; readonly json: string; readonly foreignKey: string; readonly target: string; // 'Parent', 'parent' (wire), 'ParentId' (a sibling's: 'Id'), 'gl.Center'
  readonly kind: 'reference' | 'owner' | 'sibling';  // owner: a child's [ParentKey]; sibling: spec 0012 [Sibling]
  readonly nullable: boolean; readonly labelKey: string; readonly labelFallback?: string;
}
interface TmChildMetadata {                          // name 'Contacts', json 'contacts'
  readonly name: string; readonly json: string; readonly entity: string; readonly parentKey: string; readonly maxCount: number;
  readonly labelKey: string; readonly labelFallback?: string;
}
interface TmDisplayNameRule { readonly source: string; readonly multilingual: boolean; readonly prefix?: string } // §5.1: 'Name', true, 'Code'
interface TmActionMetadata {
  readonly name: string; readonly kind: 'entity' | 'api'; readonly builtIn: boolean; // name: 'invite', 'preferences/set', 'activate'
  readonly action: string | null; readonly labelKey: string; readonly labelFallback?: string; // action: the securable action; null: member-only
  readonly mutation: boolean; readonly idempotent: boolean; readonly destructive: boolean;
  readonly allowWhenReadOnly?: true;                 // the descriptor's AllowWhenReadOnly: runs on a ReadOnly tenant (§7)
  readonly singleTarget: boolean; readonly supportsFilter: boolean; readonly result: 'entities' | 'own' | 'none'; // entity: 'entities' (no result value) or 'own'; 'none': an api action returning a bare Task
  readonly arguments?: TmJsonSchema;                 // JSON Schema 2020-12 of ArgumentsType or RequestType
  readonly fields?: readonly TmActionField[];        // the same arguments, one per top-level property
}
interface TmActionField {                            // one argument, as the generated dialog renders it (spec 0031 §10.4)
  readonly name: string; readonly labelKey: string; readonly labelFallback?: string; // name: the wire member, 'creditLimit'
  readonly required: boolean;                        // non-nullable, or [Required]
  readonly value: TmValueDescriptor;                 // §12.1; `target` from [EntityRef] (spec 0015 §11.2)
}
```

- **Each named enum is emitted once**, in the Entities chunk's `enums` section, and properties,
  action fields and setting keys (spec 0013's `SettingKeyDescriptor.EnumType`) name it. A C# enum
  is named by its type (`gl.CenterType`) and labels by keys. A value set (spec 0013 §7.7) is named
  by its set (`core.TimeZone`), and each value carries a key (`Calendar_<code>`, `Region_<code>`)
  or a literal that reads the same in every language, such as a language's own name or a zone
  id. Its values are known at build time, so the section stays tenant-independent. A set with
  `Aliases` (`core.TimeZone`, spec 0013 §7.7) carries them as `aliases`; no filter or control
  offers an alias.
- **Queryex types derive on the client:** `integer`, `decimal` → `Numeric`; `string`, `enum` →
  `String`; `date` → `Date`; `datetime` → `DateTime`; `instant` → `DateTimeOffset`; `bool` →
  `Bool`; `guid` → `Guid`.
- **Size.** About 12 KB gzip of metadata and 4 KB of summaries for Acme's ~30 entities, and about
  5 KB of enums, the zone set the largest. All three sit
  in the worker's `shell` group and are served anonymously, `public, immutable`, like the string
  packs (spec 0024 §6). The bundle's declarations and the string packs already name the model, and
  no tenant fact or `Secret` default is emitted (spec 0013 §5.3).

### 2.3 What is deliberately absent

- **Tenant facts** come from the launch documents (spec 0025 §3.1); **UI layout** from declarations (§3).
- **Physical names**, such as tables, store types and `SchemaFingerprint`.
- **Whatever the Queryex schema omits**, except `[JsonColumn]` properties. Those appear with
  `queryable: false`, for details forms.

### 2.4 Label keys

The emit resolves every label key, so the client only looks keys up in the string pack.

- **Resolution.** Each label takes the first key of its chain (below) that a neutral `Strings`
  resource declares. With none declared, it takes the conventional key plus `labelFallback`
  (humanized English, shown in every language), and `tellma-client build` warns `label-missing`
  (spec 0024 §3.6).
- **Chains** are spec 0013 §7.6's for entities, plurals, properties, navigations, collections, enum
  values and actions. The emit adds the two below, probing the neutral resources the same way.

| Label | Chain |
|---|---|
| create (`newLabelKey`) | `<Schema>_<Entity>_New` ("New customer") → Core `Entity_New` ("New") |
| action field | `<Schema>_<Entity>_Action_<Name>_<Field>` → `<Schema>_<Entity>_<Field>` → Core `<Field>` → humanized |

- **`<Field>`** is the C# property name, less the trailing `Id` of an `[EntityRef]` property
  (`AssigneeId` → `Assignee`).
- **Example.** Acme's `set-credit-limit` field `CreditLimit` (spec 0018 §1.4) finds no
  `Acme_Customer_Action_SetCreditLimit_CreditLimit` and takes `Acme_Customer_CreditLimit`.
- **Twins** carry slot 1's key; the client adds the suffix (§4.2).

## 3. The `tmEntity` declaration

A declaration names an entity and carries only what the metadata cannot know: its launcher entry,
what grids show by default, its details layout and its hatches. Every member is optional. This
section owns the shape; each member's meaning belongs to the spec its sketch comment names.

```ts
declare function tmEntity<const N extends string>(name: N, declaration?: TmEntityDeclarationInput<N>): TmEntityDeclaration;

interface TmEntityDeclarationInput<N extends string> {
  section?: string; order?: number; icon?: string; synonymsKey?: string; visibleWhen?: (context: TmNavContext) => boolean; // launcher (spec 0027 §3.1, §3.2): shown when allows(name, 'query') and visibleWhen passes
  columns?: readonly TmColumnInput<N>[];             // search page (spec 0030 §2): the one column default; keys per §6.1
  orderBy?: TmOrderInput<N>;                         // else the metadata's defaultOrderBy
  tile?: TmTileDeclaration<N>; filters?: readonly TmCustomFilterDeclaration[]; // filters: URL x.{key}
  tabs?: readonly { readonly key: string; readonly labelKey: string; readonly queryex?: string; readonly named?: string }[]; // URL tab=; at most one of queryex and named
  rowLink?: (row: TmPlanRow) => string | null; pageActions?: readonly TmSearchPageActionDeclaration[];
  search?: TmLazyComponent;                          // hatch: replaces the results region
  details?: TmLazyComponent | TmDetailsDeclaration<N>; // details page (spec 0031 §3.7)
  defaults?: () => Partial<TmEntityType<N>>;         // a new record's values
  clone?: (copy: TmEntityType<N>, source: TmEntityType<N>) => TmEntityType<N>;
  validate?: (path: SchemaPathTree<TmEntityType<N>>) => void; // a Signal Forms schema function
  beforeSave?: (entity: TmEntityType<N>) => TmEntityType<N> | Promise<TmEntityType<N>>; // values only: every collection keeps its length and order (spec 0031 §8.5)
  actions?: readonly TmEntityActionDeclaration<N>[];
  picker?: TmPickerDeclaration<N>; helpKey?: string; // picker: §14; helpKey: spec 0026 §9.8, reserved
}
type TmColumnInput<N extends string> = TmPath<N>
  | { readonly path: TmPath<N>; readonly display?: TmLazyComponent; readonly width?: number;
      readonly select?: readonly TmPath<N>[]; readonly filter?: string } // select: paths the display reads; filter: an editable cell's picker filter (§14.5)
  | { readonly expression: string; readonly labelKey: string; readonly type?: TmDataType }; // a computed column
type TmOrderInput<N extends string> = TmPath<N> | `${TmPath<N>}:desc`;
interface TmDetailsDeclaration<N extends string> {
  readonly page: TmLazyComponent; readonly create?: TmLazyComponent;
  readonly edit?: 'page';                                   // the page is the editor
  readonly refresh?: 'page';                                // the page registers its own refresh action (spec 0031 §3.7)
  readonly header?: 'standard' | 'compact';                 // default 'standard'; compact: one row and a band (spec 0031 §5.4)
  readonly createdUrl?: (entity: TmEntityType<N>, urls: TmTenantUrls) => string; // where a create lands, with replaceUrl (spec 0031 §8.3)
}
interface TmEntityActionDeclaration<N extends string> {
  readonly name: string; readonly dialog?: TmLazyComponent; // name: an action the metadata lists; dialog: replaces the generated one
  readonly icon?: string; readonly button?: boolean; readonly visible?: (entity: TmEntityType<N>, context: { readonly userId: number }) => boolean;
  readonly confirm?: TmPageAction['confirm'];               // a destructive action's confirmation (spec 0026 §9.4), else spec 0031 §10.4's default; the host adds { action, n }
  readonly bulk?: false;                                    // false: never in the search page's selection bar (spec 0030 §12.2)
}
interface TmPickerDeclaration<N extends string> {
  readonly filter?: string; readonly description?: readonly TmPath<N>[] | false; // filter: a predicate on every search; description: the second line (§14.2)
  readonly orderBy?: TmOrderInput<N>; readonly create?: boolean; // orderBy: default the display name; create: offer "Create…", default true
}
interface TmEntityDeclaration { readonly name: string; readonly [member: string]: unknown } // frozen; the merge unit (spec 0024 §2.3)
```

- **Rows and paths.** Rows are wire camelCase objects (`TmEntityType<N>`, §15); paths are PascalCase
  (`TmPath<N>`). The tile, custom-filter and page-action types are spec 0030's, exported here.
- **Typed paths.** `TmPath<N>` is checked against `TmEntityShapes` (§15). Paths of up to three
  navigation steps are checked exactly; longer ones pass when that prefix checks. A pack compiled
  without the generated file sees `string`.
- **Build check.** Spec 0024 §3.6's config step runs `tmCheckEntityDeclaration` (DOM-free) over
  every merged declaration, against the Entities chunk. It reports `unknown-path` (paths resolve by
  §4.3), `unknown-action`, `operation-missing` and `entity-not-routable`; spec 0024 Appendix A
  lists their conditions.
- **Members need operations.** `details` needs the `details` operation; `search`, `columns`,
  `filters` and `tabs` need the `query` operation.
- **Named filters.** A tab's `named` names a filter the stack declares (spec 0015 §2.2), for a
  scope that client Queryex cannot write, such as "Shared with me". The search host sends it in
  `QueryRequest.NamedFilters` (spec 0030 §2.4).
- **Overrides** follow spec 0024 §2.3 member by member; `picker` and `actions` are replaced whole.

**Example —** Acme's customers, launcher and picker members (spec 0030 §2 shows the search
members):

```ts
export const customers = tmEntity('acme.Customer', {
  section: 'sales', order: 10, icon: 'users',
  details: () => import('./customer-details'),
  picker: { description: ['Code', 'CustomerType'] },    // 'Cod' is a compile error
});
```

## 4. `TmEntities` and descriptors

`TmEntities` is the registry every page asks about entities. It answers summaries at once from the
Core chunk, so route matching, the launcher and the bell work before `/entity` loads. After one
`load()` of the Entities chunk it also answers descriptors, gated by the tenant's shape.

### 4.1 Contract

```ts
declare class TmEntities {                                      // @tellma/core primary
  summary(name: string): TmEntitySummary | undefined;           // Core chunk; ordinal-ignore-case
  bySegment(segment: string): TmEntitySummary | undefined;      // route matching (spec 0024 §9.2)
  declaration(name: string): TmEntityDeclaration | undefined;   // merged, from TM_APP_DECLARATIONS
  label(name: string, plural?: boolean): Signal<string>; readonly loaded: Signal<boolean>;
  load(): Promise<void>;                                        // the Entities chunk, once; a cache read when warm
  get(name: string): TmEntityDescriptor;                        // this and the rest need load(); reads the tenant's shape
  enum(name: string): TmEnumDescriptor | undefined;             // a C# enum, 'gl.CenterType', or a value set, 'core.TimeZone'
  resolve(root: string, key: string): TmPathResolution | null;  // §4.3
}
interface TmEntityDescriptor extends Omit<TmEntityMetadata, 'properties' | 'navigations' | 'children' | 'actions'
    | 'relatedSelect' | 'searchable' | 'capabilities' | 'operations'> {
  readonly label: Signal<string>; readonly pluralLabel: Signal<string>;
  readonly properties: readonly TmPropertyDescriptor[];         // twins beyond the shape removed (§4.2)
  readonly relatedSelect: readonly string[]; readonly searchable: readonly string[]; // the same twins removed
  readonly navigations: readonly TmNavigationDescriptor[]; readonly children: readonly TmChildDescriptor[];
  readonly capabilities: ReadonlySet<string>; readonly operations: ReadonlySet<TmEntityOperation>;
  readonly actions: readonly TmEntityActionDescriptor[]; readonly declaration: TmEntityDeclaration; // declaration: { name } when none
  readonly slots: 1 | 2 | 3;                                    // the tenant's shape
  property(name: string): TmPropertyDescriptor | undefined;     // by Queryex name, ordinal-ignore-case
  navigation(name: string): TmNavigationDescriptor | undefined;
  child(name: string): TmChildDescriptor | undefined;
}
interface TmPropertyDescriptor extends TmPropertyMetadata { readonly label: Signal<string>; readonly contentDir?: 'ltr' | 'rtl' }
interface TmNavigationDescriptor extends TmNavigationMetadata { readonly label: Signal<string> }
interface TmChildDescriptor extends TmChildMetadata { readonly label: Signal<string> }
interface TmEntityActionDescriptor extends TmActionMetadata { readonly label: Signal<string>; readonly declaration?: TmEntityActionDeclaration<string> }
interface TmEnumDescriptor extends TmEnumMetadata { label(value: string): Signal<string> }
```

- **Loading.** Spec 0024 §4.4's route host awaits `load()` beside the page chunk; both are cached
  when warm. Before `load()` settles, `get`, `enum` and `resolve` throw in development. In
  production, `get` then returns a summary-only descriptor.
- **Shape changes.** `get()` reads the shape from `TmLaunchCache.docs.settings` (spec 0025 §3.1).
  A settings change therefore rebuilds any descriptor read inside a `computed`. At most three
  descriptors exist per entity, one per shape.

### 4.2 Shape gating and labels

- **Admitted slots** are the tenant's content languages. A twin beyond them leaves the descriptor
  and every select the platform builds. A declaration naming one renders nothing.
- **Twin labels** follow spec 0013 §7.6. With two or three content languages, each slot's label is
  the slot-1 label plus the language symbol in parentheses: `Name (E)`, `Name (ع)`. With one
  content language there is no suffix.
- **`contentDir`** is a slot's content-language direction: an Arabic `Name2` input is `dir="rtl"`
  on an English page. Other text uses `dir="auto"`.
- **Labels are signals**, so a language switch re-labels every header, field and slash label in
  place (spec 0024 §7.2).

### 4.3 Path resolution

`resolve(root, key)` is the one place the client interprets a column key or a Queryex path.

```ts
interface TmPathResolution {
  readonly root: string; readonly key: string;                  // 'gl.Center', 'Parent.Manager'
  readonly steps: readonly TmNavigationDescriptor[];            // navigations crossed, in order
  readonly entity: string;                                      // the entity owning the last segment
  readonly end:
    | { readonly kind: 'value'; readonly property: TmPropertyDescriptor }        // a scalar or a twin
    | { readonly kind: 'text'; readonly slots: readonly TmPropertyDescriptor[] } // a group anchor
    | { readonly kind: 'navigation'; readonly navigation: TmNavigationDescriptor }
    | { readonly kind: 'image' | 'file'; readonly navigation: TmNavigationDescriptor; readonly policy: TmBlobKindPolicy };
  readonly label: Signal<string>;                               // slash label (§11.4)
  readonly fkPath?: string;                                     // navigation ends: the path holding the id, 'Parent.ManagerId'
}
```

- **Segments.** Every segment but the last names a navigation. The last names a scalar, a group
  anchor, a twin or a navigation (spec 0008 §6.1). Names match ordinal-ignore-case.
- **Blobs.** A navigation to `core.Blob` backed by a `[BlobReference]` ends as `image` when its
  policy re-encodes images (`policy.image` is set), else as `file`.
- **Unknown keys** return `null` (§6.4).

## 5. Display names

A display name renders any row as one string in the user's language. It shows in details titles,
picker rows, navigation cells, tiles and the bell's actors. The server declares the rule, and the
client and the server both render it by §5.2. A display name typed or pasted into a search finds
its row (§5.2).

### 5.1 The rule

- **Declaration.** The entity type carries `[DisplayNameSource(property, Prefix = …)]` (spec 0012
  §2.5). Without it, the source is the `Name` group, else the first natural key, else `Id`.
- **Columns.** A display name reads `Id`, the source (a scalar, or every admitted slot of its
  group) and the prefix. The related projection always holds them, so every navigation renders
  with no extra request.

**Example —** `[DisplayNameSource(nameof(SerialNumber))]` makes an invoice show its serial number;
`[DisplayNameSource(nameof(Name), Prefix = nameof(Code))]` makes an account show "1101 - Cash".

### 5.2 Rendering (normative for both sides)

1. **Name.** For a group: the active slot's value when non-empty, else slot 1's. For a scalar: the
   value formatted in the locale, with integers ungrouped (`grouping: false`, spec 0010 §D.15) and
   dates in the active calendar's numeric style.
2. **Prefix.** When declared and non-empty, it formats the same way.
3. **Text.** With both a name and a prefix, the text is the prefix, the fixed separator " - " and
   the name. The separator is never localized, and a string prefix value never contains it (spec
   0015 §6.6), so a display name always splits at its first separator. With only one, that one is
   the text. With neither, Core key `Entity_DisplayNameFallback` ("#{id}") applies.
4. **Plain text.** The text carries no bidi control characters, on either side (spec 0024 §7.9).
   Direction is markup: `tm-display-name` (§5.3) renders the prefix and the name each in a
   `<bdi>`, so an English code beside an Arabic name keeps its order in either page direction;
   picker options and entity grid cells do the same through §14.3's label parts. A context that
   takes only a string (an input's value, a tooltip, an accessible name, `document.title`) shows
   the text as is, inside a `dir="auto"` element where it has one; the browser then orders it from
   its first strong character.

**Example —** rule, row and active slot in; text out:

| Rule | Row | Active slot | Text |
|---|---|---|---|
| `Name` group | `{ id: 5, name: 'Head office', name2: 'المكتب الرئيسي' }` | 2 | المكتب الرئيسي |
| `Name` group | `{ id: 6, name: 'Jeddah', name2: null }` | 2 | Jeddah |
| `Name` + prefix `Code` | `{ id: 7, code: '1101', name: 'Cash' }` | 1 | 1101 - Cash |
| `SerialNumber` | `{ id: 9, serialNumber: 42 }` | 1, `ar-SA` | ٤٢ |
| `SerialNumber` | `{ id: 9, serialNumber: 42 }` | 1, `ar-SA-u-nu-latn` | 42 |
| `Name` group | `{ id: 3 }` | 1 | #3 |

- **Findable.** Search text that is a display name finds its row, on the server (spec 0015
  §5.2.1), in cacheable pickers (§14.2) and in grid paste (§14.5):
  - text holding the separator splits at its first one: the prefix equals the left part, and the
    name in any admitted slot starts with the right part;
  - an integer source or prefix matches by equality, native digits normalized, and `#{id}` matches
    the id;
  - a date source is the one exception: its calendar-formatted text is ambiguous.
- **Shared golden.** `display-name-golden.json` in `@tellma/core` holds `{ rule, shape, row, slot,
  locale, calendar, expected }` rows and §6.2's expansion rows; the TS and C# tests read one file.
- **Native digits are client-only.** Server text always uses Latin digits (spec 0013 §9.2). A row
  whose locale formats native digits, such as the `ar-SA` row above, carries `clientOnly: true`,
  and the C# test skips it.

### 5.3 `TmDisplayNames`

```ts
declare class TmDisplayNames {                                  // @tellma/core primary
  of(entity: string, row: TmJson): Signal<string>;              // a wire row; memoized per row object (WeakMap)
  parts(entity: string, row: TmJson): Signal<TmDisplayNameParts>; // the same name, for tm-display-name
  select(entity: string, navigation?: string): readonly string[]; // Id + the display-name columns, shape-aware
  label(entity: string, id: TmId): Signal<string | null>;       // a known row's display name; null while unknown
  labelParts(entity: string, id: TmId): Signal<TmDisplayNameParts | null>;
  remember(entity: string, rows: readonly TmJson[] | TmQueryRowSet): void; // wire rows, or a row set through tmRowsToObjects (§6.2)
  rememberRelated(related: Readonly<Record<string, readonly TmJson[]>>): void; // EntitiesResult.related
  resolve(entity: string, ids: readonly TmId[]): void;          // fetch unknown ids in the background
}
interface TmDisplayNameParts { readonly prefix: string | null; readonly name: string } // prefix: only with both; name: the name, else the prefix, else "#7"
@Component({ selector: 'tm-display-name' })                    // inline: <bdi>prefix</bdi> - <bdi>name</bdi>
declare class TmDisplayName { readonly parts: InputSignal<TmDisplayNameParts | null> } // null renders nothing
```

- **Text and parts.** `of` is the parts joined by §5.2 step 3. Navigation cells, details titles,
  tiles, picker pages and the bell's actors render `tm-display-name`; accessible names and other
  string contexts use `of` and `label` (§5.2 step 4).
- **Rows are wire objects.** `of` reads the rule's `json` members in the active slot. On an `en` +
  `ar` tenant, `select('gl.Center', 'Parent')` is `['Parent.Id', 'Parent.Name', 'Parent.Name2']`.
- **Ids by key type.** `label`, `remember` and `resolve` key rows by the entity's `key.type`
  (§2.2): a number for `int`, a `TmDecimal` for `long`. An id in the other form is coerced first.
  Lookups in `entitySet().byId` (spec 0025 §3.1) use the same coercion. For example, an
  assignment's `targetId` is a `long` and arrives as `"1284"` (spec 0028 §5.1). It still finds the
  invoice remembered under `1284`.
- **The cache** lives in memory only, per tab and tenant, with up to 5,000 rows per entity (least
  recently used evicted). Details reads and saves, search results, picks and the bell's related
  rows feed it.
- **Tag moves.** A move of the held `settings` tag clears the cache only when the tenant's content
  languages changed, since they decide what each name slot holds; any other settings move keeps
  it. A move of the held `permissions` tag keeps it: its names were read while the user had
  access.
- **Cacheable entities** answer `label` from `TmLaunchCache.entitySet()` (spec 0025 §3.3), which
  loads lazily on first use. Until the list is present, the cache answers.
- **`resolve`** batches unknown ids per entity until the next frame. It then reads `{segment}/query`
  with `filters: ['Id in (…)']`, the display-name select and `take` = the id count (≤ 100). The
  read is `background: true` and aborts on a tenant change.
- **Unqueryable entities** are never asked; their rows show `#id`.

## 6. Navigation expansion and select plans

A select plan turns column keys into the terms a query sends and reads each cell back. Navigations
and groups expand to every admitted slot, so a language switch needs no request.

### 6.1 Column keys

| Key | Kind | Select terms (`en` + `ar` tenant) | Cell | Order terms |
|---|---|---|---|---|
| scalar path: `CreditLimit`, `Parent.Code`, a twin `Name2` | `value` | the path | the value, formatted by type (§12) | the path |
| group anchor: `Name`, `Parent.Name` | `text` | `Name`, `Name2` | the active slot, else slot 1 | `coalesce(Name2, Name)` at slot 2; `Name` at slot 1 |
| navigation: `Parent`, `Parent.Manager` | `navigation` | `Parent.Id` + `TmDisplayNames.select('gl.Center', 'Parent')` | the display name; a link when `allows(target, 'details')` | the prefix, then the name as for `text` |
| image navigation: `Image` | `image` | `ImageId`, `Image.Variants` | a thumbnail (§16) | none |
| file navigation: `File` | `file` | `FileId`, `File.FileName` | the file name, a download link | `File.FileName` |
| `{ expression, labelKey }` | `expression` | the expression | by the response column's type | the expression |

- **The root `Id` comes first**, as the row identity; terms are deduplicated.
- **No slot-1 key.** An Arabic user sees `Name` in Arabic; no column key means "slot 1 verbatim".
- **URL form.** Keys are what spec 0024 §9.5's codec carries in `cols`, `order` and `f.{path}`.
  Expression columns live only in declarations and preferences.

### 6.2 Contract

```ts
declare function tmSelectPlan(root: TmEntityDescriptor,
  keys: readonly TmColumnInput<string>[], extra?: readonly string[]): TmSelectPlan; // @tellma/core/entity; extra: paths read beside the columns
interface TmSelectPlan {
  readonly select: string;                                      // comma list, for QueryRequest.Select
  readonly columns: readonly TmColumnPlan[];                    // the shown columns: keys in order, never extra; dropped keys absent
  readonly dropped: readonly { readonly key: string; readonly reason: 'unknown' | 'unreachable' }[];
  rows(set: TmQueryRowSet): readonly TmPlanRow[];
}
interface TmColumnPlan {
  readonly key: string; readonly kind: 'value' | 'text' | 'navigation' | 'image' | 'file' | 'expression';
  readonly resolution: TmPathResolution | null;                 // null for expressions
  readonly type: TmDataType | null;                             // null until the response types an expression
  readonly label: Signal<string>; readonly filter: TmFilterKind | null; // the slash label; the filter kind for the column's type (§13.2)
  orderBy(direction: 'asc' | 'desc'): string | null;            // reads the active slot; null: not orderable
  value(row: TmPlanRow): unknown;                               // navigation: the id; text: the shown slot; blobs: the blob id
}
interface TmPlanRow { readonly id: TmId; get(path: string): unknown }    // any selected term by path, as the wire sent it
interface TmQueryRowSet {                                       // spec 0016 §3.5's row set; decimal and long cells are TmDecimal
  readonly columns: readonly { readonly name: string; readonly type: string; readonly scale?: number;
    readonly nullable: boolean; readonly path?: readonly string[]; readonly groupingKey: boolean }[];
  readonly rows: readonly (readonly TmJson[])[];
}
declare function tmRowsToObjects(root: TmEntityDescriptor, set: TmQueryRowSet): readonly TmJsonObject[]; // @tellma/core primary
```

- **Extra paths** (a page's tile paths and tree keys) join `select` and `rows`, resolved and
  dropped as keys are, but never `columns`. A host binds its grid, export and page context to
  `columns`.
- **The server counterpart** is `IDisplayNames.Columns(entity, shape, navigationPath)` (spec 0012
  §3.1). It returns the same terms for reports, Excel (spec 0019 §4.1) and MCP. The golden's
  expansion rows pin both sides.
- **Wire objects.** `tmRowsToObjects` turns a row set into the wire's camelCase objects, for code
  that reads rows as `TmJson` (§5.3, §14.2). Each column's `path` nests by `json` names:
  `['Parent', 'Name2']` → `{ parent: { name2: … } }`. A column without a `path` (an expression) is
  left out, and cells keep the row set's values.
- **Language switch.** Cells re-render from the held rows. The `orderBy` of a `text` or
  `navigation` column may change with the slot; the host re-reads only when the rebuilt request
  differs from the held result's (spec 0030 §5.4).

### 6.3 Example — the Centers grid, `en` + `ar`, user in Arabic

`cols=Code,Name,Parent,Parent.Code&order=Parent`:

```text
select:  Id,Code,Name,Name2,Parent.Id,Parent.Name,Parent.Name2,Parent.Code
orderBy: coalesce(Parent.Name2, Parent.Name)
cells:   Code → "E-01"; Name → Name2 ?? Name; Parent → a link "المنطقة الشرقية"; Parent.Code → "R-1"
```

### 6.4 Dropped keys

A plan never sends a key it cannot serve, so a shared URL or a stale preference never `403`s the
page.

- **Dropped keys** are those that do not resolve (`unknown`) or that `TmPermissions.reachable`
  refuses (`unreachable`). They stay out of `select` and `columns` and are listed in `dropped`.
- **Telemetry.** Each drop emits `tm.entity.column-dropped` (§18). The host supplies its `source`:
  `url`, `preference` or `declaration`. A host also emits it with `reason: 'invalid-value'` for a
  URL filter value its control rejects (spec 0030 §4.1).

## 7. Client permissions

`TmPermissions` answers from the cached access summary and the securables metadata. Buttons,
entries and offered paths therefore never invite a refusal. The server stays the authority.

```ts
declare class TmPermissions {                                   // @tellma/core primary
  can(resource: string, action: string): Signal<boolean>; filtered(resource: string, action: string): Signal<boolean>;
  allows(entity: string, operation: TmEntityOperation | 'activate' | `action:${string}`): Signal<boolean>;
  sensitive(resource: string, action: string): boolean;         // a deployment fact: step-up ahead
  reachable(root: string, key: string): Signal<boolean>;        // the traversal rule; needs TmEntities.load()
  label(resource: string, action?: string): Signal<string>;     // 'Sales invoices: Create and edit'; no action: 'Sales invoices'; a settings securable: 'Settings: General' (spec 0032 §12.4)
  disabledReason(entity: string, operation: TmEntityOperation | 'activate' | `action:${string}`): Signal<string | null>;
}
```

- **Source.** `me.access` (spec 0025 §3.1), in spec 0016 §3.4's shape.
- **Grants.** `isSystem` allows everything. Otherwise a listed securable is allowed, and it is
  filtered when `hasFilter` is set. Wildcards, "write implies read" and owner grants arrive already
  composed (spec 0014 §5.2, §5.5). A time-bounded role membership's grants appear for its window
  only: the summary lists the grants current when it was built, and its `validUntil` is the next
  window boundary (spec 0014 §10), at which the client refreshes `me` (spec 0025 §5.3).
- **Before the first `me`**, every answer is false. Hosts never see that state: the route host
  creates no page host before the first `me` and `settings` (spec 0024 §4.4).
- **`allows`** holds when the operation exists, its securable is allowed, and the tenant's state
  admits it (below). `details` also needs a details page (below).
  - The summary's `operations` answer the `TmEntityOperation`s.
  - `activate` exists when `capabilities` has `activatable` and `operations` has `save`.
  - `action:<name>` reads the descriptor's `actions`, so it is false until `TmEntities.load()`
    settles; page hosts are created after it (spec 0024 §4.4).

The securable each operation needs:

| Operation | Securable |
|---|---|
| `query`, `details`, `export` | `Read` |
| `save`, `import` | `Save` |
| `delete` | `Delete` |
| `activate` | `Activate` (`activatable` only) |
| `action:<name>` | the action's `action`; always true when member-only |

- **A details page.** An entity *has a details page* when its summary lists `details` and its
  merged declaration has a `details` member (§3). Without one, `allows(E, 'details')` is false,
  spec 0024 §9.2's details routes do not match, and `TmUrls.target` returns null (spec 0024
  §9.4). So no link, row target, picker Create… or Edit…, or launcher New leads to a page that
  does not exist. The routes test only the page, never the grant.
- **Example —** Acme declares no `acme.Currency` layout. A currency cell shows its name with no
  link, and `/17/currencies/5` is the tenant not-found page.
- **Tenant state.** `TmLaunchCache.docs.tenant().state` (spec 0025 §3.1) gates the writes:
  - under `ReadOnly`, `save`, `import`, `delete`, `activate` and every mutating `action:<name>`
    without `allowWhenReadOnly` are refused;
  - under `Frozen`, every one of them is refused;
  - reads (`query`, `details`, `export`) and `can` never change.
- **`sensitive`** reads the securables section (spec 0014 §10): step-up dialogs and Save warn ahead.
- **`label`** names a resource and action pair in the words the Access tab uses (spec 0032 §4.6).
  The `forbidden` messages read it (spec 0024 §11).
  - It takes the resource's and the action's labels from the securables section. Spec 0032 §12.4
    gives their label chains.
  - It joins them with the string `tm.entity.permission`, "{resource}: {action}".
  - A pair the section does not list shows its raw names.
- **`disabledReason`** is the text a permission-disabled action shows (spec 0026 §9.4, through
  `tmButton`'s `disabledReason`, spec 0010 §D.12). It is null when `allows` holds, and when the
  operation does not exist (`allows` above), since such an action is hidden. Otherwise:
  - a refusal by the tenant's state reads `tm.entity.tenantReadOnly`, "{tenant} is read-only.",
    or `tm.entity.tenantFrozen`, "{tenant} is under maintenance.", with the tenant name the shell
    shows (spec 0027); it wins over a missing grant;
  - a missing grant reads `tm.entity.permissionNeeded`, "Needs “{permission}”. Ask your
    administrator to grant it.", with `label`'s text for the operation's securable: "Needs
    “Customers: Create and edit”…".
- **`reachable(root, key)`** mirrors spec 0015 §5.2 step 4:
  - the root's `Read` must be allowed;
  - when a navigation's target is restricted, every column read through it must be in the
    target's `relatedSelect`;
  - a navigation segment reads its foreign key;
  - "restricted" is as §1's vocabulary defines it; `core.Blob` always counts as restricted.
- **From a child root**, a path through the owner navigation is checked as if the owner were the
  root (spec 0015 §5.2): a line reaches `Invoice.PostingDate` under a filtered `Read` on invoices.

**Example —** with a filtered `Read` on `gl.Center`, from root `gl.Center`: `Parent.Name` and
`Parent.Code` are reachable (projected); `Parent.CreatedAt` and `Parent.Manager` are not.

## 8. The client Queryex

The client never parses Queryex. It lexes text for highlighting, parameters and safe insertion. It
builds and prints the Queryex it writes itself, such as filters and order terms, so no client code
concatenates query text.

### 8.1 Lexer

```ts
type TmQxTokenKind = 'number' | 'string' | 'identifier' | 'keyword' | 'parameter' | 'punctuator' | 'error';
interface TmQxToken { readonly kind: TmQxTokenKind; readonly start: number; readonly length: number; readonly text: string; readonly call?: true } // call: an identifier followed by '(', highlighted as a function
interface TmQxLexDiagnostic { readonly code: 'QX1001' | 'QX1002' | 'QX1003' | 'QX1004' | 'QX1005'; readonly start: number; readonly length: number }
declare function tmQxLex(text: string): { readonly tokens: readonly TmQxToken[]; readonly diagnostics: readonly TmQxLexDiagnostic[] };
declare function tmQxParameters(text: string): readonly { readonly name: string; readonly sites: readonly { start: number; length: number }[] }[];
```

- **Grammar.** The lexer follows spec 0008 §4. An engine-generated corpus is checked in: tokens,
  parameters and QX1xxx codes per text. The TS lexer must agree with it.
- **Spans** are UTF-16 offsets, like the engine's, so a `start` feeds `setSelectionRange` directly.
- **`tmQxParameters`** groups parameters case-insensitively under their first spelling. Text inside
  a string literal is not a parameter: `'a@b'` yields none.

### 8.2 Builder and printer

```ts
type TmQx = { readonly kind: 'path'; readonly segments: readonly string[] }
  | { readonly kind: 'string'; readonly value: string } | { readonly kind: 'number'; readonly value: string } // decimal text
  | { readonly kind: 'bool'; readonly value: boolean } | { readonly kind: 'null' } | { readonly kind: 'param'; readonly name: string }
  | { readonly kind: 'call'; readonly name: string; readonly args: readonly TmQx[] }
  | { readonly kind: 'not' | 'negate'; readonly operand: TmQx }
  | { readonly kind: 'binary'; readonly op: TmQxBinaryOp; readonly left: TmQx; readonly right: TmQx }
  | { readonly kind: 'in'; readonly value: TmQx; readonly items: readonly TmQx[] }
  | { readonly kind: 'is-null'; readonly value: TmQx; readonly negated: boolean }
  | { readonly kind: 'raw'; readonly text: string };           // prints "(text)"
type TmQxBinaryOp = 'or' | 'and' | '=' | '!=' | '<' | '<=' | '>' | '>=' | '+' | '-' | '||' | '*' | '/' | '%';
declare const tmQx: {
  path(path: string): TmQx; str(value: string): TmQx; num(value: number | string): TmQx; // a TmDecimal is a string
  bool(value: boolean): TmQx; null(): TmQx; param(name: string): TmQx; call(name: string, ...args: TmQx[]): TmQx;
  literal(type: TmDataType, value: unknown): TmQx;             // dates 'yyyy-MM-dd', date-times 'yyyy-MM-ddTHH:mm:ss', instants ISO with offset, enums as strings
  raw(text: string): TmQx;                                     // server-supplied text only, never user keystrokes
  not(x: TmQx): TmQx; and(...xs: (TmQx | null)[]): TmQx; or(...xs: (TmQx | null)[]): TmQx; // nulls skipped; empty and → true, empty or → false
  eq(a: TmQx, b: TmQx): TmQx; ne(a: TmQx, b: TmQx): TmQx; lt(a: TmQx, b: TmQx): TmQx;
  le(a: TmQx, b: TmQx): TmQx; gt(a: TmQx, b: TmQx): TmQx; ge(a: TmQx, b: TmQx): TmQx;
  in(value: TmQx, items: readonly TmQx[]): TmQx; isNull(value: TmQx, negated?: boolean): TmQx;
};
declare function tmQxPrint(expression: TmQx): string;
declare function tmQxInsert(text: string, caret: number, insertion: string): { readonly text: string; readonly start: number; readonly end: number };
```

- **Parentheses.** The printer follows spec 0008 §5.2's precedence. It parenthesizes a child that
  binds looser than its parent, a right operand of equal power (`a - (b - c)`), and a comparison
  inside a comparison.
- **Identifiers** that are keywords or break the grammar print bracketed: `[Order]`. An identifier
  containing `]` throws.
- **Literals.** Strings double each `'`. Numbers print as decimal text without an exponent. A
  `number` beyond 15 significant digits is refused, so pass its text instead, such as a
  `TmDecimal`. Text that is not a decimal number throws.
- **`raw`** parenthesizes server-supplied text, such as a report's drill filter (spec 0033 §9.4).
- **`tmQxInsert`** never splits a token and returns the inserted range. An insertion inside a token
  moves to the token's end; a space is added where it would touch an identifier, a digit or `)`.

**Example —** a numeric between filter and an escaped string:

```ts
const p = tmQx.path('CreditLimit');
tmQxPrint(tmQx.and(tmQx.ge(p, tmQx.num('100000')), tmQx.le(p, tmQx.num('500000'))));
// 'CreditLimit >= 100000 and CreditLimit <= 500000'
tmQxPrint(tmQx.or(tmQx.call('contains', tmQx.path('Name'), tmQx.str("O'Neil")), tmQx.eq(tmQx.path('Code'), tmQx.str('7'))));
// "contains(Name, 'O''Neil') or Code = '7'"
```

## 9. `queryex/discover`

One endpoint checks Queryex while it is being written, exactly as `query` or a permission filter
would compile it. It reports diagnostics, parameters with inferred types, and path uses. It runs
no SQL.

```csharp
// Tellma.Core.Api — registered by CoreFeature through contribution.ApiService<QueryexService>()
[ApiRoute("queryex")]
public sealed class QueryexService
{
    [ApiAction("discover", MemberOnly = true, Idempotent = true, Mutation = false, Mcp = McpExposure.Hidden)]
    public Task<QueryexDiscoverResult> DiscoverAsync(QueryexDiscoverRequest request);
}

// Tellma.Core.Abstractions.Api
public sealed record QueryexDiscoverRequest(
    string Root,                                       // entity name: 'gl.Center', 'acme.SalesInvoiceLine'
    string? Select = null, IReadOnlyList<string>? Filters = null, string? Having = null, string? OrderBy = null,
    string? Measures = null,                           // a pivot's or summary's measures (spec 0010 §F.14); needs Aggregate
    bool Aggregate = false, IReadOnlyList<QueryexParameterInput>? Parameters = null, // confirmed types; others are inferred
    QueryexProfile Profile = QueryexProfile.Query,
    bool Conjuncts = false);                           // also answer the filters' top-level conjuncts (spec 0010 §F.21)
public sealed record QueryexParameterInput(string Name, QueryexType Type);
public enum QueryexProfile { Query, Permission }      // Permission: one filter, no parameters, validated as role save validates it (step 3)
public sealed record QueryexDiscoverResult(
    IReadOnlyList<QueryDiagnostic> Diagnostics,       // spec 0016 §3.4, whose Clause list is closed
    IReadOnlyList<QueryexParameterInfo> Parameters, IReadOnlyList<QueryexPathInfo> Paths, bool UsesAggregation,
    IReadOnlyList<QueryexConjunct>? Conjuncts);       // null unless asked
public sealed record QueryexConjunct(
    int Filter,                                       // the index into Filters
    string? Path, string? Expression,                 // Path: dotted, 'Invoice.CustomerId'; Expression: canonical text; exactly one
    string Op,                                        // '=', '!=', '<', '<=', '>', '>=' or 'descendantOf'
    JsonElement? Value, QueryexType Type);            // Value: the literal in spec 0016 §3.2's encoding; null for null
public sealed record QueryexParameterInfo(
    string Name, IReadOnlyList<QueryexSite> Sites,
    QueryexType? Type, IReadOnlyList<QueryexType> Conflicts,  // Type: confirmed, else inferred; null: unconstrained or conflicting
    IReadOnlyList<string>? ComparedWith,              // the path the parameter meets (spec 0010 §F.13)
    string? Entity);                                  // ComparedWith's target when it ends at a key or foreign key
public sealed record QueryexPathInfo(IReadOnlyList<string> Segments, QueryexSite Site, string Entity, bool Reachable);
public sealed record QueryexSite(string Clause, string? Location, int Start, int Length);
```

1. **Authorize.** Member only; a root outside the `All` schema is `404 not-found`, and `Measures`
   without `Aggregate` is `400 bad-request`. The call runs through the guarded runner (spec 0015
   §5.1) with no statement appended, so it makes no round trip when warm.
2. **Discover.** `DiscoverQuery` over all clauses together (spec 0008 §2.4), `Measures` included
   (spec 0010 §F.14), in `CompileQuery`'s modes. With `Conjuncts`, it also answers each filter's
   top-level `and` atoms that compare a path or an expression with a literal (spec 0010 §F.21),
   which the drill list's chips render (spec 0034 §8.4).
3. **Compile.** `CompileQuery` runs under `PipelineLimits` (spec 0015 §5.2); its SQL is discarded.
   - The query is `QuerySpec { Root, Select = Select ?? "Id", Aggregate, Filter = And(Leaf(f) for
     each Filters), Having, OrderBy, Measures }`. Measure diagnostics, including QX4010 and QX4011,
     sit at `Measures[<i>]` in clause `measures`.
   - The parameter declarations are the confirmed types plus the inferred ones.
   - The `Permission` profile validates its filter as role save does (spec 0014 §3.7 step 6).
   - QX3007 is dropped, and an unconstrained parameter's `Type` is null.
4. **Traverse.** Spec 0015 §5.2's traversal rule runs over every path. Each refusal becomes a
   `Query.PathNotReachable { entity, path }` diagnostic at the path's span (spec 0015 §7.3).
5. **Answer** `200` with every diagnostic. No diagnostic turns the answer into a `400`, and syntax
   errors do not stop discovery.

- **Wire class.** A retried read (spec 0016 §2.2), cancellable per keystroke. `Idempotent = true`
  puts the route in the Contracts chunk's `idempotent` list, so `TmApi.read` accepts it (spec 0024
  §10.1). It is fingerprinted like every projected endpoint, uses rate policy `tellma-user`, and
  stamps activity.
- **Messages.** Diagnostics carry codes and arguments. The client renders the `Queryex_<code>` and
  `Query_*` keys (spec 0013 §10.1) through `TmServerText.message` (spec 0024 §7.5).

**Example —** the studio's filter with one typo, `Accnt`:

```json
→ { "root": "acme.SalesInvoiceLine", "filters": ["Invoice.PostingDate >= @From and Accnt.Code = '1101'"] }
← { "diagnostics": [ { "clause": "filters[0]", "code": "QX3001", "start": 33, "length": 5,
      "location": "Filter.And[0]", "arguments": { "name": "Accnt", "entity": "acme.SalesInvoiceLine" } } ],
    "parameters": [ { "name": "From", "sites": [ { "clause": "filters[0]", "start": 23, "length": 5 } ],
      "type": "Date", "conflicts": [], "comparedWith": ["Invoice", "PostingDate"] } ],
    "paths": [ { "segments": ["Invoice", "PostingDate"], "site": { "clause": "filters[0]", "start": 0, "length": 19 },
      "entity": "acme.SalesInvoice", "reachable": true } ], "usesAggregation": false }
```

## 10. The expression editor

`tm-expression-editor` edits one Queryex clause. It highlights tokens on every key, shows server
diagnostics a moment later, and takes fields dropped or inserted at the caret. Editors of one query
share a `TmQueryexDraft`, so inference sees every clause.

### 10.1 Contract

```ts
type TmQxClause = 'select' | 'filter' | 'having' | 'orderBy' | 'measures' | 'constant';
// measures: sent only with aggregate (§9); constant: lexical only, no root, never sent
interface TmQueryexDraft {
  readonly root: Signal<string>; text(clause: TmQxClause): WritableSignal<string>;
  readonly status: Signal<'idle' | 'checking' | 'checked' | 'offline' | 'failed'>; retry(): void;
  diagnostics(clause: TmQxClause): Signal<readonly TmQxDiagnostic[]>; // lexical at once; the server's when checked
  readonly parameters: Signal<readonly TmQxParameter[]>;           // names at once; types when checked
  readonly paths: Signal<readonly TmQxPathUse[]>;                  // §9's QueryexPathInfo, camelCase
}
interface TmQxDiagnostic { readonly code: string; readonly start: number; readonly length: number; readonly message: Signal<string> }
interface TmQxParameter { readonly name: string; readonly type: string | null; readonly conflicting: boolean; readonly entity?: string }
declare function injectTmQueryexDraft(options: {                   // lives until the injector's DestroyRef
  root: Signal<string>; aggregate?: Signal<boolean>; profile?: 'query' | 'permission';
  parameters?: Signal<readonly { name: string; type: string }[]>;  // types the author confirmed (spec 0034)
}): TmQueryexDraft;
@Component({ selector: 'tm-expression-editor' })                  // FormValueControl<string>
declare class TmExpressionEditor {
  readonly value: ModelSignal<string>; readonly clause: InputSignal<TmQxClause>;
  readonly draft: InputSignal<TmQueryexDraft | undefined>; readonly root: InputSignal<string | undefined>; // no draft: a private one over root
  readonly rows: InputSignal<number>; readonly showParameters: InputSignal<boolean>; // rows: fixed height, default 3; showParameters: the chip row, default off
  readonly ariaLabel: InputSignal<string | undefined>; readonly placeholder: InputSignal<string | undefined>;
  readonly submit: OutputEmitterRef<void>;                          // Mod+Enter
  insertAtCaret(text: string): void; focus(): void;                 // insertAtCaret: the non-drag path (§11.3)
}
```

**Example —** the search page's advanced filter (spec 0030 §10):

```html
<tm-expression-editor clause="filter" [root]="entity" [(value)]="filterText" (submit)="apply()" />
```

- **In a grid cell.** The editor implements spec 0004's `TmCellEditor` (`value`, `text`, `commit`,
  `cancel`, `focus`, `seed`) and registers through `TM_CELL_EDITOR_HOST`, as every `tm-*` control
  does, so a popup cell editor hosts it (spec 0010 §C.15). There Enter inserts a line, and
  Mod+Enter, left unconsumed, commits through the grid.

### 10.2 Checking

- **When.** The draft calls `queryex/discover` 250 ms after the last change to any clause; each
  change restarts the wait. It sends every non-empty clause except `constant`, with `filter` as
  `filters[0]` and `measures` as `measures`. The `measures` clause holds the studio's pivot and
  summary measures (spec 0034 §12.7).
- **Abort.** Each change aborts the call in flight (`TmApi.read` with the draft's signal).
- **Stale answers.** An answer applies only to clauses whose text still equals what was sent. An
  edited clause drops its server diagnostics at once and shows "Checking…".
- **Offline or failed.** Lexical diagnostics still show. The status line reads "Can't check while
  offline" or "Couldn't check the expression", with Retry.
- **Parameters.** Names come from `tmQxParameters` on every key. Types, conflicts and the compared
  entity merge in from the last applied answer (the studio's parameter table, spec 0034 §12.4).

### 10.3 Rendering and layout

- **Two layers.** A native `textarea` (`dir="ltr"`, mono, no spellcheck or autocomplete) sits over
  a `pre` mirror with the same metrics. The textarea's text is transparent and its caret visible.
- **The mirror** draws tokens in `--expr-*` colors with parameters bold, wavy underlines at
  diagnostic spans, and a 1.2 s tint on inserted text.
- **Fixed geometry.** The box is `rows` lines tall and scrolls inside. Nothing below it moves on
  typing.
- **Status line.** A 20 px line is always present. It reads "No problems", "Checking…", or the
  first problem with a count: "Line 2: Unknown property 'Accnt'" and "+2 more". "+2 more" opens a
  `tm-popover` (`size: 'md'`) listing every problem as a button that selects its span.
- **Chip row.** With `showParameters`, a 24 px row of `@From · Date` chips follows. It scrolls
  sideways. Chips for conflicting or untyped parameters are warning-toned.
- **RTL.** Code is always LTR, as an isolate in RTL pages. The status line, chips and popover
  follow the page.

### 10.4 Drop and insert

- **Drop.** A `tm.path` drag (§11.3) carries Queryex text as `text/plain`. The textarea takes it as
  a native drop at the caret, so undo works. On `insertFromDrop`, the editor re-spaces the text.
- **Insert.** `insertAtCaret(text)` applies `tmQxInsert` at the last caret, or at the end when the
  editor was never focused. It is one native undo step and leaves the inserted range selected. The
  e2e pins undo in three engines.

### 10.5 Keyboard and ARIA

- **Keys.** Editing keys are native. Tab and Shift+Tab leave the editor: no tab insertion, no
  trap. F8 and Shift+F8 select the next and previous problem's span. `Mod+Enter` emits `submit`,
  matched by `tmKeysMatch` (spec 0023 §3.2).
- **ARIA.** The editor is labelled by `ariaLabel` or the enclosing `tm-form-field`, and described
  by the status line. It is `aria-invalid` while any diagnostic exists. A polite region announces
  checks, never keys.

## 11. The path tree picker

`tm-path-picker` lists a root entity's fields as a tree on spec 0023's `tm-tree`, with
navigations as branches. It is built from local descriptors, so a branch never waits on a request.

### 11.1 Contract

```ts
@Component({ selector: 'tm-path-picker' })
declare class TmPathPicker {
  readonly root: InputSignal<string>; readonly mode: InputSignal<'columns' | 'fields'>; // mode: column keys (§6.1) or Queryex paths
  readonly selection: InputSignal<'none' | 'single' | 'multiple'>; readonly selected: ModelSignal<ReadonlySet<string>>;
  readonly draggable: InputSignal<boolean>;                 // default false
  readonly dateParts: InputSignal<boolean>;                 // 'fields' mode: date fields branch into their parts (§11.2); default false
  readonly accept: InputSignal<((node: TmPathNode) => boolean) | undefined>; // a measure: numeric only
  readonly query: ModelSignal<string>;                      // the built-in search field's text
  readonly ariaLabel: InputSignal<string | undefined>; readonly size: InputSignal<TmControlSize | undefined>;
  readonly activate: OutputEmitterRef<TmPathNode>;          // Enter, or a double-click on a leaf; never in 'multiple'
}
interface TmPathNode {
  readonly key: string; readonly kind: 'value' | 'text' | 'navigation' | 'image' | 'file'; // key: 'Parent.Manager'
  readonly type: TmDataType | null; readonly target?: string; readonly label: Signal<string>; readonly pathLabel: Signal<string>; // pathLabel: the slash label (§11.4)
  readonly expr?: string;                                   // a date part's Queryex text, which is also its key
}
```

### 11.2 Nodes

- **Shown:**
  - queryable scalars;
  - navigations, as branches with a pill naming the target ("Parent center · Center");
  - each group, as one `text` node. On a multi-language tenant its children are its admitted
    slots (`Name (E)`, `Name (ع)`), with the twins only in `columns` mode;
  - image and file navigations, as leaves in `columns` mode and as branches into `core.Blob` in
    `fields` mode;
  - with `dateParts`, each `date`, `datetime` and `instant` field as a branch of `value` nodes, its
    date parts, in this order:
    - Year, Quarter, Month and Week: `startOfYear(d)`, `startOfQuarter(d)`, `startOfMonth(d)`,
      `startOfWeek(d)`;
    - Day, for date-times and instants only: `startOfDay(d)`;
    - Quarter of year, Month of year and Day of week: `quarter(d)`, `month(d)`, `weekday(d)`.

    `d` is the field's path, wrapped in `local(…)` for an instant. The field itself stays a
    draggable node.
  - **Calendars.** A function that takes a calendar is given the active calendar's code when
    Queryex binds it, `'uq'` or `'et'` (spec 0010 §F.11, §F.19), as in
    `startOfMonth(Invoice.PostingDate, 'uq')`; Gregorian passes none. Under any other calendar the
    parts are Gregorian and say so: "Month (Gregorian)", from `tm.entity.datePartGregorian`
    ("{part} (Gregorian)").
  - **A part's node** has kind `value`; `type` `date` for the `startOf…` parts and `integer` for
    the others; `label` the part's name ("Month"); `pathLabel` "Invoice / Posting date / Month";
    and `key` and `expr` its text. `accept` sees `expr`, so a consumer can refuse parts.
- **Hidden:** foreign-key scalars (their navigation stands for them), non-queryable properties,
  twins beyond the shape, and every unreachable path (§7).
- **Order.** By label (`Intl.Collator`), with `Id` and the four audit fields last.
- **Icons** (spec 0023): numbers `hash`, text `type`, enums `list`, booleans `toggle-left`, dates
  `calendar`, navigations `link-2`, images `image`, files `paperclip`, groups `languages`.

### 11.3 Search, drag and activation

- **Search** filters the tree by label and path name, normalized by spec 0027 §9.4's
  `tmSearchNormalize`. It reaches three navigation steps deep and shows at most 200 matches, each
  inside its expanded, muted ancestors (`tm-tree` context rows). A polite announcement reads
  "12 fields" or "No fields match".
- **Drag payload** is a spec 0023 `TmDragPayload` of kind `tm.path`, carrying the node and its
  slash label. Its text is the path, for a navigation the foreign-key path (`Parent.ManagerId`),
  and for a date part its `expr`, so it is always valid Queryex.
- **Activation.** Enter, or a double-click on a leaf, emits `activate`; a double-click on a branch
  expands or collapses it and emits nothing. In `selection: 'multiple'`, Enter and the checkbox
  toggle the node in `selected` instead, and a double-click only expands or collapses (spec 0030
  §11.2). Consumers add the field (spec 0034 §12.5) or call `insertAtCaret` on the last-focused
  editor, the non-drag path for every drop (WCAG 2.5.7).

### 11.4 Slash labels

`pathLabel` joins segment labels with " / " as plain text. Every segment is a label in the UI
language, so the page's direction orders them: "Parent center / Code" in English and "المركز الأب
/ الرمز" in Arabic, where an English fallback label (§2.4) still reads in its place. The same label
serves `TmColumnPlan.label` and filter chips.

## 12. Typed values

Every value has one display format and one control. Both are chosen from its value descriptor, the
same way on every surface.

### 12.1 Contract

```ts
interface TmValueDescriptor {                               // property metadata is one; so is a setting key's
  readonly type: TmDataType; readonly nullable: boolean; readonly enum?: string; readonly maxLength?: number; readonly scale?: number; // enum: a named enum (§2.2)
  readonly long?: true;                                     // an integer that is a C# long: its values are TmDecimal
  readonly target?: string; readonly array?: true;          // target: the value is an id of this entity, 'core.User'; array: with enum or target only
}
declare class TmFormats {                                   // @tellma/core/entity
  text(value: unknown, of: TmValueDescriptor, style?: 'grid' | 'details'): string; // instants: zone label mode 'auto'
  cell(column: TmColumnPlan, row: TmPlanRow): string;       // navigation and group cells included; instants: mode 'column'
  zoneSuffix(of: TmValueDescriptor | TmColumnPlan): Signal<string>; // ' (GMT+3)' for an instant while TmL10n.zoneMismatch(); else ''
}
@Component({ selector: 'tm-value' })                        // view mode: links, badges, avatars, thumbnails
declare class TmValue { readonly property: InputSignal<TmValueDescriptor | TmPathResolution>; readonly value: InputSignal<unknown> }
@Component({ selector: 'tm-value-editor' })                 // edit mode: the right control, bound to a field
declare class TmValueEditor {
  readonly property: InputSignal<TmValueDescriptor | TmPropertyDescriptor | TmNavigationDescriptor>;
  readonly field: InputSignal<FieldTree<unknown>>; readonly path: InputSignal<string | undefined>; // path: for TmUploads
}
declare class TmUploads {                                   // provided by a form's host (spec 0031)
  readonly running: Signal<ReadonlySet<string>>;            // paths with an upload in flight
  staged(path: string): TmBlobDescriptor | undefined;       // the blob staged for a path, for previews and drafts
}
declare function tmGridColumnFor(column: TmColumnPlan, options?: { editable?: boolean }): Partial<TmGridColumnConfig>;
```

- **Direct descriptors.** Setting keys (spec 0032 §7.4) and action fields (§2.2) pass one directly.
- **`TmFormats`** reads `TmL10n`, `TM_TIME_ZONE` and the active slot, re-rendering on every switch.
- **Zone labels.** An instant carries spec 0010 §D.4's offset label when the display zone's offset
  differs from the device's. A grid uses the `'column'` mode:
  - the header shows the label once, through `zoneSuffix`: "Created at (GMT+3)" (spec 0030 §7.1);
  - the cells stay bare, except a cell whose offset differs from the header's across a DST change.
- **`tm-value-editor`** puts the control in the caller's `tm-form-field`, bound with `[formField]`.
  `tm-field`, the parameter bar, `tm-setting-field` and the generated action dialog compose it.
- **Uploads.** An image or file editor checks the file (§16) and uploads it through `TmApi.upload`
  (spec 0024 §10.1). Until the upload settles, the editor lists its `path` in the nearest
  `TmUploads`. Spec 0031 §7.1's schema reports a listed path `pending`, so Save waits. The staged
  id becomes the value.

### 12.2 The table

| Type | Text (`details` / `grid`) | Control | Grid column |
|---|---|---|---|
| `string` | as is, `dir="auto"` (a slot: `contentDir`) | `input[tmInput]` with `maxlength`; a 3-row `textarea[tmInput]` when `maxLength` is absent or above 255 | text |
| `integer` | locale digits, grouping | `input[tmNumber]`, `maxDecimals` 0 | number, physical right |
| `decimal` | `minDecimals` = `maxDecimals` = `scale` | `tmNumber`, `maxDecimals` = `scale` | number |
| `bool` | "Yes" / "No"; a check glyph in grids | `tm-checkbox` | checkbox display |
| `date` | `dateStyle` `medium` / `numeric`, active calendar | `tm-date-picker` | date |
| `instant`, `datetime` | date-time in the display zone (spec 0010 §D.4), with the zone label of §12.1; a `datetime` in `UTC`, unconverted and never labelled | read-only (below) | read-only |
| `enum` | the value's label | `tm-select` in the enum's order; an array: `tm-multi-select` | enum |
| `target` | the display name, a link when `allows(target, 'details')`; with an avatar column, `tm-avatar` and the name, the avatar's `colorKey` being the record key `{entity}:{id}`, such as `core.User:17` | `tm-entity-field` (§14); an array: `tm-entity-multi-field` | entity editor via the binding |
| image | `tm-image` thumbnail (§16) | `tm-image` in edit mode, uploading on pick | thumbnail, read-only |
| file | the file name as a download link | `button[tmFilePicker]` and the file chip | file name, read-only |
| other (`guid`, `time`) | as is, mono | `input[tmInput]` | text |

- **Null** renders empty in grids and "—" in details view (`tm.value.none`). Arrays join through
  `Intl.ListFormat`.
- **No control.** `float`, `json` and `binary` have none; layouts use spec 0031 §6.2's hatch.
- **Ownership.** `server-owned`, `database-owned` and `derived` are read-only. `write-once` is
  editable in create mode only.
- **Editable instants.** `tm-value-editor` keeps an instant read-only. A layout that edits one
  places spec 0023's `tm-date-time-field` bound to the field, in the display zone, as the role
  membership window does (spec 0032 §4). A `datetime` has no editor.
- **Numbers** keep the physical right alignment in RTL (spec 0004).
- **Decimals and longs.** A `decimal` value, or an `integer` marked `long`, is a `TmDecimal`.
  `TmFormats` formats its text exactly with `Intl.NumberFormat.prototype.format`, never through a
  `number`. The `tmNumber` control converts at the binding (spec 0024 §10.4).

## 13. Filter controls

Each column kind has one filter control. The control writes the `f.{key}` URL value (spec 0024
§9.5). Each value becomes one Queryex leaf in its own `QueryRequest.Filters` entry, so its
diagnostics never mix with other filters'.

### 13.1 Contract

```ts
type TmFilterOp = 'contains' | 'starts-with' | 'ends-with' | 'eq' | 'ne' | 'lt' | 'le' | 'gt' | 'ge' | 'between' | 'before' | 'after' | 'in' | 'is';
type TmFilterKind = 'text' | 'number' | 'date' | 'instant' | 'enum' | 'bool' | 'navigation';
declare const TM_ENUM_PICKER_THRESHOLD = 50;               // an enum with this many values or more filters through tm-entity-multi-picker
interface TmFilterSpec { readonly op: TmFilterOp; readonly values: readonly string[] }   // culture-free
declare function tmParseFilter(value: string): TmFilterSpec | null;   // 'between:1,5' → { op: 'between', values: ['1', '5'] }
declare function tmFormatFilter(spec: TmFilterSpec): string;
declare function tmFilterLeaf(column: TmColumnPlan, spec: TmFilterSpec, context: { timeZone: string }): string | null; // null: invalid here
declare function tmDescribeFilter(column: TmColumnPlan, spec: TmFilterSpec): Signal<TmFilterDescription>;
interface TmFilterDescription {                            // text: "Name contains jed", for accessible names
  readonly text: string; readonly parts: readonly { readonly text: string; readonly argument: boolean }[];
}
@Component({ selector: 'tm-filter-description' })          // inline: the parts, each argument part in a <bdi>
declare class TmFilterDescriptionText { readonly description: InputSignal<TmFilterDescription> }
@Component({ selector: 'tm-column-filter' })               // the content of a header popover (size 'sm')
declare class TmColumnFilter {
  readonly column: InputSignal<TmColumnPlan>; readonly value: InputSignal<string | null>; // the current f. value
  readonly applied: OutputEmitterRef<string | null>;       // Apply → a value; Clear → null
}
```

### 13.2 Kinds, operators and leaves

| Kind (column types) | Ops, default first | Control | `f.` value → leaf |
|---|---|---|---|
| `text` (string; a group anchor) | `contains`, `starts-with`, `ends-with`, `eq`, `ne` | `tm-select` + `input[tmInput]` | `contains:jed` → `contains(Name, 'jed') or contains(Name2, 'jed')`; `ne:x` → `Name != 'x' and Name2 != 'x'` |
| `number` (integer, decimal) | `between`, `lt`, `le`, `eq`, `ge`, `gt` | `tm-select` + `tmNumber`, two for `between` | `between:100000,500000` → `CreditLimit >= 100000 and CreditLimit <= 500000` |
| `date` (date, datetime) | `between`, `before`, `after` | `tm-select` + `tm-date-picker`; `tm-date-range` for `between` | `before:2026-03-01` → `PostingDate < '2026-03-01'` |
| `instant` (instant) | as `date` | as `date`, days in the display zone | see the example below |
| `enum` (enum) | `in` | a checkbox list up to 8 values; `tm-multi-select` below `TM_ENUM_PICKER_THRESHOLD`; else `tm-entity-multi-picker`, `maxItems` 50 | `in:Service,Sale` → `CenterType in ('Service', 'Sale')` |
| `bool` (bool) | `is` | `tm-radio-group`, True / False | `is:true` → `IsActive = true` |
| `navigation` (a navigation) | `in` | `tm-entity-multi-field`, `maxItems` 50 | `in:1012,1015` → `ParentId in (1012, 1015)`; `Parent.Manager` → `Parent.ManagerId in (…)` |
| `image`, `file`, `expression`, `guid`, `float`, `time`, `json` and `binary` columns | none | — | — |

**Example —** an `instant` column in Asia/Riyadh: `between:2026-01-01,2026-01-31` →
`CreatedAt >= '2025-12-31T21:00:00+00:00' and CreatedAt < '2026-01-31T21:00:00+00:00'`.

- **Semantics are Queryex's** (spec 0008 §9.2). `ne` also matches rows with no value; `between`
  and the orderings never do. `before` and `after` are strict, and `between` includes both ends.
- **Multilingual text** reads every admitted slot. `contains`, `starts-with`, `ends-with` and `eq`
  join the slots with `or`; `ne` joins them with `and`, so it matches a row only when no slot
  equals the value.
- **Instants** filter whole days in `TmLocalization.timeZone()`. A day starts at its first instant
  in that zone: midnight, or the earliest valid instant when midnight does not exist.
- **Date-times** (`datetime`, a wall-clock `DateTime`) filter whole days with no zone. For a
  `datetime` column `D`, `between:2026-01-01,2026-01-31` →
  `D >= '2026-01-01T00:00:00' and D < '2026-02-01T00:00:00'`; `before` compares with the day's
  start and `after` with the next day's.
- **Literals** come from `tmQx.literal` (§8.2); an invalid value yields null, and the host drops it.
- **Large enums** (`core.TimeZone`, `core.Region`) use spec 0010 §E.2's `tm-entity-multi-picker`
  over the enum's own values, with no request. `search` answers synchronously with the values whose
  label or value contains the text, ignoring case, in the enum's order: at most 20, with `hasMore`
  when more match, and the first 20 for an empty text. `itemId` is the value; `itemLabel` and
  `displayWith` give its label. It offers no advanced search and no create.
- **Descriptions** are filter chip text (spec 0030 §10.1), one whole-sentence ICU message per
  operator, `tm.filter.describe.<op>`, so translators order the words (Amharic puts the verb
  last):
  - **Arguments** are named: `{label}`, `{value}`, `{from}` and `{to}` for `between`, `{values}`
    for `in`; `is` selects on `{value, select, true {…} other {…}}`.
  - **Values** are formatted as the column displays them: numbers by `TmFormats` in the locale's
    digits, dates in the active calendar, instants as whole days in the display zone, navigation
    values as display names, enum values as labels. An `in` list comes from `Intl.ListFormat`
    (`unit`, `narrow`) in the UI locale, each element its own part.
  - **Parts.** The describer formats the message with a private-use placeholder for each displayed
    argument, splits it there and puts the formatted values in their place, so the placeholders
    never leave it. `tm-filter-description` renders each argument part in a `<bdi>`; `text` joins
    the parts as plain text.
  - **Examples.** "Type: Service, Sale" (`in`: "{label}: {values}") and "Name contains jed".

### 13.3 The control

- **Layout.** Operator select, value inputs, Clear and Apply, 320 px wide. A `between` pair sits
  side by side ("From", "To"). Below `sm`, spec 0030 hosts the content in an end drawer.
- **Keys.** Apply is enabled only for a valid value. Enter applies; Escape belongs to the host
  popover.
- **ARIA.** The operator select is labelled "Operator" (`tm.filter.operator`) and the inputs by the
  column label. The enum list is a labelled `group`.
- **Digits.** Arabic-Indic digits parse; values serialize in Latin digits.

## 14. The generic entity picker binding

One binding, built from the target's descriptor, turns spec 0006's picker into an entity picker. It
brings search, display names, two-line rows and the target's pages in modals. Every entity field,
cell and filter uses it.

### 14.1 Contract

```ts
interface TmEntityPickerBinding {                           // spreads into tm-entity-picker inputs and grid entity columns
  readonly search: TmEntitySearchFn<TmJson>; readonly itemId: (item: TmJson) => TmId;
  readonly itemLabel: (item: TmJson) => string; readonly itemDescription?: (item: TmJson) => string | null;
  readonly itemLabelParts: (item: TmJson) => readonly string[];  // spec 0010 §E.1
  readonly displayWith: (id: TmId) => string | null; readonly createLabel?: string; readonly editLabel?: string;
  readonly advancedSearch?: TmEntityPickerPage; readonly create?: TmEntityPickerPage; readonly edit?: TmEntityPickerPage;
}
declare class TmEntityPickers {                             // @tellma/core/entity
  binding(entity: string, options?: { filter?: Signal<string | null>; create?: boolean; multiple?: boolean }): TmEntityPickerBinding;
}
@Component({ selector: 'tm-entity-field' })                // FormValueControl<TmId | null>; provides TM_FORM_FIELD_CONTROL
declare class TmEntityField {
  readonly entity: InputSignal<string>; readonly filter: InputSignal<string | null>; // filter: beside the declaration's
  readonly create: InputSignal<boolean>; readonly value: ModelSignal<TmId | null>; // create: default true
  readonly picked: OutputEmitterRef<TmEntityPicked<TmJson, TmId>>; // field-state and aria inputs: forwarded to the picker
}
@Component({ selector: 'tm-entity-multi-field' })          // FormValueControl<readonly TmId[]> over tm-entity-multi-picker
declare class TmEntityMultiField {
  readonly entity: InputSignal<string>; readonly filter: InputSignal<string | null>;
  readonly value: ModelSignal<readonly TmId[]>; readonly maxItems: InputSignal<number | undefined>;
}
```

**Example —** most forms write `<tm-field path="Parent" />` (spec 0031 §6.2). The raw hatch inside
a `tm-form-field` is:

```html
<tm-entity-field entity="gl.Center" [formField]="details.form.parentId" filter="CenterType = 'Abstract'" />
```

It renders one `tm-entity-picker` and provides `TM_FORM_FIELD_CONTROL` as a delegate over it.
`tm-form-field` therefore labels, describes and shows errors exactly as for the bare picker.

### 14.2 Search

1. **Request.** The binding calls
   `TmApi.read('{segment}/query', { select, search, filters, orderBy, take: 21 }, { signal })`:
   - `select`: `Id`, the display-name columns and the description columns;
   - `search`: the text without bidi controls (`tmStripBidiControls`, spec 0024 §7.9), trimmed,
     at most `limits.maxSearchLength`; omitted to browse;
   - `filters`: `picker.filter` and the field's `filter`, as separate leaves;
   - `orderBy`: `picker.orderBy`, else the display name's order.

   Inactive rows stay excluded.
2. **Result.** 20 rows, with `hasMore` when a 21st came back. The binding turns them into wire
   objects with `tmRowsToObjects` (§6.2), so items, `itemLabel` and `itemDescription` read `json`
   members. Each is remembered in `TmDisplayNames`.
3. **Cacheable targets** with no Queryex filter search `TmLaunchCache.entitySet()`'s list in
   memory. The search is synchronous: no debounce, no spinner, and it works offline. It returns
   the active rows ordered by display name whose admitted slots or natural keys contain the
   normalized text, or that §5.2's findable rule matches: the split at the separator, an integer
   source or prefix, `#{id}`, or the id itself. While the list is absent (loading, or refused with
   `403`), the binding queries the server.
4. **Description** is the row's second line (spec 0010 §E.1). It joins the `picker.description`
   paths with " · ". By default it shows the natural keys the name does not show and, for a tree,
   the parent's name.
5. **Unsearchable targets.** When `allows(entity, 'query')` is false, the field is read-only with
   its `displayWith` text, so a user keeps a value they cannot choose again.

### 14.3 Labels

- **`itemLabel(row)`** is `TmDisplayNames.of(entity, row)()`.
- **`itemLabelParts(row)`** is `TmDisplayNames.parts(entity, row)()` as `[prefix, name]`, or
  `[name]` without a prefix, so the picker renders each in its own `<bdi>` (spec 0010 §E.1).
- **`displayWith(id)`** is `TmDisplayNames.label(entity, id)()`, so a committed value re-renders on
  a language switch. An unknown id calls `resolve` in the meantime.
- **`createLabel`** is the target's `newLabelKey` text with "…", such as "New customer…".

### 14.4 Advanced search, Create and Edit

- **Offered.** Advanced search when `allows(entity, 'query')`. Edit when `allows(entity, 'save')`,
  and Create too, unless turned off. Both also need the entity to have a details page (§7), since
  they open it.
- **Loading.** The binding's page, `TmLazyPickerPage`, `import()`s `TmSearchPickerPage` (spec 0030
  §17) or `TmDetailsPickerPage` (spec 0031 §15) inside the modal, at `size: 'lg'`. The target
  travels in the page's `context` (spec 0010 §E.3):
  `{ entity, filter, kind: 'search' | 'create' | 'edit' }`.
- **Outcomes** follow spec 0006 §6:
  - the search page closes with `{ id, label, item }`; opened with `multiple`, it closes with
    `{ ids, picks }`, whose `ids` replace the field's value (spec 0010 §E.2), and
    `TmEntityMultiField` passes its `maxItems` to the page (spec 0010 §E.3);
  - for Create, the details page opens in create mode with the typed text in the display-name
    source's active slot;
  - for Edit, the details page opens on the id;
  - the details page closes with the pick, or `null` when the record is gone.

### 14.5 Grid cells

For a `navigation` column, `tmGridColumnFor(column, { editable: true })` spreads the binding into
spec 0006 §7.1's column inputs. It adds the column's `filter` (§3) to the picker's filters, sets
`format` to `displayWith`, and sets `formatParts` to `labelParts` in §14.3's array form (spec 0010
§E.4). `resolvePastedLabels` strips bidi controls from the pasted names (`tmStripBidiControls`),
then resolves them with one `query`. A name matches the display-name columns of every admitted slot
with `in (…)`, and §5.2's findable rule with equality in place of `startsWith`: a name holding the
separator matches its prefix and name halves.

## 15. Typed entity codegen

The emit writes `generated/tellma-entities.d.ts`: one wire interface and one path shape per entity,
registered by name. Declarations therefore type-check with no type argument.

```ts
// generated/tellma-entities.d.ts — emitted; never edited
export type GlCenterType = 'Abstract' | 'Service' | 'Sale' | 'Operation';
export interface GlCenter { id: number; code: string; name: string; name2?: string | null; centerType: GlCenterType; parentId?: number | null; /* … */ }
export interface AcmeCustomer { id: number; code: string; creditLimit: TmDecimal; contacts?: AcmeCustomerContact[] | null; /* … */ }
declare module '@tellma/core' {
  interface TmEntityTypes { 'gl.Center': GlCenter; 'acme.Customer': AcmeCustomer; /* … */ }
  interface TmEntityShapes {
    'gl.Center': { scalars: 'Id' | 'Code' | 'Name' | 'Name2' | 'Name3' | 'CenterType' | 'IsActive' /* | … */;
      groups: 'Name'; navigations: { Parent: 'gl.Center'; Manager: 'core.User'; CreatedBy: 'core.User' }; children: {} };
  }
}
type TmEntityType<N extends string> = N extends keyof TmEntityTypes ? TmEntityTypes[N] : TmJsonObject; // in @tellma/core
```

- **Names and members.** Interfaces are named `<Schema><Entity>`, such as `GlCenter`. Members use
  wire camelCase; twins and nullable members are optional.
- **Types.** `decimal` and `long` members are `TmDecimal`, including a `long` key. `int` keys and
  other integers are `number`. Dates and instants are `string`, C# enums are literal unions,
  value-set properties are `string`, and children are arrays.
- **Uses.** `TmEntityShapes` types `TmPath<N>`. `TmEntityType<N>` types the declaration's rows and
  `injectTmDetails<AcmeCustomer>()` (spec 0031 §4.1). Packs compile without the file; the build
  check (§3) still checks their paths in every distribution build.

## 16. Client blob-kind policies

Each `[BlobReference]` property carries its kind's policy (spec 0017 §2.3), so the client checks a
file before uploading it and asks only for variants that exist.

```ts
interface TmBlobKindPolicy {
  readonly kind: string; readonly accept: readonly string[];  // 'customer-image'; content types for the picker's accept
  readonly maxSize: number;                                   // bytes: min(the kind's MaxSize, the upload limit)
  readonly image: { readonly maxDimension: number; readonly fit: 'contain' | 'cover-square'; readonly format: 'webp' | 'jpeg' | 'png' } | null;
  readonly thumbnail: boolean; readonly readAccess: 'owner-read' | 'any-member'; // thumbnail: a 'thumb' variant renders
}
declare function tmCheckBlobFile(policy: TmBlobKindPolicy, file: File): 'ok' | 'size' | 'type';
```

- **Before upload**, `tm-value-editor` sets `accept` and the size cap, and crops `cover-square`
  images on the client. The server never re-crops, and it stays the authority on type.
- **Variants.** A thumbnail is requested only when the row's `Variants` lists `thumb` (§6.1); URLs
  come from `TmUrls.blob(kind, id, { variant, download })` (spec 0024 §9.4).

## 17. Testing

Nine suites pin the design, and every one runs on PR.

| Suite | Tier | Pins |
|---|---|---|
| emit (C#) | unit | §2 over Acme's composition; determinism; every label chain, `newLabelKey` and action fields included; fallbacks; twins; blob policies; `json` names equal the wire's; each named enum emitted once, a value set's keyed and literal labels, `core.TimeZone`'s `aliases`, a property naming it; `defaulted`, `allowWhenReadOnly` and `maxFilterLength`; a TS type test over the emitted fixture, the Entities chunk typed as spec 0024's `TmEntitiesChunk` (C# and TS shapes agree) |
| `display-name-golden.json` | unit, TS and C# | §5.2's rows (fallback, prefix with the fixed separator, scalar formats, `ar-SA` native digits on the client only, `ar-SA-u-nu-latn` on both, no bidi controls on either side) and §6.2's expansion rows per shape; findable rows (a pasted "1101 - Cash", a name holding " - ", "42" and "٤٢" for an integer source, `#7`) that the server lowering (spec 0015 §5.2.1), the cacheable search and the paste resolver all match |
| descriptors, permissions | unit | shape gating; twin labels for one to three languages; `contentDir`; `resolve` for every end kind and unknown paths; `allows` per operation, `details` false for an entity with no `details` member (`acme.Currency`); `isSystem`; §7's `reachable` vectors, child roots, `core.Blob`; `label` for an entity, a settings category, another resource and an unlisted pair; `disabledReason` per operation; `allows` and `disabledReason` under `ReadOnly` and `Frozen`, an `allowWhenReadOnly` action included; `TmDisplayNames.label` given a `TmDecimal` id for an `int` key, and a number for a `long` key; the `TmDisplayNames` cache cleared by a `settings` move that changes the languages, kept by any other `settings` move and by a `permissions` move |
| plans, filters, formats | unit | §6.1 per kind; `Id` first; order terms per slot; dropped keys; §13.2 row by row, a multilingual `ne` and a `datetime` `between` included; a `guid` column with no filter; instant days in `UTC`, `Asia/Riyadh`, `Asia/Beirut` (DST); invalid values; descriptions per operator in `en`, in `ar` with an LTR value and in `am` with the verb last, their parts and plain `text`; `TmDecimal` text at 28 digits and `long` values formatted exactly; §12.1's zone labels (`text`, `zoneSuffix`, `'column'` cells across Asia/Beirut's DST change); `tmRowsToObjects` nesting navigation paths by `json` names and skipping expression columns; extra paths in `select` and never in `columns` |
| Queryex lexer, printer | unit | spec 0008 §4's vectors; the engine-generated lexer corpus; `queryex-print-golden.json` with its C# `Discover` check; `tmQxInsert` boundaries; `raw`; `num` refusing a 16-digit `number` and non-decimal text |
| `queryex/discover` (C#) | integration, SQL Server | each step of §9; the same diagnostics as `query`; `Query.PathNotReachable` under a filtered grant; an unconstrained parameter; the `Permission` profile matching role save; `measures` with QX4010 and QX4011 at their `Measures[<i>]` sites, and `400` without `aggregate`; `conjuncts` across two filters, with each literal's wire encoding; no round trip when warm |
| components | harness | editor, path picker, column filter and entity field over `TmFakeDiscover` and the fake API: stale answers, F8, insert, the editor as a cell editor (Enter inserts a line, Mod+Enter commits), date parts per calendar (`'uq'` and `'et'` passed, a Gregorian fallback labelled), search caps, cacheable search and its server fallback, search text stripped of bidi controls, an enum filter of 49 values as `tm-multi-select` and of 50 as `tm-entity-multi-picker` searching its `ar` labels, the unsearchable field, form-field forwarding, pending uploads; `tm-display-name`, `tm-filter-description` and the binding's `itemLabelParts` and `formatParts` isolating their parts in `<bdi>` |
| typed codegen | unit | the emitted `.d.ts` compiles; `columns: ['Cod']` fails (expect-error); depth-4 paths pass by prefix |
| e2e on Acme | Playwright | drag a field into a filter at the caret, then undo (Chromium, Firefox, WebKit); keyboard-only insert; no unreachable path for a restricted role; a URL with an unreachable column loads without `403`; picker advanced search and create round trips; a display name copied from a cell, a title and a picker input pastes into a search and finds its row, and the clipboard holds no bidi control; a language switch with no request; the §18 geometry specs; every flow in `ar` |

## 18. Definition of done

- **Model.** §2–§16 behave as stated and the suites of §17 are green, both goldens in TS and C#.
- **UI rules:**

| Rule | How this design meets it | Pinning test |
|---|---|---|
| Instant warm launch | summaries in the Core chunk; the Entities chunk from the worker cache, awaited beside the page chunk; no metadata request ever | spec 0024's `warm-launch.spec` with every request held (headers, filter buttons, the details title render); `entities-load.spec` (one cache fetch, no API call) |
| Instant feedback | highlighting and chips on every key; the path tree expands from memory; cacheable pickers answer synchronously; a language switch needs no request | `expression-editor.spec` (chips in the key's frame), `path-picker.spec`, `entity-language-switch.spec` |
| Non-blocking | discover and picker searches abort on the next key; `resolve` runs in the background and aborts on a tenant change | `discover-abort.spec` (three keys: two aborted, one applied); spec 0006's picker suite |
| Stable layout | the editor's fixed rows, status line and chip row; the filter panel's fixed width; Apply disabled, never hidden | `expression-editor-geometry.spec` (one box for 0 and 5 problems, 0 and 4 parameters), `column-filter-geometry.spec` |
| RTL | display names and filter arguments in `<bdi>` isolates, with no bidi control in any text; slash labels in the page's direction; slot inputs in their content direction; code LTR; numbers physical right | `path-picker-rtl.spec`, `display-name-bidi.spec` (an English code beside an Arabic name, and the reverse, in both page directions), every e2e in `ar` |
| WCAG AA | the tree keyboard model; a non-drag path for every drop; the editor labelled, `aria-invalid`, problems reached by F8 and buttons; labelled filter controls | `keyboard-insert.spec`; the axe battery (light, dark, LTR, RTL, popovers open) |

- **Page context.** This design adds no context kind of its own. Descriptors label `TmVisibleData`
  and `TmFormContext` (spec 0026 §9.1); hosts list editor diagnostics in `errors`; picker modals
  are specs 0030's and 0031's.
- **Telemetry** (closed properties, spec 0026 §3.2):
  - timings: `tm.entity.metadata-loaded`, `tm.entity.names-resolved { count: 1 | 2-10 | 11+ }`,
    `tm.entity.picker-search { cached: true | false, outcome: ok | error }`,
    `tm.queryex.discover { clauses: 1-4, problems: none | some }`;
  - events: `tm.entity.column-dropped { reason: unknown | unreachable | invalid-value, source }`
    (§6.4),
    `tm.entity.picker-page { page: search | create | edit, outcome: picked | dismissed }`,
    `tm.queryex.insert { via: drop | keyboard }`.
- **Reference distribution.** Acme builds on spec 0018 §1.4's server composition and adds the
  `customers` declaration (§3). It brings a cacheable navigation, an enum, a decimal, an avatar, a
  child collection and `set-credit-limit`'s generated dialog. Tenant 1 (`en` + `ar`) exercises
  twins and slot fallback. The e2e's restricted role holds a filtered `Read` on `gl.Center`.

## Decisions record

1. **One tenant-independent model, emitted at build and read from the cache** — names render at
   once and no page requests metadata; a per-tenant route would wait on every warm launch (§2, §4).
2. **Label keys are resolved at emit** — the flat string pack cannot express assembly chains (§2.4).
3. **The display name is server metadata that both sides render alike** — one attribute
   (`[DisplayNameSource]`), one fixed separator, the active slot with fallback, one golden.
   Rejected: a client function (Excel, MCP and messages need it), the related projection
   (broader), a localized template, which would make splitting a pasted name depend on the
   language (§5).
4. **Paths are PascalCase C# names; rows are the wire's camelCase objects** — no recursive rename
   per response; every property descriptor carries both names (§2.2, §3).
5. **A group-anchor column shows the active slot** — else `Name` shows English to Arabic users (§6.1).
6. **Unknown and unreachable keys are dropped, never sent** — a stale URL never `403`s (§6.4).
7. **Multilingual filters read every admitted slot: a positive operator matches when any slot
   does, `ne` when none does** — one URL, the same rows in every language (§13.2).
8. **A lexer, builder and printer, with no client parser** — instant highlighting and safe
   insertion without a second grammar to keep in step with spec 0008 (§8).
9. **`queryex/discover` compiles exactly as the pipeline does** — so it reports every error `query`
   would, refusals included. Rejected: `Discover` alone (misses binding errors) and per-clause
   `Validate` (misses cross-clause checks). The `Permission` profile spares a second endpoint (§9).
10. **A native `textarea` over a highlighted mirror, with native text drop** — IME, undo and screen
    readers work for free. Rejected: `contenteditable` (caret and undo bugs), a code editor (budget
    and RTL cost), and a custom drop, since pointer-based caret placement is unspecified for text
    controls (§10).
11. **One generic picker binding** — the same binding serves fields, cells and filters, with tweaks
    in `picker`. Rejected: a component per entity, or a picker configured per call site; both drift (§14).
12. **Typing comes from the entity-name literal** — an explicit `tmEntity<AcmeCustomer>(…)` type
    argument disables inference of the name, and TypeScript has no partial inference (§3, §15).
13. **A permission is named by the securables' own labels** — a `forbidden` message and the Access
    tab then use the same words, and a user can quote what to grant; raw pairs mean nothing to
    users (§7).
14. **Display names, slash labels and filter descriptions are plain text; direction is markup** —
    copied text equals what the user sees, so it pastes into a search, a spreadsheet or a script
    and behaves the same; invisible characters make two equal-looking texts differ (§5.2, §11.4,
    §13.2).
15. **A pasted display name splits at its first separator and needs both halves** — "1101 - Cash"
    finds account 1101 alone, so a form picker's Tab resolution gets one match; either half alone
    would also match every name starting "Cash" (§5.2).
16. **Filter descriptions are whole-sentence messages with formatted, isolated arguments** — word
    order and agreement are the translator's, which a label, operator and value concatenation
    cannot give Amharic or Arabic (§13.2).
17. **C# enums and value sets are one kind, a named enum emitted once and named by its
    properties** — languages, calendars, zones and regions then render as labels in grids,
    filters, reports and exports alike, and a 400-value zone list is not repeated per property;
    field components alone fixed only the details page (§2.2).
18. **The tenant's state gates writes inside `TmPermissions`** — every write action then explains
    a read-only or frozen tenant the way it explains a missing grant, with no per-page check (§7).
19. **Date parts pass only the calendars Queryex binds, else they are Gregorian and labelled so** —
    a grouping is never labelled in a calendar it was not computed in (§11.2).
20. **An enum of 50 values or more filters through `tm-entity-multi-picker`** — a list of over 400
    zones needs a search field, and the multi-picker already searches a synchronous source with
    chips and a cap; a new searchable select was the alternative (§13.2).

## Review flags

1. **No column key for slot 1 verbatim** (§6.1). Alternative: a slot suffix in the column-key
   grammar (spec 0024 §9.5 changes). Flips if bilingual side-by-side review is a real workflow.
2. **Multilingual filters read every slot** (§13.2). Alternative: the active slot only. Flips if
   users report matches on text they cannot see.
3. **Wire camelCase rows, PascalCase paths** (§3). Alternative: PascalCase responses. Flips if
   layout authors confuse the two casings.
4. **No client parser; discover 250 ms after the last change** (§8, §10.2). Alternative: a TS
   parser or a shorter debounce. Flips on a need for offline or faster syntax feedback.
5. **Generated forms keep instants read-only; a layout places `tm-date-time-field`** (§12.2).
   Alternative: `tm-value-editor` renders it for every editable instant. Flips when a second
   entity edits an instant.
6. **`resolve` needs the target's `Read`** (§5.3), else `#id` shows. Alternative: a member-only
   "names by ids" endpoint, bypassing row-level security. Flips if `#id` shows often.
7. **Cacheable picker search runs on the client** (§14.2), and may differ slightly from SQL
   collation. Alternative: always query. Flips on mismatch reports.
8. **English fallback labels in every language** (§2.4). Alternative: fail the build on a missing
   label. Flips once packs are fully translated.
9. **String-only contexts order a display name by its first strong character** (§5.2 step 4). A
   picker input or a plain tooltip shows "INV-0011 - نقد" name-first in an Arabic page, because
   its prefix has Latin letters; numeric prefixes read correctly. Alternative: draw a closed
   picker's value as `tm-display-name` over the input while it is not focused. Flips if lettered
   prefixes beside other-script names are common.
