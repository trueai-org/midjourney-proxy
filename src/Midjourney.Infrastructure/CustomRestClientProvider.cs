// Midjourney Proxy - Proxy for Midjourney's Discord, enabling AI drawings via API with one-click face swap. A free, non-profit drawing API project.
// Copyright (C) 2024 trueai.org

// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.

// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <https://www.gnu.org/licenses/>.

// Additional Terms:
// This software shall not be used for any illegal activities. 
// Users must comply with all applicable laws and regulations,
// particularly those related to image and video processing. 
// The use of this software for any form of illegal face swapping,
// invasion of privacy, or any other unlawful purposes is strictly prohibited. 
// Violation of these terms may result in termination of the license and may subject the violator to legal action.
using Discord.Net.Rest;
using Newtonsoft.Json;
using System.Collections.Immutable;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;

namespace Midjourney.Infrastructure
{
    public static class CustomRestClientProvider
    {
        public static readonly RestClientProvider Instance = Create();

        /// <exception cref="PlatformNotSupportedException">The default RestClientProvider is not supported on this platform.</exception>
        public static RestClientProvider Create(IWebProxy proxy = null, bool useProxy = false)
        {
            return url =>
            {
                try
                {
                    return new CustomRestClient(url, proxy, useProxy);
                }
                catch (PlatformNotSupportedException ex)
                {
                    throw new PlatformNotSupportedException("The default RestClientProvider is not supported on this platform.", ex);
                }
            };
        }
    }

    public class CustomRestClient : IRestClient, IDisposable
    {
        private const int HR_SECURECHANNELFAILED = -2146233079;

        private readonly HttpClient _client;
        private readonly string _baseUrl;
        private readonly JsonSerializer _errorDeserializer;
        private CancellationToken _cancelToken;
        private bool _isDisposed;

        public CustomRestClient(string baseUrl, IWebProxy proxy = null, bool useProxy = false)
        {
            _baseUrl = baseUrl;

#pragma warning disable IDISP014
            _client = new HttpClient(new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
                UseCookies = false,
                UseProxy = useProxy,
                Proxy = proxy
            });
#pragma warning restore IDISP014
            SetHeader("accept-encoding", "gzip, deflate");

            _cancelToken = CancellationToken.None;
            _errorDeserializer = new JsonSerializer();
        }

        private void Dispose(bool disposing)
        {
            if (!_isDisposed)
            {
                if (disposing)
                    _client.Dispose();
                _isDisposed = true;
            }
        }

        public void Dispose()
        {
            Dispose(true);
        }

        public void SetHeader(string key, string value)
        {
            _client.DefaultRequestHeaders.Remove(key);
            if (value != null)
                _client.DefaultRequestHeaders.Add(key, value);
        }

        public void SetCancelToken(CancellationToken cancelToken)
        {
            _cancelToken = cancelToken;
        }

        public async Task<RestResponse> SendAsync(string method, string endpoint, CancellationToken cancelToken, bool headerOnly, string reason = null,
            IEnumerable<KeyValuePair<string, IEnumerable<string>>> requestHeaders = null)
        {
            string uri = Path.Combine(_baseUrl, endpoint);
            using (var restRequest = new HttpRequestMessage(GetMethod(method), uri))
            {
                if (reason != null)
                    restRequest.Headers.Add("X-Audit-Log-Reason", Uri.EscapeDataString(reason));
                if (requestHeaders != null)
                    foreach (var header in requestHeaders)
                        restRequest.Headers.Add(header.Key, header.Value);
                return await SendInternalAsync(restRequest, cancelToken, headerOnly).ConfigureAwait(false);
            }
        }

        public async Task<RestResponse> SendAsync(string method, string endpoint, string json, CancellationToken cancelToken, bool headerOnly, string reason = null,
            IEnumerable<KeyValuePair<string, IEnumerable<string>>> requestHeaders = null)
        {
            string uri = Path.Combine(_baseUrl, endpoint);
            using (var restRequest = new HttpRequestMessage(GetMethod(method), uri))
            {
                if (reason != null)
                    restRequest.Headers.Add("X-Audit-Log-Reason", Uri.EscapeDataString(reason));
                if (requestHeaders != null)
                    foreach (var header in requestHeaders)
                        restRequest.Headers.Add(header.Key, header.Value);
                restRequest.Content = new StringContent(json, Encoding.UTF8, "application/json");
                return await SendInternalAsync(restRequest, cancelToken, headerOnly).ConfigureAwait(false);
            }
        }

