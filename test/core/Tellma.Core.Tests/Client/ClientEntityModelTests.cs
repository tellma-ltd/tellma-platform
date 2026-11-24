// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Tellma.Core.Abstractions.Api;
using Tellma.Core.Abstractions.Client;
using Tellma.Core.Abstractions.Composition;
using Tellma.Core.Client;
using Tellma.Core.Composition;

namespace Tellma.Core.Tests.Client
{
    /// <summary>
    ///     The client entity model of an Acme-like composition: what the entity contributor writes to
    ///     the Core and Entities chunks, its label keys and fallbacks, its enums and value sets, its
    ///     blob policies and action fields, and the declarations it writes beside them.
    /// </summary>
    public sealed class ClientEntityModelTests(ClientEntityModelComposition composition) : IClassFixture<ClientEntityModelComposition>
    {
        // --- The composition ---

        [Fact]
        public async Task The_model_passes_the_startup_checks()
        {
            IReadOnlyList<CompositionProblem> problems = await TellmaStartupGate.RunChecksAsync(
                composition.Services, includeInfrastructure: false, TestContext.Current.CancellationToken);

            Assert.Empty(problems);
        }

        [Fact]
        public void Entities_are_ordered_by_name_and_the_summaries_are_their_leading_members()
        {
            string[] names = [.. composition.Entities.EnumerateArray().Select(static entity => entity.GetProperty("name").GetString()!)];
            Assert.Equal(names.Order(StringComparer.Ordinal), names);
            Assert.Equal(
                [ClientModel.Currencies, ClientModel.Customers, ClientModel.Contacts, ClientModel.Extras],
                names.Where(static name => name.StartsWith("acme.", StringComparison.Ordinal)));

            // Each summary is the same entity's leading members, in the same order and with the same values.
            Assert.Equal(names, composition.Summaries.EnumerateArray().Select(static summary => summary.GetProperty("name").GetString()!));
            foreach ((JsonElement summary, JsonElement entity) in composition.Summaries.EnumerateArray().Zip(composition.Entities.EnumerateArray()))
            {
                JsonProperty[] leading = [.. entity.EnumerateObject().Take(summary.EnumerateObject().Count())];
                Assert.Equal(summary.EnumerateObject().Select(static member => member.Name), leading.Select(static member => member.Name));
                Assert.All(
                    summary.EnumerateObject().Zip(leading),
                    pair => Assert.True(JsonElement.DeepEquals(pair.First.Value, pair.Second.Value), pair.First.Name));
            }
        }

        [Fact]
        public async Task Two_compositions_write_the_same_bytes()
        {
            await using ServiceProvider again = ClientModel.Compose().BuildServiceProvider(validateScopes: true);
            ClientAssetContext second = ClientEntityModelComposition.Contribute(again);

            Assert.Equal(composition.Context.Sections.Count, second.Sections.Count);
            foreach ((ClientMetadataSection first, ClientMetadataSection other) in composition.Context.Sections.Zip(second.Sections))
            {
                Assert.Equal((first.Chunk, first.Name), (other.Chunk, other.Name));
                Assert.True(first.Json.Span.SequenceEqual(other.Json.Span), $"{first.Chunk} {first.Name}");
            }

            Assert.Equal(composition.Context.Files.Select(static file => (file.RelativePath, file.Content)), second.Files.Select(static file => (file.RelativePath, file.Content)));
        }

        [Fact]
        public void The_model_finds_an_entity_without_regard_to_case()
        {
            Assert.Equal(ClientModel.Customers, composition.Model.Find("ACME.customer")?.Name);
            Assert.Null(composition.Model.Find("acme.Missing"));
        }

        // --- Kinds, operations and owners ---

