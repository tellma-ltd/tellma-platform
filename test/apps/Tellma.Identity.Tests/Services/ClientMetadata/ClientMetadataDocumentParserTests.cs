// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.IdentityModel.Tokens;
using System.Text;
using System.Text.Json.Nodes;
using Tellma.Identity.Services.ClientMetadata;

namespace Tellma.Identity.Tests.Services.ClientMetadata
{
    /// <summary>
    ///     Document and key-set validation vectors: what a document must say about itself, its
    ///     redirect URIs, its authentication method, its keys and its grants, and the reason each
    ///     refusal carries.
    /// </summary>
    public sealed class ClientMetadataDocumentParserTests
    {
        private static readonly Uri ClientId = new(MetadataDocuments.ClientId);

        [Fact]
        public void A_valid_public_document_is_read()
        {
            ParsedClientMetadata parsed = Parse(MetadataDocuments.Public());

            Assert.Equal("Example Agent", parsed.ClientName);
            Assert.Equal([new Uri(MetadataDocuments.LoopbackRedirect)], parsed.RedirectUris);
            Assert.Equal(ClientMetadataDocumentParser.AuthMethodNone, parsed.TokenEndpointAuthMethod);
            Assert.Null(parsed.JwksUri);
        }

        [Fact]
        public void A_valid_private_key_jwt_document_is_read()
        {
            ParsedClientMetadata parsed = Parse(MetadataDocuments.PrivateKeyJwt());

            Assert.Equal(ClientMetadataDocumentParser.AuthMethodPrivateKeyJwt, parsed.TokenEndpointAuthMethod);
            Assert.Equal(new Uri(MetadataDocuments.JwksUri), parsed.JwksUri);
        }

        [Fact]
        public void Optional_members_may_be_absent()
        {
            JsonObject document = MetadataDocuments.Public();
            document.Remove("client_name");
            document.Remove("grant_types");

            ParsedClientMetadata parsed = Parse(document);

            Assert.Null(parsed.ClientName);
        }

        [Theory]
        [InlineData("https://agent.example.com/oauth/other.json")]
        [InlineData("https://agent.example.com/oauth/client.json/")]
        [InlineData("HTTPS://agent.example.com/oauth/client.json")]
        public void A_document_naming_another_client_id_is_refused(string documentClientId)
        {
            JsonObject document = MetadataDocuments.Public();
            document["client_id"] = documentClientId;

            AssertRefused(document, ClientMetadataRejectionReasons.ClientIdMismatch);
        }

        [Fact]
        public void A_document_without_a_client_id_is_refused()
        {
            JsonObject document = MetadataDocuments.Public();
            document.Remove("client_id");

            AssertRefused(document, ClientMetadataRejectionReasons.ClientIdMismatch);
        }

        [Theory]
        [InlineData("not json")]
        [InlineData("[]")]
        [InlineData("\"https://agent.example.com/oauth/client.json\"")]
        [InlineData(/*lang=json*/ "{\"client_id\": \"x\", \"client_id\": \"y\"}")]
        [InlineData(/*lang=json*/ "{\"client_id\": \"x\",}")]
        public void A_body_that_is_not_one_unambiguous_json_object_is_refused(string body)
        {
            ClientMetadataException exception = Assert.Throws<ClientMetadataException>(() =>
                ClientMetadataDocumentParser.ParseDocument(ClientId, Encoding.UTF8.GetBytes(body)));
            Assert.Equal(ClientMetadataRejectionReasons.Document, exception.Reason);
        }

        [Fact]
        public void A_repeated_member_inside_a_nested_object_is_refused()
        {
            string body = MetadataDocuments.Public().ToJsonString().TrimEnd('}') + ",\"extra\":{\"a\":1,\"a\":2}}";

            ClientMetadataException exception = Assert.Throws<ClientMetadataException>(() =>
                ClientMetadataDocumentParser.ParseDocument(ClientId, Encoding.UTF8.GetBytes(body)));
            Assert.Equal(ClientMetadataRejectionReasons.Document, exception.Reason);
        }

        [Fact]
        public void A_document_carrying_a_secret_is_refused()
        {
            JsonObject document = MetadataDocuments.Public();
            document["client_secret"] = "s3cret";

            AssertRefused(document, ClientMetadataRejectionReasons.Document);
        }

