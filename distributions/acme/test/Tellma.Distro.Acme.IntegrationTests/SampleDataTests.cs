// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Api;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Entities;
using Tellma.Core.Abstractions.Errors;
using Tellma.Core.Abstractions.Hosting;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.AspNetCore;
using Tellma.Core.Migrator;
using Tellma.Distro.Acme.Development;
using Tellma.Distro.Acme.Entities;
using Tellma.Distro.Acme.Services;
using Tellma.Distro.Acme.TestSupport;
using Tellma.Identity.TestSupport;

namespace Tellma.Distro.Acme.IntegrationTests
{
    /// <summary>
    ///     Acme's development data as <c>migrate</c> writes it from a fresh checkout, and the
    ///     feature's actions and related-documents extra over it: the counts in both seed tenants,
    ///     the principals bound and signing in, none of it outside Development, the set-credit-limit
    ///     and close actions, and the invoices' related documents, read by the platform and by
    ///     members of filtered and denied invoice reads.
    /// </summary>
    /// <remarks>
    ///     The class migrates one deployment for all its tests but the one outside Development. The
    ///     tests that write (a sign-in, the actions, more invoices) write into the sandbox tenant
    ///     alone, and the counts that depend on what they write are read from the live tenant.
    /// </remarks>
    /// <param name="sql">The shared SQL Server.</param>
    /// <param name="development">The class's migrated deployment and its running web host.</param>
    [Trait("Category", "Integration")]
    public sealed class SampleDataTests(SqlServerFixture sql, SampleDataTests.Deployment development) : IClassFixture<SampleDataTests.Deployment>
    {
        /// <summary>The live seed tenant.</summary>
        private const int Live = 1;

        /// <summary>The sandbox seed tenant.</summary>
        private const int Sandbox = 2;

        /// <summary>The header a browser's API call carries, as the client shell sends it.</summary>
        private const string ClientHeaderValue = "web/1.0";

        /// <summary>The sample data's rows: centers, currencies, customers and contacts, roles, users beside the system and the administrator, invoices and lines.</summary>
        private const string Census = """
            SELECT (SELECT COUNT(*) FROM [gl].[Centers]) + (SELECT COUNT(*) FROM [acme].[Currencies])
                + (SELECT COUNT(*) FROM [acme].[Customers]) + (SELECT COUNT(*) FROM [acme].[CustomerContacts])
                + (SELECT COUNT(*) FROM [core].[Roles] WHERE [Code] IN (N'Sales', N'CenterReaders', N'CustomerEditors'))
                + (SELECT COUNT(*) FROM [core].[Users] WHERE [Id] > 1 AND ([Email] IS NULL OR [Email] <> N'admin@localhost'))
                + (SELECT COUNT(*) FROM [acme].[SalesInvoices]) + (SELECT COUNT(*) FROM [acme].[SalesInvoiceLines]);
            """;

        /// <summary>The test's cancellation.</summary>
        private static CancellationToken Token => TestContext.Current.CancellationToken;