        [Fact]
        public void A_stack_a_child_and_a_read_only_root_carry_their_kinds_operations_and_owners()
        {
            JsonElement currency = composition.Entity(ClientModel.Currencies);
            Assert.Equal("stack", currency.GetProperty("kind").GetString());
            Assert.Equal("currencies", currency.GetProperty("segment").GetString());
            Assert.Equal(["query", "export"], Strings(currency.GetProperty("operations")));
            Assert.Equal(["activatable", "cacheable", "excel", "searchable"], Strings(currency.GetProperty("capabilities")));
            Assert.False(currency.TryGetProperty("owner", out _));

            JsonElement customer = composition.Entity(ClientModel.Customers);
            Assert.Equal(["query", "details", "save", "delete", "import", "export"], Strings(customer.GetProperty("operations")));
            Assert.Equal(["activatable", "blobs", "excel", "multilingual", "searchable"], Strings(customer.GetProperty("capabilities")));
            AssertJson("""{"source":"Name","multilingual":true,"prefix":"Code"}""", customer.GetProperty("displayName"));
            AssertJson("""{"property":"Id","type":"int"}""", customer.GetProperty("key"));

            JsonElement contact = composition.Entity(ClientModel.Contacts);
            Assert.Equal("child", contact.GetProperty("kind").GetString());
            Assert.Equal(["query", "export"], Strings(contact.GetProperty("operations")));
            AssertJson("""{"entity":"acme.Customer","navigation":"Customer","collection":"Contacts"}""", contact.GetProperty("owner"));
            Assert.Equal("owner", ClientEntityModelComposition.Member(contact, "navigations", "Customer").GetProperty("kind").GetString());

            // A read-only root has no route of its own, so its segment is written, and written as null.
            JsonElement stamp = composition.Summaries.EnumerateArray().Single(static summary => summary.GetProperty("name").GetString() == "core.UserStamp");
            Assert.Equal("readonly", stamp.GetProperty("kind").GetString());
            Assert.Equal(JsonValueKind.Null, stamp.GetProperty("segment").ValueKind);
        }

        /// <summary>
        ///     A read-only root lists no related column, as the traversal rule projects none of it,
        ///     so a client never admits a path through one that the server refuses.
        /// </summary>
        [Fact]
        public void A_read_only_root_has_no_related_projection()
        {
            Assert.Equal(0, composition.Entity("core.UserStamp").GetProperty("relatedSelect").GetArrayLength());
        }

        /// <summary>
        ///     The searchable columns a search matches from their start are listed apart, so a client's
        ///     own search of a cached list matches each column as the server does.
        /// </summary>
        [Fact]
        public void The_columns_search_matches_from_their_start_are_listed_apart()
        {
            JsonElement currency = composition.Entity(ClientModel.Currencies);
            Assert.Equal(["Code", "Name"], Strings(currency.GetProperty("searchable")));
            Assert.Equal(["Code"], Strings(currency.GetProperty("prefixSearchable")));

            JsonElement customer = composition.Entity(ClientModel.Customers);
            Assert.Equal(["Name"], Strings(customer.GetProperty("searchable")));
            Assert.Empty(Strings(customer.GetProperty("prefixSearchable")));
            Assert.Empty(Strings(composition.Entity("core.UserStamp").GetProperty("prefixSearchable")));
        }

        [Fact]
        public void Every_entity_carries_the_query_limits_with_the_longest_filter()
        {
            Assert.All(composition.Entities.EnumerateArray(), static entity =>
            {
                JsonElement limits = entity.GetProperty("limits");
                Assert.Equal(8192, limits.GetProperty("maxFilterLength").GetInt32());
                Assert.Equal(10000, limits.GetProperty("maxTake").GetInt32());
            });
        }

        // --- Labels ---

        [Fact]
        public void An_entitys_labels_take_the_first_declared_key_or_the_conventional_key_with_a_fallback()
        {
            // Every key declared: no fallback.
            JsonElement customer = composition.Entity(ClientModel.Customers);
            Assert.Equal("Acme_Customer", customer.GetProperty("labelKey").GetString());
            Assert.Equal("Acme_Customer_Plural", customer.GetProperty("pluralLabelKey").GetString());
            Assert.Equal("Acme_Customer_New", customer.GetProperty("newLabelKey").GetString());
            Assert.False(customer.TryGetProperty("labelFallback", out _));

            // A plural falls back to the singular key, a create label to Core's.
            JsonElement currency = composition.Entity(ClientModel.Currencies);
            Assert.Equal("Acme_Currency", currency.GetProperty("pluralLabelKey").GetString());
            Assert.Equal("Entity_New", currency.GetProperty("newLabelKey").GetString());
            Assert.False(currency.TryGetProperty("labelFallback", out _));

            // No key declared: the conventional keys and the humanized name.
            JsonElement center = composition.Entity(ClientModel.Centers);
            Assert.Equal("Gl_Center", center.GetProperty("labelKey").GetString());
            Assert.Equal("Gl_Center_Plural", center.GetProperty("pluralLabelKey").GetString());
            Assert.Equal("Center", center.GetProperty("labelFallback").GetString());
        }

