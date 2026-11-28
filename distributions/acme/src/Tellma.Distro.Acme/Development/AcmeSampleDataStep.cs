// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Api;
using Tellma.Core.Abstractions.Blobs;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Entities;
using Tellma.Core.Abstractions.Hosting;
using Tellma.Core.Abstractions.Localization;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Distro.Acme.Entities;
using Tellma.Distro.Acme.Services;
using Tellma.Module.Gl;

namespace Tellma.Distro.Acme.Development
{
    // Spec 0018 §6.3. Like GL's sample step (spec 0018 §5.6) it reads the environment from the
    // deployment identity and writes through the stacks as the system, so every rule, stamp and
    // version-tag bump of a user's save applies. It runs after gl.sample-centers (order 100), whose
    // centers the invoice lines reference. The principals are bound to the development identity
    // server's seeded subjects rather than invited: provisioning runs in the migrator, where no
    // identity server is reachable (spec 0018 §6.1's development half, extended to them).
    /// <summary>
    ///     <c>acme.sample-data</c>: writes Acme's sample rows into a development tenant (the
    ///     currencies, the customers with some pictures and contacts, the <c>Sales</c> and center
    ///     readers' roles, the end-to-end principals and the sales invoices), then closes the oldest
    ///     invoices and deactivates some customers and centers. Outside Development it records its
    ///     completion and writes nothing.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Each kind of row is found first by its natural key, and only the rows the tenant lacks
    ///         are inserted; a stored row is kept as it is, so a re-run at a higher version never
    ///         duplicates and never overwrites a developer's edits. The closed invoices and the
    ///         inactive customers and centers of <see cref="AcmeSampleData" /> and
    ///         <see cref="AcmeSampleCenters" /> end closed and inactive.
    ///     </para>
    ///     <para>
    ///         A row's <c>Name</c> is its name in the tenant's primary language, else its English
    ///         name; <c>Name2</c> and <c>Name3</c> are its names in the second and third languages,
    ///         else null. The invoices' posting dates count back from today, in UTC.
    ///     </para>
    /// </remarks>
    /// <param name="deployment">The deployment, whose environment decides whether the step writes.</param>
    /// <param name="time">The clock the posting dates count back from.</param>
    /// <param name="logger">The logger.</param>
    public sealed class AcmeSampleDataStep(DeploymentIdentity deployment, TimeProvider time, ILogger<AcmeSampleDataStep> logger) : ITenantProvisioningStep
    {
        /// <summary>The step's name.</summary>
        public const string StepName = "acme.sample-data";

        /// <summary>The step's order: after GL's sample centers.</summary>
        public const int StepOrder = 200;

        /// <summary>
        ///     The step's version: 2 added the pinned bar's end-to-end principals, which a re-run at
        ///     this version inserts into a tenant provisioned at version 1.
        /// </summary>
        public const int StepVersion = 2;

        /// <summary>The most keys one <c>in</c> list names, the query language's ceiling on list items.</summary>
        private const int KeysPerList = 128;

        /// <summary>The blob kind of the customers' pictures, which their image reference declares.</summary>
        private const string CustomerImageKind = "customer-image";

        /// <summary>The content type of the customers' drawn pictures.</summary>
        private const string PortraitContentType = "image/png";

        /// <summary>The query name of the currencies.</summary>
        private const string CurrencyResource = "acme.Currency";

        /// <summary>The query name of the customers.</summary>
        private const string CustomerResource = "acme.Customer";

        /// <summary>The query name of the sales invoices.</summary>
        private const string InvoiceResource = "acme.SalesInvoice";

        /// <summary>The query name of the invoice lines' child surface.</summary>
        private const string InvoiceLineResource = "acme.SalesInvoiceLine";

        /// <summary>The securable action of the invoices' close action.</summary>
        private const string CloseActionName = "Close";

        /// <summary>The save options of every insert: the system's save, answering ids alone.</summary>
        private static readonly SaveOptions Inserts = new() { ReturnEntities = false, Source = SaveSource.System };

        /// <inheritdoc />
        public string Name => StepName;

        /// <inheritdoc />
        public int Order => StepOrder;

        /// <inheritdoc />
        public int Version => StepVersion;