        /// <summary>
        ///     <c>migrate</c> writes the sample data into both seed tenants: the forty centers with
        ///     four inactive, two currencies, sixty customers with pictures and contacts, the roles,
        ///     the principals bound to their seeded subjects with their catalog hints, and twenty
        ///     invoices, six of them closed; running the step again inserts nothing and records a hint
        ///     the catalog lost.
        /// </summary>
        /// <returns>A task that completes when the data is checked.</returns>
        [Fact]
        public async Task Migrate_writes_the_sample_data_into_both_development_tenants_once()
        {
            AcmeMigrator acme = development.Acme;
            foreach (int tenant in (int[])[Live, Sandbox])
            {
                string db = acme.TenantConnectionString(tenant);
                bool live = tenant == Live;

                // --- The step completed at its version, after GL's ---
                Assert.Equal(2, await ScalarAsync<int>(db, $"SELECT [Version] FROM [dbo].[__TellmaProvisioning] WHERE [Step] = N'{AcmeSampleDataStep.StepName}';"));

                // --- The centers: Acme's tree in place of GL's default, four of them inactive ---
                Assert.Equal(AcmeSampleCenters.Count, await ScalarAsync<int>(db, "SELECT COUNT(*) FROM [gl].[Centers];"));
                Assert.Equal(
                    AcmeSampleCenters.Inactive.Order(StringComparer.Ordinal),
                    (await ColumnAsync(db, "SELECT [Code] FROM [gl].[Centers] WHERE [IsActive] = 0;")).Order(StringComparer.Ordinal));

                // --- The currencies and customers: pictures committed, contacts written, four inactive ---
                Assert.Equal(2, await ScalarAsync<int>(db, "SELECT COUNT(*) FROM [acme].[Currencies];"));
                Assert.Equal(AcmeSampleData.CustomerCount, await ScalarAsync<int>(db, "SELECT COUNT(*) FROM [acme].[Customers];"));
                Assert.Equal(
                    AcmeSampleData.DeactivatedCustomers.Order(StringComparer.Ordinal),
                    (await ColumnAsync(db, "SELECT [Code] FROM [acme].[Customers] WHERE [IsActive] = 0;")).Order(StringComparer.Ordinal));
                Assert.Equal(3, await ScalarAsync<int>(db, "SELECT COUNT(DISTINCT [CustomerType]) FROM [acme].[Customers];"));
                Assert.Equal(10, await ScalarAsync<int>(db, "SELECT COUNT(*) FROM [acme].[Customers] AS C JOIN [core].[Blobs] AS B ON B.[Id] = C.[ImageId] WHERE B.[State] = 'Committed';"));
                Assert.Equal(
                    AcmeSampleData.Customers.Sum(static customer => customer.Contacts.Count),
                    await ScalarAsync<int>(db, "SELECT COUNT(*) FROM [acme].[CustomerContacts];"));

                // --- The roles, the center readers' one grant filtered ---
                Assert.Equal(AcmeSampleData.CenterReadersFilter, await ScalarAsync<string>(db, """
                    SELECT P.[Filter] FROM [core].[Permissions] AS P JOIN [core].[Roles] AS R ON R.[Id] = P.[RoleId]
                    WHERE R.[Code] = N'CenterReaders' AND P.[Resource] = 'gl.Center' AND P.[Action] = 'Read';
                    """));
                Assert.Equal(7, await ScalarAsync<int>(db, "SELECT COUNT(*) FROM [core].[Permissions] AS P JOIN [core].[Roles] AS R ON R.[Id] = P.[RoleId] WHERE R.[Code] = N'Sales';"));
                Assert.Equal(
                    ["acme.Currency|Read", "acme.Customer|Read", "acme.Customer|Save"],
                    await ColumnAsync(db, $"""
                        SELECT CONCAT(P.[Resource], '|', P.[Action]) FROM [core].[Permissions] AS P JOIN [core].[Roles] AS R ON R.[Id] = P.[RoleId]
                        WHERE R.[Code] = N'{AcmeSampleData.CustomerEditorsRoleCode}' AND P.[Filter] IS NULL ORDER BY P.[Resource], P.[Action];
                        """));

                // --- The individuals: bound to their seeded subjects, invited, each in its role; the sandbox's own in tenant 2 alone ---
                List<AcmeDevelopmentPrincipal> individuals = [AcmeDevelopmentPrincipals.SalesMember, AcmeDevelopmentPrincipals.CenterReader, AcmeDevelopmentPrincipals.CustomerEditor];
                if (tenant == Sandbox)
                {
                    individuals.Add(AcmeDevelopmentPrincipals.SandboxOnly);
                }
                else
                {
                    Assert.Equal(0, await ScalarAsync<int>(db, $"SELECT COUNT(*) FROM [core].[Users] WHERE [Email] = N'{AcmeDevelopmentPrincipals.SandboxOnly.Email}';"));
                }

                foreach (AcmeDevelopmentPrincipal principal in individuals)
                {
                    // The sandbox's sales representative may have signed in by now, which joins it.
                    string where = $"WHERE [Email] = N'{principal.Email}' AND [Kind] = 'Individual'";
                    Assert.Equal(principal.Subject, await ScalarAsync<string>(db, $"SELECT [Subject] FROM [core].[Users] {where};"));
                    Assert.Equal("Invited", await ScalarAsync<string>(db, $"SELECT [InviteStatus] FROM [core].[Users] {where};"));
                    if (live)
                    {
                        Assert.Equal("Invited", await ScalarAsync<string>(db, $"SELECT [State] FROM [core].[Users] {where};"));
                    }

                    Assert.Equal(1, await ScalarAsync<int>(db, $"""
                        SELECT COUNT(*) FROM [core].[RoleMemberships] AS M JOIN [core].[Users] AS U ON U.[Id] = M.[UserId] JOIN [core].[Roles] AS R ON R.[Id] = M.[RoleId]
                        WHERE U.[Email] = N'{principal.Email}' AND R.[Code] = N'{RoleOf(principal)}';
                        """));
                }

                Assert.Equal(
                    individuals.Select(static principal => principal.Subject).Order(StringComparer.Ordinal),
                    (await ColumnAsync(acme.CatalogConnectionString, string.Create(
                        CultureInfo.InvariantCulture,
                        $"SELECT [Subject] FROM [catalog].[TenantMemberships] WHERE [TenantId] = {tenant} AND [IsActive] = 1 AND [Subject] LIKE '00000000-0000-0000-0000-00000000000[2-9]';"))).Order(StringComparer.Ordinal));

                // --- The service account: no subject until it is issued credentials ---
                Assert.Equal(1, await ScalarAsync<int>(db, $"SELECT COUNT(*) FROM [core].[Users] WHERE [Kind] = 'Service' AND [Name] = N'{AcmeDevelopmentPrincipals.ServiceAccountName}' AND [Subject] IS NULL;"));

                // --- The invoices: twenty, the six oldest closed, every line on an invoiced sales center ---
                if (!live)
                {
                    continue;
                }

                Assert.Equal(AcmeSampleData.InvoiceCount, await ScalarAsync<int>(db, "SELECT COUNT(*) FROM [acme].[SalesInvoices];"));
                Assert.Equal(
                    AcmeSampleData.ClosedInvoices,
                    (await ColumnAsync(db, "SELECT CONVERT(nvarchar(10), [SerialNumber]) FROM [acme].[SalesInvoices] WHERE [IsClosed] = 1 ORDER BY [SerialNumber];")).Select(static serial => int.Parse(serial, CultureInfo.InvariantCulture)));
                Assert.Equal(
                    AcmeSampleData.Invoices.Sum(static invoice => invoice.Lines.Count),
                    await ScalarAsync<int>(db, "SELECT COUNT(*) FROM [acme].[SalesInvoiceLines] AS L JOIN [gl].[Centers] AS C ON C.[Id] = L.[CenterId] WHERE C.[CenterType] = 'Sale' AND C.[IsActive] = 1;"));
            }

            // --- The step run again, as a bumped version runs it, inserts nothing and restores a lost hint ---
            string salesHint = string.Create(
                CultureInfo.InvariantCulture,
                $"FROM [catalog].[TenantMemberships] WHERE [TenantId] = {Live} AND [Subject] = '{AcmeDevelopmentPrincipals.SalesMember.Subject}'");
            await SqlServerFixture.ExecuteAsync(acme.CatalogConnectionString, $"DELETE {salesHint};");
            int before = await ScalarAsync<int>(acme.TenantConnectionString(Live), Census);
            await using (TenantScope scope = await SystemScopeAsync(development.Host, Live))
            {
                TenantDescriptor tenant = scope.Services.GetRequiredService<IRequestContextAccessor>().Current.RequireTenant();
                TenantProvisioningContext context = new(tenant, scope.Services, IsNew: false, AdminSubject: null, AdminEmail: null);
                await new AcmeSampleDataStep(new DeploymentIdentity(AcmeComposition.Slug, "Development"), TimeProvider.System, NullLogger<AcmeSampleDataStep>.Instance).RunAsync(context);
            }

            Assert.Equal(before, await ScalarAsync<int>(acme.TenantConnectionString(Live), Census));
            Assert.Equal(1, await ScalarAsync<int>(acme.CatalogConnectionString, $"SELECT COUNT(*) {salesHint} AND [IsActive] = 1;"));
        }

