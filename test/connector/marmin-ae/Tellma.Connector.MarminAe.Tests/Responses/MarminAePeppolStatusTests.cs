// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Net;
using System.Text.Json;
using Tellma.Connector.MarminAe.Tests.Infrastructure;

namespace Tellma.Connector.MarminAe.Tests.Responses
{
    /// <summary>The transmission snapshot, which is camel-cased alone among these endpoints.</summary>
    /// <remarks>
    ///     Read <c>Vectors/PROVENANCE.md</c> before treating the snapshot vectors as evidence: no
    ///     document in the sandbox account reaches a transmitted state, so the two-leg shape is
    ///     authored from the vendor's published example rather than captured. What the tests over it
    ///     establish is that the reader survives every plausible shape, not that this is the shape.
    /// </remarks>
    public class MarminAePeppolStatusTests
    {
        [Fact]
        public async Task Reads_the_camel_cased_snapshot_the_vendor_documents()
        {
            MarminAePeppolStatusSnapshot snapshot = await ReadAsync("PeppolStatus/snapshot-transmitted");

            Assert.Equal("INVOICE", snapshot.DocumentType);
            Assert.Equal("bd63aefd-df82-430a-858c-ff77108584fe", snapshot.DocumentUuid);
            Assert.Equal("fb4172e3-7589-4a0a-bfb9-739d06420109", snapshot.Uuid);
            Assert.Equal("0235:marmin-1000000002", snapshot.IssuedBy);
            Assert.Equal("0235:1000000001", snapshot.IssuedTo);

            // The one field no camel-case naming policy produces, which is why every member here
            // spells its wire name out.
            Assert.Equal("PEludm9pY2UvPg==", snapshot.DocumentXml);
            Assert.Equal(
                DateTimeOffset.FromUnixTimeMilliseconds(1775193892692), snapshot.LastUpdated);
            Assert.Empty(snapshot.ValidationResults!);
        }

        [Fact]
        public async Task Reads_the_delivery_leg()
        {
            MarminAePeppolStatusSnapshot snapshot = await ReadAsync("PeppolStatus/snapshot-transmitted");

            MarminAePeppolLeg leg = Assert.IsType<MarminAePeppolLeg>(snapshot.ToC3);
            Assert.Equal("AE", leg.Country);
            Assert.Equal("urn:peppol:pint:billing-1@ae-1", leg.DocumentTypeId);
            Assert.Equal("urn:peppol:bis:billing", leg.ProcessId);
            Assert.Equal("MLS_C3_TRANSMISSION_ERROR", leg.Status);
            Assert.Equal("4ff91b61-d731-458b-bd6e-21cf138c8a1c", leg.TransmissionUuid);
            Assert.Equal(
                DateTimeOffset.FromUnixTimeMilliseconds(1775193891678), leg.LastUpdated);
            Assert.Null(leg.MlsResponse?.ValueKind == JsonValueKind.Null ? null : leg.MlsResponse);
        }

        [Fact]
        public async Task Reads_the_reporting_leg_and_what_the_transport_said()
        {
            MarminAePeppolStatusSnapshot snapshot = await ReadAsync("PeppolStatus/snapshot-transmitted");

            MarminAePeppolLeg leg = Assert.IsType<MarminAePeppolLeg>(snapshot.ToC5);
            Assert.Equal("urn:peppol:taxreporting", leg.ProcessId);
            Assert.Equal("SUBMITTED", leg.Status);

            MarminAePeppolTransmissionResponse transmission =
                Assert.IsType<MarminAePeppolTransmissionResponse>(leg.TransmissionResponse);
            Assert.Equal("phase4@Conv5373778413757239022", transmission.ConversationId);
            Assert.Equal("1eb68454-7cc1-4bac-801f-536da5d1d05a@phase4", transmission.MessageId);
            Assert.Equal(2035, transmission.OverallDurationToTransmitMilliseconds);
            Assert.False(transmission.TransmissionError);
            Assert.Equal("SUCCESS", transmission.TransmissionResult);
            Assert.Null(transmission.TransmissionErrors);
        }

