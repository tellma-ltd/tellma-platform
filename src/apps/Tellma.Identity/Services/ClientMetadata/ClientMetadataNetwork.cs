// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Net;
using System.Net.Sockets;

namespace Tellma.Identity.Services.ClientMetadata
{
    /// <summary>
    ///     Resolves the host of a metadata URL to its addresses. The fetch connects only to an
    ///     address this resolution returned, after checking every one of them.
    /// </summary>
    public interface IClientMetadataHostResolver
    {
        /// <summary>Resolves a host name.</summary>
        /// <param name="host">The host name, never an IP literal.</param>
        /// <param name="cancellationToken">Aborts the resolution.</param>
        /// <returns>Every address the name resolves to.</returns>
        Task<IPAddress[]> ResolveAsync(string host, CancellationToken cancellationToken);
    }

    /// <summary>Opens the TCP connection a metadata fetch runs over.</summary>
    public interface IClientMetadataSocketConnector
    {
        /// <summary>Connects to one checked address.</summary>
        /// <param name="endpoint">The address and port, already checked by the address policy.</param>
        /// <param name="cancellationToken">Aborts the connection attempt.</param>
        /// <returns>The connected stream, which the HTTP handler owns from then on.</returns>
        ValueTask<Stream> ConnectAsync(IPEndPoint endpoint, CancellationToken cancellationToken);
    }

    /// <summary>The system resolver.</summary>
    public sealed class DnsClientMetadataHostResolver : IClientMetadataHostResolver
    {
        /// <inheritdoc />
        public Task<IPAddress[]> ResolveAsync(string host, CancellationToken cancellationToken)
        {
            return Dns.GetHostAddressesAsync(host, cancellationToken);
        }
    }

    /// <summary>Connects a TCP socket and wraps it in a stream that owns it.</summary>
    public sealed class TcpClientMetadataSocketConnector : IClientMetadataSocketConnector
    {
        /// <inheritdoc />
        public async ValueTask<Stream> ConnectAsync(IPEndPoint endpoint, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(endpoint);

            Socket socket = new(endpoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            try
            {
                await socket.ConnectAsync(endpoint, cancellationToken);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        }
    }

    /// <summary>
    ///     The metadata client's connection step (its handler's <c>ConnectCallback</c>): resolve
    ///     the host once, refuse the connection unless every address it resolved to is allowed,
    ///     then connect only to one of those addresses. Because the handler connects to an
    ///     address this step checked, never to a name, a second resolution that answers
    ///     differently — DNS rebinding — cannot swap in an unchecked address.
    /// </summary>
    /// <param name="resolver">Resolves the host.</param>
    /// <param name="socketConnector">Opens the connection to a checked address.</param>
    public sealed class ClientMetadataConnector(IClientMetadataHostResolver resolver, IClientMetadataSocketConnector socketConnector)
    {
        /// <summary>Opens a connection to a metadata host.</summary>
        /// <param name="endpoint">The host and port the request is for.</param>
        /// <param name="cancellationToken">Aborts the connection.</param>
        /// <returns>The connected stream.</returns>
        /// <exception cref="ClientMetadataAddressException">The host is an IP literal, resolves to nothing, or resolves to a refused address.</exception>
        public async ValueTask<Stream> ConnectAsync(DnsEndPoint endpoint, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(endpoint);

            // The URL guard already refuses an IP literal; this is the backstop at the socket.
            if (IPAddress.TryParse(endpoint.Host, out _))
            {
                throw new ClientMetadataAddressException($"The host '{endpoint.Host}' is an IP literal.");
            }

            IPAddress[] addresses = await resolver.ResolveAsync(endpoint.Host, cancellationToken);
            if (addresses.Length == 0)
            {
                throw new ClientMetadataAddressException($"The host '{endpoint.Host}' resolves to no address.");
            }

            // Every address is checked, not only the one connected to: a name that answers with a
            // private address beside a public one is refused outright.
            foreach (IPAddress address in addresses)
            {
                if (!ClientMetadataAddressPolicy.IsAllowed(address))
                {
                    throw new ClientMetadataAddressException(
                        $"The host '{endpoint.Host}' resolves to the refused address {address}.");
                }
            }

            // Connect to the checked addresses in the resolver's order, as the handler itself
            // would, so a host with one unreachable address still answers on another.
            List<Exception> failures = [];
            foreach (IPAddress address in addresses)
            {
                try
                {
                    return await socketConnector.ConnectAsync(new IPEndPoint(address, endpoint.Port), cancellationToken);
                }
                catch (Exception exception) when (exception is SocketException or IOException)
                {
                    failures.Add(exception);
                }
            }

            throw new AggregateException($"No address of the host '{endpoint.Host}' accepted a connection.", failures);
        }
    }

    /// <summary>The metadata client refused to connect: the host's addresses failed the address policy.</summary>
    public sealed class ClientMetadataAddressException : Exception
    {
        /// <summary>Creates the exception.</summary>
        public ClientMetadataAddressException()
            : base("The metadata host's address is refused.")
        {
        }

        /// <summary>Creates the exception with a message.</summary>
        /// <param name="message">Which host and address were refused.</param>
        public ClientMetadataAddressException(string message)
            : base(message)
        {
        }

        /// <summary>Creates the exception with a message and an inner exception.</summary>
        /// <param name="message">Which host and address were refused.</param>
        /// <param name="innerException">The underlying failure.</param>
        public ClientMetadataAddressException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}
