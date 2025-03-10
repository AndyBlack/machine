namespace SIL.Machine.AspNetCore.Services;

public class S3WriteStream : Stream
{
    private readonly AmazonS3Client _client;
    private readonly string _key;
    private readonly string _uploadId;
    private readonly string _bucketName;
    private readonly List<UploadPartResponse> _uploadResponses;
    private readonly ILogger<S3WriteStream> _logger;

    public const int MaxPartSize = 5 * 1024 * 1024;

    public S3WriteStream(
        AmazonS3Client client,
        string key,
        string bucketName,
        string uploadId,
        ILoggerFactory loggerFactory
    )
    {
        _client = client;
        _key = key;
        _bucketName = bucketName;
        _uploadId = uploadId;
        _logger = loggerFactory.CreateLogger<S3WriteStream>();
        _uploadResponses = new List<UploadPartResponse>();
    }

    public override bool CanRead => false;

    public override bool CanSeek => false;

    public override bool CanWrite => true;

    public override long Length => 0;

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override void Flush() { }

    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        try
        {
            using MemoryStream ms = new(buffer, offset, count);
            int partNumber = _uploadResponses.Count + 1;
            UploadPartRequest request =
                new()
                {
                    BucketName = _bucketName,
                    Key = _key,
                    UploadId = _uploadId,
                    PartNumber = partNumber,
                    InputStream = ms,
                    PartSize = MaxPartSize
                };
            request.StreamTransferProgress += new EventHandler<StreamTransferProgressArgs>(
                (_, e) =>
                {
                    _logger.LogDebug($"Transferred {e.TransferredBytes}/{e.TotalBytes}");
                }
            );
            UploadPartResponse response = await _client.UploadPartAsync(request);
            if (response.HttpStatusCode != HttpStatusCode.OK)
                throw new HttpRequestException(
                    $"Tried to upload part {partNumber} of upload {_uploadId} to {_bucketName}/{_key} but received response code {response.HttpStatusCode}"
                );
            _uploadResponses.Add(response);
        }
        catch (Exception e)
        {
            await AbortAsync(e);
            throw;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            try
            {
                CompleteMultipartUploadRequest request =
                    new()
                    {
                        BucketName = _bucketName,
                        Key = _key,
                        UploadId = _uploadId
                    };
                request.AddPartETags(_uploadResponses);
                CompleteMultipartUploadResponse response = _client
                    .CompleteMultipartUploadAsync(request)
                    .WaitAndUnwrapException();
                Dispose(disposing: false);
                GC.SuppressFinalize(this);
                if (response.HttpStatusCode != HttpStatusCode.OK)
                    throw new HttpRequestException(
                        $"Tried to complete {_uploadId} to {_bucketName}/{_key} but received response code {response.HttpStatusCode}"
                    );
            }
            catch (Exception e)
            {
                AbortAsync(e).WaitAndUnwrapException();
                throw;
            }
        }
        base.Dispose(disposing);
    }

    public async override ValueTask DisposeAsync()
    {
        try
        {
            CompleteMultipartUploadRequest request =
                new()
                {
                    BucketName = _bucketName,
                    Key = _key,
                    UploadId = _uploadId
                };
            request.AddPartETags(_uploadResponses);
            CompleteMultipartUploadResponse response = await _client.CompleteMultipartUploadAsync(request);
            Dispose(disposing: false);
            GC.SuppressFinalize(this);
            if (response.HttpStatusCode != HttpStatusCode.OK)
                throw new HttpRequestException(
                    $"Tried to complete {_uploadId} to {_bucketName}/{_key} but received response code {response.HttpStatusCode}"
                );
        }
        catch (Exception e)
        {
            await AbortAsync(e);
        }
    }

    private async Task AbortAsync(Exception e)
    {
        _logger.LogError(e, $"Aborted upload {_uploadId} to {_bucketName}/{_key}");
        AbortMultipartUploadRequest abortMPURequest =
            new()
            {
                BucketName = _bucketName,
                Key = _key,
                UploadId = _uploadId
            };
        await _client.AbortMultipartUploadAsync(abortMPURequest);
    }
}