        [Fact]
        public async Task Reads_a_snapshot_with_one_leg_missing_and_a_status_it_has_never_seen()
        {
            MarminAePeppolStatusSnapshot snapshot = await ReadAsync("PeppolStatus/snapshot-one-leg-absent");

            Assert.Null(snapshot.ToC3);
            Assert.Equal("A_STATUS_THIS_CLIENT_HAS_NEVER_SEEN", snapshot.ToC5?.Status);
            Assert.Null(snapshot.ToC5?.TransmissionResponse);

            JsonElement objection = Assert.Single(snapshot.ValidationResults!);
            Assert.Equal("IBR-108-AE", objection.GetProperty("reasonCode").GetString());
            Assert.Equal("fatal", objection.GetProperty("flag").GetString());
        }

        [Fact]
        public async Task Reads_a_snapshot_that_is_nothing_but_an_empty_object()
        {
            using var harness = MarminAeHarness.Create();
            harness.Enqueue(MarminAeResponses.Json(HttpStatusCode.OK, "{}"));

            MarminAeResponse<MarminAePeppolStatusSnapshot> response = await harness.Client.GetPeppolStatusAsync(
                MarminAeDocumentKind.SalesInvoice,
                MarminAeOperations.DocumentId,
                TestContext.Current.CancellationToken);

            Assert.Null(response.Value.ToC3);
            Assert.Null(response.Value.LastUpdated);
        }

        [Fact]
        public async Task Surfaces_the_vendors_own_words_when_there_is_no_snapshot_yet()
        {
            // The recorded answer for a document the network has not started carrying. It is a
            // refusal rather than an empty snapshot, and the client is responsible for saying so.
            Vector vector = Vectors.Load("PeppolStatus/status-not-ready");

            using var harness = MarminAeHarness.Create();
            harness.Enqueue(MarminAeResponses.Json(HttpStatusCode.NotFound, vector.Content));

            MarminAeRequestException exception = await Assert.ThrowsAsync<MarminAeRequestException>(
                () => harness.Client.GetPeppolStatusAsync(
                    MarminAeDocumentKind.SalesInvoice,
                    MarminAeOperations.DocumentId,
                    TestContext.Current.CancellationToken));

            Assert.Equal(404, exception.StatusCode);
            Assert.Equal("Peppol status not available yet", exception.Detail?.Message);
        }

        [Fact]
        public void Ignores_snapshot_fields_it_has_never_seen()
        {
            const string body = """{"documentType":"INVOICE","toC7":{"status":"NEW_LEG"},"newField":1}""";

            MarminAePeppolStatusSnapshot snapshot = JsonSerializer.Deserialize(
                body, MarminAeJsonContext.Default.MarminAePeppolStatusSnapshot)!;

            Assert.Equal("INVOICE", snapshot.DocumentType);
        }

        private static async Task<MarminAePeppolStatusSnapshot> ReadAsync(string logicalName)
        {
            Vector vector = Vectors.Load(logicalName);

            using var harness = MarminAeHarness.Create();
            harness.Enqueue(MarminAeResponses.Json(HttpStatusCode.OK, vector.Content));

            MarminAeResponse<MarminAePeppolStatusSnapshot> response = await harness.Client.GetPeppolStatusAsync(
                MarminAeDocumentKind.SalesInvoice,
                MarminAeOperations.DocumentId,
                TestContext.Current.CancellationToken);

            Assert.Equal(200, response.StatusCode);

            return response.Value;
        }
    }

