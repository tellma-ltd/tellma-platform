// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Tellma.Core.Abstractions.Api;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Hosting;
using Tellma.Core.Abstractions.Localization;
using Tellma.Core.Abstractions.Tenancy;

namespace Tellma.Module.Gl
{
    // Spec 0018 §5.6. The step reads the deployment's environment from the platform's deployment
    // identity, which every composition registers, rather than from the host environment, which a
    // composition outside a generic host does not register. Its round trips are the read, then the
    // save's two; none for an empty list or outside Development.
    /// <summary>
    ///     <c>gl.sample-centers</c>: writes the sample centers <see cref="IGlSampleCenters" /> supplies
    ///     into a development tenant, so a developer starts from a populated tree. Outside Development
    ///     it records its completion and writes nothing.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The step first finds the centers the tenant already stores by code, then saves the whole
    ///         list as the system: a stored center keeps its id and takes the list's names, type and
    ///         parent, and a new one is inserted. A re-run at a higher version therefore updates and
    ///         never duplicates.
    ///     </para>
    ///     <para>
    ///         A center's <see cref="Center.Name" /> is its name in the tenant's primary language, else
    ///         its first name; <see cref="Center.Name2" /> and <see cref="Center.Name3" /> are its names
    ///         in the second and third languages, else null.
    ///     </para>
    ///     <para>
    ///         A list that cannot be written as given (a center without a code or a name, a code given
    ///         twice, a parent code that names no other center of the list, or more centers than one
    ///         read can find) is refused with <see cref="InvalidOperationException" /> before anything
    ///         is written. The save's own validation reports the rest.
    ///     </para>
    /// </remarks>
    /// <typeparam name="TCenter">The stack's center leaf.</typeparam>
    /// <param name="samples">The sample centers.</param>
    /// <param name="deployment">The deployment, whose environment decides whether the step writes.</param>
    /// <param name="logger">The logger.</param>
    public sealed class GlSampleCentersStep<TCenter>(IGlSampleCenters samples, DeploymentIdentity deployment, ILogger<GlSampleCentersStep<TCenter>> logger)
        : ITenantProvisioningStep
        where TCenter : Center, new()
    {
        /// <summary>The step's name.</summary>
        public const string StepName = "gl.sample-centers";

        /// <summary>The step's order: the first of the packs' steps, after every platform step below 100.</summary>
        public const int StepOrder = 100;

        /// <summary>
        ///     The most centers a list may hold: one parameter per code, within the query language's
        ///     ceiling on parameters, so the stored centers are found in one read.
        /// </summary>
        public const int MaxCenters = 512;

        /// <summary>The most codes one <c>in</c> list names, the query language's ceiling on list items.</summary>
        private const int CodesPerList = 128;

        /// <inheritdoc />
        public string Name => StepName;

        /// <inheritdoc />
        public int Order => StepOrder;

        /// <inheritdoc />
        public int Version => samples.Version;

        /// <inheritdoc />
        public async Task RunAsync(TenantProvisioningContext context)
        {
            ArgumentNullException.ThrowIfNull(context);

            // --- Sample data is a development convenience: anywhere else the step only completes ---
            if (!deployment.IsDevelopment)
            {
                return;
            }

            // --- The list, checked before anything is read or written ---
            IReadOnlyList<SampleCenter> centers = samples.Centers;
            Dictionary<string, int> positions = Check(centers, samples.GetType());
            if (centers.Count == 0)
            {
                return;
            }

            // --- The scope's stack service and the tenant's content languages ---
            EntityService<TCenter, int> service = context.Services.GetRequiredService<EntityService<TCenter, int>>();
            IReadOnlyList<LanguageInfo> languages = context.Services.GetRequiredService<IRequestContextAccessor>().Current.TenantSettings?.Languages
                ?? throw new InvalidOperationException($"The step '{StepName}' runs in a tenant scope whose settings are loaded; the scope has none.");

            // --- The read: the stored centers of the list's codes ---
            IReadOnlyDictionary<string, int> stored = await ResolveAsync(service, centers).ConfigureAwait(false);

            // --- The save: stored centers by their ids, new ones and their references by temporary ids ---
            List<TCenter> rows = Rows(centers, positions, stored, languages);
            SaveOptions options = new()
            {
                ReturnEntities = false,
                Source = SaveSource.System,

                // A stored center is found by its code, without its stamp: the list overwrites it.
                Concurrency = ConcurrencyMode.Override,
            };
            _ = await service.SaveAsync(rows, options).ConfigureAwait(false);
            GlLog.SampleCentersProvisioned(logger, context.Tenant.Id, rows.Count);
        }

        /// <summary>Checks that a list can be written as given, and returns each code's position.</summary>
        /// <param name="centers">The list.</param>
        /// <param name="implementation">The type that supplied it, which an error names.</param>
        /// <returns>The position of each code, compared without regard to case as the stored codes are.</returns>
        /// <exception cref="InvalidOperationException">The list cannot be written as given.</exception>
        internal static Dictionary<string, int> Check(IReadOnlyList<SampleCenter> centers, Type implementation)
        {
            ArgumentNullException.ThrowIfNull(implementation);
            string name = implementation.FullName ?? implementation.Name;
            if (centers is null)
            {
                throw new InvalidOperationException($"The sample centers of '{name}' are null; supply an empty list to write none.");
            }

            if (centers.Count > MaxCenters)
            {
                throw new InvalidOperationException(
                    string.Create(CultureInfo.InvariantCulture, $"The sample centers of '{name}' number {centers.Count}; a list holds at most {MaxCenters}."));
            }

            // --- Each center: a code given once and at least one name ---
            Dictionary<string, int> positions = new(StringComparer.OrdinalIgnoreCase);
            for (int position = 0; position < centers.Count; position++)
            {
                SampleCenter? center = centers[position];
                if (center is null || string.IsNullOrWhiteSpace(center.Code))
                {
                    throw new InvalidOperationException(
                        string.Create(CultureInfo.InvariantCulture, $"The sample center at position {position} of '{name}' has no code."));
                }

                if (!positions.TryAdd(center.Code, position))
                {
                    throw new InvalidOperationException($"The sample centers of '{name}' give the code '{center.Code}' more than once.");
                }

                if (center.Names is null || center.Names.Count == 0 || center.Names.Any(static entry => entry is null || string.IsNullOrWhiteSpace(entry.Text)))
                {
                    throw new InvalidOperationException($"The sample center '{center.Code}' of '{name}' needs at least one name, and every name a text.");
                }
            }

            // --- Each parent code names another center of the list ---
            foreach (SampleCenter center in centers)
            {
                if (center.ParentCode is string parentCode
                    && (!positions.TryGetValue(parentCode, out int parent) || parent == positions[center.Code]))
                {
                    throw new InvalidOperationException(
                        $"The sample center '{center.Code}' of '{name}' names the parent code '{parentCode}', which names no other center of the list.");
                }
            }

            return positions;
        }

        /// <summary>The name slots of a center: its names in the tenant's three content languages, the first falling back to its first name.</summary>
        /// <param name="names">The center's names.</param>
        /// <param name="languages">The tenant's content languages, in position order.</param>
        /// <returns>The primary, secondary and tertiary names.</returns>
        internal static (string Name, string? Name2, string? Name3) Names(IReadOnlyList<SampleCenterName> names, IReadOnlyList<LanguageInfo> languages)
        {
            ArgumentNullException.ThrowIfNull(names);
            ArgumentNullException.ThrowIfNull(languages);
            string? primary = languages.Count > 0 ? TextIn(names, languages[0].Code) : null;
            string? secondary = languages.Count > 1 ? TextIn(names, languages[1].Code) : null;
            string? tertiary = languages.Count > 2 ? TextIn(names, languages[2].Code) : null;
            return (primary ?? names[0].Text, secondary, tertiary);
        }

        /// <summary>The filter text that finds the stored centers of some codes: one <c>in</c> list per chunk of codes, disjoined.</summary>
        /// <param name="count">The number of codes, each a parameter <c>@c0</c>, <c>@c1</c> and on.</param>
        /// <returns>The filter text.</returns>
        internal static string Filter(int count)
        {
            StringBuilder filter = new();
            for (int start = 0; start < count; start += CodesPerList)
            {
                _ = filter.Append(start == 0 ? "Code in (" : " or Code in (");
                for (int code = start; code < Math.Min(count, start + CodesPerList); code++)
                {
                    _ = filter.Append(code == start ? "@c" : ", @c").Append(code.ToString(CultureInfo.InvariantCulture));
                }

                _ = filter.Append(')');
            }

            return filter.ToString();
        }

        /// <summary>The text of a center's name in a language, matched by the primary language subtag.</summary>
        /// <param name="names">The center's names.</param>
        /// <param name="language">The tenant's language code.</param>
        /// <returns>The first matching name's text, or null.</returns>
        private static string? TextIn(IReadOnlyList<SampleCenterName> names, string language)
        {
            ReadOnlySpan<char> wanted = Subtag(language);
            foreach (SampleCenterName entry in names)
            {
                if (Subtag(entry.Language).Equals(wanted, StringComparison.OrdinalIgnoreCase))
                {
                    return entry.Text;
                }
            }

            return null;
        }

        /// <summary>The primary subtag of a language tag: <c>ar</c> of <c>ar-SA</c>.</summary>
        /// <param name="tag">The tag.</param>
        /// <returns>The primary subtag.</returns>
        private static ReadOnlySpan<char> Subtag(string? tag)
        {
            ReadOnlySpan<char> span = (tag ?? string.Empty).AsSpan().Trim();
            int separator = span.IndexOfAny('-', '_');
            return separator < 0 ? span : span[..separator];
        }

        /// <summary>Finds the stored centers of a list's codes in one read, inactive ones included.</summary>
        /// <param name="service">The centers' stack service.</param>
        /// <param name="centers">The list.</param>
        /// <returns>The id of each stored center, by code compared without regard to case.</returns>
        private static async Task<IReadOnlyDictionary<string, int>> ResolveAsync(EntityService<TCenter, int> service, IReadOnlyList<SampleCenter> centers)
        {
            Dictionary<string, JsonElement> arguments = new(StringComparer.Ordinal);
            for (int code = 0; code < centers.Count; code++)
            {
                arguments["c" + code.ToString(CultureInfo.InvariantCulture)] = JsonSerializer.SerializeToElement(centers[code].Code);
            }

            QueryRequest request = new()
            {
                Select = "Id, Code",
                Filters = [Filter(centers.Count)],
                Arguments = arguments,
                Take = centers.Count,
                IncludeInactive = true,
            };
            RowPage page = await service.QueryAsync(request).ConfigureAwait(false);

            // --- The columns in select order: the id, then the code ---
            int[] ids = (int[])page.Rows.GetBuffer(0);
            string?[] codes = (string?[])page.Rows.GetBuffer(1);
            Dictionary<string, int> stored = new(StringComparer.OrdinalIgnoreCase);
            for (int row = 0; row < page.Rows.RowCount; row++)
            {
                if (codes[row] is string code)
                {
                    stored[code] = ids[row];
                }
            }

            return stored;
        }

        /// <summary>The rows the save writes, in list order.</summary>
        /// <param name="centers">The list.</param>
        /// <param name="positions">The position of each code.</param>
        /// <param name="stored">The id of each stored center, by code.</param>
        /// <param name="languages">The tenant's content languages.</param>
        /// <returns>The rows: a stored center under its id, a new one under the temporary id minus its position plus one.</returns>
        private static List<TCenter> Rows(
            IReadOnlyList<SampleCenter> centers, Dictionary<string, int> positions, IReadOnlyDictionary<string, int> stored, IReadOnlyList<LanguageInfo> languages)
        {
            // A new center's temporary id is negative and unique within the payload; the save
            // rewrites it in every reference once the real ids are reserved.
            int IdOf(int position)
            {
                return stored.TryGetValue(centers[position].Code, out int id) ? id : -(position + 1);
            }

            List<TCenter> rows = new(centers.Count);
            for (int position = 0; position < centers.Count; position++)
            {
                SampleCenter center = centers[position];
                (string name, string? name2, string? name3) = Names(center.Names, languages);
                rows.Add(new TCenter
                {
                    Id = IdOf(position),
                    ParentId = center.ParentCode is string parentCode ? IdOf(positions[parentCode]) : null,
                    CenterType = center.CenterType,
                    Code = center.Code,
                    Name = name,
                    Name2 = name2,
                    Name3 = name3,
                });
            }

            return rows;
        }
    }
}
