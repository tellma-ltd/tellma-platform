// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Identity.Services.ClientMetadata;

namespace Tellma.Identity.Tests.Services.ClientMetadata
{
    /// <summary>
    ///     Which client ids are document clients, and which of those the engine refuses before any
    ///     fetch: only an https URL with a host and no fragment is recognized, so every other id is
    ///     looked up as a registered client exactly as before.
    /// </summary>
    public sealed class ClientIdUrlTests
    {
        [Theory]
        [InlineData("https://agent.example.com/client.json")]
        [InlineData("https://agent.example.com")]
        [InlineData("https://agent.example.com:8443/c")]
        public void An_https_url_with_a_host_is_a_document_client(string clientId)
        {
            Assert.True(ClientIdUrl.IsDocumentClientId(clientId));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("acme")]
        [InlineData("tellma-cli")]
        [InlineData("http://agent.example.com/client.json")]
        [InlineData("https://agent.example.com/client.json#frag")]
        [InlineData("https://agent.example.com/client.json#")]
        [InlineData("urn:example:client")]
        [InlineData("/relative/client.json")]
        public void Anything_else_is_a_registered_client_id(string? clientId)
        {
            Assert.False(ClientIdUrl.IsDocumentClientId(clientId));
        }

        [Fact]
        public void A_canonical_client_id_of_at_most_100_characters_is_accepted()
        {
            string clientId = "https://agent.example.com/" + new string('a', ClientIdUrl.MaxLength - "https://agent.example.com/".Length);
            Assert.Equal(ClientIdUrl.MaxLength, clientId.Length);
            Assert.True(ClientIdUrl.TryRecognize(clientId, out Uri? url));

            Assert.Null(ClientIdUrl.FindProblem(url));
        }

        [Fact]
        public void A_client_id_longer_than_the_column_is_refused()
        {
            // The applications table's ClientId holds 100 characters; a longer id could never be
            // stored, so it is refused before anything is fetched.
            string clientId = "https://agent.example.com/" + new string('a', ClientIdUrl.MaxLength);
            Assert.True(ClientIdUrl.TryRecognize(clientId, out Uri? url));

            Assert.Equal(ClientMetadataRejectionReasons.ClientIdTooLong, ClientIdUrl.FindProblem(url));
        }

        [Theory]
        [InlineData("https://127.0.0.1/client.json")]
        [InlineData("https://10.0.0.5/client.json")]
        [InlineData("https://[::1]/client.json")]
        [InlineData("https://[2001:4860::8888]/client.json")]
        [InlineData("https://2130706433/client.json")]
        [InlineData("https://0x7f.1/client.json")]
        [InlineData("https://017700000001/client.json")]
        [InlineData("https://127.1/client.json")]
        public void An_ip_literal_host_is_refused_by_the_address_guard(string clientId)
        {
            Assert.True(ClientIdUrl.TryRecognize(clientId, out Uri? url));

            Assert.Equal(ClientMetadataRejectionReasons.Address, ClientIdUrl.FindProblem(url));
        }

        [Theory]
        [InlineData("https://Agent.Example.com/client.json")]
        [InlineData("HTTPS://agent.example.com/client.json")]
        [InlineData("https://agent.example.com:443/client.json")]
        [InlineData("https://agent.example.com/a/../client.json")]
        [InlineData("https://agent.example.com/a/%2e%2e/client.json")]
        [InlineData("https://agent.example.com/./client.json")]
        [InlineData("https://user@agent.example.com/client.json")]
        [InlineData("https://bücher.example/client.json")]
        [InlineData("https://agent.example.com./client.json")]
        [InlineData("https://agent.example.com/%63lient.json")]
        [InlineData("https://agent.example.com/client.json?%61=1")]
        public void A_client_id_in_a_non_canonical_spelling_is_refused(string clientId)
        {
            // Two spellings of one URL would name one document, and under the store's
            // case-insensitive collation one row; only the canonical spelling is accepted.
            Assert.True(ClientIdUrl.TryRecognize(clientId, out Uri? url));

            Assert.Equal(ClientMetadataRejectionReasons.ClientId, ClientIdUrl.FindProblem(url));
        }

        [Theory]
        [InlineData("https://agent.example.com/c", "agent.example.com")]
        [InlineData("https://agent.example.com:8443/c", "agent.example.com:8443")]
        public void The_display_host_keeps_a_non_default_port(string clientId, string host)
        {
            Assert.Equal(host, ClientIdUrl.DisplayHost(new Uri(clientId)));
        }
    }
}