        /// <summary>
        ///     A tenant provisioned outside Development holds none of the sample data, though both
        ///     sample steps are recorded as complete.
        /// </summary>
        /// <returns>A task that completes when the tenant is checked.</returns>
        [Fact]
        public async Task A_tenant_provisioned_outside_development_holds_none_of_the_sample_data()
        {
            AcmeMigrator acme = new(sql, "acme_sample_prod") { EnvironmentName = "Production" };

            // --- A deployment's own settings: the engine elsewhere, encrypted connections, its server listed ---
            acme.Values["Tellma:Identity:Authority"] = "https://identity.acme.test";
            acme.Values["Tellma:Identity:ServiceClientSecret"] = "test-service-client-secret";
            acme.Values["Tellma:Catalog:ConnectionString"] = Encrypted(acme.Values["Tellma:Catalog:ConnectionString"]);
            acme.Values["Tellma:Sql:Profiles:default"] = Encrypted(acme.Values["Tellma:Sql:Profiles:default"]);
            acme.Values["Tellma:Sql:AllowSqlPassword"] = "true";
            acme.Values["Tellma:Sql:AllowedServers:0"] = Host(sql.MasterConnectionString);

            // Outside Development the Azure defaults compose the Azure blob store, whose startup check
            // requires its account; the migrator serves the in-memory store in its place.
            acme.Values["Tellma:Blobs:Azure:ServiceUri"] = "https://acmesample.blob.core.windows.net/";

            // Outside Development a deployment names a real email transport; the migrator sends nothing through it.
            acme.Values["Email:Provider"] = "acs-email";
            acme.Values["Email:AcsEmail:Endpoint"] = "https://acmesample.communication.azure.com";
            acme.Values["Email:AcsEmail:From:Address"] = "no-reply@example.com";

            MigratorResult migrated = await acme.RunAsync("migrate");
            Assert.True(migrated.ExitCode == MigratorExitCodes.Converged, migrated.ToString());
            MigratorResult provisioned = await acme.RunAsync("provision", "--name", "Acme Trading", "--category", "Live");
            Assert.True(provisioned.ExitCode == MigratorExitCodes.Converged, provisioned.ToString());

            string db = acme.TenantConnectionString(1000);
            Assert.Equal(
                ["gl.sample-centers|1", $"{AcmeSampleDataStep.StepName}|2"],
                await ColumnAsync(db, "SELECT CONCAT([Step], '|', [Version]) FROM [dbo].[__TellmaProvisioning] WHERE [Step] LIKE '%sample%' ORDER BY [Step] DESC;"));
            Assert.Equal(0, await ScalarAsync<int>(db, Census));
        }