    /// <summary>The transmission log, which is snake-cased and arrives as a bare array.</summary>
    public class MarminAePeppolStatusLogTests
    {
        [Fact]
        public async Task Reads_the_log_the_vendor_returns_for_a_freshly_accepted_document()
        {
            IReadOnlyList<MarminAePeppolStatusLogEntry> log = await ReadAsync("PeppolStatus/status-logs");

            MarminAePeppolStatusLogEntry entry = Assert.Single(log);
            Assert.Equal("DOCUMENT_CREATED", entry.Event);
            Assert.Equal("Document created successfully", entry.Message);
            Assert.Equal("INVOICE", entry.DocumentType);

            // The identifiers change every time the capture is refreshed, so what is asserted is
            // that they were read at all and that the timestamp is milliseconds rather than seconds.
            Assert.True(Guid.TryParse(entry.DocumentUuid, out _));
            Assert.NotNull(entry.TimestampMilliseconds);
            Assert.Equal(
                DateTimeOffset.FromUnixTimeMilliseconds(entry.TimestampMilliseconds.Value), entry.Timestamp);
            Assert.InRange(
                entry.Timestamp!.Value,
                new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2100, 1, 1, 0, 0, 0, TimeSpan.Zero));
            Assert.Null(entry.SentMessageId);
        }

        [Fact]
        public async Task Reads_an_empty_log_as_an_empty_list()
        {
            IReadOnlyList<MarminAePeppolStatusLogEntry> log = await ReadAsync("PeppolStatus/status-logs-empty");

            Assert.Empty(log);
        }

        [Fact]
        public async Task Preserves_the_order_the_vendor_sent_the_log_in()
        {
            const string body = /*lang=json,strict*/ """
                [{"event":"DOCUMENT_CREATED","timestamp":1},
                 {"event":"DOCUMENT_SUBMITTED","timestamp":2},
                 {"event":"A_STEP_THIS_CLIENT_HAS_NEVER_SEEN","timestamp":3}]
                """;

            using var harness = MarminAeHarness.Create();
            harness.Enqueue(MarminAeResponses.Json(HttpStatusCode.OK, body));

            MarminAeResponse<IReadOnlyList<MarminAePeppolStatusLogEntry>> response =
                await harness.Client.GetPeppolStatusLogsAsync(
                    MarminAeDocumentKind.SalesCreditNote,
                    MarminAeOperations.DocumentId,
                    TestContext.Current.CancellationToken);

            Assert.Equal(
                ["DOCUMENT_CREATED", "DOCUMENT_SUBMITTED", "A_STEP_THIS_CLIENT_HAS_NEVER_SEEN"],
                response.Value.Select(static entry => entry.Event));
        }

        [Fact]
        public async Task Refuses_a_log_that_is_not_an_array()
        {
            using var harness = MarminAeHarness.Create();
            harness.Enqueue(MarminAeResponses.Json(HttpStatusCode.OK, /*lang=json,strict*/ """{"not":"an array"}"""));

            MarminAeRequestException exception = await Assert.ThrowsAsync<MarminAeRequestException>(
                () => harness.Client.GetPeppolStatusLogsAsync(
                    MarminAeDocumentKind.SalesInvoice,
                    MarminAeOperations.DocumentId,
                    TestContext.Current.CancellationToken));

            Assert.IsAssignableFrom<JsonException>(exception.InnerException);
        }

        private static async Task<IReadOnlyList<MarminAePeppolStatusLogEntry>> ReadAsync(string logicalName)
        {
            Vector vector = Vectors.Load(logicalName);

            using var harness = MarminAeHarness.Create();
            harness.Enqueue(MarminAeResponses.Json(HttpStatusCode.OK, vector.Content));

            MarminAeResponse<IReadOnlyList<MarminAePeppolStatusLogEntry>> response =
                await harness.Client.GetPeppolStatusLogsAsync(
                    MarminAeDocumentKind.SalesInvoice,
                    MarminAeOperations.DocumentId,
                    TestContext.Current.CancellationToken);

            Assert.Equal(200, response.StatusCode);

            return response.Value;
        }
    }
}