        [Theory]
        [InlineData(ClientModel.Customers, "CreditLimit", "Acme_Customer_CreditLimit", null)]
        [InlineData(ClientModel.Customers, "Code", "Code", null)]
        [InlineData(ClientModel.Customers, "CustomerType", "Acme_Customer_CustomerType", "Customer type")]
        [InlineData(ClientModel.Customers, "Name2", "Name", null)]
        [InlineData(ClientModel.Customers, "Name3", "Name", null)]
        [InlineData(ClientModel.Centers, "ManagerId", "Gl_Center_ManagerId", null)]
        public void A_property_takes_its_entitys_key_then_the_shared_key(string entity, string property, string key, string? fallback)
        {
            JsonElement member = ClientEntityModelComposition.Member(composition.Entity(entity), "properties", property);

            Assert.Equal(key, member.GetProperty("labelKey").GetString());
            Assert.Equal(fallback, member.TryGetProperty("labelFallback", out JsonElement value) ? value.GetString() : null);
        }

        [Theory]
        [InlineData(ClientModel.Centers, "Manager", "reference", "Gl_Center_ManagerId", null)]
        [InlineData(ClientModel.Customers, "Currency", "reference", "Acme_Customer_Currency", "Currency")]
        [InlineData(ClientModel.Customers, "CreatedBy", "reference", "CreatedBy", null)]
        [InlineData(ClientModel.Customers, "Extra", "sibling", "Acme_Customer_Extra", "Extra")]
        public void A_navigation_takes_its_own_key_then_its_foreign_keys_then_the_shared_key(string entity, string navigation, string kind, string key, string? fallback)
        {
            JsonElement member = ClientEntityModelComposition.Member(composition.Entity(entity), "navigations", navigation);

            Assert.Equal(kind, member.GetProperty("kind").GetString());
            Assert.Equal(key, member.GetProperty("labelKey").GetString());
            Assert.Equal(fallback, member.TryGetProperty("labelFallback", out JsonElement value) ? value.GetString() : null);
        }

        [Fact]
        public void A_collection_takes_its_child_entitys_plural_key()
        {
            JsonElement contacts = ClientEntityModelComposition.Member(composition.Entity(ClientModel.Customers), "children", "Contacts");

            AssertJson("""{"name":"Contacts","json":"contacts","entity":"acme.CustomerContact","parentKey":"CustomerId","maxCount":10000,"labelKey":"Acme_CustomerContact_Plural"}""", contacts);
        }

        [Theory]
        [InlineData("set-credit-limit", "Acme_Customer_Action_SetCreditLimit", null)]
        [InlineData("activate", "Action_Activate", null)]
        [InlineData("ranks/recompute", "Acme_Customer_Action_RanksRecompute", "Ranks recompute")]
        public void An_action_takes_its_entitys_key_then_the_shared_key(string action, string key, string? fallback)
        {
            JsonElement member = ClientEntityModelComposition.Member(composition.Entity(ClientModel.Customers), "actions", action);

            Assert.Equal(key, member.GetProperty("labelKey").GetString());
            Assert.Equal(fallback, member.TryGetProperty("labelFallback", out JsonElement value) ? value.GetString() : null);
        }