        /// <summary>
        ///     The sample sales representative signs in with an email code from a fresh checkout, holds
        ///     the <c>Sales</c> grants, and joins the tenant on that first request.
        /// </summary>
        /// <returns>A task that completes when the session is checked.</returns>
        [Fact]
        public async Task The_sample_sales_representative_signs_in_and_joins_with_the_sales_grants()
        {
            AcmeHost host = development.Host;
            using OidcFlowClient flow = host.CreateFlowClient();
            await AcmeSignIn.SignInAsync(flow, AcmeDevelopmentPrincipals.SalesMember.Email);

            using HttpRequestMessage request = new(HttpMethod.Post, new Uri($"/{Sandbox}/api/web/users/me", UriKind.Relative));
            _ = request.Headers.TryAddWithoutValidation(TellmaAuthentication.CsrfHeaderName, ClientHeaderValue);
            using HttpResponseMessage me = await flow.Browser.SendAsync(request, Token);
            Assert.True(me.StatusCode == HttpStatusCode.OK, $"{(int)me.StatusCode} {await me.Content.ReadAsStringAsync(Token)}\n{host.Warnings}");
            MeResult envelope = (await me.Content.ReadFromJsonAsync<MeResult>(TellmaJson.Options, Token))!;

            Assert.Equal(AcmeDevelopmentPrincipals.SalesMember.Email, envelope.User.Email);
            Assert.Contains(envelope.Access.Securables, static securable => securable is { Resource: "acme.SalesInvoice", Action: "Close", HasFilter: false });
            Assert.DoesNotContain(envelope.Access.Securables, static securable => securable.Resource == "acme.Customer" && securable.Action == AccessActions.Save);
            Assert.Equal("Joined", await ScalarAsync<string>(development.Acme.TenantConnectionString(Sandbox), $"SELECT [State] FROM [core].[Users] WHERE [Email] = N'{AcmeDevelopmentPrincipals.SalesMember.Email}';"));
        }