        /// <inheritdoc />
        public async Task RunAsync(TenantProvisioningContext context)
        {
            ArgumentNullException.ThrowIfNull(context);

            // --- Sample data is a development convenience: anywhere else the step only completes ---
            if (!deployment.IsDevelopment)
            {
                return;
            }

            // --- The scope's services and the tenant's content languages ---
            IServiceProvider services = context.Services;
            IReadOnlyList<LanguageInfo> languages = services.GetRequiredService<IRequestContextAccessor>().Current.TenantSettings?.Languages
                ?? throw new InvalidOperationException($"The step '{StepName}' runs in a tenant scope whose settings are loaded; the scope has none.");

            // --- The rows, each kind after the kinds it references ---
            (IReadOnlyDictionary<string, int> currencies, int newCurrencies) = await CurrenciesAsync(services, languages).ConfigureAwait(false);
            (IReadOnlyDictionary<string, int> customers, int newCustomers) = await CustomersAsync(services, languages, currencies).ConfigureAwait(false);
            (IReadOnlyDictionary<string, int> roles, int newRoles) = await RolesAsync(services, languages).ConfigureAwait(false);
            int newUsers = await UsersAsync(services, context.Tenant.Id, languages, roles).ConfigureAwait(false);
            int newInvoices = await InvoicesAsync(services, customers).ConfigureAwait(false);
            await DeactivateCentersAsync(services).ConfigureAwait(false);
            AcmeLog.SampleDataProvisioned(logger, context.Tenant.Id, newCurrencies, newCustomers, newRoles, newUsers, newInvoices);
        }

        /// <summary>The name slots of a row named in English and Arabic: its names in the tenant's three content languages.</summary>
        /// <param name="english">The English name, the primary's fallback.</param>
        /// <param name="arabic">The Arabic name.</param>
        /// <param name="languages">The tenant's content languages, in position order.</param>
        /// <returns>The primary, secondary and tertiary names.</returns>
        internal static (string Name, string? Name2, string? Name3) Names(string english, string arabic, IReadOnlyList<LanguageInfo> languages)
        {
            ArgumentNullException.ThrowIfNull(languages);
            string? TextIn(int position)
            {
                if (position >= languages.Count)
                {
                    return null;
                }

                // Matched by the primary subtag, so en-GB takes the English name and ar-SA the Arabic.
                ReadOnlySpan<char> tag = languages[position].Code.AsSpan().Trim();
                int separator = tag.IndexOfAny('-', '_');
                ReadOnlySpan<char> subtag = separator < 0 ? tag : tag[..separator];
                return subtag.Equals("en", StringComparison.OrdinalIgnoreCase) ? english
                    : subtag.Equals("ar", StringComparison.OrdinalIgnoreCase) ? arabic
                    : null;
            }

            return (TextIn(0) ?? english, TextIn(1), TextIn(2));
        }

        /// <summary>The filter text that finds rows by some keys: one <c>in</c> list per chunk of keys, disjoined.</summary>
        /// <param name="key">The key's column.</param>
        /// <param name="count">The number of keys, each a parameter <c>@k0</c>, <c>@k1</c> and on.</param>
        /// <returns>The filter text.</returns>
        internal static string KeyFilter(string key, int count)
        {
            StringBuilder filter = new("(");
            for (int start = 0; start < count; start += KeysPerList)
            {
                _ = filter.Append(start == 0 ? string.Empty : " or ").Append(key).Append(" in (");
                for (int index = start; index < Math.Min(count, start + KeysPerList); index++)
                {
                    _ = filter.Append(index == start ? "@k" : ", @k").Append(index.ToString(CultureInfo.InvariantCulture));
                }

                _ = filter.Append(')');
            }

            return filter.Append(')').ToString();
        }

