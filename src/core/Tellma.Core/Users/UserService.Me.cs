// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Collections.Frozen;
using System.Globalization;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Api;
using Tellma.Core.Abstractions.Caching;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Errors;
using Tellma.Core.Abstractions.Localization;
using Tellma.Core.Abstractions.TableTypes;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.Abstractions.Validation;
using Tellma.Core.Crud;
using Tellma.Core.Crud.Save;
using Tellma.Core.Data.Batches;
using Tellma.Core.Data.Queries;
using Tellma.Core.Queryex;

namespace Tellma.Core.Users
{
    // Spec 0018 §3.5's self-service of the caller's own row: me (one Read batch, which is also the
    // cold prologue's round trip), me/save (a load, then an enlisted save of the caller's row
    // through the invoker's open frame: three round trips) and me/locale (in-memory validation,
    // then one Persist batch carrying the access guards, the update and the read-back). None needs
    // a role grant: the caller is a connected member, and the users' stack writes its own row.
    public partial class UserService<TUser>
    {
        /// <summary>The name of the caller's own envelope.</summary>
        public const string MeAction = "me";

        /// <summary>The name of the caller's own profile edit.</summary>
        public const string MeSaveAction = "me/save";

        /// <summary>The name of the caller's own locale change.</summary>
        public const string MeLocaleAction = "me/locale";

        /// <summary>The users' table, which a locale change writes under the stack's authority.</summary>
        private static readonly FrozenSet<TableName> UsersTable = new[] { PersistChecks.Users }.ToFrozenSet();

        /// <summary>
        ///     Reads the caller's own envelope in one round trip: the profile, the preference bag, what
        ///     the caller may do, and the four client tags, all as of that round trip.
        /// </summary>
        /// <param name="cancellationToken">Cancels the read.</param>
        /// <returns>The envelope.</returns>
        [ApiAction(MeAction, MemberOnly = true, Idempotent = true, Mutation = false)]
        public async Task<MeResult> MeAsync(CancellationToken cancellationToken = default)
        {
            // --- One Read batch: the connect prologue and the caller's bag ---
            BatchResult<RawResult>? bag = null;
            (ConnectedUser caller, BatchOutcome outcome) = await _dependencies.Runner.RunAsync(
                BatchPurpose.Read,
                (_, batch) =>
                {
                    bag = MeEnvelope.ComposeBag(batch, idempotent: true);
                    return ValueTask.CompletedTask;
                },
                static (user, outcome) => (user, outcome),
                cancellationToken).ConfigureAwait(false);

            // --- The envelope, the access summary from the set the round trip validated ---
            return EnvelopeOf(MeEnvelope.View(caller), MeEnvelope.Bag(bag!.Value), outcome.VersionTags, MeEnvelope.CallerTags(outcome.UserVersionTags), caller.Access);
        }

        /// <summary>
        ///     Saves the caller's own profile: the request's eight members onto the caller's row as
        ///     stored, through the calling action's frame, so the save runs the users' rules and access
        ///     rules without a role grant. A row changed since the caller read it is a conflict unless
        ///     the caller chose to overwrite it.
        /// </summary>
        /// <param name="request">The profile as the caller edited it.</param>
        /// <param name="cancellationToken">Cancels the load, the validation and the persist.</param>
        /// <returns>The caller's envelope after the save.</returns>
        /// <exception cref="ConcurrencyException">The row changed since <see cref="MeSaveRequest.ModifiedAt" />, and the caller did not choose to overwrite it.</exception>
        /// <exception cref="ValidationException">A member breaks the users' rules; the error lands at the request's member.</exception>
        [ApiAction(MeSaveAction, MemberOnly = true)]
        public async Task<MeResult> SaveMeAsync(MeSaveRequest request, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);