        /// <summary>
        ///     The set-credit-limit action writes its argument to every target, a negative limit is
        ///     refused at the argument, and close closes invoices.
        /// </summary>
        /// <returns>A task that completes when the actions are checked.</returns>
        [Fact]
        public async Task Set_credit_limit_writes_its_argument_refuses_a_negative_one_and_close_closes_invoices()
        {
            await using TenantScope scope = await SystemScopeAsync(development.Host, Sandbox);
            EntityService<Customer, int> customers = scope.Services.GetRequiredService<EntityService<Customer, int>>();
            int[] targets = [.. await IdsAsync(customers, "Code", "C-001", "C-002")];

            // --- Both targets take the new limit, read back ---
            EntitiesResult<Customer> set = await customers.ExecuteActionAsync(
                CustomerService.SetCreditLimitAction, targets, new SetCreditLimitArguments(123_456.78m), new ActionOptions(ReturnEntities: true), Token);
            Assert.Equal([123_456.78m, 123_456.78m], set.Entities.Select(static customer => customer.CreditLimit));

            // --- A negative limit: refused at the argument, nothing written ---
            ValidationException refused = await Assert.ThrowsAsync<ValidationException>(() => customers.ExecuteActionAsync(
                CustomerService.SetCreditLimitAction, targets, new SetCreditLimitArguments(-1m), new ActionOptions(), Token));
            ValidationError error = Assert.Single(refused.Errors);
            Assert.Equal(("Arguments.CreditLimit", AcmeValidationCodes.NegativeCreditLimit), (error.Path.ToString(), error.Code));
            Assert.Equal(123_456.78m, await ScalarAsync<decimal>(development.Acme.TenantConnectionString(Sandbox), "SELECT [CreditLimit] FROM [acme].[Customers] WHERE [Code] = N'C-001';"));

            // --- A save of a negative limit is refused at the row ---
            EntitiesResult<Customer> loaded = await customers.GetByIdAsync(targets[1], DetailsRequest.None, Token);
            Customer customer = Assert.Single(loaded.Entities);
            customer.CreditLimit = -5m;
            ValidationException saveRefused = await Assert.ThrowsAsync<ValidationException>(() => customers.SaveAsync([customer], new SaveOptions { ReturnEntities = false }, Token));
            Assert.Equal(AcmeValidationCodes.NegativeCreditLimit, Assert.Single(saveRefused.Errors).Code);

            // --- Close: an open invoice reads closed ---
            EntityService<SalesInvoice, int> invoices = scope.Services.GetRequiredService<EntityService<SalesInvoice, int>>();
            int[] open = [.. await IdsAsync(invoices, "SerialNumber", 20)];
            EntitiesResult<SalesInvoice> closed = await invoices.ExecuteActionAsync(SalesInvoiceService.CloseAction, open, null, new ActionOptions(ReturnEntities: true), Token);
            Assert.True(Assert.Single(closed.Entities).IsClosed);
        }

        /// <summary>
        ///     The related-documents extra answers each requested invoice's customer's other invoices,
        ///     latest first, at most ten, the requested invoices themselves left out.
        /// </summary>
        /// <returns>A task that completes when the extra is checked.</returns>
        [Fact]
        public async Task The_related_documents_are_the_customer_s_other_invoices_latest_first_at_most_ten()
        {
            await using TenantScope scope = await SystemScopeAsync(development.Host, Sandbox);
            EntityService<SalesInvoice, int> invoices = scope.Services.GetRequiredService<EntityService<SalesInvoice, int>>();
            DetailsRequest related = new() { Include = [RelatedDocumentsContributor.RelatedDocumentsExtra] };

            // --- Invoice 1's customer holds invoices 1, 9 and 17: the other two, the later first ---
            Dictionary<int, int> serials = await SerialsAsync(invoices);
            EntitiesResult<SalesInvoice> one = await invoices.GetByIdAsync(serials[1], related, Token);
            Assert.Equal([17, 9], RelatedSerials(one));

            // --- Invoices 1 and 9 together: only 17 is neither ---
            EntitiesResult<SalesInvoice> two = await invoices.GetByIdsAsync([serials[1], serials[9]], related, Token);
            Assert.Equal([17], RelatedSerials(two));

            // --- Twelve more invoices of that customer: the ten latest of the fourteen others ---
            int customerId = Assert.Single(one.Entities).CustomerId;
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            _ = await invoices.SaveAsync(
                [.. Enumerable.Range(101, 12).Select(serial => new SalesInvoice { SerialNumber = serial, PostingDate = today.AddDays(serial - 100), CustomerId = customerId, Lines = [] })],
                new SaveOptions { ReturnEntities = false },
                Token);
            EntitiesResult<SalesInvoice> many = await invoices.GetByIdAsync(serials[1], related, Token);
            Assert.Equal([.. Enumerable.Range(103, 10).Reverse()], RelatedSerials(many));
        }