        [Theory]
        [InlineData("creditLimit", "Acme_Customer_CreditLimit", null)]
        [InlineData("assigneeId", "Acme_Customer_Action_SetCreditLimit_Assignee", "Assignee")]
        [InlineData("comment", "Acme_Customer_Action_SetCreditLimit_Comment", null)]
        [InlineData("effectiveAt", "Acme_Customer_Action_SetCreditLimit_EffectiveAt", "Effective at")]
        public void An_action_field_takes_the_actions_key_then_the_entitys_then_the_shared_key(string field, string key, string? fallback)
        {
            JsonElement member = ClientEntityModelComposition.Member(SetCreditLimit(), "fields", field);

            Assert.Equal(key, member.GetProperty("labelKey").GetString());
            Assert.Equal(fallback, member.TryGetProperty("labelFallback", out JsonElement value) ? value.GetString() : null);
        }

        // --- Properties ---

        [Fact]
        public void Multilingual_twins_carry_their_group_and_slot_and_the_anchors_label()
        {
            JsonElement customer = composition.Entity(ClientModel.Customers);
            foreach ((string name, int slot) in new[] { ("Name", 1), ("Name2", 2), ("Name3", 3) })
            {
                JsonElement twin = ClientEntityModelComposition.Member(customer, "properties", name);
                AssertJson("""{"group":"Name","slot":""" + slot + "}", twin.GetProperty("multilingual"));
                Assert.Equal(255, twin.GetProperty("maxLength").GetInt32());
                Assert.Equal(slot > 1, twin.GetProperty("nullable").GetBoolean());
            }
        }

        [Fact]
        public void A_property_carries_its_type_facets_and_flags()
        {
            JsonElement customer = composition.Entity(ClientModel.Customers);
            JsonElement Property(string name)
            {
                return ClientEntityModelComposition.Member(customer, "properties", name);
            }

            JsonElement limit = Property("CreditLimit");
            Assert.Equal(("decimal", 19, 4), (limit.GetProperty("type").GetString(), limit.GetProperty("precision").GetInt32(), limit.GetProperty("scale").GetInt32()));

            JsonElement reference = Property("ExternalReference");
            Assert.True(reference.GetProperty("long").GetBoolean());
            Assert.False(reference.GetProperty("excel").GetBoolean());

            Assert.Equal(("json", false), (Property("Notes").GetProperty("type").GetString(), Property("Notes").GetProperty("queryable").GetBoolean()));
            Assert.Equal("Currency", Property("CurrencyId").GetProperty("foreignKeyOf").GetString());
            Assert.Equal("server-owned", Property("Id").GetProperty("ownership").GetString());
            Assert.Equal("editable", Property("Code").GetProperty("ownership").GetString());
            Assert.True(Property("Code").GetProperty("unique").GetBoolean());

            // Defaulted is written only when true.
            Assert.True(Property("Rank").GetProperty("defaulted").GetBoolean());
            Assert.False(Property("Code").TryGetProperty("defaulted", out _));
        }

        [Fact]
        public void A_blob_reference_carries_its_kinds_policy()
        {
            JsonElement image = ClientEntityModelComposition.Member(composition.Entity(ClientModel.Customers), "properties", "ImageId");

            AssertJson(
                """{"kind":"customer-image","accept":["image/jpeg","image/png","image/webp","image/gif"],"maxSize":10485760,"image":{"maxDimension":512,"fit":"cover-square","format":"webp"},"thumbnail":true,"readAccess":"owner-read"}""",
                image.GetProperty("blob"));
        }

        [Fact]
        public async Task A_blob_policys_size_is_its_kinds_whatever_the_emitting_hosts_upload_limit()
        {
            // The chunk is built once and served by every host; each host's startup gate already keeps
            // every kind's size within its own upload limit.
            await using ServiceProvider capped = ClientModel.Compose(new Dictionary<string, string?> { ["Tellma:Blobs:MaxUploadSize"] = "1000000" })
                .BuildServiceProvider(validateScopes: true);
            ClientAssetContext context = ClientEntityModelComposition.Contribute(capped);
            using var entities = JsonDocument.Parse(ClientEntityModelComposition.Section(context, ClientMetadataChunk.Entities, EntityClientAssetContributor.EntitiesSection));

            JsonElement customer = entities.RootElement.EnumerateArray().Single(static entity => entity.GetProperty("name").GetString() == ClientModel.Customers);
            JsonElement image = ClientEntityModelComposition.Member(customer, "properties", "ImageId");
            Assert.Equal(10 * 1024 * 1024, image.GetProperty("blob").GetProperty("maxSize").GetInt64());
        }

