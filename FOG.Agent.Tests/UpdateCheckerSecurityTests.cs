using FOG.WebView2;
using System.Net;
using System.Text;

internal static class UpdateCheckerSecurityTests
{
    internal static void BoundsAndCancellation() => CheckBoundsAsync().GetAwaiter().GetResult();
    internal static void ReleaseValidation() => CheckValidationAsync().GetAwaiter().GetResult();

    private static async Task CheckBoundsAsync()
    {
        foreach (var content in new HttpContent[]
        {
            new ByteArrayContent(new byte[131073]),
            new StreamContent(new UnmeasuredStream(new byte[131073]))
        })
        {
            using var checker = new UpdateChecker(new Handler(() => new HttpResponseMessage(HttpStatusCode.OK) { Content = content }));
            try
            {
                await checker.CheckAsync("2.0.3", CancellationToken.None);
                throw new InvalidOperationException("Oversized release response accepted.");
            }
            catch (InvalidDataException) { }
        }
        using var slow = new UpdateChecker(new Handler(() => new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StreamContent(new WaitingStream()) }));
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        try
        {
            await slow.CheckAsync("2.0.3", cancellation.Token).WaitAsync(TimeSpan.FromSeconds(2));
            throw new InvalidOperationException("Release body cancellation ignored.");
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        try
        {
            await slow.CheckAsync("2.0.3", CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));
            throw new InvalidOperationException("Release body deadline ignored.");
        }
        catch (OperationCanceledException) { }
    }

    private static async Task CheckValidationAsync()
    {
        const string valid = "{\"tag_name\":\"v2.0.4\",\"html_url\":\"https://github.com/DenisRapira/FOG-Prime/releases/tag/v2.0.4\"}";
        using var checker = new UpdateChecker(new Handler(() => new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent(valid, Encoding.UTF8, "application/json") }));
        if ((await checker.CheckAsync("2.0.3", CancellationToken.None))?.Version != "2.0.4")
            throw new InvalidOperationException("Valid release rejected.");
        using var missing = new UpdateChecker(new Handler(() => new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent("{\"tag_name\":null}") }));
        if (await missing.CheckAsync("2.0.3", CancellationToken.None) is not null)
            throw new InvalidOperationException("Missing release fields accepted.");
        foreach (var uri in new[]
        {
            "https://github.com:444/DenisRapira/FOG-Prime/releases/tag/v2.0.4",
            "https://user@github.com/DenisRapira/FOG-Prime/releases/tag/v2.0.4"
        })
            if (UpdateChecker.IsTrustedReleaseUri(new Uri(uri)))
                throw new InvalidOperationException("Untrusted release URL accepted.");
    }

    private sealed class Handler(Func<HttpResponseMessage> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(response());
        }
    }

    private sealed class UnmeasuredStream(byte[] bytes) : MemoryStream(bytes)
    {
        public override bool CanSeek => false;
    }

    private sealed class WaitingStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return 0;
        }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