        /// <summary>Finds stored rows by a key in one read.</summary>
        /// <typeparam name="TEntity">The stack's entity type.</typeparam>
        /// <param name="service">The stack's service.</param>
        /// <param name="key">The key's column.</param>
        /// <param name="values">The keys.</param>
        /// <param name="condition">A further filter the rows meet; null for none.</param>
        /// <param name="includeInactive">Whether inactive rows are found too.</param>
        /// <returns>The id of each stored row, by its key as invariant text compared without regard to case.</returns>
        private static async Task<Dictionary<string, int>> FindAsync<TEntity>(
            EntityService<TEntity, int> service, string key, IReadOnlyList<object> values, string? condition = null, bool includeInactive = true)
            where TEntity : Entity<int>
        {
            Dictionary<string, int> found = new(StringComparer.OrdinalIgnoreCase);
            if (values.Count == 0)
            {
                return found;
            }

            // --- The keys as parameters, and the conditions conjoined ---
            Dictionary<string, JsonElement> arguments = new(StringComparer.Ordinal);
            for (int index = 0; index < values.Count; index++)
            {
                arguments["k" + index.ToString(CultureInfo.InvariantCulture)] = JsonSerializer.SerializeToElement(values[index]);
            }

            QueryRequest request = new()
            {
                Select = $"{nameof(Entity<>.Id)}, {key}",
                Filters = condition is null ? [KeyFilter(key, values.Count)] : [KeyFilter(key, values.Count), condition],
                Arguments = arguments,
                Take = values.Count,
                IncludeInactive = includeInactive,
            };
            RowPage page = await service.QueryAsync(request).ConfigureAwait(false);

            // --- The columns in select order: the id, then the key ---
            int[] ids = (int[])page.Rows.GetBuffer(0);
            Array keys = page.Rows.GetBuffer(1);
            for (int row = 0; row < page.Rows.RowCount; row++)
            {
                if (Convert.ToString(keys.GetValue(row), CultureInfo.InvariantCulture) is string text)
                {
                    found[text] = ids[row];
                }
            }

            return found;
        }

        /// <summary>The id of a key that names a stored row.</summary>
        /// <param name="ids">The ids by key.</param>
        /// <param name="key">The key.</param>
        /// <param name="what">What the key names, for the error.</param>
        /// <returns>The id.</returns>
        /// <exception cref="InvalidOperationException">The key names no row.</exception>
        private static int IdOf(IReadOnlyDictionary<string, int> ids, string key, string what)
        {
            return ids.TryGetValue(key, out int id)
                ? id
                : throw new InvalidOperationException($"The step '{StepName}' found no {what} '{key}', which its sample data references.");
        }

        /// <summary>The ids a save reserved for its rows.</summary>
        /// <typeparam name="TEntity">The saved entity type.</typeparam>
        /// <param name="saved">The save's result.</param>
        /// <returns>The ids, in payload order.</returns>
        private static int[] Saved<TEntity>(EntitiesResult<TEntity> saved)
        {
            return [.. saved.Ids.Select(static id => checked((int)id))];
        }

        /// <summary>Inserts the currencies the tenant lacks.</summary>
        /// <param name="services">The scope's services.</param>
        /// <param name="languages">The tenant's content languages.</param>
        /// <returns>The id of every sample currency by code, and how many were inserted.</returns>
        private static async Task<(IReadOnlyDictionary<string, int> Ids, int Inserted)> CurrenciesAsync(IServiceProvider services, IReadOnlyList<LanguageInfo> languages)
        {
            EntityService<Currency, int> service = services.GetRequiredService<EntityService<Currency, int>>();
            Dictionary<string, int> ids = await FindAsync(service, nameof(Currency.Code), [.. AcmeSampleData.Currencies.Select(static currency => currency.Code)]).ConfigureAwait(false);

            List<Currency> rows = [];
            foreach (SampleCurrency currency in AcmeSampleData.Currencies.Where(currency => !ids.ContainsKey(currency.Code)))
            {
                (string name, string? name2, string? name3) = Names(currency.English, currency.Arabic, languages);
                rows.Add(new Currency { Code = currency.Code, Name = name, Name2 = name2, Name3 = name3 });
            }

            if (rows.Count > 0)
            {
                int[] saved = Saved(await service.SaveAsync(rows, Inserts).ConfigureAwait(false));
                for (int index = 0; index < rows.Count; index++)
                {
                    ids[rows[index].Code] = saved[index];
                }
            }

            return (ids, rows.Count);
        }