        [Theory]
        [InlineData("[]")]
        [InlineData("\"http://127.0.0.1/callback\"")]
        [InlineData("[\"/callback\"]")]
        [InlineData("[\"callback\"]")]
        [InlineData("[42]")]
        [InlineData("[\"https://agent.example.com/cb#x\"]")]
        [InlineData("[\"https://agent.example.com/cb#\"]")]
        [InlineData("[\"https://user@agent.example.com/cb\"]")]
        [InlineData("[\"https://agent.example.com/cb?iss=x\"]")]
        [InlineData("[\"http://agent.example.com/cb\"]")]
        [InlineData("[\"javascript:alert(1)\"]")]
        [InlineData("[\"data:text/html,x\"]")]
        [InlineData("[\"file:///etc/passwd\"]")]
        [InlineData("[\" https://agent.example.com/cb\"]")]
        [InlineData("[\"https://agent.example.com/c b\"]")]
        public void An_unacceptable_redirect_uri_list_is_refused(string redirectUris)
        {
            JsonObject document = MetadataDocuments.Public();
            document["redirect_uris"] = JsonNode.Parse(redirectUris);

            AssertRefused(document, ClientMetadataRejectionReasons.RedirectUris);
        }

        [Theory]
        [InlineData("http://127.0.0.1/callback")]
        [InlineData("http://127.0.0.1:8400/callback")]
        [InlineData("http://[::1]/callback")]
        [InlineData("http://localhost/callback")]
        [InlineData("https://agent.example.com/callback?x=1")]
        [InlineData("com.example.agent:/oauth/callback")]
        public void An_acceptable_redirect_uri_is_kept_as_written(string redirectUri)
        {
            JsonObject document = MetadataDocuments.Public();
            document["redirect_uris"] = new JsonArray(redirectUri, redirectUri);

            ParsedClientMetadata parsed = Parse(document);

            Assert.Equal(redirectUri, Assert.Single(parsed.RedirectUris).OriginalString);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("client_secret_basic")]
        [InlineData("client_secret_post")]
        [InlineData("client_secret_jwt")]
        [InlineData("tls_client_auth")]
        [InlineData("NONE")]
        public void An_authentication_method_other_than_none_or_private_key_jwt_is_refused(string? method)
        {
            // An absent method defaults to client_secret_basic, a secret no document client holds.
            JsonObject document = MetadataDocuments.Public();
            document["token_endpoint_auth_method"] = method;
            if (method is null)
            {
                document.Remove("token_endpoint_auth_method");
            }

            AssertRefused(document, ClientMetadataRejectionReasons.AuthMethod);
        }

        [Fact]
        public void An_inline_key_set_is_refused()
        {
            JsonObject document = MetadataDocuments.PrivateKeyJwt();
            document["jwks"] = JsonNode.Parse(MetadataDocuments.KeySet(MetadataDocuments.RsaKey()));

            AssertRefused(document, ClientMetadataRejectionReasons.InlineJwks);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("https://keys.example.com/jwks.json")]
        [InlineData("https://agent.example.com:8443/jwks.json")]
        [InlineData("http://agent.example.com/jwks.json")]
        [InlineData("https://evil.agent.example.com/jwks.json")]
        [InlineData("/oauth/jwks.json")]
        [InlineData("https://agent.example.com/jwks.json#k")]
        [InlineData("https://user@agent.example.com/jwks.json")]
        public void A_private_key_jwt_key_set_off_the_documents_origin_is_refused(string? jwksUri)
        {
            JsonObject document = MetadataDocuments.PrivateKeyJwt();
            document["jwks_uri"] = jwksUri;
            if (jwksUri is null)
            {
                document.Remove("jwks_uri");
            }

            AssertRefused(document, ClientMetadataRejectionReasons.JwksUri);
        }

        [Fact]
        public void A_key_set_url_on_a_public_client_is_refused()
        {
            JsonObject document = MetadataDocuments.Public();
            document["jwks_uri"] = MetadataDocuments.JwksUri;

            AssertRefused(document, ClientMetadataRejectionReasons.JwksUri);
        }

        [Theory]
        [InlineData("[\"client_credentials\"]")]
        [InlineData("[\"authorization_code\", \"implicit\"]")]
        [InlineData("[\"authorization_code\", \"urn:ietf:params:oauth:grant-type:device_code\"]")]
        [InlineData("[\"refresh_token\"]")]
        [InlineData("[]")]
        [InlineData("\"authorization_code\"")]
        public void Grant_types_beyond_the_code_flow_are_refused(string grantTypes)
        {
            JsonObject document = MetadataDocuments.Public();
            document["grant_types"] = JsonNode.Parse(grantTypes);

            AssertRefused(document, ClientMetadataRejectionReasons.GrantTypes);
        }

        [Fact]
        public void The_code_grant_alone_is_accepted()
        {
            JsonObject document = MetadataDocuments.Public();
            document["grant_types"] = new JsonArray("authorization_code");

            Assert.NotNull(Parse(document));
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("Agent\u0007")]
        [InlineData("Agent‮txt.exe")]
        [InlineData("Agent​")]
        public void A_client_name_that_cannot_be_shown_safely_is_refused(string name)
        {
            JsonObject document = MetadataDocuments.Public();
            document["client_name"] = name;

            AssertRefused(document, ClientMetadataRejectionReasons.ClientName);
        }

