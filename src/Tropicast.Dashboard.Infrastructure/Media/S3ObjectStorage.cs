using System.Net;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Options;
using Tropicast.Dashboard.Application.Media;

namespace Tropicast.Dashboard.Infrastructure.Media;

/// <summary>
/// The private bucket for uploaded audio (<c>Storage</c> section): any S3-compatible service, e.g. Hetzner Object
/// Storage in production and MinIO in <c>compose.yaml</c>. Unset endpoint, bucket or keys: uploads are refused.
/// </summary>
public sealed class StorageOptions
{
    /// <summary>S3 API address the server uses, e.g. <c>https://fsn1.your-objectstorage.com</c> or <c>http://minio:9000</c>.</summary>
    public Uri? Endpoint { get; set; }
    /// <summary>
    /// Address in presigned URLs, which browsers and the playout worker open; the <see cref="Endpoint"/> when unset
    /// (they differ in Compose: the API reaches <c>minio:9000</c>, the browser <c>localhost:9000</c>).
    /// </summary>
    public Uri? PublicEndpoint { get; set; }
    public string Region { get; set; } = "us-east-1";
    public string? Bucket { get; set; }
    public string? AccessKey { get; set; }
    /// <summary>From a secret, never committed.</summary>
    public string? SecretKey { get; set; }
    /// <summary>How long a presigned download URL (playout worker) lives.</summary>
    public TimeSpan DownloadUrlLifetime { get; set; } = TimeSpan.FromMinutes(15);
}

internal sealed class S3ObjectStorage(IOptions<StorageOptions> options) : IObjectStorage, IDisposable
{
    private readonly Lazy<AmazonS3Client> _client = new(() => Client(options.Value, options.Value.Endpoint!));
    private readonly Lazy<AmazonS3Client> _presigner = new(() => Client(options.Value, options.Value.PublicEndpoint ?? options.Value.Endpoint!));

    private StorageOptions Settings => options.Value;

    public bool IsConfigured => Settings is { Endpoint: not null, Bucket.Length: > 0, AccessKey.Length: > 0, SecretKey.Length: > 0 };

    private static AmazonS3Client Client(StorageOptions settings, Uri endpoint) => new(
        new BasicAWSCredentials(settings.AccessKey, settings.SecretKey),
        new AmazonS3Config
        {
            ServiceURL = endpoint.AbsoluteUri,
            AuthenticationRegion = settings.Region,
            // Bucket in the path, not the host name: what MinIO and most S3-compatible services expect.
            ForcePathStyle = true,
            // Checksums only where S3 requires them: S3-compatible services do not all accept the SDK's defaults.
            RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED,
            ResponseChecksumValidation = ResponseChecksumValidation.WHEN_REQUIRED,
        });

    public PresignedUpload PresignUpload(string key, string contentType, long size, TimeSpan lifetime)
    {
        var request = PresignRequest(key, HttpVerb.PUT, lifetime);
        request.ContentType = contentType;
        // Signed headers: the bucket refuses a body of another length, and a PUT once an object exists.
        request.Headers.ContentLength = size;
        request.Headers["If-None-Match"] = "*";
        return new PresignedUpload(new Uri(_presigner.Value.GetPreSignedURL(request)),
            new Dictionary<string, string> { ["Content-Type"] = contentType, ["If-None-Match"] = "*" });
    }

    public Uri PresignDownload(string key, TimeSpan lifetime) => new(_presigner.Value.GetPreSignedURL(PresignRequest(key, HttpVerb.GET, lifetime)));

    private GetPreSignedUrlRequest PresignRequest(string key, HttpVerb verb, TimeSpan lifetime)
    {
        var endpoint = Settings.PublicEndpoint ?? Settings.Endpoint!;
        return new GetPreSignedUrlRequest
        {
            BucketName = Settings.Bucket,
            Key = key,
            Verb = verb,
            Expires = DateTime.UtcNow + lifetime,
            Protocol = endpoint.Scheme == Uri.UriSchemeHttp ? Protocol.HTTP : Protocol.HTTPS,
        };
    }

    public async Task<StoredObject?> HeadAsync(string key, CancellationToken cancellationToken = default)
    {
        try
        {
            var metadata = await _client.Value.GetObjectMetadataAsync(Settings.Bucket, key, cancellationToken).ConfigureAwait(false);
            return new StoredObject(metadata.ContentLength, metadata.Headers.ContentType);
        }
        catch (AmazonS3Exception e) when (e.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<byte[]> ReadRangeAsync(string key, long offset, int length, CancellationToken cancellationToken = default)
    {
        if (length <= 0)
        {
            return []; // "bytes=0--1" is no range.
        }
        try
        {
            using var response = await _client.Value.GetObjectAsync(new GetObjectRequest
            {
                BucketName = Settings.Bucket,
                Key = key,
                ByteRange = new ByteRange(offset, offset + length - 1),
            }, cancellationToken).ConfigureAwait(false);
            using var buffer = new MemoryStream();
            await response.ResponseStream.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
            return buffer.ToArray();
        }
        catch (AmazonS3Exception e) when (e.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable)
        {
            return [];
        }
    }

    public async Task DeleteAsync(string key, CancellationToken cancellationToken = default)
        => await _client.Value.DeleteObjectAsync(Settings.Bucket, key, cancellationToken).ConfigureAwait(false);

    public async Task<int> DeletePrefixAsync(string prefix, CancellationToken cancellationToken = default)
    {
        var deleted = 0;
        var request = new ListObjectsV2Request { BucketName = Settings.Bucket, Prefix = prefix };
        ListObjectsV2Response page;
        do
        {
            page = await _client.Value.ListObjectsV2Async(request, cancellationToken).ConfigureAwait(false);
            if (page.S3Objects is { Count: > 0 } objects)
            {
                await _client.Value.DeleteObjectsAsync(new DeleteObjectsRequest
                {
                    BucketName = Settings.Bucket,
                    Objects = [.. objects.Select(o => new KeyVersion { Key = o.Key })],
                }, cancellationToken).ConfigureAwait(false);
                deleted += objects.Count;
            }
            request.ContinuationToken = page.NextContinuationToken;
        }
        while (page.IsTruncated == true);
        return deleted;
    }

    public void Dispose()
    {
        if (_client.IsValueCreated)
        {
            _client.Value.Dispose();
        }
        if (_presigner.IsValueCreated)
        {
            _presigner.Value.Dispose();
        }
    }
}