        /// <summary>Inserts the customers the tenant lacks, staging their pictures first, then deactivates the listed ones.</summary>
        /// <param name="services">The scope's services.</param>
        /// <param name="languages">The tenant's content languages.</param>
        /// <param name="currencies">The currencies' ids by code.</param>
        /// <returns>The id of every sample customer by code, and how many were inserted.</returns>
        private static async Task<(IReadOnlyDictionary<string, int> Ids, int Inserted)> CustomersAsync(
            IServiceProvider services, IReadOnlyList<LanguageInfo> languages, IReadOnlyDictionary<string, int> currencies)
        {
            EntityService<Customer, int> service = services.GetRequiredService<EntityService<Customer, int>>();
            Dictionary<string, int> ids = await FindAsync(service, nameof(Customer.Code), [.. AcmeSampleData.Customers.Select(static customer => customer.Code)]).ConfigureAwait(false);

            // --- The new customers, each picture staged as an upload the save then claims ---
            IBlobService blobs = services.GetRequiredService<IBlobService>();
            List<Customer> rows = [];
            foreach (SampleCustomer customer in AcmeSampleData.Customers.Where(customer => !ids.ContainsKey(customer.Code)))
            {
                int? imageId = null;
                if (customer.ImageNumber is int number)
                {
                    byte[] png = SamplePortraits.Png(number);
                    using MemoryStream content = new(png, writable: false);
                    BlobDescriptor staged = await blobs.StageAsync(new BlobStageRequest(
                        CustomerImageKind, content, png.Length, PortraitContentType, customer.Code + ".png")).ConfigureAwait(false);
                    imageId = staged.Id;
                }

                (string name, string? name2, string? name3) = Names(customer.English, customer.Arabic, languages);
                rows.Add(new Customer
                {
                    Code = customer.Code,
                    Name = name,
                    Name2 = name2,
                    Name3 = name3,
                    CustomerType = customer.Type,
                    CurrencyId = IdOf(currencies, customer.CurrencyCode, "currency"),
                    CreditLimit = customer.CreditLimit,
                    ImageId = imageId,
                    Contacts = [.. customer.Contacts.Select(static contact => new CustomerContact { Name = contact.Name, Email = contact.Email, Mobile = contact.Mobile })],
                });
            }

            if (rows.Count > 0)
            {
                int[] saved = Saved(await service.SaveAsync(rows, Inserts).ConfigureAwait(false));
                for (int index = 0; index < rows.Count; index++)
                {
                    ids[rows[index].Code] = saved[index];
                }
            }

            // --- The listed customers that are still active ---
            Dictionary<string, int> active = await FindAsync(service, nameof(Customer.Code), [.. AcmeSampleData.DeactivatedCustomers], includeInactive: false).ConfigureAwait(false);
            if (active.Count > 0)
            {
                _ = await service.DeactivateAsync([.. active.Values]).ConfigureAwait(false);
            }

            return (ids, rows.Count);
        }

        /// <summary>Inserts the roles the tenant lacks: <c>Sales</c>, and the center readers'.</summary>
        /// <param name="services">The scope's services.</param>
        /// <param name="languages">The tenant's content languages.</param>
        /// <returns>The id of each role by code, and how many were inserted.</returns>
        private static async Task<(IReadOnlyDictionary<string, int> Ids, int Inserted)> RolesAsync(IServiceProvider services, IReadOnlyList<LanguageInfo> languages)
        {
            EntityService<Role, int> service = services.GetRequiredService<EntityService<Role, int>>();
            Dictionary<string, int> ids = await FindAsync(service, nameof(Role.Code), [AcmeSampleData.SalesRoleCode, AcmeSampleData.CenterReadersRoleCode]).ConfigureAwait(false);

            // --- The grants: the sales pages for Sales, and a filtered read of the centers for the readers ---
            static Permission Grant(string resource, string action, string? filter = null)
            {
                return new Permission { Resource = resource, Action = action, Filter = filter };
            }

            List<Role> rows = [];
            if (!ids.ContainsKey(AcmeSampleData.SalesRoleCode))
            {
                (string name, string? name2, string? name3) = Names("Sales", "المبيعات", languages);
                rows.Add(new Role
                {
                    Code = AcmeSampleData.SalesRoleCode,
                    Name = name,
                    Name2 = name2,
                    Name3 = name3,
                    Permissions =
                    [
                        Grant(CurrencyResource, AccessActions.Read),
                        Grant(CustomerResource, AccessActions.Read),
                        Grant(InvoiceResource, AccessActions.Read),
                        Grant(InvoiceResource, AccessActions.Save),
                        Grant(InvoiceResource, CloseActionName),
                        Grant(InvoiceLineResource, AccessActions.Read),
                        Grant(GlResources.Center, AccessActions.Read),
                    ],
                });
            }

            if (!ids.ContainsKey(AcmeSampleData.CenterReadersRoleCode))
            {
                (string name, string? name2, string? name3) = Names("Center Readers", "قراء المراكز", languages);
                rows.Add(new Role
                {
                    Code = AcmeSampleData.CenterReadersRoleCode,
                    Name = name,
                    Name2 = name2,
                    Name3 = name3,
                    Permissions = [Grant(GlResources.Center, AccessActions.Read, AcmeSampleData.CenterReadersFilter)],
                });
            }

            if (rows.Count > 0)
            {
                int[] saved = Saved(await service.SaveAsync(rows, Inserts).ConfigureAwait(false));
                for (int index = 0; index < rows.Count; index++)
                {
                    ids[rows[index].Code!] = saved[index];
                }
            }

            return (ids, rows.Count);
        }