            // --- Round trip 1: the caller's row as stored, every mapped column and no children, its
            // last modifier's names for a conflict, and the caller's bag ---
            BatchResult<EntityQueryResult<TUser>>? row = null;
            BatchResult<RawResult>? modifier = null;
            BatchResult<RawResult>? bag = null;
            (TUser stored, ConnectedUser caller) = await _dependencies.Runner.RunAsync(
                BatchPurpose.Read,
                (user, batch) =>
                {
                    (row, modifier) = ComposeOwnRow(batch, user.UserId);
                    bag = MeEnvelope.ComposeBag(batch, idempotent: true);
                    return ValueTask.CompletedTask;
                },
                (user, _) => (row!.Value.Entities is [TUser only] ? only : throw new InvalidOperationException("The connected caller's own row was not found."), user),
                cancellationToken).ConfigureAwait(false);
            IReadOnlyDictionary<string, string> preferences = MeEnvelope.Bag(bag!.Value);

            // --- A stale stamp conflicts before anything is enlisted, unless the caller overrides it ---
            if (request.Concurrency != ConcurrencyMode.Override && request.ModifiedAt != stored.ModifiedAt)
            {
                throw Conflict(stored, modifier!.Value, _dependencies.Accessor.Current.ContentLanguageIndex);
            }

            // --- Rounds 2 and 3: the request's members on the stored row, enlisted into the invoker's
            // frame, validated and persisted; the memberships stay as they are ---
            Apply(stored, request);
            EnlistedSave<TUser> saved = await this.EnlistSaveAsync(
                _dependencies.OpenWrites,
                [stored],
                new EnlistSaveOptions { Concurrency = request.Concurrency, MapPath = WithoutRowIndex }).ConfigureAwait(false);
            EnlistmentOutcome persisted = await _dependencies.OpenWrites.PersistAsync(cancellationToken).ConfigureAwait(false);

            // --- The envelope over the saved row, the first batch's bag, the set the first batch
            // validated and the persist's tags ---
            return EnvelopeOf(MeEnvelope.View(saved.Rows[0]), preferences, persisted.VersionTags, MeEnvelope.CallerTags(persisted.UserVersionTags), caller.Access);
        }

        /// <summary>
        ///     Changes the caller's own locale in one round trip: each member set is stored as the
        ///     deployment spells it, each member reset returns to the tenant's default, and a member
        ///     equal to its stored value writes nothing. A read-only tenant admits it.
        /// </summary>
        /// <param name="request">The members to set and to reset.</param>
        /// <param name="cancellationToken">Cancels the write.</param>
        /// <returns>The locale members as stored, the row's stamp and the caller's preferences tag.</returns>
        /// <exception cref="ValidationException">The request changes nothing, sets and resets one member, or names a value the deployment does not offer.</exception>
        /// <exception cref="TenantUnavailableException">The tenant is frozen.</exception>
        [ApiAction(MeLocaleAction, MemberOnly = true, AllowWhenReadOnly = true)]
        public async Task<MeLocaleResult> SetMyLocaleAsync(MeLocaleRequest request, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);

            // --- Validation in memory, every failure at once ---
            IReadOnlyDictionary<string, object?> assignments = LocaleAssignments(request, _dependencies.Languages, _dependencies.ValueSets);

            // --- One Persist batch: the guards' lock, the update of the caller's row, the guards'
            // invariants, then the stored values; the epilogue moves the caller's preferences tag
            // when the update wrote the row ---
            PersistVerdict.Ensure(_dependencies.Accessor.Current, mutation: true, allowWhenReadOnly: true, _dependencies.Frames.Open);
            BatchResult<RawResult>? stored = null;
            BatchOutcome outcome = await _dependencies.Runner.RunAsync(
                BatchPurpose.Persist,
                (user, batch) =>
                {
                    if (batch is IBatchAuthority authority)
                    {
                        authority.SetCallerAuthority(UsersTable);
                    }

                    _ = batch.Update(new UpdateSpec<TUser>.ByIds([user.UserId], ReadFilter: null, Filter: null, assignments, Stamp: true));
                    _ = PersistGuards.Contribute(_dependencies.Guards, _dependencies.Accessor.Current, batch, UsersTable);
                    stored = batch.Sql(
                        $"SELECT [PreferredLanguage], [PreferredCalendar], [PreferredTimeZone], [PreferredRegion], [PreferredDigits], [ModifiedAt] FROM [core].[Users] WHERE [Id] = @tm_UserId;",
                        new SqlOptions(resultSets: 1));
                    return ValueTask.CompletedTask;
                },
                static (_, outcome) => outcome,
                cancellationToken).ConfigureAwait(false);