        [Fact]
        public void A_client_name_longer_than_the_limit_is_refused()
        {
            JsonObject document = MetadataDocuments.Public();
            document["client_name"] = new string('a', ClientMetadataDocumentParser.MaxClientNameLength + 1);

            AssertRefused(document, ClientMetadataRejectionReasons.ClientName);
        }

        [Fact]
        public void A_valid_key_set_is_read()
        {
            JsonWebKeySet keys = ParseKeySet(MetadataDocuments.KeySet(MetadataDocuments.RsaKey(), MetadataDocuments.RsaKey(kid: "k2")));

            Assert.Equal(2, keys.Keys.Count);
        }

        [Fact]
        public void An_ec_key_on_an_accepted_curve_is_read()
        {
            using var ec = System.Security.Cryptography.ECDsa.Create(System.Security.Cryptography.ECCurve.NamedCurves.nistP256);
            System.Security.Cryptography.ECParameters parameters = ec.ExportParameters(includePrivateParameters: false);
            JsonObject key = new()
            {
                ["kty"] = "EC",
                ["crv"] = "P-256",
                ["x"] = Base64UrlEncoder.Encode(parameters.Q.X),
                ["y"] = Base64UrlEncoder.Encode(parameters.Q.Y),
            };

            Assert.Single(ParseKeySet(MetadataDocuments.KeySet(key)).Keys);
        }

        [Theory]
        [InlineData("{}")]
        [InlineData(/*lang=json*/ "{\"keys\": []}")]
        [InlineData(/*lang=json*/ "{\"keys\": {}}")]
        [InlineData(/*lang=json*/ "{\"keys\": [42]}")]
        [InlineData(/*lang=json*/ "{\"keys\": [{\"kty\": \"oct\", \"k\": \"c2VjcmV0\"}]}")]
        [InlineData(/*lang=json*/ "{\"keys\": [{\"kty\": \"RSA\", \"e\": \"AQAB\"}]}")]
        [InlineData(/*lang=json*/ "{\"keys\": [{\"kty\": \"EC\", \"crv\": \"P-192\", \"x\": \"AA\", \"y\": \"AA\"}]}")]
        [InlineData(/*lang=json*/ "{\"keys\": [{\"kty\": \"OKP\", \"crv\": \"Ed25519\", \"x\": \"AA\"}]}")]
        [InlineData("not json")]
        public void A_key_set_without_a_usable_public_signing_key_is_refused(string body)
        {
            AssertKeySetRefused(body);
        }

        [Fact]
        public void A_short_rsa_key_is_refused()
        {
            AssertKeySetRefused(MetadataDocuments.KeySet(MetadataDocuments.RsaKey(bits: 1024)));
        }

        [Theory]
        [InlineData("d")]
        [InlineData("p")]
        [InlineData("q")]
        [InlineData("dp")]
        [InlineData("dq")]
        [InlineData("qi")]
        public void A_key_carrying_private_material_is_refused(string member)
        {
            JsonObject key = MetadataDocuments.RsaKey();
            key[member] = "AQAB";

            AssertKeySetRefused(MetadataDocuments.KeySet(key));
        }

        [Fact]
        public void An_encryption_key_is_refused()
        {
            JsonObject key = MetadataDocuments.RsaKey();
            key["use"] = "enc";

            AssertKeySetRefused(MetadataDocuments.KeySet(key));
        }

        /// <summary>Parses a document for <see cref="ClientId" />.</summary>
        private static ParsedClientMetadata Parse(JsonObject document)
        {
            return ClientMetadataDocumentParser.ParseDocument(ClientId, Encoding.UTF8.GetBytes(document.ToJsonString()));
        }

        /// <summary>Parses a key set.</summary>
        private static JsonWebKeySet ParseKeySet(string body)
        {
            return ClientMetadataDocumentParser.ParseKeySet(Encoding.UTF8.GetBytes(body));
        }

        /// <summary>Asserts a document is refused under a reason.</summary>
        private static void AssertRefused(JsonObject document, string reason)
        {
            ClientMetadataException exception = Assert.Throws<ClientMetadataException>(() => Parse(document));
            Assert.Equal(reason, exception.Reason);
        }

        /// <summary>Asserts a key set is refused.</summary>
        private static void AssertKeySetRefused(string body)
        {
            ClientMetadataException exception = Assert.Throws<ClientMetadataException>(() => ParseKeySet(body));
            Assert.Equal(ClientMetadataRejectionReasons.KeySet, exception.Reason);
        }
    }
}