        /// <summary>
        ///     Inserts the principals the tenant lacks: the individuals bound to their seeded subjects,
        ///     and the service account; then records the membership hint of every individual bound,
        ///     on every run.
        /// </summary>
        /// <param name="services">The scope's services.</param>
        /// <param name="tenantId">The tenant, whose id decides whether the sandbox's own user belongs.</param>
        /// <param name="languages">The tenant's content languages.</param>
        /// <param name="roles">The roles' ids by code.</param>
        /// <returns>How many users were inserted.</returns>
        private static async Task<int> UsersAsync(IServiceProvider services, int tenantId, IReadOnlyList<LanguageInfo> languages, IReadOnlyDictionary<string, int> roles)
        {
            EntityService<User, int> service = services.GetRequiredService<EntityService<User, int>>();
            int sales = roles[AcmeSampleData.SalesRoleCode];
            int readers = roles[AcmeSampleData.CenterReadersRoleCode];

            // --- The tenant's individuals, each with its one role ---
            List<(AcmeDevelopmentPrincipal Principal, int RoleId)> individuals =
            [
                (AcmeDevelopmentPrincipals.SalesMember, sales),
                (AcmeDevelopmentPrincipals.CenterReader, readers),
                (AcmeDevelopmentPrincipals.PinsEnglish, sales),
                (AcmeDevelopmentPrincipals.PinsArabic, sales),
            ];
            if (tenantId == AcmeDevelopmentPrincipals.SandboxTenantId)
            {
                individuals.Add((AcmeDevelopmentPrincipals.SandboxOnly, sales));
            }

            Dictionary<string, int> stored = await FindAsync(service, nameof(User.Email), [.. individuals.Select(static individual => individual.Principal.Email)]).ConfigureAwait(false);
            List<User> rows = [];
            List<AcmeDevelopmentPrincipal> created = [];
            foreach ((AcmeDevelopmentPrincipal principal, int roleId) in individuals.Where(individual => !stored.ContainsKey(individual.Principal.Email)))
            {
                (string name, string? name2, string? name3) = Names(principal.Name, principal.Name2, languages);
                rows.Add(new User
                {
                    Kind = UserKind.Individual,
                    Email = principal.Email,
                    Name = name,
                    Name2 = name2,
                    Name3 = name3,
                    RoleMemberships = [new RoleMembership { RoleId = roleId }],
                });
                created.Add(principal);
            }

            // --- The service account, found by its name among the service accounts; no credentials ---
            (string serviceName, string? serviceName2, string? serviceName3) = Names(
                AcmeDevelopmentPrincipals.ServiceAccountName, AcmeDevelopmentPrincipals.ServiceAccountName2, languages);
            Dictionary<string, int> accounts = await FindAsync(service, nameof(User.Name), [serviceName], $"{nameof(User.Kind)} = '{nameof(UserKind.Service)}'").ConfigureAwait(false);
            if (accounts.Count == 0)
            {
                rows.Add(new User
                {
                    Kind = UserKind.Service,
                    Name = serviceName,
                    Name2 = serviceName2,
                    Name3 = serviceName3,
                    RoleMemberships = [new RoleMembership { RoleId = sales }],
                });
            }

            // --- One save, with the individuals' subjects held so each row commits bound ---
            if (rows.Count > 0)
            {
                IDevelopmentSubjects subjects = services.GetRequiredService<IDevelopmentSubjects>();
                using (subjects.Hold(created.ToDictionary(static principal => principal.Email, static principal => principal.Subject, StringComparer.Ordinal)))
                {
                    _ = await service.SaveAsync(rows, Inserts).ConfigureAwait(false);
                }
            }

            // --- The catalog's hints, so each individual's sign-in lists the tenant ---
            // Recorded on every run for each active individual whose row holds its development subject,
            // created now or found: the directory's write is a best-effort upsert, so a hint a failed
            // write or an interrupted run left out is written by the next run.
            Dictionary<string, int> bound = await FindAsync(
                service, nameof(User.Subject), [.. individuals.Select(static individual => individual.Principal.Subject)], includeInactive: false).ConfigureAwait(false);
            TenantMembershipRecord[] hints =
            [
                .. individuals
                    .Where(individual => bound.ContainsKey(individual.Principal.Subject))
                    .Select(individual => new TenantMembershipRecord { TenantId = tenantId, Subject = individual.Principal.Subject, IsActive = true }),
            ];
            if (hints.Length > 0)
            {
                await services.GetRequiredService<ITenantMembershipDirectory>().RecordAsync(hints).ConfigureAwait(false);
            }

            return rows.Count;
        }

