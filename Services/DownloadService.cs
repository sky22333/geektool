using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using GeekToolDownloader.Models;
using MihaZupan;

namespace GeekToolDownloader.Services
{
    public readonly struct DownloadProgressInfo
    {
        public DownloadProgressInfo(long downloadedBytes, double bytesPerSecond)
        {
            DownloadedBytes = downloadedBytes;
            BytesPerSecond = bytesPerSecond;
        }

        public long DownloadedBytes { get; }
        public double BytesPerSecond { get; }
    }

    public interface IDownloadService : IDisposable
    {
        Task DownloadFileAsync(string url, string destination, IProgress<DownloadProgressInfo> progress, CancellationToken ct);
        Task<bool> VerifyHashAsync(string filePath, string expectedHash, CancellationToken ct);
    }

    public class DownloadService : IDownloadService
    {
        private static HttpClient _httpClient = CreateHttpClient(new AppConfig());
        private static readonly object _httpClientLock = new object();

        public static HttpClient GetHttpClient()
        {
            lock (_httpClientLock)
            {
                return _httpClient;
            }
        }

        public static void UpdateHttpClient(AppConfig config)
        {
            HttpClient? oldClient = null;
            lock (_httpClientLock)
            {
                oldClient = _httpClient;
                _httpClient = CreateHttpClient(config);
            }

            if (oldClient != null)
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await Task.Delay(2000);
                        oldClient.Dispose();
                    }
                    catch { }
                });
            }
        }

        public static void DisposeHttpClient()
        {
            lock (_httpClientLock)
            {
                try
                {
                    _httpClient?.Dispose();
                }
                catch { }
            }
        }

        private static HttpClient CreateHttpClient(AppConfig config)
        {
            var handler = new HttpClientHandler
            {
                MaxConnectionsPerServer = config.MaxConcurrentDownloads > 0 ? config.MaxConcurrentDownloads : 6
            };

            var proxy = CreateProxy(config);
            if (proxy != null)
            {
                handler.UseProxy = true;
                handler.Proxy = proxy;
            }
            else
            {
                handler.UseProxy = false;
            }

            return new HttpClient(handler)
            {
                Timeout = TimeSpan.FromMinutes(15)
            };
        }

        private static IWebProxy? CreateProxy(AppConfig config)
        {
            if (!config.ProxyEnabled || string.IsNullOrWhiteSpace(config.ProxyUrl))
            {
                return null;
            }

            if (!Uri.TryCreate(config.ProxyUrl.Trim(), UriKind.Absolute, out var proxyUri))
            {
                return null;
            }

            var scheme = proxyUri.Scheme.ToLowerInvariant();
            if (scheme == "http" || scheme == "https")
            {
                var webProxy = new WebProxy(proxyUri);
                ApplyProxyCredentials(webProxy, proxyUri);
                return webProxy;
            }

            if (scheme == "socks5")
            {
                var port = proxyUri.IsDefaultPort ? 1080 : proxyUri.Port;
                var socksProxy = new HttpToSocks5Proxy(proxyUri.Host, port);
                ApplyProxyCredentials(socksProxy, proxyUri);
                return socksProxy;
            }

            return null;
        }

        private static void ApplyProxyCredentials(IWebProxy proxy, Uri proxyUri)
        {
            if (string.IsNullOrWhiteSpace(proxyUri.UserInfo)) return;

            var parts = proxyUri.UserInfo.Split(new[] { ':' }, 2);
            var user = Uri.UnescapeDataString(parts[0]);
            var pass = parts.Length > 1 ? Uri.UnescapeDataString(parts[1]) : string.Empty;
            proxy.Credentials = new NetworkCredential(user, pass);
        }

        public async Task DownloadFileAsync(string url, string destination, IProgress<DownloadProgressInfo> progress, CancellationToken ct)
        {
            long existingLength = 0;

            if (File.Exists(destination))
            {
                existingLength = new FileInfo(destination).Length;
            }

            HttpResponseMessage? response = null;
            try
            {
                response = await SendDownloadRequestAsync(url, existingLength, ct);

                // Server ignored range request or returned requested range not satisfiable. Start from scratch to avoid file corruption.
                if (existingLength > 0 && response.StatusCode != HttpStatusCode.PartialContent)
                {
                    response.Dispose();
                    response = null;
                    File.Delete(destination);
                    existingLength = 0;
                    response = await SendDownloadRequestAsync(url, 0, ct);
                }

                response.EnsureSuccessStatusCode();
                await ReadStreamAsync(response, destination, existingLength, progress, ct);
            }
            finally
            {
                response?.Dispose();
            }
        }

        private async Task<HttpResponseMessage> SendDownloadRequestAsync(string url, long existingLength, CancellationToken ct)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, url);
            if (existingLength > 0)
            {
                request.Headers.Range = new RangeHeaderValue(existingLength, null);
            }

            return await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        }

        private async Task ReadStreamAsync(HttpResponseMessage response, string destination, long existingLength, IProgress<DownloadProgressInfo> progress, CancellationToken ct)
        {
            using (var contentStream = await response.Content.ReadAsStreamAsync())
            {
                using (var fileStream = new FileStream(destination, existingLength > 0 ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.None, 1048576, true))
                {
                    var buffer = System.Buffers.ArrayPool<byte>.Shared.Rent(81920);
                    try
                    {
                        int bytesRead;
                        long totalRead = existingLength;
                        long bytesSinceLastReport = 0;
                        var sw = System.Diagnostics.Stopwatch.StartNew();

                        while ((bytesRead = await contentStream.ReadAsync(buffer, 0, 81920, ct)) > 0)
                        {
                            await fileStream.WriteAsync(buffer, 0, bytesRead, ct);
                            totalRead += bytesRead;
                            bytesSinceLastReport += bytesRead;

                            if (sw.ElapsedMilliseconds >= 300)
                            {
                                var seconds = sw.Elapsed.TotalSeconds;
                                var speed = seconds > 0 ? bytesSinceLastReport / seconds : 0;
                                progress?.Report(new DownloadProgressInfo(totalRead, speed));
                                bytesSinceLastReport = 0;
                                sw.Restart();
                            }
                        }

                        progress?.Report(new DownloadProgressInfo(totalRead, 0));
                    }
                    finally
                    {
                        System.Buffers.ArrayPool<byte>.Shared.Return(buffer);
                    }
                }
            }
        }

        public async Task<bool> VerifyHashAsync(string filePath, string expectedHash, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(expectedHash)) return true;

            using (var sha256 = SHA256.Create())
            {
                using (var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 1048576, true))
                {
                    // Stream hashing as per docs.md, running in Task.Run to keep off UI thread
                    var hashBytes = await Task.Run(() => sha256.ComputeHash(stream), ct);
                    var hashString = BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
                    return hashString == expectedHash.ToLowerInvariant();
                }
            }
        }

        public void Dispose()
        {
            // _httpClient is static, do not dispose here
        }
    }
}
