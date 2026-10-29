# Editor A: applied changes (G-1, G-3, and G-2's line in spec 0026)

## Spec 0026

- **§10.3 Size cap.** One sentence at the end of the paragraph after the steps says the support dialog
  tells the user when step 1 or 3 runs (§12.2).
- **§10.4 Building and downloading.** A new step 3 says the support dialog builds its dump from the
  open-time snapshot before the compose form replaces the placeholder, and rebuilds it when the box
  changes. View, Download and Send use the dump it holds. Download becomes step 4.
- **§12.2 The support dialog.**
  - In the compose form's top-to-bottom list, "the page-data notice" now sits between the checkbox
    and the warning.
  - New bullet **The page-data notice**: a `tm-alert` (`kind="info"`, `live="polite"`) that shows
    when the size cap cut page data from a ticked box's dump. After step 1: "The rows shown on the
    page are too large to send, so they're left out." After step 3: "The data shown on the page is
    too large to send, so it's left out.", and the checkbox shows unticked and disabled, so the
    warning takes its unticked variant.
  - Send flow step 1 now reads "The dialog takes the dump it holds for the box's state (§10.4)."
    Before, it said "builds the dump".
- **§12.3 Reading the dump.** The details `get` carries `DumpJson` "because its entity query asks
  for every mapped property (spec 0012 §11.4, spec 0015 §5.3)". The spec 0016 §3.6 citation is
  dropped, because that section does not state the rule.
- **§12.5 (G-2).** The `tmEntity` sketch declares
  `visibleWhen: ({ distribution }) => distribution.support.enabled`. The "Launcher entry and context"
  bullet reads: while support is enabled the entry shows for every member (self-scope `Read`, §12.6),
  and its `visibleWhen` hides it otherwise (spec 0027 §3.1). The `hide: ['entry:core.SupportRequest']`
  sentence is gone.
- **§13, "Capture, support dialog" row.** It adds: the page-data notice after step 1 and after
  step 3; then the box unticked and disabled with the unticked warning; and the submitted dump
  matching the notice.

## Spec 0012

- **§2.6 JSON columns.** One clause: an entity query that asks for every mapped property still reads
  the column (§11.4).
- **§5.2 `EntityQuery<TEntity>` sketch.** It gains
  `public bool AllProperties { get; set; } = false; // Select names every mapped property; also reads [JsonColumn] columns (§11.4)`.
- **§11.4 Entity queries.** New paragraph **Every mapped property**. `AllProperties` marks the query
  (as the details read does). The host compiles the Queryex part as before, then appends the root's
  `[JsonColumn]` columns to the same statement as plain column projections on the root alias
  (`[T].[DumpJson]`, spec 0008 §13.4). Each child query takes the flag from its root
  (`DashboardWidget.SettingsJson`, spec 0035 §3.1). Without the flag, no `[JsonColumn]` column is
  read.
- **§11.5 The materializer.** One sentence: the §11.4 columns carry no `QueryexColumn`, and the
  materializer sets each one by column name.
- **§13.1 Unit row.** Materializer routing adds "`[JsonColumn]` by name".
- **§13.3 Queries.** It adds: `fixture.Widgets.Settings` reads through an `AllProperties` query and
  stays null without the flag.

## Spec 0015

- **§5.3 step 2.** The details `EntityQuery` sets `AllProperties = true`, "so `[JsonColumn]`
  properties load too (spec 0012 §11.4)". The paragraph is rewrapped.

## Spec 0020

- **§ Schedule entity (the paragraph after the class).** "the details read carries it" now cites
  spec 0012 §11.4 instead of spec 0015 §5.3.

## Deviations and choices

1. **The signal is the new `EntityQuery.AllProperties` flag.** Today a request asks for every mapped
   property only through the text of `Select`, which spec 0015 §5.3 builds. Nothing marks it, and
   `Select` cannot name a `[JsonColumn]` because the schema omits it. A `bool` is the smallest
   explicit signal. `Select` still lists the schema properties, so the Queryex part compiles
   unchanged, as Ahmad's rule says.
2. **`[T]` instead of `[root]`.** Queryex aliases the root `[T]` (spec 0008 §12.2, §13.4), so the
   example reads `[T].[DumpJson]`. Ahmad's `[root]` was an illustration.