        /// <summary>Deactivates the listed sample centers that are still active, in one action.</summary>
        /// <param name="services">The scope's services.</param>
        /// <returns>A task that completes when they are inactive.</returns>
        private static async Task DeactivateCentersAsync(IServiceProvider services)
        {
            EntityService<MyCenter, int> service = services.GetRequiredService<EntityService<MyCenter, int>>();
            Dictionary<string, int> active = await FindAsync(service, nameof(Center.Code), [.. AcmeSampleCenters.Inactive], includeInactive: false).ConfigureAwait(false);
            if (active.Count > 0)
            {
                _ = await service.DeactivateAsync([.. active.Values]).ConfigureAwait(false);
            }
        }

        /// <summary>Inserts the invoices the tenant lacks, then closes the listed ones that are still open.</summary>
        /// <param name="services">The scope's services.</param>
        /// <param name="customers">The customers' ids by code.</param>
        /// <returns>How many invoices were inserted.</returns>
        private async Task<int> InvoicesAsync(IServiceProvider services, IReadOnlyDictionary<string, int> customers)
        {
            EntityService<SalesInvoice, int> service = services.GetRequiredService<EntityService<SalesInvoice, int>>();
            Dictionary<string, int> stored = await FindAsync(service, nameof(SalesInvoice.SerialNumber), [.. AcmeSampleData.Invoices.Select(static invoice => (object)invoice.SerialNumber)]).ConfigureAwait(false);

            // --- The centers the lines are analysed by ---
            EntityService<MyCenter, int> centers = services.GetRequiredService<EntityService<MyCenter, int>>();
            Dictionary<string, int> centerIds = await FindAsync(centers, nameof(Center.Code), [.. AcmeSampleCenters.InvoicedSales]).ConfigureAwait(false);

            // --- The new invoices, dated back from today ---
            var today = DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime);
            List<SalesInvoice> rows = [];
            foreach (SampleInvoice invoice in AcmeSampleData.Invoices.Where(invoice => !stored.ContainsKey(invoice.SerialNumber.ToString(CultureInfo.InvariantCulture))))
            {
                rows.Add(new SalesInvoice
                {
                    SerialNumber = invoice.SerialNumber,
                    PostingDate = today.AddDays(-invoice.DaysAgo),
                    CustomerId = IdOf(customers, invoice.CustomerCode, "customer"),
                    Lines =
                    [
                        .. invoice.Lines.Select(line => new SalesInvoiceLine
                        {
                            CenterId = IdOf(centerIds, line.CenterCode, "center"),
                            Description = line.Description,
                            Amount = line.Amount,
                        }),
                    ],
                });
            }

            if (rows.Count > 0)
            {
                _ = await service.SaveAsync(rows, Inserts).ConfigureAwait(false);
            }

            // --- The listed invoices that are still open, closed through the action ---
            Dictionary<string, int> open = await FindAsync(
                service, nameof(SalesInvoice.SerialNumber), [.. AcmeSampleData.ClosedInvoices.Select(static serial => (object)serial)], $"{nameof(SalesInvoice.IsClosed)} = false").ConfigureAwait(false);
            if (open.Count > 0)
            {
                _ = await service.ExecuteActionAsync(SalesInvoiceService.CloseAction, [.. open.Values], arguments: null, new ActionOptions()).ConfigureAwait(false);
            }

            return rows.Count;
        }
    }
}
