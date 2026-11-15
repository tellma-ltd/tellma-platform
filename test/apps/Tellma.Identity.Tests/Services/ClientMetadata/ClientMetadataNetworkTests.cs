// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using Tellma.Identity.Services.ClientMetadata;

namespace Tellma.Identity.Tests.Services.ClientMetadata
{
    /// <summary>
    ///     The address guard at the socket: which addresses are refused, that every resolved
    ///     address is checked, that only a checked address is ever connected to — so a host that
    ///     answers a second resolution differently cannot swap one in — and that the metadata
    ///     client's own handler goes through it with no proxy and no redirect. No test opens a
    ///     real connection or resolves a real name.
    /// </summary>
    public sealed class ClientMetadataNetworkTests
    {
        [Theory]
        [InlineData("0.0.0.0")]
        [InlineData("10.1.2.3")]
        [InlineData("100.64.0.1")]
        [InlineData("127.0.0.1")]
        [InlineData("127.255.255.254")]
        [InlineData("169.254.169.254")]
        [InlineData("172.16.0.1")]
        [InlineData("172.31.255.255")]
        [InlineData("192.0.0.8")]
        [InlineData("192.0.2.1")]
        [InlineData("192.168.1.1")]
        [InlineData("198.18.0.1")]
        [InlineData("198.51.100.1")]
        [InlineData("203.0.113.1")]
        [InlineData("224.0.0.1")]
        [InlineData("239.255.255.250")]
        [InlineData("255.255.255.255")]
        [InlineData("::")]
        [InlineData("::1")]
        [InlineData("fe80::1")]
        [InlineData("fc00::1")]
        [InlineData("fd12:3456::1")]
        [InlineData("ff02::1")]
        [InlineData("::ffff:127.0.0.1")]
        [InlineData("::ffff:10.0.0.1")]
        [InlineData("64:ff9b::a9fe:a9fe")]
        [InlineData("2002:0a00:0001::1")]
        [InlineData("2001:db8::1")]
        [InlineData("2001::1")]
        public void A_non_public_address_is_refused(string address)
        {
            Assert.False(ClientMetadataAddressPolicy.IsAllowed(IPAddress.Parse(address)));
        }

        [Theory]
        [InlineData("8.8.8.8")]
        [InlineData("93.184.216.34")]
        [InlineData("172.32.0.1")]
        [InlineData("2606:4700::1111")]
        [InlineData("::ffff:8.8.8.8")]
        [InlineData("64:ff9b::808:808")]
        public void A_public_address_is_allowed(string address)
        {
            Assert.True(ClientMetadataAddressPolicy.IsAllowed(IPAddress.Parse(address)));
        }

        [Fact]
        public async Task A_host_resolving_to_any_refused_address_is_never_connected_to()
        {
            // A name that answers with a private address beside a public one is refused
            // outright: connecting to "the public one" would leave the choice to the resolver.
            FakeResolver resolver = new(["93.184.216.34", "10.0.0.7"]);
            FakeSocketConnector sockets = new();
            ClientMetadataConnector connector = new(resolver, sockets);

            await Assert.ThrowsAsync<ClientMetadataAddressException>(async () =>
                await connector.ConnectAsync(new DnsEndPoint("agent.example.com", 443), TestContext.Current.CancellationToken));

            Assert.Empty(sockets.Connected);
        }

        [Fact]
        public async Task A_rebinding_host_cannot_swap_in_an_unchecked_address()
        {
            // The first resolution answers with a public address, the next with loopback — the
            // shape of a DNS rebinding attack. Each connection resolves once and connects to the
            // address it checked, so the second is refused and loopback is never dialed.
            FakeResolver resolver = new(["93.184.216.34"], ["127.0.0.1"]);
            FakeSocketConnector sockets = new();
            ClientMetadataConnector connector = new(resolver, sockets);

            await using (await connector.ConnectAsync(new DnsEndPoint("rebind.example.com", 443), TestContext.Current.CancellationToken))
            {
            }

            await Assert.ThrowsAsync<ClientMetadataAddressException>(async () =>
                await connector.ConnectAsync(new DnsEndPoint("rebind.example.com", 443), TestContext.Current.CancellationToken));

            IPEndPoint connected = Assert.Single(sockets.Connected);
            Assert.Equal(IPAddress.Parse("93.184.216.34"), connected.Address);
            Assert.Equal(443, connected.Port);
            Assert.Equal(2, resolver.Calls);
        }

