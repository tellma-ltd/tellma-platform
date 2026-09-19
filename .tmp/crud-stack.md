# CRUD stack
This describes the CRUD stack, the set of standard API endpoints that are implemented by every entity on Tellma, their implementation details and the subsystems they rely on.


## Design goals to live by
1. **Plumbing lives in the platform**: Distributions will be authored and maintained by coding agents at scale with minimum human engineering oversight, therefore distribution code should be very simple and authoring it highly mechanical. All the common complexity is pushed to the platform behind a simple public API at zero or minimal config. The ideal: a distribution repo only contains custom entities, business logic, and UI specific to its domain. Every line of code in the distro repo must earn its keep, the platform API should be designed to make it easy for the distro author to do the right thing, while at the same always keeping an escape hatch for "unusual" customer requirements that do not fit the platform shape. Common Requirement -> Straightforward to implement.
2. **Tier 2 Performance**: Tellma's selling point is Tier 2 performance without Tier 2 cost, every API is bulk shaped, the system remains highly responsive at enterprise workloads. Minimize round trips from the UI to the backend, and from the backend to the DB. Observability in place from day 1  to catch performance bottlenecks or excessive resource utilization.
3. **AI-native** The distribution's full feature set is accessible to AI agents, whether a built-in AI assistant or external AI tools like Codex or Claude Cowork. Every Tellma action that a user performs on a regular basis should be exposed in a first class MCP server that ships from day 1. It should be possible for a user to do their day to day work on Tellma from Claude Cowork and similar tools without ever touching the Tellma UI. The only exceptions are highly technical actions available from advanced admin pages, or very dangerous actions (e.g. delete all)


## What we want to design and build
1. A reference distribution whose source lives in the platform repo (for now only the backend, we add the UI next).
2. A few top-level entities to start with: User, Role, and Center (2 in Core, and 1 tree shaped in GL module)
3. The data layer that allows reading and writing to these entities in the backing SQL server DB
4. The CRUD API endpoints that they share at the service layer and the web layer
5. The MCP server seam (MCP is of scope for now as long as everything today should be designed to support it)
6. The supporting cast for all these layers (auth, Excel codec, background tasks, blob storage, inbox, caching, multi-tenancy, etc...)


## Backend Layers
1. Data Layer: Entity model, and access to the SQL database
2. Service Layer: access control, business logic, integration with
3. Web: Auth, and where the functionality is exposed as REST and MCP endpoints.

In the distro, the layers are isolated as folders in the same project rather than as separate projects to keep things simple.

**Open Questions**
1. Are those the proper layer names in a .NET business app? Or are there more recognizable and common names for these layers?

## Data Layer
The data layer is the bridge to the SQL database and where the model is defined.
The model is defined as C# classes, the three entities (User, Role, Center) are registered with EF Core as tables and as UDTTs (refer to the UDTT extension spec 0001).

### User entity
Temporal top-level entity that stores human users. Service accounts get a separate table later.

```
core.User {
	Id, -- integer
	Subject, -- sub from identity server
	State, -- ?? We need to track the user's invitation state
	Name, -- Name in primary language, the field label is rendered as "Name (<symbol>)" where <symbol> is a hardcoded symbol for every language e.g. "Name (E)" for English and "Name (ع)" for Arabic. For mono-lingual tenants, drop the symbol.
	Name2, -- Name in secondary language, removed from queryex schema if secondary language is not configured
	Name3, -- Name in ternary language, removed from queryex schema if ternary language is not configured
	Email,
	ImageId, -- How do we best store, retrieve, and modify the user profile picture?
	ImageFitJson, -- Should this be here or in a centralized blob metadata table?

	-- Notification settings
	ContactEmail,
	ContactMobile,
	PushSettings, -- ?? what shape should this take

	-- Those columns will cause temporal churn, they need to be moved to a sibling non-temporal table, or we need to make the User table non-temporal
	LastActive, -- Updated on every user-initiated API interaction (not background polling)
	UserSettingsVersion, -- fingerprint/etag/version-stamp for caching user preferences - updated whenever any cached user setting is changed, validated as soon as the user makes an API call
	PermissionsVersion, -- fingerprint/etag/version-stamp of caching user permissions - 
	InboxTracking, -- Values to track when the user last checked their inbox to determine the red counter to show them on the inbox bell icon
	
	IsActive, -- Disabling a user revokes their access to this tenant
	SavedAt,
	SavedById,
	ValidFrom,
	ValidTo
}

-- generic bag, stores things like: preferred calendar, preferred language, 
-- quick access pinned screens, and potentially other personal customizations 
-- like resized grid columns widths, and dismissed UI introduction tour
core.UserSettings { 
	Id,
	UserId,
	Key, -- String
	Value -- String
}
```

**The record + blobs pattern**
The User image is the first use case of a record that comes with associated blobs, more use cases will come later (product images, document attachments). We need to standardize the pattern for how this is implemented.
The key observation is that, the record and its blobs:
	1. Travel across the wire in two different formats (json vs binary respectively)
	2. Are stored in 2 different places (SQL table vs blob storage respectively)
	3. Are retrieved in different ways: records are retrieved with queryex, images are cached and validated with etags.

The image is uploaded by the admin or by the user themselves. The admin can upload the image while creating the user in the UI at which point the user only exists as an object in the browser's memory, no record saved in the DB yet and no stable ID yet. Which pattern is more suitable?
Option 1. When the record is still in memory, allow uploading the image to the blob storage in a "Staging" state, and confirm the blob when the user record is created, or garbage collect it if it remains in staging for longer than N days (what is an appropriate period of time)
Option 2. When the record is still in memory, keep the image in memory too, and save it to the DB together with the record. Downsides: Bloats the payload for saving the user with an image binary, heavier save operation, some API endpoints have to depart from the template in order to accept multi-part payloads. Upsides: Less moving parts, single save API, no staging blobs to garbage collect.

