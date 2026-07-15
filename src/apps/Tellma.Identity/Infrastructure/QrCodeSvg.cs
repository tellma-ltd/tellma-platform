// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Net.Codecrete.QrCodeGenerator;
using System.Text;

namespace Tellma.Identity.Infrastructure
{
    /// <summary>
    ///     Renders a QR code as an inline SVG data URI, server-side and dependency-free on the
    ///     browser, so the TOTP enrollment page needs no client-side QR library and the strict CSP
    ///     (which forbids third-party scripts) holds.
    /// </summary>
    public static class QrCodeSvg
    {
        /// <summary>Builds an <c>image/svg+xml</c> data URI encoding the given text as a QR code.</summary>
        /// <param name="text">The text to encode (for example an <c>otpauth://</c> URI).</param>
        /// <returns>A data URI suitable for an <c>img</c> <c>src</c>.</returns>
        public static string ToSvgDataUri(string text)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(text);

            var qr = QrCode.EncodeText(text, QrCode.Ecc.Medium);
            string svg = ToSvgString(qr, border: 2);
            return "data:image/svg+xml;base64," + Convert.ToBase64String(Encoding.UTF8.GetBytes(svg));
        }

        /// <summary>Serializes a QR code to a minimal, self-contained SVG string.</summary>
        private static string ToSvgString(QrCode qr, int border)
        {
            int size = qr.Size + (border * 2);
            StringBuilder builder = new();
            builder.Append("<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 ").Append(size).Append(' ').Append(size).Append("\">");
            builder.Append("<rect width=\"100%\" height=\"100%\" fill=\"#ffffff\"/>");
            builder.Append("<path d=\"");
            for (int y = 0; y < qr.Size; y++)
            {
                for (int x = 0; x < qr.Size; x++)
                {
                    if (qr.GetModule(x, y))
                    {
                        builder.Append('M').Append(x + border).Append(',').Append(y + border).Append("h1v1h-1z");
                    }
                }
            }

            builder.Append("\" fill=\"#000000\"/></svg>");
            return builder.ToString();
        }
    }
}