        [Fact]
        public async Task An_ip_literal_is_refused_without_resolving()
        {
            FakeResolver resolver = new(["93.184.216.34"]);
            FakeSocketConnector sockets = new();
            ClientMetadataConnector connector = new(resolver, sockets);

            await Assert.ThrowsAsync<ClientMetadataAddressException>(async () =>
                await connector.ConnectAsync(new DnsEndPoint("93.184.216.34", 443), TestContext.Current.CancellationToken));

            Assert.Equal(0, resolver.Calls);
            Assert.Empty(sockets.Connected);
        }

        [Fact]
        public async Task A_host_resolving_to_nothing_is_refused()
        {
            ClientMetadataConnector connector = new(new FakeResolver([]), new FakeSocketConnector());

            await Assert.ThrowsAsync<ClientMetadataAddressException>(async () =>
                await connector.ConnectAsync(new DnsEndPoint("nothing.example.com", 443), TestContext.Current.CancellationToken));
        }

        [Fact]
        public async Task An_unreachable_address_falls_through_to_the_next_checked_one()
        {
            FakeResolver resolver = new(["93.184.216.34", "93.184.216.35"]);
            FakeSocketConnector sockets = new() { Refuse = IPAddress.Parse("93.184.216.34") };
            ClientMetadataConnector connector = new(resolver, sockets);

            await using (await connector.ConnectAsync(new DnsEndPoint("agent.example.com", 443), TestContext.Current.CancellationToken))
            {
            }

            Assert.Equal(IPAddress.Parse("93.184.216.35"), Assert.Single(sockets.Connected).Address);
        }

        [Fact]
        public void The_primary_handler_uses_no_proxy_no_redirect_and_no_cookie()
        {
            using SocketsHttpHandler handler = ClientMetadataHttp.CreatePrimaryHandler(
                new ClientMetadataConnector(new FakeResolver([]), new FakeSocketConnector()), TimeSpan.FromSeconds(5));

            Assert.False(handler.UseProxy);
            Assert.Null(handler.Proxy);
            Assert.False(handler.AllowAutoRedirect);
            Assert.False(handler.UseCookies);
            Assert.Equal(DecompressionMethods.None, handler.AutomaticDecompression);
            Assert.NotNull(handler.ConnectCallback);
            Assert.Equal(TimeSpan.FromSeconds(5), handler.ConnectTimeout);
        }

        [Fact]
        public async Task The_handler_refuses_a_private_host_before_any_connection()
        {
            // The real handler, end to end, against a host that resolves to the cloud metadata
            // address: the request fails in the connection step and nothing is dialed.
            FakeSocketConnector sockets = new();
            using SocketsHttpHandler handler = ClientMetadataHttp.CreatePrimaryHandler(
                new ClientMetadataConnector(new FakeResolver(["169.254.169.254"]), sockets), TimeSpan.FromSeconds(5));
            using HttpClient client = new(handler);

            HttpRequestException exception = await Assert.ThrowsAsync<HttpRequestException>(async () =>
                await client.GetAsync(new Uri("https://metadata.example.com/client.json"), TestContext.Current.CancellationToken));

            Assert.IsType<ClientMetadataAddressException>(exception.InnerException);
            Assert.Empty(sockets.Connected);
        }

        /// <summary>A resolver that answers each call with the next configured list, repeating the last.</summary>
        private sealed class FakeResolver(params string[][] answers) : IClientMetadataHostResolver
        {
            /// <summary>How many resolutions were asked for.</summary>
            private int _calls;

            /// <summary>How many resolutions were asked for.</summary>
            public int Calls => _calls;

            /// <inheritdoc />
            public Task<IPAddress[]> ResolveAsync(string host, CancellationToken cancellationToken)
            {
                int call = Interlocked.Increment(ref _calls) - 1;
                string[] answer = answers.Length == 0 ? [] : answers[Math.Min(call, answers.Length - 1)];
                return Task.FromResult(answer.Select(IPAddress.Parse).ToArray());
            }
        }

        /// <summary>Records the endpoints it is asked to connect to and hands back an empty stream.</summary>
        private sealed class FakeSocketConnector : IClientMetadataSocketConnector
        {
            /// <summary>The endpoints connected to.</summary>
            public ConcurrentQueue<IPEndPoint> Connected { get; } = new();

            /// <summary>An address that refuses connections.</summary>
            public IPAddress? Refuse { get; init; }

            /// <inheritdoc />
            public ValueTask<Stream> ConnectAsync(IPEndPoint endpoint, CancellationToken cancellationToken)
            {
                if (endpoint.Address.Equals(Refuse))
                {
                    throw new SocketException((int)SocketError.ConnectionRefused);
                }

                Connected.Enqueue(endpoint);
                return ValueTask.FromResult<Stream>(new MemoryStream());
            }
        }
    }
}