        /// <exception cref="InvalidOperationException">Unsupported param type.</exception>
        public Task<RestResponse> SendAsync(string method, string endpoint, IReadOnlyDictionary<string, object> multipartParams, CancellationToken cancelToken, bool headerOnly, string reason = null,
            IEnumerable<KeyValuePair<string, IEnumerable<string>>> requestHeaders = null)
        {
            string uri = Path.Combine(_baseUrl, endpoint);

            // HttpRequestMessage implements IDisposable but we do not need to dispose it as it merely disposes of its Content property,
            // which we can do as needed. And regarding that, we do not want to take responsibility for disposing of content provided by
            // the caller of this function, since it's possible that the caller wants to reuse it or is forced to reuse it because of a
            // 429 response. Therefore, by convention, we only dispose the content objects created in this function (if any).
            //
            // See this comment explaining why this is safe: https://github.com/aspnet/Security/issues/886#issuecomment-229181249
            // See also the source for HttpRequestMessage: https://github.com/microsoft/referencesource/blob/master/System/net/System/Net/Http/HttpRequestMessage.cs
#pragma warning disable IDISP004
            var restRequest = new HttpRequestMessage(GetMethod(method), uri);
#pragma warning restore IDISP004

            if (reason != null)
                restRequest.Headers.Add("X-Audit-Log-Reason", Uri.EscapeDataString(reason));
            if (requestHeaders != null)
                foreach (var header in requestHeaders)
                    restRequest.Headers.Add(header.Key, header.Value);
            var content = new MultipartFormDataContent("Upload----" + DateTime.Now.ToString(CultureInfo.InvariantCulture));

            static StreamContent GetStreamContent(Stream stream)
            {
                if (stream.CanSeek)
                {
                    // Reset back to the beginning; it may have been used elsewhere or in a previous request.
                    stream.Position = 0;
                }

#pragma warning disable IDISP004
                return new StreamContent(stream);
#pragma warning restore IDISP004
            }

            foreach (var p in multipartParams ?? ImmutableDictionary<string, object>.Empty)
            {
                switch (p.Value)
                {
#pragma warning disable IDISP004
                    case string stringValue:
                        { content.Add(new StringContent(stringValue, Encoding.UTF8, "text/plain"), p.Key); continue; }
                    case byte[] byteArrayValue:
                        { content.Add(new ByteArrayContent(byteArrayValue), p.Key); continue; }
                    case Stream streamValue:
                        { content.Add(GetStreamContent(streamValue), p.Key); continue; }
                    case MultipartFile fileValue:
                        {
                            var streamContent = GetStreamContent(fileValue.Stream);

                            if (fileValue.ContentType != null)
                                streamContent.Headers.ContentType = new MediaTypeHeaderValue(fileValue.ContentType);

                            content.Add(streamContent, p.Key, fileValue.Filename);
#pragma warning restore IDISP004

                            continue;
                        }
                    default:
                        throw new InvalidOperationException($"Unsupported param type \"{p.Value.GetType().Name}\".");
                }
            }

            restRequest.Content = content;
            return SendInternalAsync(restRequest, cancelToken, headerOnly);
        }

        private async Task<RestResponse> SendInternalAsync(HttpRequestMessage request, CancellationToken cancelToken, bool headerOnly)
        {
            using (var cancelTokenSource = CancellationTokenSource.CreateLinkedTokenSource(_cancelToken, cancelToken))
            {
                cancelToken = cancelTokenSource.Token;
                HttpResponseMessage response = await _client.SendAsync(request, cancelToken).ConfigureAwait(false);

                var headers = response.Headers.ToDictionary(x => x.Key, x => x.Value.FirstOrDefault(), StringComparer.OrdinalIgnoreCase);
                var stream = (!headerOnly || !response.IsSuccessStatusCode) ? await response.Content.ReadAsStreamAsync().ConfigureAwait(false) : null;

                return new RestResponse(response.StatusCode, headers, stream);
            }
        }

        private static readonly HttpMethod Patch = new HttpMethod("PATCH");

        private HttpMethod GetMethod(string method)
        {
            return method switch
            {
                "DELETE" => HttpMethod.Delete,
                "GET" => HttpMethod.Get,
                "PATCH" => Patch,
                "POST" => HttpMethod.Post,
                "PUT" => HttpMethod.Put,
                _ => throw new ArgumentOutOfRangeException(nameof(method), $"Unknown HttpMethod: {method}"),
            };
        }
    }

    internal struct MultipartFile
    {
        public Stream Stream { get; }
        public string Filename { get; }
        public string ContentType { get; }

        public MultipartFile(Stream stream, string filename, string contentType = null)
        {
            Stream = stream;
            Filename = filename;
            ContentType = contentType;
        }
    }
}