        /// <summary>
        ///     A member's related documents follow the member's own filtered read of the invoices: a
        ///     hidden invoice of the same customer is not listed, a hidden invoice requested beside a
        ///     visible one reveals nothing of its customer, and a read of more than 128 invoices carries
        ///     no extra.
        /// </summary>
        /// <returns>A task that completes when the extra is checked.</returns>
        [Fact]
        public async Task A_member_s_related_documents_are_only_invoices_the_member_may_read()
        {
            Dictionary<int, int> serials;
            int member;
            await using (TenantScope system = await SystemScopeAsync(development.Host, Sandbox))
            {
                serials = await SerialsAsync(system.Services.GetRequiredService<EntityService<SalesInvoice, int>>());
                member = await MemberAsync(system, "OpenInvoiceReaders", "open.readers@example.com", new Permission { Resource = "acme.SalesInvoice", Action = AccessActions.Read, Filter = "IsClosed = false" });
            }

            await using TenantScope scope = await MemberScopeAsync(development.Host, Sandbox, member);
            EntityService<SalesInvoice, int> invoices = scope.Services.GetRequiredService<EntityService<SalesInvoice, int>>();
            DetailsRequest related = new() { Include = [RelatedDocumentsContributor.RelatedDocumentsExtra] };

            // --- Invoice 10's customer holds 2, 10 and 18; invoice 2 is closed, so only 18 is listed ---
            EntitiesResult<SalesInvoice> open = await invoices.GetByIdAsync(serials[10], related, Token);
            Assert.Equal([18], RelatedSerials(open));

            // --- Closed invoice 2 beside open invoice 11: only 11's customer's other open invoice, 19 ---
            int[] hiddenBesideVisible = [serials[2], serials[11]];
            EntitiesResult<SalesInvoice> mixed = await invoices.GetByIdsAsync(hiddenBesideVisible, related, Token);
            Assert.Equal([serials[11]], mixed.Entities.Select(static invoice => invoice.Id));
            Assert.Equal([19], RelatedSerials(mixed));

            // --- 128 targets carry the extra; 129 do not ---
            int[] unknown = [.. Enumerable.Range(1_000_001, RelatedDocumentsContributor.MaxTargets)];
            int[] most = [serials[10], .. unknown[..^1]];
            int[] tooMany = [serials[10], .. unknown];
            EntitiesResult<SalesInvoice> within = await invoices.GetByIdsAsync(most, related, Token);
            Assert.Equal([18], RelatedSerials(within));
            EntitiesResult<SalesInvoice> beyond = await invoices.GetByIdsAsync(tooMany, related, Token);
            Assert.Single(beyond.Entities);
            Assert.False(beyond.Extras?.ContainsKey(RelatedDocumentsContributor.RelatedDocumentsExtra) ?? false);
        }

        /// <summary>
        ///     A member with no grant on invoices is refused the read itself, so no related document
        ///     reaches them; a member whose one invoice grant is a filtered save reads by the read it
        ///     implies, and a save's read-back lists only the related invoices that filter admits.
        /// </summary>
        /// <returns>A task that completes when the extra is checked.</returns>
        [Fact]
        public async Task The_related_documents_of_a_member_without_an_invoice_read_follow_the_read_their_grants_imply()
        {
            Dictionary<int, int> serials;
            int customerId;
            int outsider;
            int saver;
            await using (TenantScope system = await SystemScopeAsync(development.Host, Sandbox))
            {
                serials = await SerialsAsync(system.Services.GetRequiredService<EntityService<SalesInvoice, int>>());
                customerId = Assert.Single(await IdsAsync(system.Services.GetRequiredService<EntityService<Customer, int>>(), "Code", "C-005"));
                Permission customers = new() { Resource = "acme.Customer", Action = AccessActions.Read };
                outsider = await MemberAsync(system, "CustomerReaders", "customer.readers@example.com", customers);
                saver = await MemberAsync(
                    system,
                    "OpenInvoiceSavers",
                    "open.savers@example.com",
                    new Permission { Resource = "acme.SalesInvoice", Action = AccessActions.Save, Filter = "IsClosed = false" },
                    new Permission { Resource = "acme.Customer", Action = AccessActions.Read });
            }

            DetailsRequest related = new() { Include = [RelatedDocumentsContributor.RelatedDocumentsExtra] };

            // --- No grant on invoices: the read is refused before any extra is composed ---
            await using (TenantScope scope = await MemberScopeAsync(development.Host, Sandbox, outsider))
            {
                EntityService<SalesInvoice, int> invoices = scope.Services.GetRequiredService<EntityService<SalesInvoice, int>>();
                ForbiddenException refused = await Assert.ThrowsAsync<ForbiddenException>(() => invoices.GetByIdAsync(serials[13], related, Token));
                Assert.Equal("acme.SalesInvoice", refused.Resource);
            }

            // --- A filtered save implies the filtered read: customer C-005 holds 5 (closed) and 13 (open) ---
            await using (TenantScope scope = await MemberScopeAsync(development.Host, Sandbox, saver))
            {
                EntityService<SalesInvoice, int> invoices = scope.Services.GetRequiredService<EntityService<SalesInvoice, int>>();
                EntitiesResult<SalesInvoice> saved = await invoices.SaveAsync(
                    [new SalesInvoice { SerialNumber = 201, PostingDate = DateOnly.FromDateTime(DateTime.UtcNow), CustomerId = customerId, Lines = [] }],
                    new SaveOptions { ReturnEntities = true, Details = related },
                    Token);
                Assert.Equal([13], RelatedSerials(saved));
            }
        }

