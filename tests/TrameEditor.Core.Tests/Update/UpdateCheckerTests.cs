using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using TrameEditor.Core.Session;
using TrameEditor.Core.Update;

namespace TrameEditor.Core.Tests.Update;

/// <summary>
/// Controllo della versione pubblicata. Le risposte del sito sono simulate: un test non
/// deve dipendere dalla rete, e servono anche i casi che in rete non si sanno provocare
/// (sito fermo, intestazione cambiata, formato inatteso).
/// </summary>
public class UpdateCheckerTests
{
    /// <summary>Risponde come la pagina di download di tramefuture.com, che dichiara il nome
    /// del file — e quindi la versione — già nella risposta a una HEAD.</summary>
    private sealed class FakeSite(HttpStatusCode status, string? fileName) : HttpMessageHandler
    {
        public HttpMethod? MethodUsed { get; private set; }
        public string? UserAgent { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            MethodUsed = request.Method;
            UserAgent = request.Headers.UserAgent.ToString();
            var response = new HttpResponseMessage(status) { Content = new ByteArrayContent([]) };
            if (fileName is not null)
                response.Content.Headers.ContentDisposition =
                    new ContentDispositionHeaderValue("attachment") { FileName = $"\"{fileName}\"" };
            return Task.FromResult(response);
        }
    }

    private static UpdateChecker CheckerFor(FakeSite site) => new(new HttpClient(site));

    [Fact]
    public async Task Reports_TheUpdate_WhenTheSiteServesANewerVersion()
    {
        var checker = CheckerFor(new FakeSite(HttpStatusCode.OK, "TrameEditor-Setup-2.13.0.zip"));

        var result = await checker.CheckAsync(new Version(2, 12, 0));

        Assert.True(result.UpdateAvailable);
        Assert.Equal(new Version(2, 13, 0), result.Latest);
    }

    [Fact]
    public async Task StaysSilent_WhenTheVersionIsTheSame()
    {
        var checker = CheckerFor(new FakeSite(HttpStatusCode.OK, "TrameEditor-Setup-2.12.0.zip"));

        var result = await checker.CheckAsync(new Version(2, 12, 0));

        Assert.False(result.UpdateAvailable);
        Assert.Equal(new Version(2, 12, 0), result.Latest);
    }

    /// <summary>La versione in esecuzione ha quattro cifre (2.12.0.0), quella del file tre:
    /// senza normalizzare, la stessa versione risulterebbe più vecchia di sé.</summary>
    [Fact]
    public async Task StaysSilent_WhenTheRunningVersionHasAFourthNumber()
    {
        var checker = CheckerFor(new FakeSite(HttpStatusCode.OK, "TrameEditor-Setup-2.12.0.zip"));

        var result = await checker.CheckAsync(new Version(2, 12, 0, 0));

        Assert.False(result.UpdateAvailable);
    }

    [Fact]
    public async Task StaysSilent_WhenTheInstalledVersionIsAheadOfTheSite()
    {
        var checker = CheckerFor(new FakeSite(HttpStatusCode.OK, "TrameEditor-Setup-2.12.0.zip"));

        var result = await checker.CheckAsync(new Version(2, 13, 0));

        Assert.False(result.UpdateAvailable);
    }

    [Fact]
    public async Task StaysSilent_WhenTheSiteIsDown()
    {
        var checker = CheckerFor(new FakeSite(HttpStatusCode.ServiceUnavailable, null));

        var result = await checker.CheckAsync(new Version(2, 12, 0));

        Assert.False(result.UpdateAvailable);
        Assert.Null(result.Latest);
    }

    [Fact]
    public async Task StaysSilent_WhenTheAnswerCarriesNoFileName()
    {
        var checker = CheckerFor(new FakeSite(HttpStatusCode.OK, null));

        var result = await checker.CheckAsync(new Version(2, 12, 0));

        Assert.False(result.UpdateAvailable);
        Assert.Null(result.Latest);
    }

    [Fact]
    public async Task StaysSilent_WhenTheNetworkThrows()
    {
        var checker = new UpdateChecker(new HttpClient(new ThrowingHandler()));

        var result = await checker.CheckAsync(new Version(2, 12, 0));

        Assert.False(result.UpdateAvailable);
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new HttpRequestException("nessuna rete");
    }

    [Fact]
    public async Task Asks_WithHead_AndSaysNothingAboutTheInstallation()
    {
        var site = new FakeSite(HttpStatusCode.OK, "TrameEditor-Setup-2.12.0.zip");

        await CheckerFor(site).CheckAsync(new Version(2, 12, 0));

        // HEAD: la versione si legge senza scaricare i 70 MB del file.
        Assert.Equal(HttpMethod.Head, site.MethodUsed);
        // Nessuna versione nello User-Agent: al sito non si racconta cosa c'è installato.
        Assert.Equal("TrameEditor", site.UserAgent);
    }

    [Theory]
    [InlineData("TrameEditor-Setup-2.12.0.zip", "2.12.0")]
    [InlineData("attachment;filename=\"TrameEditor-Setup-10.0.4.exe\"", "10.0.4")]
    [InlineData("TrameEditor-Setup.zip", null)]
    [InlineData("", null)]
    public void ParseVersion_ReadsTheNumberOrGivesUp(string text, string? expected)
    {
        var parsed = UpdateChecker.ParseVersion(text);

        Assert.Equal(expected is null ? null : Version.Parse(expected), parsed);
    }

    [Fact]
    public void ShouldCheck_OnlyWithConsent_AndAtMostOncePerDay()
    {
        var now = new DateTime(2026, 8, 19, 12, 0, 0, DateTimeKind.Utc);

        // Senza risposta dell'utente non si esce in rete: è opt-in.
        Assert.False(new AppSettings().ShouldCheckForUpdates(now));
        Assert.False(new AppSettings { UpdateCheckEnabled = false }.ShouldCheckForUpdates(now));

        Assert.True(new AppSettings { UpdateCheckEnabled = true }.ShouldCheckForUpdates(now));
        Assert.False(new AppSettings
        {
            UpdateCheckEnabled = true,
            LastUpdateCheckUtc = now.AddHours(-3),
        }.ShouldCheckForUpdates(now));
        Assert.True(new AppSettings
        {
            UpdateCheckEnabled = true,
            LastUpdateCheckUtc = now.AddDays(-1).AddMinutes(-1),
        }.ShouldCheckForUpdates(now));
    }
}