        [Theory]
        [InlineData(typeof(Customer), ClientModel.Customers)]
        [InlineData(typeof(CustomerContact), ClientModel.Contacts)]
        [InlineData(typeof(CustomerExtra), ClientModel.Extras)]
        [InlineData(typeof(Currency), ClientModel.Currencies)]
        [InlineData(typeof(Center), ClientModel.Centers)]
        public void Property_and_collection_names_are_the_wires(Type type, string name)
        {
            JsonElement entity = composition.Entity(name);
            HashSet<string> wire = [.. TellmaJson.Options.GetTypeInfo(type).Properties.Select(static property => property.Name)];

            Assert.All(entity.GetProperty("properties").EnumerateArray().Concat(entity.GetProperty("children").EnumerateArray()), member =>
                Assert.Contains(member.GetProperty("json").GetString()!, wire));
        }

        // --- Actions ---

        [Fact]
        public void An_entity_action_with_arguments_carries_its_flags_schema_and_fields()
        {
            JsonElement action = SetCreditLimit();
            Assert.Equal(("entity", false, "SetCreditLimit", "entities"), (
                action.GetProperty("kind").GetString(),
                action.GetProperty("builtIn").GetBoolean(),
                action.GetProperty("action").GetString(),
                action.GetProperty("result").GetString()));
            Assert.True(action.GetProperty("allowWhenReadOnly").GetBoolean());
            Assert.True(action.GetProperty("supportsFilter").GetBoolean());

            // The schema says what the wire carries: decimals and longs as text, instants as date-times.
            JsonElement schema = action.GetProperty("arguments");
            Assert.False(schema.TryGetProperty("$schema", out _));
            JsonElement properties = schema.GetProperty("properties");
            AssertJson("""{"type":"string","pattern":"^-?[0-9]+(\\.[0-9]+)?$"}""", properties.GetProperty("creditLimit"));
            AssertJson("""{"type":["string","null"],"pattern":"^-?[0-9]+$"}""", properties.GetProperty("reference"));
            AssertJson("""{"type":["string","null"],"format":"date-time"}""", properties.GetProperty("effectiveAt"));
            AssertJson("""{"type":"string","enum":["Low","High"]}""", properties.GetProperty("priority"));
            AssertJson("""{"type":["string","null"],"maxLength":200}""", properties.GetProperty("comment"));
            Assert.Equal(["creditLimit", "priority"], Strings(schema.GetProperty("required")));

            // The fields describe each value for a form.
            Assert.Equal(
                ["creditLimit", "assigneeId", "priority", "comment", "reference", "effectiveAt", "reviewers", "tags"],
                action.GetProperty("fields").EnumerateArray().Select(static field => field.GetProperty("name").GetString()!));
            AssertJson("""{"type":"decimal","nullable":false}""", FieldValue("creditLimit"));
            AssertJson("""{"type":"integer","nullable":true,"target":"core.User"}""", FieldValue("assigneeId"));
            AssertJson("""{"type":"enum","nullable":false,"enum":"acme.Priority"}""", FieldValue("priority"));
            AssertJson("""{"type":"string","nullable":true,"maxLength":200}""", FieldValue("comment"));
            AssertJson("""{"type":"integer","nullable":true,"long":true}""", FieldValue("reference"));
            AssertJson("""{"type":"instant","nullable":true}""", FieldValue("effectiveAt"));
            AssertJson("""{"type":"integer","nullable":true,"target":"core.User","array":true}""", FieldValue("reviewers"));
            AssertJson("""{"type":"json","nullable":true}""", FieldValue("tags"));
            Assert.True(ClientEntityModelComposition.Member(action, "fields", "priority").GetProperty("required").GetBoolean());
            Assert.False(ClientEntityModelComposition.Member(action, "fields", "assigneeId").GetProperty("required").GetBoolean());

            JsonElement FieldValue(string field)
            {
                return ClientEntityModelComposition.Member(action, "fields", field).GetProperty("value");
            }
        }