        // --- Helpers ---

        /// <summary>The code of the one role the sample data gives a development individual.</summary>
        /// <param name="principal">The individual.</param>
        /// <returns>The role's code.</returns>
        private static string RoleOf(AcmeDevelopmentPrincipal principal)
        {
            return principal == AcmeDevelopmentPrincipals.CenterReader ? AcmeSampleData.CenterReadersRoleCode
                : principal == AcmeDevelopmentPrincipals.CustomerEditor ? AcmeSampleData.CustomerEditorsRoleCode
                : AcmeSampleData.SalesRoleCode;
        }

        /// <summary>The serial numbers of an invoice read's related documents, in the extra's order.</summary>
        /// <param name="result">The read.</param>
        /// <returns>The serial numbers.</returns>
        private static int[] RelatedSerials(EntitiesResult<SalesInvoice> result)
        {
            JsonElement extra = result.Extras![RelatedDocumentsContributor.RelatedDocumentsExtra];
            return [.. RowValues(extra, nameof(SalesInvoice.SerialNumber)).Select(static value => value.GetInt32())];
        }

        /// <summary>The values of one column of a row set's JSON.</summary>
        /// <param name="rows">The row set.</param>
        /// <param name="column">The column's name.</param>
        /// <returns>The values, in row order.</returns>
        private static IEnumerable<JsonElement> RowValues(JsonElement rows, string column)
        {
            // The columns, then the rows as arrays of cells in column order.
            int index = rows.GetProperty("columns").EnumerateArray()
                .TakeWhile(entry => !string.Equals(entry.GetProperty("name").GetString(), column, StringComparison.OrdinalIgnoreCase))
                .Count();
            return rows.GetProperty("rows").EnumerateArray().Select(row => row[index]);
        }

        /// <summary>The ids of the invoices by serial number.</summary>
        /// <param name="invoices">The invoices' service.</param>
        /// <returns>The ids by serial number.</returns>
        private static async Task<Dictionary<int, int>> SerialsAsync(EntityService<SalesInvoice, int> invoices)
        {
            RowPage page = await invoices.QueryAsync(new QueryRequest { Select = "Id, SerialNumber", Take = 100 }, Token);
            int[] ids = (int[])page.Rows.GetBuffer(0);
            int[] serials = (int[])page.Rows.GetBuffer(1);
            return Enumerable.Range(0, page.Rows.RowCount).ToDictionary(row => serials[row], row => ids[row]);
        }

        /// <summary>The ids of rows by a key, in key order.</summary>
        /// <typeparam name="TEntity">The entity type.</typeparam>
        /// <param name="service">The stack's service.</param>
        /// <param name="key">The key's column.</param>
        /// <param name="values">The keys.</param>
        /// <returns>The ids.</returns>
        private static async Task<IEnumerable<int>> IdsAsync<TEntity>(EntityService<TEntity, int> service, string key, params object[] values)
            where TEntity : Entity<int>
        {
            List<int> ids = [];
            foreach (object value in values)
            {
                RowPage page = await service.QueryAsync(
                    new QueryRequest
                    {
                        Select = "Id",
                        Filters = [$"{key} = @k"],
                        Arguments = new Dictionary<string, JsonElement> { ["k"] = JsonSerializer.SerializeToElement(value) },
                        IncludeInactive = true,
                    },
                    Token);
                ids.Add(Assert.Single(((int[])page.Rows.GetBuffer(0))[..page.Rows.RowCount]));
            }

            return ids;
        }

        /// <summary>Saves a role with some grants, and an active individual holding it alone.</summary>
        /// <param name="system">The platform's own scope of the tenant.</param>
        /// <param name="code">The role's code, also its name.</param>
        /// <param name="email">The individual's email address.</param>
        /// <param name="grants">The role's grants.</param>
        /// <returns>The individual's id.</returns>
        private static async Task<int> MemberAsync(TenantScope system, string code, string email, params Permission[] grants)
        {
            EntitiesResult<Role> role = await system.Services.GetRequiredService<EntityService<Role, int>>().SaveAsync(
                [new Role { Code = code, Name = code, Permissions = [.. grants] }], new SaveOptions { ReturnEntities = false }, Token);
            EntitiesResult<User> user = await system.Services.GetRequiredService<EntityService<User, int>>().SaveAsync(
                [new User { Kind = UserKind.Individual, Email = email, Name = code, RoleMemberships = [new RoleMembership { RoleId = checked((int)Assert.Single(role.Ids)) }] }],
                new SaveOptions { ReturnEntities = false },
                Token);
            return checked((int)Assert.Single(user.Ids));
        }

