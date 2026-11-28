// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Globalization;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Api;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Errors;
using Tellma.Core.Abstractions.Reports;
using Tellma.Core.Abstractions.Settings;
using Tellma.Core.Abstractions.Sharing;
using Tellma.Core.Abstractions.Validation;
using Tellma.Core.Queryex;

namespace Tellma.Core.Reports
{
    // Spec 0033 §2: the stored reports' stack. On top of the shareable service's ownership rules it
    // fills an empty title with the default one in each content slot's tenant language, derives it
    // again when the root or the kind changes under a slot that still holds the old default, binds
    // the definition document strictly, stamps the query language version its expressions were
    // written in, keeps the seeds' codes to the seeds (§13), and refuses to delete a report a
    // dashboard's widget shows (§5.6). The three sharing actions delegate to the shareable's
    // commands (§5.4); the audience delegates to the audience resolver (§6).
    /// <summary>The stored reports' stack service: default titles, the document's binding and version, reserved codes, the sharing actions and the audience.</summary>
    /// <param name="factory">The pipeline factory the scope resolves.</param>
    /// <param name="commands">The reports' sharing commands.</param>
    /// <param name="dashboards">The read-through into dashboards, which says which reports a widget shows.</param>
    /// <param name="audiences">The audience resolver, which says who sees a report and why.</param>
    /// <param name="dependencies">The platform's collaborators of the service.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public class ReportService(
        IEntityPipelineFactory factory,
        IShareCommands<Report> commands,
        IDashboardReadThrough dashboards,
        IAudienceResolver audiences,
        ReportServiceDependencies dependencies)
        : ShareableService<Report, ReportShare>(factory, commands)
    {
        /// <summary>The property of a delete's targets, under which a target's error sits.</summary>
        private const string IdsProperty = "Ids";

        /// <summary>The most dashboard titles a delete's refusal names per report.</summary>
        private const int DashboardsNamed = 5;

        /// <summary>The most dashboards whose audiences see a report through them.</summary>
        private const int DashboardsThrough = 20;


        /// <summary>The read-through into dashboards.</summary>
        private readonly IDashboardReadThrough _dashboards = dashboards ?? throw new ArgumentNullException(nameof(dashboards));

        /// <summary>The platform's collaborators.</summary>
        private readonly ReportServiceDependencies _dependencies = dependencies ?? throw new ArgumentNullException(nameof(dependencies));

        /// <summary>The audience resolver.</summary>
        private readonly IAudienceResolver _audiences = audiences ?? throw new ArgumentNullException(nameof(audiences));

        /// <summary>Shares reports with users: merges each user grant into every target and notifies each newly granted user.</summary>
        /// <param name="context">The action's context.</param>
        /// <param name="arguments">The user grants to set and the users whose grants to remove.</param>
        /// <returns>A task that completes when the writes are composed.</returns>
        [EntityAction(ShareableNames.ShareAction, Action = "Share", Description = "Shares reports with users.")]
        public Task ShareAsync(ActionContext<Report, int> context, ShareArguments arguments)
        {
            return Commands.ShareAsync(context, arguments);
        }

        /// <summary>Shares reports with roles or everyone: merges each role grant, and the Everyone grant, into every target.</summary>
        /// <param name="context">The action's context.</param>
        /// <param name="arguments">The role grants to set, the roles whose grants to remove, and the Everyone grant's fate.</param>
        /// <returns>A task that completes when the writes are composed.</returns>
        [EntityAction(ShareableNames.PublishAction, Action = "Publish", Description = "Shares reports with roles or everyone.")]
        public Task PublishAsync(ActionContext<Report, int> context, PublishArguments arguments)
        {
            return Commands.PublishAsync(context, arguments);
        }

        /// <summary>Changes the owner of reports, removing the new owner's own grant.</summary>
        /// <param name="context">The action's context.</param>
        /// <param name="arguments">The new owner.</param>
        /// <returns>A task that completes when the writes are composed.</returns>
        [EntityAction(ShareableNames.TransferAction, Action = "Transfer", Description = "Changes the owner of reports.")]
        public Task TransferAsync(ActionContext<Report, int> context, TransferArguments arguments)
        {
            return Commands.TransferAsync(context, arguments);
        }

        /// <summary>
        ///     Says which users see a report and why: each member with its reasons and highest access,
        ///     each grant with its members, and who among them cannot read the report's root; the
        ///     audiences of the dashboards that show the report see it through them.
        /// </summary>
        /// <param name="request">The report.</param>
        /// <param name="cancellationToken">Cancels the reads.</param>
        /// <returns>The audience.</returns>
        /// <exception cref="NotFoundException">The caller cannot read or share the report, or it does not exist.</exception>
        [ApiAction("audience", Action = AccessActions.Share, Mutation = false, Idempotent = true, Long = true)]
        public async Task<ShareAudience> AudienceAsync(IdRequest request, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            string resource = CoreResources.Report;
            string id = request.Id.ToString(CultureInfo.InvariantCulture);
            if (request.Id is <= 0 or > int.MaxValue)
            {
                throw new NotFoundException(resource, [id]);
            }

            // --- The report's root, whose Read its members need to see its data; a row the caller cannot read is not found ---
            int reportId = (int)request.Id;
            RowPage page = await QueryAsync(
                new QueryRequest { Select = nameof(Report.Root), Filters = [$"{nameof(Report.Id)} = {id}"], IncludeInactive = true },
                cancellationToken).ConfigureAwait(false);
            if (page.Rows.RowCount == 0 || ((string?[])page.Rows.GetBuffer(0))[0] is not string root)
            {
                throw new NotFoundException(resource, [id]);
            }

            // --- The dashboards that show it, each once, the first twenty by id ---
            IReadOnlyList<(int ReportId, int Id, string? Title)> showing = await _dashboards.DashboardsUsing([reportId]).ConfigureAwait(false);
            int[] through = [.. showing.Select(static dashboard => dashboard.Id).Distinct().Order().Take(DashboardsThrough)];

            // --- The audience, under the caller's Share decision on the report ---
            return await _audiences.ResolveAsync(
                new AudienceQuery(resource, reportId, [new SecurableRef(root, AccessActions.Read)], CoreResources.Dashboard, through),
                cancellationToken).ConfigureAwait(false);
        }

        /// <summary>Stamps the owners, then fills each empty title with the default one in every empty configured slot.</summary>
        /// <param name="context">The save's context.</param>
        /// <returns>A task that completes when the rows are filled.</returns>
        protected override async Task PreprocessAsync(SaveContext<Report> context)
        {
            ArgumentNullException.ThrowIfNull(context);
            await base.PreprocessAsync(context).ConfigureAwait(false);

            // An empty title fills every empty configured slot, each in its tenant language; a slot
            // the caller typed keeps its text.
            TenantSettings? settings = context.Context.TenantSettings;
            int slots = ReportTitles.Slots(settings);
            foreach (Report row in context.Entities)
            {
                if (!string.IsNullOrWhiteSpace(row.Title))
                {
                    continue;
                }

                for (int slot = 0; slot < slots; slot++)
                {
                    if (string.IsNullOrWhiteSpace(Slot(row, slot)))
                    {
                        SetSlot(row, slot, _dependencies.Titles.Default(row.Root, row.Kind, settings, slot));
                    }
                }
            }
        }

        /// <summary>
        ///     Runs the ownership rules, then binds each document, stamps its query language version,
        ///     derives the default titles again under a changed root or kind, and keeps the seeds'
        ///     codes to the seeds.
        /// </summary>
        /// <param name="context">The save's context.</param>
        /// <returns>A task that completes when the rules ran.</returns>
        protected override async Task ValidateAsync(SaveContext<Report> context)
        {
            ArgumentNullException.ThrowIfNull(context);

            // The base loads the before images, which every rule below reads.
            await base.ValidateAsync(context).ConfigureAwait(false);
            TenantSettings? settings = context.Context.TenantSettings;
            int slots = ReportTitles.Slots(settings);
            for (int index = 0; index < context.Entities.Count; index++)
            {
                Report row = context.Entities[index];
                Report? before = context.IsNew(index) ? null : context.Before(index);

                // --- The document, bound strictly; its version follows its expressions ---
                ReportDocument? document = null;
                if (row.DefinitionJson is string json && !ReportDocuments.TryParse(json, out document, out string? pointer))
                {
                    context.Errors.Add(index, static x => x.DefinitionJson, ReportValidationCodes.UnknownMember, Arguments(("pointer", pointer)));
                }

                row.QueryexVersion = Version(document, before);

                // --- The default titles, derived again where a slot still holds the before image's ---
                if (before is not null && (!string.Equals(before.Root, row.Root, StringComparison.Ordinal) || before.Kind != row.Kind))
                {
                    for (int slot = 0; slot < slots; slot++)
                    {
                        string? text = Slot(row, slot);
                        if (text is not null && string.Equals(text, _dependencies.Titles.Default(before.Root, before.Kind, settings, slot), StringComparison.Ordinal))
                        {
                            SetSlot(row, slot, _dependencies.Titles.Default(row.Root, row.Kind, settings, slot));
                        }
                    }
                }

                // --- A seed's code, set or changed only in a system scope ---
                if (!context.Context.IsSystem
                    && row.Code is string code
                    && (before is null || !string.Equals(before.Code, code, StringComparison.Ordinal))
                    && _dependencies.ReservedPrefixes.FirstOrDefault(prefix => code.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) is string reserved)
                {
                    context.Errors.Add(index, static x => x.Code, ReportValidationCodes.CodeReserved, Arguments(("prefix", reserved)));
                }
            }
        }

        /// <summary>Runs the ownership rules, then refuses to delete each report a dashboard's widget shows.</summary>
        /// <param name="context">The delete's context.</param>
        /// <returns>A task that completes when the rules ran.</returns>
        protected override async Task ValidateDeleteAsync(DeleteContext<Report, int> context)
        {
            ArgumentNullException.ThrowIfNull(context);
            await base.ValidateDeleteAsync(context).ConfigureAwait(false);
            if (context.Ids.Count == 0)
            {
                return;
            }

            // --- The dashboards that show the targets, each report's titles the caller can read first ---
            IReadOnlyList<(int ReportId, int Id, string? Title)> dashboardsUsing = await _dashboards.DashboardsUsing([.. context.Ids.Distinct()]).ConfigureAwait(false);
            ILookup<int, (int ReportId, int Id, string? Title)> byReport = dashboardsUsing.ToLookup(static entry => entry.ReportId);
            for (int index = 0; index < context.Ids.Count; index++)
            {
                (int ReportId, int Id, string? Title)[] shown = [.. byReport[context.Ids[index]].DistinctBy(static entry => entry.Id).OrderBy(static entry => entry.Id)];
                if (shown.Length == 0)
                {
                    continue;
                }

                // Up to five readable titles are named; "others" counts the rest, and "named" lets the
                // message say what it can when the caller can read none of them.
                string[] titles = [.. shown.Select(static entry => entry.Title).OfType<string>().Take(DashboardsNamed)];
                context.Errors.Add(
                    ValidationPath.Root.Property(IdsProperty).Index(index),
                    ReportValidationCodes.InUse,
                    Arguments(("dashboards", string.Join(", ", titles)), ("others", shown.Length - titles.Length), ("named", titles.Length)));
            }
        }

        /// <summary>
        ///     The query language version a row is stored under: the current one when the row is new
        ///     or any of its expressions changed, else the stored one.
        /// </summary>
        /// <param name="document">The row's bound document; null when it failed to bind, which the save then refuses.</param>
        /// <param name="before">The row's before image; null for a new row.</param>
        /// <returns>The version.</returns>
        internal static int Version(ReportDocument? document, Report? before)
        {
            // A new row, a document that failed to bind, or a before image whose document does not
            // bind any more: the current version. Otherwise the stored one, unless an expression changed.
            return document is not null
                && before is { DefinitionJson: string stored }
                && ReportDocuments.TryParse(stored, out ReportDocument? old, out _)
                && ReportDocuments.Expressions(document).SequenceEqual(ReportDocuments.Expressions(old!), StringComparer.Ordinal)
                ? before.QueryexVersion
                : QueryexLanguage.Version;
        }

        /// <summary>A title's text in one content slot.</summary>
        /// <param name="row">The row.</param>
        /// <param name="slot">The zero-based slot.</param>
        /// <returns>The text.</returns>
        private static string? Slot(Report row, int slot)
        {
            return slot switch
            {
                0 => row.Title,
                1 => row.Title2,
                _ => row.Title3,
            };
        }

        /// <summary>Sets a title's text in one content slot.</summary>
        /// <param name="row">The row.</param>
        /// <param name="slot">The zero-based slot.</param>
        /// <param name="text">The text.</param>
        private static void SetSlot(Report row, int slot, string text)
        {
            switch (slot)
            {
                case 0:
                    row.Title = text;
                    break;
                case 1:
                    row.Title2 = text;
                    break;
                default:
                    row.Title3 = text;
                    break;
            }
        }

        /// <summary>An error's arguments.</summary>
        /// <param name="pairs">The arguments' names and values.</param>
        /// <returns>The arguments.</returns>
        private static Dictionary<string, object?> Arguments(params (string Name, object? Value)[] pairs)
        {
            Dictionary<string, object?> arguments = new(StringComparer.Ordinal);
            foreach ((string name, object? value) in pairs)
            {
                arguments[name] = value;
            }

            return arguments;
        }
    }
}