        [Fact]
        public void Api_actions_carry_their_results_permissions_and_bodies()
        {
            JsonElement customer = composition.Entity(ClientModel.Customers);
            JsonElement Action(string name)
            {
                return ClientEntityModelComposition.Member(customer, "actions", name);
            }

            // A body: a member it must carry is required whatever its nullability, by annotation, by
            // the C# required modifier or by the JSON rules, in the schema and the fields alike.
            JsonElement statement = Action("statement");
            Assert.Equal(("api", "Read", "own", false), (
                statement.GetProperty("kind").GetString(),
                statement.GetProperty("action").GetString(),
                statement.GetProperty("result").GetString(),
                statement.GetProperty("mutation").GetBoolean()));
            Assert.Equal(["language", "layout", "channel"], Strings(statement.GetProperty("arguments").GetProperty("required")));
            Assert.All(
                ["language", "layout", "channel"],
                field => Assert.True(ClientEntityModelComposition.Member(statement, "fields", field).GetProperty("required").GetBoolean()));

            // A member-only action names no permission action, and the null is written.
            JsonElement ping = Action("ping");
            Assert.Equal(JsonValueKind.Null, ping.GetProperty("action").ValueKind);
            Assert.False(ping.TryGetProperty("arguments", out _));
            Assert.False(ping.TryGetProperty("fields", out _));

            // A task without a result returns none.
            JsonElement recompute = Action("ranks/recompute");
            Assert.Equal(("none", true), (recompute.GetProperty("result").GetString(), recompute.GetProperty("destructive").GetBoolean()));

            // Allowed-when-read-only is written only when true.
            Assert.False(Action("activate").TryGetProperty("allowWhenReadOnly", out _));
            Assert.False(statement.TryGetProperty("allowWhenReadOnly", out _));
        }

        // --- Enums ---

        [Fact]
        public void Every_enum_is_written_once_in_name_order()
        {
            string[] names = [.. composition.Enums.EnumerateArray().Select(static item => item.GetProperty("name").GetString()!)];

            Assert.Equal(names.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal), names);
            Assert.Equal(composition.Model.Enums.Select(static item => item.Name), names);
            Assert.Contains("acme.Priority", names);
            Assert.DoesNotContain("gl.CustomerType", names);
        }

        [Fact]
        public void A_csharp_enum_carries_its_wire_names_and_a_key_by_member_name_from_every_schema_that_carries_it()
        {
            // The home schema names it; every site's schema adds a key, so gl's declared key wins.
            AssertJson(
                """{"name":"acme.CustomerType","values":[{"value":"Retail","labelKey":"Gl_CustomerType_Retail"},{"value":"wholesale-trade","labelKey":"Acme_CustomerType_Wholesale"}]}""",
                composition.Enum("acme.CustomerType"));
            Assert.Equal("acme.CustomerType", ClientEntityModelComposition.Member(composition.Entity(ClientModel.Centers), "properties", "ServedCustomerType").GetProperty("enum").GetString());

            // An enum only an action's arguments carry is named under the action's schema.
            AssertJson(
                """{"name":"acme.Priority","values":[{"value":"Low","labelKey":"Acme_Priority_Low","labelFallback":"Low"},{"value":"High","labelKey":"Acme_Priority_High","labelFallback":"High"}]}""",
                composition.Enum("acme.Priority"));
        }

        [Fact]
        public void A_value_set_carries_keyed_and_literal_labels()
        {
            AssertJson(
                """{"name":"acme.Tier","values":[{"value":"gold","labelKey":"Acme_Tier_Gold"},{"value":"silver","labelKey":"Acme_Tier_Silver","labelFallback":"silver"},{"value":"VIP","label":"VIP"}]}""",
                composition.Enum(ClientModel.Tiers));
            Assert.Equal(ClientModel.Tiers, ClientEntityModelComposition.Member(composition.Entity(ClientModel.Customers), "properties", "Tier").GetProperty("enum").GetString());
            Assert.Equal("English", composition.Enum("core.Language").GetProperty("values")[0].GetProperty("label").GetString());
        }