        /// <summary>A member's own scope of a tenant, as a background job of theirs runs.</summary>
        /// <param name="host">The host.</param>
        /// <param name="tenantId">The tenant.</param>
        /// <param name="userId">The member.</param>
        /// <returns>The scope.</returns>
        private static Task<TenantScope> MemberScopeAsync(AcmeHost host, int tenantId, int userId)
        {
            RequestContextSnapshot member = new(tenantId, PrincipalKind.User, Subject: null, userId, "worker", TraceParent: null);
            return host.Services.GetRequiredService<ITenantScopeFactory>().CreateScopeAsync(member, allowNonActive: false, Token);
        }

        /// <summary>The platform's own scope of a tenant.</summary>
        /// <param name="host">The host.</param>
        /// <param name="tenantId">The tenant.</param>
        /// <returns>The scope.</returns>
        private static Task<TenantScope> SystemScopeAsync(AcmeHost host, int tenantId)
        {
            RequestContextSnapshot system = new(tenantId, PrincipalKind.System, "system", WellKnownIds.SystemUserId, "worker", TraceParent: null);
            return host.Services.GetRequiredService<ITenantScopeFactory>().CreateScopeAsync(system, allowNonActive: false, Token);
        }

        /// <summary>Reads one value.</summary>
        /// <typeparam name="T">The value's type.</typeparam>
        /// <param name="connectionString">The database.</param>
        /// <param name="query">The query.</param>
        /// <returns>The value.</returns>
        private static async Task<T> ScalarAsync<T>(string connectionString, string query)
        {
            return (await SqlServerFixture.ScalarAsync<T>(connectionString, query))!;
        }

        /// <summary>Reads one text column.</summary>
        /// <param name="connectionString">The database.</param>
        /// <param name="query">The query.</param>
        /// <returns>The values, in row order.</returns>
        private static async Task<List<string>> ColumnAsync(string connectionString, string query)
        {
            await using SqlConnection connection = new(connectionString);
            await connection.OpenAsync(Token);
            await using SqlCommand command = connection.CreateCommand();
            command.CommandText = query;
            await using SqlDataReader reader = await command.ExecuteReaderAsync(Token);
            List<string> values = [];
            while (await reader.ReadAsync(Token))
            {
                values.Add(reader.GetString(0));
            }

            return values;
        }

        /// <summary>A connection string that requires encryption, as every connection outside Development does.</summary>
        /// <param name="connectionString">The connection string.</param>
        /// <returns>The encrypted one.</returns>
        private static string Encrypted(string? connectionString)
        {
            return new SqlConnectionStringBuilder(connectionString) { Encrypt = SqlConnectionEncryptOption.Mandatory, TrustServerCertificate = true }.ConnectionString;
        }

        /// <summary>The host of a connection string's server, as an allowed-servers entry names it.</summary>
        /// <param name="connectionString">The connection string.</param>
        /// <returns>The host.</returns>
        private static string Host(string connectionString)
        {
            string server = new SqlConnectionStringBuilder(connectionString).DataSource;
            server = server.StartsWith("tcp:", StringComparison.OrdinalIgnoreCase) ? server[4..] : server;
            return server.Split(',', '\\')[0];
        }

        /// <summary>The class's deployment: one migrated from an empty server, as a fresh checkout's <c>migrate</c> leaves it, and its web host.</summary>
        /// <param name="sql">The shared SQL Server.</param>
        public sealed class Deployment(SqlServerFixture sql) : IAsyncLifetime
        {
            /// <summary>The deployment's migrator.</summary>
            internal AcmeMigrator Acme { get; } = new(sql, "acme_sample");

            /// <summary>The web host over the deployment, started once it is migrated.</summary>
            internal AcmeHost Host { get; private set; } = null!;

            /// <inheritdoc />
            public async ValueTask InitializeAsync()
            {
                _ = await Acme.MigrateAsync();
                Host = await AcmeHost.StartAsync(Acme.Values);
            }

            /// <inheritdoc />
            public async ValueTask DisposeAsync()
            {
                if (Host is not null)
                {
                    await Host.DisposeAsync();
                }
            }
        }
    }
}