**User state*
- The user State from creation to active on the tenant is complex and involves many possible terminal states given the interaction between the tenant and the identity server:
	- New -> not invited yet
	- Invitation submitted -> The tenant called the id server invitation API successfully, triggered by admin action
	- Active -> The user logged into the tenant

Under "Invitation submitted", the user runs through multiple states that are known to the identity server, so the tenant has to query the identity server for them:
	- Email queued -> the identity server accepted the invitation request and queued the email operation
	- Email sent -> The email was accepted by the email service (SendGrid, ACS, etc)
	- Email failed -> The email was rejected by the email service

The under "Email sent", the email runs through multiple states that are known to the email service, reported (unreliably) to the identity server via email delivery event webhooks: Dropped, Bounced, Delivered, etc...

The admin should be able to see the tenant view of the state immediately, with a more adavnaced query available returning the the fine grain state from the identity server as well, including any error messages to help troubleshoot.

**Open Questions**
- What is the better approach for the record+blobs pattern?
- Is JSON the right shape for storing User preferences? Should quick access pinned screens specifically be moved out to a distinct table to allow an admin to customize them for a less tech-savvy user?
- What do we need to store for the notification settings? We need the user's email, phone, and push channels, and what notifications would they like to receive on either of those. Should we store all of this in one JSON field?
- Is it better to separate SettingsVersion and PermissionsVersion, or keep a single version field for both? One has security implications and one doesn't
- Should we call them Version, ETag, or fingerprint?
- What is the shape of inbox tracking?
- Are the user states listed above exhaustive of all possibilities?
- For write-once columns like Subject and Email, do we need handling for that at the Data layer by having 2 UDTT for create and update? Or should we stick to our direction that the Data layer as dumb as possible and capture such a business rule at the Service layer by ensuring those values never change (either via validation or by resetting them to what's in the DB)?


### RoleMembership entity
Weak temporal entity, edited and saved together with the User.

```
core.RoleMembership {
	Id, -- integer
	UserId,
	RoleId,
	Notes, -- free text
}
```

### Role entity
Temporal top-level entity

```
core.Role {
	Id, -- integer
	Name, -- Unique (e.g. "Finance Manager")
	Name2, -- Unique when not null
	Name3, -- Unique when not null
	Code, -- Unique when not null
	IsActive,
	SavedById,
	ValidFrom,
	ValidTo
}
```

### Permission entity
A weak temporal entity, edited and saved together with the Role

```
Permission {
	Id, -- integer
	RoleId,
	Resource, -- distro specific e.g. invoices. Or "all" for everything
	Action, -- read/save/activate/deactivate/etc... Or "all" for everything
	Filter, -- RLS filter, in Queryex, only on resources and actions that support it
	FilterLanguageVersion, -- The queryex language version when the filter was authorerd and validated
	Notes, -- free text	
}
```

### Centers entity
Non temporal tree entity, lives in the GL module.

```
gl.Center {
	Id, -- integer
	ParentId, -- FK to table
	CenterType, -- Service, Operation, or Sale
	Name,
	Name2,
	Name3,
	Code,
	IsActive,
	CreatedAt,
	CreatedById,
	ModifiedAt,
	ModifiedById,

	-- Standard tree properties
	Node, -- HierarchyId
	IsLeaf,
	SubtreeCount, -- Count of all descendants + self -> used for showing expanders on the UI tree view
	ActiveSubtreeCount -- Count of all descendants who are active + self if active -> Used for showing expanders on the active-only tree view
}
```

We need a robust and performant mechanism for maintaining the tree properties, while keeping in mind that saving some Centers will affect the tree properties of other Centers that are not in the payload, this can be done either in memory or using a SQL statement that is appended to the save statements. We also need validation that guards against cycles.

**Open Questions**
- We need the concept of a global set of permissions that apply to all users, should we make it a system role with a hardcoded Id, a field on Roles IsPublic, or a separate set of role-less permissions that live in a dedicated settings table. This is needed to cover all the many entities that are public lookups like countries, units, etc, so that the admin doesn't have to manage them in every role or for every user.
- Do we need SavedById on the week entities, or is it redundant?
- What is the best way to keep the hierarchyId column up to date with the ParentId column, while supporting bulk save. Would the C# update the values in memory before saving, or is it better if the C# saved the records and then ran a SQL statement that fixes the hierarchyId values on all affected rows in the same transaction.
- How to best model the CenterType column? An Enum? It needs to be stored in the DB as a string so that the queryex filter could say CenterType = 'Service'
- What is the best standard or convention to encode the Resource that a permission secures?

### Extensibility

Note: We need distros to be able to extend or fully replace these entities while reusing the service logic, which means as much of the service logic should rely on interfaces rather than the concrete entity. And it should validate that the interface matches the DB (no needed columns are dropped)

### The SQL Save emitter and Multi-Statement Builder
Saving entities in the DB is done in bulk, to minimize round trips: one database call persists multiple collections of different types of entities, and may read data back as well in the same call.

A few pieces of functionality are needed:
1. A SQL save-emitter, takes a SaveSpec (which is A. a collection of entities with their IDs populated B. whether this is upsert or synchronize and C. what are the key and parent key columns) and it emits the bulk Save (upsert for top-level entities, and synchronize the weak entities under each top-level entity) SQL statement from the table and UDTT schema ef core model based on these C# entities. The goal of this emitter is to reduce the need to hardcoded SQL strings.
2. A multi-statement builder/executor, which knows how to string together a series of Queryex queries, save statements and raw SQL statements and load their results with a friendly API.

Every statement added in the builder comes with a flag "MayRetry", true for reads and idempotent writes. This allows the Data layer to implement safe retry on transient failures.

**Open Questions**
- What is a better name for the SQL Save emitter, and multi-statement builder/executor? I need something short, technically correct, and conveys quickly what their function is.
- Does the emitter need to also know whether it's upsert or synchronize? Or are those discerned by the
- What should the multi-statement builder/executor API look like?


### Id ranges
Id ranges are reserved by each app instance from a DB sequence dedicated for every entity (e.g. InvoiceSequence). Every time a new record is saved, the app has to reserve the IDs from the DB and assign them to the records in memory.

**Open Questions**
- What strategy can we use to minimize Id gaps (caused by server restarts) without a dedicated DB roundtrip in the save operation just to reserve Ids? Can we hitch a ride on OnConnect or the validation context query to optimistically reserve the Id range needed for the save operation (after consulting what is already reserved)? Having instances reserve a large ranges upfront will cause big gaps in Ids, and for larger tables might risk running out of IDs, and using BIGINT for Id has a perf and storage cost that is ideally avoided if possible.
- Who should maintain the reserved IDs for every entity, a dedicated thread-safe singleton service? 
- If a save operation that consumed some Ids fails to save, should we bother un-consuming those Ids (in the C# service not the DB sequence) to minimize gaps?
- Who is responsible for consuming the IDs and assigning them to the entities? The Service layer or the data layer?


## Service Layer
The service layer backs the Web API and the MCP, implements the business logic (access control, validation, and 3rd party integration), and uses the data layer for persistence.

3 services, one for each top level entity:
UserService - Core service
RoleService - Core service
CenterService - GL service

Important: The plumbing should be reused, by the 3 services, and most future services that implement the CRUD stack.

### Dedicated API per client

The API will be used by the UI, external scripts, and will also be exposed as an MCP server.

- The UI we own, so we don't have to worry about backward compatibility (beyond N+1 to support 0-downtime deployments), the API schema should be optimized for minimum network roundtrips (The 1-1 rule: 1 API network call to answer 1 user action) and can be tailored heavily to the layout and mechanics of how the UI is designed (e.g. lazy load X because it's hidden by default unless the user clicks some tab)
- The MCP server is needed on day 1 for users who rely on external agentic tools like Claude Cowork, the server schema is discoverable at runtime by design and therefore can be changed without breaking existing agents (is this true?), however the surface should be designed for agentic tools: smaller number of tools to avoid context bloat and AI confusion, intent-oriented descriptions and categorizations, etc
- External integrations (e.g. a migration scripts running on the customer server and calling Tellma) do require public documentation, versioning, and backward compatibility guarantees. Also - unlike the first two - they are not needed for all entities on day 1, they are added on demand based on specific customer requirements.

Given the differences, the 3 clients get separate API surfaces rather than a single surface that all of them reuse:
- {tenantId}/api/web/... for the private web app API
- {tenantId}/api/v1/... for the versioned public API
- {tenantId}/mcp/... for the mcp server

### Endpoint summary

The following methods are for the private API for the SPA web app. The MCP server and the public API get separate 

- GetByQuery -> Powers the search page
- GetByIdForDetails -> Powers the details page
- ExportByQuery ->  Powers the "Export" button on the search page
- ExportByQueryForImport -> Powers the "Export for Import" button on the search page
- ExportByIds -> Powers the "Export" button on the search page after multi-selecting one or more rows
- ExportByIdsForImport -> Powers the "Export for Import" button no the search page after multi-selecting one or more rows
- Save -> Powers the details page's edit-and-save
- Import -> Powers the "Import" button on the search page
- DeleteByIds -> Powers the "Delete" button on the search screen after mult-selecting some rows
- DeleteByQuery -> Supports a hypothetical "Delete By Filter" button which we keep hidden under an advanced menu

Tree entities add a couple more:
- GetByParentIds -> Search page tree view (initial load with auto expanded nodes, and expand another node)
- DeleteWithDescendants -> Powers the "Delete with Children" button on the search page tree view


The "ByQuery" endpoints are for all public top-level entities
The "ByIds" endpoints are for all top-level entities with a key column
The "Save" and "Delete" endpoints are for editable entities

All entities in this spec have key columns and are editable. One of them is a tree entity.

### GetByQuery
This method is implemented by all public top level entities, it powers search pages and reports.

**Input**
```
{  
	Select, // Queryex, may contain complex expressions like "Amount * Rate"
	Search, // General search, the service translates this into a queryex filter as it sees fit (should we keep this?)
	Filter,
	Having,
	OrderBy,
	Skip,
	Take,
	IncludeCount,
	Arguments -- The values of any parameters embedded in the queryex expressions
	IncludeAncestors -- for tree entities that need to display search results in a tree?
} 
```

**Output**
The query result as an array of arrays filtered by the user's RLS, also optionally returns the total count of the query (paging-ignored) in order to show X-Y/Z style paging stats in the UI, that count stops counting beyond 9999 for performance on millions of records.

Another array of arrays for the ancestors? The UI needs to be able to distinguish the query results vs merely an ancestors that tagged along, since they are displayed differently.

**Flow**
1. OnConnect: Call the DB (DB call #1) to do multiple things:
	A. Verify that a user with this sub exists in this tenant
	B. Record their activity in LastActive
	C. Retrieve all the versions/etags of the tenant and of the user (including the user permissions version), reading these early on guarantees that all subsequent logic relies on a fresh server cache
2. If the user lacks any read permissions on the entity, throw a ForbiddenException (?)
3. Build the queryex query, including the user RLS filters from the permissions (guaranteed fresh by step 2)
4. If count is requested, build another count queryex query
5. Execute the queries against the DB together (DB call #2) using a multi-statement builder/executer
6. Return the result

**Search**
- Should we keep the search parameter? It is intended to power the search field on the search page and the entity picker search. Or should we shift that burden to the client to construct the filter parameter based on user input?
- If we kept the search parameter, do we need to also supply hints to the server on whether this is coming from a search page or from an entity picker? Is it reasonable to interpret search differently between the two since the search page shows many more columns than the entity picker? Can the server simply rely on the contents of Select to determine which is which. But the queryex parser is internal, so a server doesn't have the ability to inspect the contents of the select text. All of this is moot if we shift the burden to the client.

### GetByIdForDetails, 
This method is implemented by all public top level entities that have a key, it returns everything needed to render the details page in one network call.

**Input**
```
{
	Id, 
	Select -> Optional queryex Select,
	+ potentially other flags indicating which extras to load, based on e.g. which tab the user has open
}
```

**Output**
	1. The main entity, with all its editable properties populated including any child collection, that allows the caller to edit any property and send the entity back to the Save endpoint without losing data.
	2. A dictionary of all the related entities pointed to by a FK on the main entity or any of its children, this keeps the response slim when 10k children point to the same Center, the dictionary and what's in it is different per entity, and is dictated by what is shown in the UI.
	3. Optional extras like workflow history, edit history, comments, signatures, aggregate queries for embedded widgets, related documents, posted entries. Those are specific to each entity/service, and little can be assumed about them at the platform level. Everything a details page needs to render initially should be fetched in one call. The rule is one server action = one network call.
	4. Search page row echo: The Query shaped response given the Id and the Select parameter, this is used to update the cached row of the search page when loading the details page, so that they don't desync.

**Flow**
1. OnConnect: (DB call #1)
2. If the user lacks any read permissions on the entity, throw a ForbiddenException (?)
3. Build the queries (queryex or raw SQL? SQL might be better here considering all the joins and extras that need to be loaded together efficiently, but that could be determined per entity)
4. Execute the queries against the DB together (DB call #2) using a multi-statement builder/executer
5. Return the result, if no entity comes back (whether missing or due to RLS) return 404 not found.


### Save
Implemented by all editable top level entities. Powers the details page's edit-and-save.


**Input**
The entity for save DTO, including all its editable properties and weak collections (the operation is save not patch).
For both the main entity and its children: Id=0|null for create, and Id=value for update.
For a child entity, not including it in the payload means delete it from the DB.

**Output**
Returns the same entity back in the same shape and extras as GetByIdForDetails.

**Flow**
1. OnConnect: (DB call #1)
2. If the user lacks any write permissions on the entity type, throw a ForbiddenException (?)
3. If the entities are being updated, confirm that the user's RLS allows them to update those entities by running a DB query that includes the user's RLS filter (DB call #2)
3. Preprocess the entities -> Trim whitespaces from string values, other custom logic per service
4. Start the transaction (is this right?)
5. Validate the entities -> 
	A. Load all validation context from the DB in bulk using the multi-statement executer: (DB call #3), including the entity being updated if we need to compare before and after
	B. Standard validation like: All IDs are unique etc
	C. Custom validations: E.g. Name is unique, Debit = Credit, etc...
	D. In an ideal world, only one DB call loads the validation context, but sometimes the need for a second more expensive query is only determined based on the result of the first query, so we need flexibility to issue a second batch of validation context queries, and a third etc, as long as the total number of DB calls is O(1) with the input cardinality. Observability in place to count how many DB calls every entity in every distro is making, and how long do they take
6. If the entities are invalid, return an error that surfaces as 422 if specific to fields or 400 for general client errors.
7. Save the entities and their children in bulk, top level entities UPSERT, their children synchronized under their parents, using the SQL Save emitter and multi-statement executor (described in the data layer section), load the response back in the same DB call if requested (DB call #4), also include a query with the RLS filter to determine if the saved entities pass the RLS filter after they had been updated. Also load back any context needed for the side effects such as saved or deleted blobs, emails to send, etc. (see next steps).
8. If RLS fails, roll-back the transaction and return ForbiddenException
9. Run any transactional side effects that are not DB modifications, DB modifications ride the same train as saving the entities.
10. Run any pre-commit non-transactional side effects, those are non-transactional operations that are safer to do before committing the transaction e.g. creating blobs.
11. Commit the transaction
12. Run any post-commit non-transactional side effects (e.g. deleting blobs, calling 3rd party integrations, sending emails, etc), those are specific per service. Queuing background operations IS transactional, and therefore not handled here.
13. Return the result

**Notes**
- An empty child collection is deleted, a missing child collection is not touched (either skipped from the update multi-statement, or hydrated from the DB whichever is easiest/fastest)
- The save operation updates the audit columns
- Even though the Save endpoint admits a single entity, the part of the pipeline that is reused with Import is entirely bulk-shaped.

**Open Questions**
- What is a good platform API design that allows injecting custom validators that are still able to participates in the batch DB call to load the context they need?
- How can we dedupe validation context loading queries (if two validators want to run the same raw SQL query, the same queryex query, or the same queryex query just with a different select clause)
- Where should the transaction boundary begin and end? Start it too soon and it's a perf issue, start it too late and it's a correctness issue
- If the user's permissions are cached, can we optimistically collapse DB calls #1 and #2 into one call? And if the permissions turn out to be stale, we run DB call #2 again as usual?

### ExportByQuery 
Powers the "Export" button on the search page, returns an Excel file containing the same data on the search page: same columns, same rows but without the paging (up to a limit). Basically the Excel version of GetByQuery.

### ExportByQueryForImport
Powers the "Export for Import" button on the search page, returns an Excel file containing all the editable fields in the entities and their children, the file can be re-imported as is to the same tenant, another tenant, or even another distro if it happens to share the same entity schema. 
This requires every FK to be represented by a natural key on the target entity, the system should be able to make a good guess on the default (E.g. Name if unique, otherwise Code, otherwise the first unique and required text column, otherwise the first unique text column, otherwise the first text column, otherwise the first column). It should be possible to declare in code which property is the default natural key (e.g. the Email property in Users).

The values are exported in the Excel sheet raw (numbers, booleans). Localization (numeric/date formatting, calendar) is metadata added on the Excel columns which is ignored during import since the importing user may be on a different locale.

**Open Questions**
1. What is the best way to declare the natural key of each entity? A Core attribute?
2. Should we mandate that every entity has a natural key that is required and unique? It is the only way to guarantee that any exported file can be imported. If no natural key is found that is required and unique, do we make a best effort? Fail he export-for-import? Or fall back to surrogate keys? The later works reliably within tenants, but never across tenants.
3. Does Excel understand the same numeric/date formatting primitives as Tellma? Does it understand calendars?
4. If we wanted to implement export and import of multiple entities at the same time, what machinery can we reuse from the single-entity pipeline? And would Excel still be the correct medium for this? Or should we instead use something else like SQLite, binary, or zipped json files?

### ExportByIds
Powers the "Export" button on the search page after multi-selecting one or more rows, accepts a list of Ids and a Queryex select (encoding the displayed columns), and exports those specific rows as per the select argument.

### ExportByIdsForImport
Powers the "Export for Import" button on the search page after multi-selecting one or more rows, exports those specific entities and their children whole into an Excel sheet, including every editable field, so that the sheet can be edited and re-imported again.

### Import
Accepts an Excel file containing the data of a bunch of entities (same format as the one returned by ExportByXxxForImport), and saves them. This is why the entire save pipeline (preprocess, validation, persist, transactional side effects) must be 100% bulkified, import must remain very fast.

This method is implemented by all public top level entities that implement Save.

FKs are represented by natural keys in the import file, so those need to be translated - in bulk - into their surrogate keys during import, if the translation fails because the natural key was not found, or because of ambiguity that's a validation error.

Import support tree entities, using the surrogate key to reference's a row's parents. It should work even if the parent is created a new in the same import (doesn't have a DB Id yet).

Columns are mapped by default using the Excel sheet headers, multi-lingual columns are mapped intelligently (Arabic Name -> Arabic Name not Name2 -> Name2), so that importing the sheet into a tenant whose languages are swapped still works. The default mapping can be overridden by the user. Every service supplies the default mapping for export and import.

Multiple import modes (specified by the user)
1. Insert -> Saves all the Excel rows as new entities.
2. Update -> Updates existing entities, if it doesn't find the entity returns a validation error
3. Merge -> Creates or updates entities

Update and Merge require designating one of the columns on the main entities as the natural key to use for identifying rows.
In both update and merge, partial sheets (e.g. containing only the natural key and one more column) should not wipe out the remaining properties of the entities, the import pipeline hydrates the remaining properties from the DB, and updates the ones present in the sheet.

Import is subject to the same access control checks as Save. And unlike save it does not read and return the entities just saved back from the DB.

### DeleteByIds
Accepts a list of Ids to delete. Powers the "Delete" button on the search screen after multi-selecting some rows, and also powers the "Delete" button on the details page which passes a single

### DeleteByQuery
Accepts a queryex filter and deletes all the entities that satisfy that filter, this supports a hypothetical "Delete By Filter" button which we keep hidden under an advanced menu as a dangerous option, but must exist as the only way to compensate for accidentally importing 10K records without calling an operator with access to a DB backdoor.

### GetByParentIds
Only for tree entities. 
Used to implement the search page tree view to render an auto-expanded tree, so that every tree refresh doesn't collapse the tree to its roots.

### DeleteByIdsWithDescedants
Only for tree entities.
Used to power the "Delete with Children" button on the search page tree view.

**Open Questions**
1. How do we best extract the common API of these services to make it reusable? A base service class? What about inheritance vs composition.
2. If the user requests 5 ids, and 4 of them are found, do we return 4 entities or 404?

### Queryex engine

**Custom filter**
Many operations require a filter by a list of Ids or a filter by a list of codes. This requires adding a feature on the queryex engine QuerySpec to say: restrict results where a specific column is IN a list of values which are passed in as a TVP to support large lists (e.g. `Code IN (SELECT Id from @foo)`), this doesn't have to be a language feature, just an engine feature. It would require passing in the name/schema of the UDTT for the TVP in the engine config (since the engine is database agnostic). This is currently not implemented, but would is needed for GetByIds, validation context loading, GetByParentIds, DeleteByIds, Import's natural-to-surrogate translation, and other uses which would now have to be done in raw SQL.

**Level(...) function**
We need to add `level` as a queryex function that returns the hierarchyid's level.


### Capability endpoints
Some patterns are shared across many - but not all - entities, such as The tree shape, a record with an image, and the IsActive property, and audit properties (CreatedXX, ModifiedXX)

Take IsActive as an example, adding that capability implies:
1. Adding a Boolean property IsActive on the entity
2. Adding a permission actions "toggle_activate" that accepts a filter
3. Adding 2 service methods: Activate and Deactivate, accepting a list of Ids and enforcing the permission from #2
4. Adding 2 REST endpoints: <entity>/active and <entity>/deactive
5. Adding the UI action buttons to activate or deactivate records from search page when the user multi-selects
6. Adding the UI action buttons to activate or deactivate the record from the details page
7. Showing the Inactive banner on top of the Details page
8. Adding a default filter on the search screen to hide inactive records
9. Adding the UI control to remove the default filter and reveal inactive records

That boilerplate repeats for every single entity that wants to add IsActive. We have to design a way to move as much of that as possible to the platform such that a distro wanting to support IsActive on an entity is a simple recipe involving a few lines of code.

**Open Questions**
 - What is a good design that allows capability reusing boilerplate across distros while keeping the surface flexible?

### Custom endpoints
UserService also have custom endpoints:
- Invite: To send user invitations to the identity server in bulk
- Self service endpoints for updating my profile pic, and other preferences, no permissions needed.
- Testing the notification email and mobile
- Save and Delete preference key and value pairs.

## Web Layer
The thin web layer uses ASP.NET Core Minimal API to wrap the service methods and expose them as rest endpoints. All the UI endpoints are exposed as POST to allow carrying the potentially long and complex Queryex parameters in the request body.

**Open Questions**
- How do we extract the common CRUD endpoint boilerplate into the platform Core, so that the distro codebase stays lean?
- Can we use source-generated JSON serialization for maximum performance?
- Should we accept a "X-Today" header from the client that specifies what today is on the client machine to feed the "today()" queryex function? This ensures the server's interpretation of today matches the user's. Or is there a better idea than the header? We may also need the user's timezone, which would render X-Today redundant if we supplied that instead.

## Access control
All services need to register in a machine readable securables registry, which is a collection of (Resource x Action x SupportsFilter and the filter's queryex filter root Entity), this is used in the UI when editing a role, and used on the backend when validating a saved role.
And we need to define how to handle existing permissions that rendered invalid when the securables registry changes due to a new distro deployment or a config change.

- Public permissions are union'ed with the user permissions.
- If a user has WRITE permission on X, they also have implicit READ permission on the same X.
- Filters are merged as a disjunction (OR), if a user has permission on X where A, and also has permission on X where B, then they have permission on X where (A OR B).
- Services can grant bespoke permissions criteria not coming from the permissions table, e.g. A document may be visible to any user it is assigned to.
- Permissions under inactive roles are not included
- Accessing a record that I have no read permission on should return the same response as a non-existent record.
- If weak entities are exposed as queryable entities for reports (later), then their permissions are those of the top-level entity that owns them with the filters adjusted to add the parent step to every path (e.g. `PostingDate > X` -> `Parent.PostingDate > X`)
- A user cannot delete or deactivate their own user, and should not be able to strip away their own admin permissions, this is to guarantee that at least one admin user will continue to be active on the tenant and prevent a lockout.
- If you can read an entity than you can read all the related entities and extras that comes with it from GetByIdForDetails, including related entities that are inaccessible, no partially visible document.

We also need an endpoint on UserService that answers the question: Does user X have permission to do Y on resource Z and subject to what filter (the what), and because of which roles or public perms (the why).

**Open Questions**
- How do we design the API surface to make registering securables and enforcing access controlin a way that it is difficult to forget for the developer to secure an endpoint?
- Is "Securable" the right word for the tuple?
- How do we handle permissions that stop being valid due to a schema change (e.g. renamed column)? Do we add a permission shim or simply block users until the permission is migrated?

## Optimistic concurrency writing
Relying on the RowVersion comes with challenges: Not all properties on a row participate in conflict detection, specifically any columns not updated by the user such as workflow tracking columns, and system updated columns like ModifiedAt and ModifiedById.

We need another simple mechanism that supports subsets of columns, and allows us to quickly verify if another user had updated any of the values.
The Save payload accepts an override flag, initially set to false. When a concurrency collision is detected and the flag is off an error is returned. The UI prompts the user that user Y has modified the record, and gives them the option to override the other user's which re-attempts the save operation but with the override flag set to true.

**Open Questions**
- What is a good alternative to RowVersion? Can one be implemented C# to keep the database layer thin and dumb?

## Rate limiting
API endpoints should impose a generous but bounded limit on the input size and rate.
- Total payload size
- Number of entities in payload
- Length of every free text parameter in the payload
- etc...

Rate limiting should be purely in-memory for perf, it should not require another network round-trip to any central state in SQL or Redis. Which means it is enforceable only per app server instance.

## Caching
Both the app server and the UI cache values from the SQL database in memory using version tags to ensure their freshness:
1. Permissions of every user
2. Preferences of every user
3. The tenant settings
4. Cacheable entities - entities that are small in count (up to 50-100 records?) and don't change often, e.g. country and unit lookups, improving user experience when entering data.

In scope is the app server cache, the front-end cache might be differently shaped

The cache versions of the user stuff live in the User table. The cache versions of the tenant settings and for every cached entity live in settings tables, and is updated to a new value every time the data they represent changes, this needs to be guaranteed at framework level (how?) to prevent awful stale-cache bugs that are hard to catch and diagnose.
Before executing any API call, the versions are read from the DB and compared to the ones in memory.

Caches are LRUs, thread safe, stampede safe, and have observability in place to see how many cache hits and misses are we getting, and each cache size, so that we can tune the limits over time. We should also be able to determine if a cached entity was right to cache in the first place.

Alongside every cache version is a cache metaversion that is hardcoded, and incremented when the shape of the cached item has changed due to a new deployment.

**Open Questions**
- How do we guarantee the cache version is invalidated when a cacheable entity is updated?
- Is "version" the accurate technical name here? Or do we go with etag or fingerprint or something else?
- Is "metaversion" the accurate technical name here?


## Settings tables
The settings tables are a main Settings temporal table with a single row, and a collection of related tables for storing settings collections (do we need those?). Settings that are needed by the server are cached whole by the server, and settings needed for the UI are cached by the UI (different cache DTOs). Both are invalidated when SettingsVersion changes, any operation that updates the settings resets SettingsVersion.

Settings are publicly accessible, and therefore not subject to READ permissions.

```
-- For the essential typed configuration
core.Settings {
	TenantId,
	TenantName, -- To display in the UI when logged into the tenant
	TenantName2?,
	TenantName3?, 
	PrimaryLanguage,
	SecondaryLanguage?, -- Allows users to toggle between languages
	TernaryLanguage?,
	PrimaryCalendar,
	SecondaryCalendar?, -- Allows users to toggle calendars
	TenantTimeZone,
	SettingsVersion, -- For the cache - this will cause temporal churn, needs to be moved to its own table
	SavedById,
	ValidFrom,
	ValidTo
}

-- Temporal table for optional adhoc settings that come with defaults.
core.TenantConfigurationKV { -- Need a better name
	Id,
	Category, -- string (The scope of permissions)
	Key, -- string containing alphanumeric and dots (ala vscode settings keys)
	Value, -- string or JSON value
	SavedById,
	ValidFrom,
	ValidTo
}
```

**Open Questions**
- Is calling the table Settings correct? Or is there a better name?
- What do we call the KV table?
- Do we keep the language axis separate from the culture axis? E.g. PrimaryLanguage, SecondaryLanguage, TernaryLanguage? Or PrimaryCulture, SecondaryCulture, TernaryCulture? The culture for formatting strings and dates can be anything CLDR supports and there is no restriction set, whereas the languages are restricted since there are only Name, Name2, and Name3, so a tenant has to scope its languages to up to 3.
- What is the criteria for storing a setting in the top level table vs a key value pair? Are any of the distinct columns listed above better kept in the KV table? Should we store everything in the KV table?
- Should we have a single API method for editing settings? Or multiple methods for each category of settings? And the permissions resource is the category?
- What is the edit API signature? This one might be better off as a patch operation rather than load and save back like regular entities, given how many setting fields could exist and the common case is modifying only one or a few of them at a time?


## Exception handling

The web layer needs to catch exceptions and errors and surface them as HTTP status codes.

**Open Questions**
- How would the web layer determine the status code to return? Does it enumerate all the possible exceptions that come back and handle each intentionally? Or do the exceptions implement some kind of core interface that inform the web layer what status code is appropriate without having to know about each individual exception type?
- Does the server return the validation error messages or does it just return error codes that the client must translate to error messages? I feel like the later will bloat the client size with all the many error messages most of which the user will never see.


## Localization
The core library supports a large number of languages (initially English and Arabic, more will be added).
Core entities support up to 3 languages (e.g. Name, Name2, and Name3), the primary language is required the other two are optional. The tenant settings define what the three languages are, and a language service makes that information available to all the server logic, the tenant languages must be distinct and a subset of the languages supported by the Core library.
Every distro supports 
The server accepts standard culture headers, and returns localized error messages and numeric formats according to that culture
The supplied culture headers are ignored if they are not one of the tenant's languages
The server also accepts a custom calendar header (does a standard one exist?), and renders all dates in error messages formatted in that calendar, initial support for Gregorian, Umm Al Qura, and Ethiopian.

To summarize the hierarchy:
1. Dozens of languages and multiple calendars supported by Core library
2. A subset of those are supported by the distro
3. A subset of those are configured for each tenant in the distro
4. One of those is specified by the API caller in request headers based on the user's preferences

If a string is not available in the request's culture, fallback to the tenant's primary, if that is not available fall back to English, an English string must always be supplied.

**Open Questions**
1. Some compliance modules - specifically region specific ones - may not support all languages, should we account for that and say that if a distro references a compliance module than that restricts the set of languages allowed? Or leave that
2. How do we organize the resource files in the backend.
3. How do we support ICU message format for better support of count and gender specific grammar in various languages


## Multi-tenancy
Every request comes with a tenantId as part of the url api/{tenantId}/documents 

All Tellma distros are multi-tenant. But they are divided into two categories:
1. Single live tenant: distro implemented for a single customer: 1 live tenant + zero or more sandbox tenants associated with that live tenant, the live tenant database connection string is defined in config
2. Multiple-live tenants: one distro, multiple independent customers from a similar domain, live tenants can be added and removed programmatically. The tenant-to-db-conn-string map lives in a catalog db (if each db gets a password, where do we store these passwords? Is storing them in a DB a good practice?)

A core service is responsible for resolving the connection string of the tenant's database:
TenantRegistry {
	ResolveConnectionString(tenantId) -> conn string
	RegisterConnectionString(tenantId, connString) -> only in multiple-live tenant distros
	CanRegisterConnectionString -> bool
}

The implementation reads the connection string from a catalog table, the table is either in the live db in single-live-tenant distros, or in a dedicated catalog db in multi-live-tenant distros
Tenant routing info is heavily read and rarely changed, so it benefits from aggressive caching.

Keys and secrets are never stored in the DB in clear text.

Alongside the TenantRegistry we need an API to efficiently determine the tenants I'm a member of. For single-live-tenant distros we can just fan out a request to all the sandboxes to check if the user is a member, there will never be too many sandboxes, and chances are if you have access the live-db, you have access to the sandbox. But for multi-live-tenant distros we need a best effort table in the catalog to avoid having to call every single live-db, which can grow to hundreds. The seam needs to be there from the beginning even if we didn't ship the multi-live-tenant distros later.

**Open Questions**
- Is the TenantRegistery the right shape?
- The MCP counterpart endpoints, should there be one MCP server per tenant, or one server for the entire distro? I feel like most users will belong to a single live tenant, and would want to configure their AI harness to have access to only that, without overloading. But that adds friction when we come across a user with access to multiple tenants, and agents may wish to have simul-access to both live and sandbox to try complex operations there first before running them on live.
- Should we extend the TenantRegistry to blob storage connection strings, azure key vault?
- Should we extend the TenantRegistry to support provisioning new tenants? Provisioning new tenants is distro specific and not uniform, some distros will have tenant-specific integrations with external APIs that other distros don't. So the keys and connection strings that the tenantId keys on can vary substantially per tenant, how can we unify as much as possible while leaving the system extensible for such scenarios?


## Background Tasks and Scheduling
A lot of tasks in Tellma run in the background both scheduled and user-triggered:
1. Sending emails (the email outbox)
2. Integration with UAE e-invoice
3. Importing or Exporting enormous Excel files
4. Routine data cleanup
5. Generating large data: e.g. employee salaries
6. Generating expensive reports
7. Updating search indexes
8. Data quality analyzer
9. etc.

Some background tasks take a few seconds (like sending email), others hours or even days.
Some background tasks may report progress, either as distinct states or as a percentage or both.
Some background tasks may fail and need to report the failure as an error code + error message + diagnostics dump for investigation.
Some background tasks (like sending email) need to be able to "nudge" the background engine to process a new email without waiting for the next check cycle, so that it is processed immediately in the common case. Even waiting 2 mins for the check cyclce may be inconvenient.
Background tasks are isolated by tenant.

There are two parts to this:
1. The common machinery that allows running tasks in the background and reporting progress and completion. This machinery lives in Core, and supports multi-instance deployments without double executing tasks.
2. A scheduler that - given a CRON expression - triggers a background task

### Common Machinery - General Design
Any entity that wants to track some background task can add a bunch of standard columns for leasing and progress/error notifications, and registers a custom handler. The background task machinery periodically acquires a lease on a batch of rows (to prevent double processing by app instances), and processes them using the custom handler for that particular background task. If the processing takes too long and the lease has expired the task is not processed unless a new lease is acquired first. Lease expiry math takes into account a safety buffer that guarantees it won't expire before the handler processes it. Either that or if the task is processing and does complete in time, the lease is renewed to prevent double processing. We need to figure out the best approach here.

Logs and metrics need to be collected to ensure the health of the background scheduling system, and to detect things like: mis-configured estimates for task durations, sub-optimal task batch sizes, tasks taking too long between scheduling, leasing, and completion, severely backed up queues, etc...

### Scheduler
To handle scenarios where the app was down for extended time, do we replay all missed triggers? Or only trigger the last one?
- The risk with the former is that a innocent restore of an old backup risks triggering hundrends or thosuands of tasks.
- The risk of the later is that triggers are dropped, which might be bad in some scenarios.

Perhaps a common ground is to make the semantics configurable per CRON schedule: Either as a Replay all events vs. last event only toggle. Or drop event after X hours dial. We still need a safeguard against the year-old backup restore -> Recommend an option.

**Open Questions**
- Is the design robust?
- For background tasks, do we need shared coordination state in the catalog DB? Or can the system work reliably without a centralized table using the leasing pattern?
- How do otel traces fit into background tasks? Does a background task inherit the trace Id of the user request that scheduled them? Or go they get their own?
- How do we implement a scheduler where CRON expressions can be stored anywhere and can trigger all kinds of background tasks?
- For user triggered background tasks, is it best if the tasks ran under the user credentials? That feels reasonable
- For background tasks that run on a trigger, under what credentials do they run? Having them run under the credentials of the user who scheduled them is weird in two ways: A. Some of those schedules might be built into the system (e.g. updating search indexes), no one configures them. B. For the ones scheduled by users, they may be intended as permanent configuration (like a weekly report to execs), but that schedule is interrupted as soon as that user is terminated from the company or their permissions are altered. Is it best to have a built-in "system" user with full permissions, that such background tasks run under? And how do we ensure that a user who cannot see some data, doesn't schedule something that reads that data and sends it to him?
- What is the best option for the replay vs last-only question?

## Blobs
The system uses an IBlobService with 3 implementations
1. File system (default for air-gapped on-prem and for local development)
2. Azure Blob storage (default for SaaS on Azure)

IBlobService {
	WriteBlobAsync(tenantId, pathBlobPairs);
	ReadBlobAsync(tenantId, paths);
	DeleteBlobsAsync(tenantId, paths);
}


**Open Questions**
- Azure blob storage utilizes the connector/adapter pattern. Should the FileSystem one utilize the same?
- Should every tenant get their own 
- Should tenantId be a parameter in the interface, or simply part of the config passed to the 


## MCP Server
Out of scope for now until we have added enough entities to make it useful.
But we need to design a draft of the MCP tools so that our design doesn't fight when we add them later.

The root collection of MCP tools should not be a 1-1 wrapper around the API endpoints, they need to be few enough of them to not overwhelm the context window of AI models, and they should be intent-based and well documented.

MCP authentication protocol should support a human user connecting via a tool like Claude Code or Codex, and should also support autonomous agents running on a server.


## Inbox
All kinds of notifications land in the user's inbox, 
- New document was assigned to them
- A background operation they initiated has completed (e.g. a big export)
- What else?

The inbox is visible in the UI in the top utility bar as an inbox icon with 2 counters: seen and unread
- Seen counter: displayed as an eye-popping red badge, and is reset to 0 as soon as you click the inbox icon to expand the inbox dropdown.
- Read: appears as a nutral number, is decremented when you click on an unead item and read it. There is also a mark-all-as-read button

The inbox dropdown is capped to the latest N notifications, a button "View all notifications" takes you to the dedicated notifications page (a standard search page) where you can use advanced filtering and column selection.
The user's notifications settings (linkable from the inbox dropdown) allow users to finely control what notification types they want to receive.

Notifying a user that a document has been assigned to them should NOT add another network roundtrip to the DB, it rides on the same train as the save operation itself.

Signal-R is used to notify the client that the notification counters have changed, and the client fetches the latest counts and the latest inbox items.


**Open Questions**
- Is it better to include self-initiated background task completion in the notifications inbox? Certain types of background tasks should not be possible to unsubscribe from (like your giant export is ready) since it is the only way to access the exported artifacts? Or should we always provide an alternative to accessing the artifact without a notification?
- What happens when you click on an unread item? It should take you directly to the document that was assigned to you, or download the exported file that is ready, the action is entirely different per notification type
- What is the best entity model and API interface?

