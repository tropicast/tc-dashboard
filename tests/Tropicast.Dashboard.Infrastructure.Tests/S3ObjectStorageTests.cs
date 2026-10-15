using System.Net;
using System.Net.Http.Headers;
using Amazon.Runtime;
using Amazon.S3;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Microsoft.Extensions.Options;
using Tropicast.Dashboard.Infrastructure.Media;

namespace Tropicast.Dashboard.Infrastructure.Tests;

/// <summary>
/// The bucket adapter against a real S3-compatible server (RustFS, as in compose.yaml): presigned PUT and GET as a
/// browser and the playout worker use them, ranged reads for the upload check, and cleanup by prefix.
/// </summary>
public sealed class S3ObjectStorageTests : IAsyncLifetime
{
    private const string AccessKey = "tropicast-test";
    private const string SecretKey = "tropicast-test-secret";
    private const string Bucket = "tracks";
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private readonly IContainer _server = new ContainerBuilder("mirror.gcr.io/rustfs/rustfs:1.0.1")
        .WithEnvironment("RUSTFS_ACCESS_KEY", AccessKey)
        .WithEnvironment("RUSTFS_SECRET_KEY", SecretKey)
        .WithPortBinding(9000, true)
        .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(r => r.ForPort(9000).ForPath("/health")
            .ForStatusCodeMatching(code => code < HttpStatusCode.InternalServerError)))
        .Build();

    private S3ObjectStorage _storage = null!;

    public async ValueTask InitializeAsync()
    {
        await _server.StartAsync(Token);
        var endpoint = new Uri($"http://{_server.Hostname}:{_server.GetMappedPublicPort(9000)}");
        using var admin = new AmazonS3Client(new BasicAWSCredentials(AccessKey, SecretKey),
            new AmazonS3Config { ServiceURL = endpoint.AbsoluteUri, ForcePathStyle = true, AuthenticationRegion = "us-east-1" });
        await admin.PutBucketAsync(Bucket, Token);
        _storage = new S3ObjectStorage(Options.Create(new StorageOptions
        {
            Endpoint = endpoint, Bucket = Bucket, AccessKey = AccessKey, SecretKey = SecretKey,
        }));
    }

    [Fact]
    public async Task A_presigned_put_stores_the_file_and_a_presigned_get_reads_it()
    {
        var bytes = Enumerable.Range(0, 300_000).Select(i => (byte)(i % 251)).ToArray();
        const string key = "stations/abc/tracks/one";
        using var http = new HttpClient();

        var put = await PutAsync(http, _storage.PresignUpload(key, "audio/mpeg", bytes.Length, TimeSpan.FromMinutes(5)), bytes);
        Assert.True(put.IsSuccessStatusCode, $"PUT answered {put.StatusCode}");

        Assert.Equal(new Application.Media.StoredObject(bytes.Length, "audio/mpeg"), await _storage.HeadAsync(key, Token));
        Assert.Equal(bytes[..1000], await _storage.ReadRangeAsync(key, 0, 1000, Token));
        Assert.Equal(bytes[^500..], await _storage.ReadRangeAsync(key, bytes.Length - 500, 500, Token));
        Assert.Equal(bytes, await http.GetByteArrayAsync(_storage.PresignDownload(key, TimeSpan.FromMinutes(5)), Token));
        Assert.Empty(await _storage.ReadRangeAsync(key, 0, 0, Token));
    }

    [Fact]
    public async Task A_put_of_another_length_than_signed_is_refused()
    {
        using var http = new HttpClient();
        var upload = _storage.PresignUpload("stations/abc/tracks/big", "audio/mpeg", 3, TimeSpan.FromMinutes(5));

        var put = await PutAsync(http, upload, new byte[3000]);

        Assert.False(put.IsSuccessStatusCode, $"PUT answered {put.StatusCode}");
        Assert.Null(await _storage.HeadAsync("stations/abc/tracks/big", Token));
    }

    [Fact]
    public async Task The_upload_url_cannot_replace_the_stored_file()
    {
        const string key = "stations/abc/tracks/once";
        using var http = new HttpClient();
        var upload = _storage.PresignUpload(key, "audio/mpeg", 3, TimeSpan.FromMinutes(5));
        (await PutAsync(http, upload, [1, 2, 3])).EnsureSuccessStatusCode();

        var again = await PutAsync(http, upload, [9, 9, 9]);

        Assert.False(again.IsSuccessStatusCode, $"PUT answered {again.StatusCode}");
        Assert.Equal([1, 2, 3], await _storage.ReadRangeAsync(key, 0, 3, Token));
    }

    [Fact]
    public async Task A_put_with_another_content_type_than_signed_is_refused()
    {
        using var http = new HttpClient();
        var upload = _storage.PresignUpload("stations/abc/tracks/html", "audio/mpeg", 3, TimeSpan.FromMinutes(5));

        var put = await PutAsync(http, upload with { Headers = new Dictionary<string, string>(upload.Headers) { ["Content-Type"] = "text/html" } }, [1, 2, 3]);

        Assert.False(put.IsSuccessStatusCode);
        Assert.Null(await _storage.HeadAsync("stations/abc/tracks/html", Token));
    }

    [Fact]
    public async Task Deleting_by_prefix_removes_only_that_station()
    {
        foreach (var key in new[] { "stations/s1/tracks/a", "stations/s1/tracks/b", "stations/s2/tracks/c" })
        {
            await PutAsync(key);
        }

        Assert.Equal(2, await _storage.DeletePrefixAsync("stations/s1/", Token));

        Assert.Null(await _storage.HeadAsync("stations/s1/tracks/a", Token));
        Assert.Null(await _storage.HeadAsync("stations/s1/tracks/b", Token));
        Assert.NotNull(await _storage.HeadAsync("stations/s2/tracks/c", Token));
        // Missing objects and empty prefixes are not errors.
        await _storage.DeleteAsync("stations/s1/tracks/a", Token);
        Assert.Equal(0, await _storage.DeletePrefixAsync("stations/s1/", Token));
    }

    private async Task PutAsync(string key)
    {
        using var http = new HttpClient();
        (await PutAsync(http, _storage.PresignUpload(key, "audio/ogg", 3, TimeSpan.FromMinutes(5)), [1, 2, 3])).EnsureSuccessStatusCode();
    }

    /// <summary>As the browser does: the returned headers, exactly.</summary>
    private static async Task<HttpResponseMessage> PutAsync(HttpClient http, Application.Media.PresignedUpload upload, byte[] body)
    {
        using var content = new ByteArrayContent(body);
        using var request = new HttpRequestMessage(HttpMethod.Put, upload.Url) { Content = content };
        foreach (var (name, value) in upload.Headers)
        {
            if (name == "Content-Type")
            {
                content.Headers.ContentType = MediaTypeHeaderValue.Parse(value);
            }
            else
            {
                request.Headers.Add(name, value);
            }
        }
        return await http.SendAsync(request, Token);
    }

    public async ValueTask DisposeAsync()
    {
        _storage?.Dispose();
        await _server.DisposeAsync();
    }
}