        [Fact]
        public void The_time_zones_carry_their_aliases_in_ordinal_order()
        {
            JsonElement zones = composition.Enum("core.TimeZone");
            string[] aliases = [.. zones.GetProperty("aliases").EnumerateObject().Select(static alias => alias.Name)];

            Assert.Equal(aliases.Order(StringComparer.Ordinal), aliases);
            Assert.Equal("Asia/Kolkata", zones.GetProperty("aliases").GetProperty("Asia/Calcutta").GetString());
            Assert.Equal("core.TimeZone", ClientEntityModelComposition.Member(composition.Entity(ClientModel.Customers), "properties", "TimeZone").GetProperty("enum").GetString());
        }

        // --- Declarations ---

        [Fact]
        public void The_declarations_type_every_entity_and_enum()
        {
            string text = composition.Declarations;

            Assert.Contains("import type { TmDecimal, TmJson } from '@tellma/core';\n", text, StringComparison.Ordinal);
            Assert.Contains("export type AcmeCustomerType = 'Retail' | 'wholesale-trade';\n", text, StringComparison.Ordinal);
            Assert.Contains("export interface AcmeCustomer {\n", text, StringComparison.Ordinal);
            Assert.Contains("  creditLimit: TmDecimal;\n", text, StringComparison.Ordinal);
            Assert.Contains("  name2?: string | null;\n", text, StringComparison.Ordinal);
            Assert.Contains("  externalReference?: TmDecimal | null;\n", text, StringComparison.Ordinal);
            Assert.Contains("  notes?: TmJson | null;\n", text, StringComparison.Ordinal);
            Assert.Contains("  contacts?: AcmeCustomerContact[] | null;\n", text, StringComparison.Ordinal);
            Assert.Contains("  servedCustomerType?: AcmeCustomerType | null;\n", text, StringComparison.Ordinal);
            Assert.Contains("declare module '@tellma/core' {\n", text, StringComparison.Ordinal);
            Assert.Contains("    'acme.Customer': AcmeCustomer;\n", text, StringComparison.Ordinal);
            Assert.Contains(
                "      navigations: { CreatedBy: 'core.User'; ModifiedBy: 'core.User'; Currency: 'acme.Currency'; Image: 'core.Blob'; Extra: 'acme.CustomerExtra' };\n"
                + "      children: { Contacts: 'acme.CustomerContact' };\n",
                text,
                StringComparison.Ordinal);
            Assert.Contains("      groups: 'Name';\n", text, StringComparison.Ordinal);
            Assert.DoesNotContain('\r', text);
        }

        [Fact]
        public void The_declarations_refuse_two_names_that_differ_only_by_the_schemas_case()
        {
            InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => EntityTypesWriter.Write(
                [],
                [new ClientEnumMetadata("acme.Kind", [], null), new ClientEnumMetadata("Acme.Kind", [], null)]));

            Assert.Contains("would both be named AcmeKind", error.Message, StringComparison.Ordinal);
        }

        /// <summary>The customer's set-credit-limit action.</summary>
        /// <returns>The action.</returns>
        private JsonElement SetCreditLimit()
        {
            return ClientEntityModelComposition.Member(composition.Entity(ClientModel.Customers), "actions", "set-credit-limit");
        }

        /// <summary>Asserts that JSON holds the expected value, whatever its escaping.</summary>
        /// <param name="expected">The expected JSON.</param>
        /// <param name="actual">The JSON.</param>
        private static void AssertJson([StringSyntax(StringSyntaxAttribute.Json)] string expected, JsonElement actual)
        {
            using var document = JsonDocument.Parse(expected);
            Assert.True(JsonElement.DeepEquals(document.RootElement, actual), $"Expected {expected} but found {actual.GetRawText()}.");
        }

        /// <summary>A JSON array's strings.</summary>
        /// <param name="array">The array.</param>
        /// <returns>The strings.</returns>
        private static string[] Strings(JsonElement array)
        {
            return [.. array.EnumerateArray().Select(static item => item.GetString()!)];
        }
    }
}