            // --- The stored values, the stamp and the caller's tag ---
            QueryRowSet set = stored!.Value.ResultSets[0];
            if (set.RowCount != 1)
            {
                throw new InvalidOperationException("The connected caller's own row was not found after its locale changed.");
            }

            string? digits = Text(set, 4);
            return new MeLocaleResult(
                Text(set, 0),
                Text(set, 1),
                Text(set, 2),
                Text(set, 3),
                digits is null ? null : Enum.Parse<DigitStyle>(digits, ignoreCase: false),
                ((DateTimeOffset[])set.GetBuffer(5))[0],
                VersionTagWire.Preferences(MeEnvelope.CallerTags(outcome.UserVersionTags).Preferences));
        }

        /// <summary>
        ///     The assignments of a locale change, validated in memory: each member set, normalised as
        ///     a save normalises it and spelled as its value set spells it, and null for each member
        ///     reset. The errors land at the request's members.
        /// </summary>
        /// <param name="request">The change.</param>
        /// <param name="languages">The languages the deployment offers.</param>
        /// <param name="valueSets">The value sets the calendar and the time zone are checked against.</param>
        /// <returns>The assignments, by the user's property name.</returns>
        /// <exception cref="ValidationException">The change is empty, sets and resets one member, or names a value not offered.</exception>
        internal static IReadOnlyDictionary<string, object?> LocaleAssignments(MeLocaleRequest request, ILanguageCatalog languages, IValueSetRegistry valueSets)
        {
            ArgumentNullException.ThrowIfNull(request);
            ArgumentNullException.ThrowIfNull(languages);
            ArgumentNullException.ThrowIfNull(valueSets);

            // --- The members, preprocessed as a save preprocesses them ---
            string? language = UserRules.Language(request.Language);
            string? calendar = UserRules.Calendar(request.Calendar);
            // An empty zone is not set, as an empty language, calendar or region is.
            string? zone = string.IsNullOrEmpty(request.TimeZone) ? null : request.TimeZone;
            string? region = UserRules.Region(request.Region);
            HashSet<MeLocaleMember> reset = [.. request.Reset ?? []];
            Dictionary<MeLocaleMember, object?> set = [];
            Put(set, MeLocaleMember.Language, language);
            Put(set, MeLocaleMember.Calendar, calendar);
            Put(set, MeLocaleMember.TimeZone, zone);
            Put(set, MeLocaleMember.Region, region);
            Put(set, MeLocaleMember.Digits, request.Digits);

            // --- The request's shape: something to change, and no member both set and reset ---
            ValidationErrors errors = new();
            if (set.Count == 0 && reset.Count == 0)
            {
                errors.Add(ValidationPath.Root.Property(nameof(MeLocaleRequest.Language)), AccessCodes.LocaleEmpty);
            }

            if (reset.Overlaps(set.Keys))
            {
                errors.Add(ValidationPath.Root.Property(nameof(MeLocaleRequest.Reset)), AccessCodes.LocaleResetConflict);
            }

            // --- Each value set against what the deployment offers; the calendar and the zone as
            // their value sets spell them ---
            if (language is not null && !languages.IsOffered(language))
            {
                errors.Add(ValidationPath.Root.Property(nameof(MeLocaleRequest.Language)), AccessCodes.LanguageNotOffered);
            }

            if (calendar is not null)
            {
                set[MeLocaleMember.Calendar] = UserRules.Spelling(valueSets, CoreValueSets.Calendar, calendar);
                if (set[MeLocaleMember.Calendar] is null)
                {
                    errors.Add(ValidationPath.Root.Property(nameof(MeLocaleRequest.Calendar)), AccessCodes.CalendarUnknown);
                }
            }

            if (zone is not null)
            {
                set[MeLocaleMember.TimeZone] = UserRules.Spelling(valueSets, CoreValueSets.TimeZone, zone);
                if (set[MeLocaleMember.TimeZone] is null)
                {
                    errors.Add(ValidationPath.Root.Property(nameof(MeLocaleRequest.TimeZone)), AccessCodes.TimeZoneUnknown);
                }
            }

            if (region is not null && !languages.IsRegionOffered(region))
            {
                errors.Add(ValidationPath.Root.Property(nameof(MeLocaleRequest.Region)), AccessCodes.RegionNotOffered);
            }

            if (errors.HasErrors)
            {
                throw new ValidationException(errors.Items);
            }

            // --- The assignments: each member set, then null for each member reset ---
            Dictionary<string, object?> assignments = new(StringComparer.Ordinal);
            foreach ((MeLocaleMember member, object? value) in set)
            {
                assignments[PropertyOf(member)] = value;
            }

            foreach (MeLocaleMember member in reset)
            {
                assignments[PropertyOf(member)] = null;
            }

            return assignments;
        }

        /// <summary>Copies the request's eight self-editable members onto the caller's stored row and leaves its memberships as stored.</summary>
        /// <param name="row">The caller's row as stored.</param>
        /// <param name="request">The profile as the caller edited it.</param>
        internal static void Apply(TUser row, MeSaveRequest request)
        {
            ArgumentNullException.ThrowIfNull(row);
            ArgumentNullException.ThrowIfNull(request);
            row.Name = request.Name;
            row.Name2 = request.Name2;
            row.Name3 = request.Name3;
            row.ImageId = request.ImageId;
            row.SignatureId = request.SignatureId;
            row.Gender = request.Gender;
            row.ContactEmail = request.ContactEmail;
            row.ContactMobile = request.ContactMobile;

            // A save without the collection leaves the memberships as they are.
            row.RoleMemberships = null;
        }

        /// <summary>
        ///     Maps a path of the enlisted save to the request's: the save's one row is the request
        ///     itself, so its index drops and <c>[0].ContactEmail</c> lands at <c>ContactEmail</c>.
        /// </summary>
        /// <param name="path">The save's path.</param>
        /// <returns>The request's path.</returns>
        internal static ValidationPath WithoutRowIndex(ValidationPath path)
        {
            ArgumentNullException.ThrowIfNull(path);
            return path.Segments.Count > 0 && path.Segments[0] is IndexSegment ? new ValidationPath([.. path.Segments.Skip(1)]) : path;
        }

        /// <summary>The conflict of a stale profile edit: the stored stamp and the last modifier, named in the reader's content language.</summary>
        /// <param name="stored">The caller's row as stored.</param>
        /// <param name="modifier">The read of the last modifier's names: one row, or none when that user is gone.</param>
        /// <param name="languageIndex">The reader's 1-based content language.</param>
        /// <returns>The conflict.</returns>
        internal static ConcurrencyException Conflict(TUser stored, RawResult modifier, int languageIndex)
        {
            ArgumentNullException.ThrowIfNull(stored);
            ArgumentNullException.ThrowIfNull(modifier);
            QueryRowSet names = modifier.ResultSets[0];
            string? name = null;
            if (names.RowCount == 1)
            {
                string? primary = Text(names, 0);
                string? localized = languageIndex switch
                {
                    2 => Text(names, 1),
                    3 => Text(names, 2),
                    _ => primary,
                };
                name = string.IsNullOrEmpty(localized) ? primary : localized;
            }

            return new ConcurrencyException(
                PersistFailures.ConflictCode,
                [new ConcurrencyConflict(stored.Id, stored.ModifiedAt.ToString(PersistFailures.StampFormat, CultureInfo.InvariantCulture), stored.ModifiedById, name, IsMissing: false)]);
        }

        /// <summary>
        ///     The envelope of a profile, a bag and the tags a round trip left: the access summary
        ///     from every registered securable decided over the caller's set as that round trip
        ///     validated it, as the evaluator decides them all, with no round trip of its own.
        /// </summary>
        /// <param name="user">The caller's profile.</param>
        /// <param name="preferences">The caller's bag.</param>
        /// <param name="tenant">The tenant's tags after the round trip.</param>
        /// <param name="caller">The caller's own tags after the round trip.</param>
        /// <param name="access">The caller's set, as the round trip validated it.</param>
        /// <returns>The envelope.</returns>
        private MeResult EnvelopeOf(
            UserProfileView user, IReadOnlyDictionary<string, string> preferences, VersionTagSnapshot tenant, UserVersionTagSnapshot caller, UserAccess access)
        {
            return MeEnvelope.Of(user, preferences, tenant, caller, access, _dependencies.Securables, _dependencies.Evaluator, _dependencies.Time.GetUtcNow());
        }

        /// <summary>
        ///     Appends the read of the caller's own row by id, every property the tenant's schema
        ///     declares and no children, as an enlisted save loads its stored rows, and of its last
        ///     modifier's names.
        /// </summary>
        /// <param name="batch">The batch.</param>
        /// <param name="userId">The caller's id.</param>
        /// <returns>The row's read and the names' read.</returns>
        /// <exception cref="InvalidOperationException">The tenant's settings are not loaded in the scope.</exception>
        private (BatchResult<EntityQueryResult<TUser>> Row, BatchResult<RawResult> Modifier) ComposeOwnRow(IDataBatch batch, int userId)
        {
            // The declared properties name the audit columns too, which the stamp check compares;
            // all properties adds the JSON columns a declared list leaves out.
            QueryexSchemaMap map = _dependencies.Schemas.Map(_dependencies.Accessor.Current.TenantSettings?.Shape ?? throw new InvalidOperationException(
                "The tenant's settings are not loaded in this scope yet, so the caller's row has no schema to be read under."));
            EntityMetadata leaf = _dependencies.Metadata.Get(typeof(TUser));
            SqlIdentifier keys = batch.Tvp([new IdList { Id = userId }]);
            BatchResult<EntityQueryResult<TUser>> row = batch.Query(new EntityQuery<TUser>
            {
                Select = string.Join(", ", map.Declared(leaf).Select(static property => property.Name)),
                AllProperties = true,
                Restrictions = [new KeySetRestriction(nameof(User.Id), keys.Name)],
            });
            BatchResult<RawResult> modifier = batch.Sql(
                $"""
                SELECT [m].[Name], [m].[Name2], [m].[Name3] FROM [core].[Users] AS [u]
                INNER JOIN [core].[Users] AS [m] ON [m].[Id] = [u].[ModifiedById]
                WHERE [u].[Id] = @tm_UserId;
                """,
                new SqlOptions(idempotent: true, resultSets: 1));
            return (row, modifier);
        }

        /// <summary>The first row's text in a column of a one-row result set.</summary>
        /// <param name="set">The result set.</param>
        /// <param name="column">The column's ordinal.</param>
        /// <returns>The text; null for a null.</returns>
        private static string? Text(QueryRowSet set, int column)
        {
            return set.IsNull(column, 0) ? null : ((string[])set.GetBuffer(column))[0];
        }

        /// <summary>Records a member the request sets.</summary>
        /// <param name="set">The members set so far.</param>
        /// <param name="member">The member.</param>
        /// <param name="value">Its value; null when the request leaves it.</param>
        private static void Put(Dictionary<MeLocaleMember, object?> set, MeLocaleMember member, object? value)
        {
            if (value is not null)
            {
                set[member] = value;
            }
        }

        /// <summary>The user's property a locale member stores in.</summary>
        /// <param name="member">The member.</param>
        /// <returns>The property's name.</returns>
        /// <exception cref="ArgumentOutOfRangeException">The member is not one of the five.</exception>
        private static string PropertyOf(MeLocaleMember member)
        {
            return member switch
            {
                MeLocaleMember.Language => nameof(User.PreferredLanguage),
                MeLocaleMember.Calendar => nameof(User.PreferredCalendar),
                MeLocaleMember.TimeZone => nameof(User.PreferredTimeZone),
                MeLocaleMember.Region => nameof(User.PreferredRegion),
                MeLocaleMember.Digits => nameof(User.PreferredDigits),
                _ => throw new ArgumentOutOfRangeException(nameof(member), member, "The member is not a locale member of the caller's profile."),
            };
        }
    }
}