3. **Children's JSON columns are included, not only the root's.** Spec 0035 has a child consumer:
   `DashboardWidget.SettingsJson` (a child of `Dashboard`, §3.1) reaches the client through
   `dashboards/get` as the parsed `settings` (§6, §8.1). Each child query therefore takes the flag
   from its root. Under a root-only rule, `dashboards/get` would return widgets without settings.
   `Dashboard.ParametersJson`, `ReportDefinition.DefinitionJson`, `Schedule.ArgumentsJson` and
   `SupportRequest.DumpJson` are on roots.
4. **The G-1 sequence.** §10.4/§12.2 used to build the dump at Send (Send flow step 1) and, it
   seems, again for View and Download. The snapshot was taken at open. Build-on-open, rebuild on the
   box, and send-what-you-hold make the notice accurate. The compose form waits for the first build
   to replace the placeholder, so the notice never shifts the layout (§14 Stable layout). The build's
   only await is the 200 ms-capped `storage.estimate()`, after the `/support` import the placeholder
   already waits for.
5. **`kind`, not `tone`.** `tm-alert`'s API is `kind` and `live` (spec 0005 §13). The existing
   warning bullet's "tone warning" wording was left alone.
6. **Telemetry: nothing added.** `tm.support.sent` already carries `pageData`.

## Not edited (checked)

- **0013.** `SettingEntry.Value` is read by raw SQL (`SELECT [Key], [Value] FROM [core].[SettingEntries]`), and the cache
  probe selects scalars only. Neither restates the details-read rule.
- **0016 §3.6.** "Every property" in the wire shape holds without restating the mechanism.
- **0033.** It does not say how a run loads `DefinitionJson` ("the row loads", §8.2).
- **0035.** It never states how widget settings are read, so it gets no cross-reference. Spec 0012
  §11.4 now cites it as the child example.
- **0032 §10.1.** "Bodies arrive with the details read, which returns every mapped property (spec
  0015 §5.3)." Outgoing e-mail bodies are not `[JsonColumn]`, so the line is still accurate.

## Notes for Ahmad

- **An action that loads and saves can drop JSON values. Please decide.** Spec 0015 §11.2 RT1 loads
  the target rows with `Query<TEntity>` (Select unstated). RT2 then saves the rows marked by `Save`
  under `Override`. If that load does not set `AllProperties`, the loaded entity's `[JsonColumn]`
  members are null. Saving it would write null over the stored JSON, for example on a
  `ReportDefinition` or `Dashboard` action with `LoadChildren` that calls `Save`. The likely fix is
  one clause in 0015 §11.2 step 2: the load sets `AllProperties`. I didn't make it because it is
  outside the pinned scope.
- **`AllProperties` trusts `Select`.** The host does not check that `Select` really names every
  mapped property. If you'd rather the flag also fill in the root's schema scalars, so that `Select`
  carries only the related paths, that is a slightly bigger change to 0012 §11.4 and 0015 §5.3.
- **0012 §11.4 leaves the child query's `Select` implicit.** I left it as it was.
- **G-2's `TmNavContext.distribution` type.** Editor B owns its name. Spec 0026 §12.5 only uses
  `distribution.support.enabled`, matching `DistributionInfo.support { enabled, providerAccess }`
  (§12.1).

## Fix pass

Applied as proposed:

- **R-F-1, applied.** 0015 §11.2 step 2 loads the targets with every mapped property and
  `AllProperties` (spec 0012 §11.4). This was the open hazard in the notes above: 0033 §5.4's
  `share`/`publish` mark rows through `ActionContext.Save` under `Override`. 0012 §11.4 now names
  both callers (details read, an action's target load, spec 0015 §5.3, §11.2).
- **R-F-2, applied.** 0026 §10.2: the box starts ticked "unless the size cap drops the page data
  (§12.2)".
- **R-F-3, applied** (merged with S-F10). 0026 §10.4 step 3: "View, Download, Download instead and
  Send use the dump it holds".
- **R-F-4, applied.** In 0027 §13, the fit stays unchanged by hover or focus, and by a navigation
  from `md` up. Decision 11 adds "(and, below `md`, navigation)".
- **R-F-5, applied.** 0012 §13.2: `fixture.WidgetParts` gains `Settings nvarchar(max)
  [JsonColumn]`, styled like the `fixture.Widgets` row, so without `NULL`. In §13.3, both columns
  read through `AllProperties` (the child's through `Children`) and stay null without the flag.
  The §8.3 `WidgetParts` SQL fragment lists no data columns, so it stays accurate.
- **S-F1, applied, shortened.** In 0012 §11.4 the host writes the projections at the end of the
  compiled `Body`'s select list, before its `FROM` line. It adds one `QueryColumn` per column after
  the compiled ones (`String`, `Nullable`, no `Path`), so §11.3's reader buffers each one as
  `string?[]`. In §11.5 the materializer sets each one by column name on the statement's own
  entity, root or child. §13.1's Unit row pins the projection in golden SQL. I dropped the
  reviewer's claim "the one place it edits engine text", because nothing else confirms it.
- **S-F2, applied.** 0027 §8.7 Widths: the hidden row renders each item in its current state, with
  §8.12's `lock` included, and widths are re-read on a pin's state change. §13 adds "a pin's state
  change".
- **S-F3, applied.** 0027 §8.7 First render: the fit waits for `document.fonts.status` `loaded`,
  or the tick after `document.fonts.ready`, so no pre-swap frame paints. A face that loads later
  re-fits. The "Preloaded fonts with metric-adjusted fallbacks (spec 0024 §7.6)" sentence is gone,
  because spec 0002 §7.1 defines no such fallbacks and preloads Latin by default. In §14 the
  Stable layout row says "with the chips hidden until the fonts are loaded". §13 adds a case for a
  font that loads late.
- **S-F5, applied.** 0027 §8.1: below `md`, pills take the chips' 120 px label cap and tooltip. In
  §8.7 the hidden row renders pills there. §13 adds "below `md`".
- **S-F6, applied.** 0027 §8.5: step 2 reads "the bar renders it". In step 3 the popover opens once
  that render's fit (§8.7) shows the new chip. The not-visible case is when "the fit puts the pin
  in More".
- **S-F9, applied.** 0026 §12.2 Send step 3 copies `includesPageData` from the dump. The §13
  support-dialog row now checks that the submission's `includesPageData` matches.
- **S-F11, applied.** 0026 §12.2: the page-data notice is one `tm-alert`, worded by the last step
  that ran.

Applied with changes:

- **S-F4, applied partly.** 0027 §8.7 Items: each item takes its width in the hidden row, with
  the bar's spacing and a folder's separator included. I did not add the proposed "4 px gap" to
  §8.1. That would be a new visual value, and the hidden row's own layout covers whatever spacing
  the bar has.
- **S-F7, applied differently.** A `visibleWhen`-hidden pin and a gone pin share `unavailable`. A
  second message would need a reason that `TmPinView.state` does not carry. So §8.12's one message
  now reads "This page isn't available.", which is true in both cases. §13 needs no new copy case.
- **S-F8, applied narrowly.** 0027 §9.3: the Recently visited band skips a page whose URL §8.3
  resolves `unavailable`. §9.4's recent-page candidates are now "every recent page §9.3 shows".
  `TmLauncherHarness` adds "a recent page hidden by `visibleWhen`". The reviewer's fix would also
  hide `no-access` recents. I left those alone: that is earlier behaviour and outside this round.
- **S-F10, applied differently.** `tmBuildSupportDump` awaits `storage.estimate()`, and no API
  lets it reuse an earlier build's `environment`, so a synchronous rebuild has no contract to stand
  on. Instead, 0026 §10.4 step 3 has View, Download, Download instead and Send wait for a rebuild
  in flight, which takes at most about 200 ms.

Rejected: none.

Checks: the stale greps ("count never depends", "Items before More", "§8.7's count",
"hide: ['entry:core.SupportRequest']", "Overflow by count") find nothing in docs/specs/0010–0035
or ARCHITECTURE.md. No "no longer exists" is left for pins. Every edited file is LF with no BOM.

### Notes for Ahmad (fix pass)

- **The host edits Queryex text (0012 §11.4).** To follow your rule ("appends … to the same
  statement"), the host writes the `[JsonColumn]` projections into the compiled `Body`, before its
  `FROM` line. That relies on spec 0008 §13.4's layout. Spec 0010 §F.3 only documents embedding
  `Body`, not editing it. The cleaner alternative is a Part F amendment, where `CompileQuery` takes
  extra raw root columns. Say if you want that.
- **0024 §7.6 has an unsupported claim (earlier text, not edited).** It says "metric-adjusted
  fallbacks keep the swap shift-free (spec 0002)", but spec 0002 (frozen) defines none and
  preloads only Latin. Either 0024 §7.6 defines the fallbacks, or the line goes.
- **Recents of `no-access` pages.** The launcher still shows them (spec 0025 §13 has no access
  filter). Say if recents should skip them as well.
