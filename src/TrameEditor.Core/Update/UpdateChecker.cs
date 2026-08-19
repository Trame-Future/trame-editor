using System.Net.Http;
using System.Text.RegularExpressions;

namespace TrameEditor.Core.Update;

/// <summary>Esito di un controllo aggiornamenti. <paramref name="Latest"/> è null quando
/// il controllo non è riuscito: in quel caso non si dice niente all'utente, perché una
/// rete assente non è una notizia.</summary>
public sealed record UpdateCheckResult(Version Current, Version? Latest, bool UpdateAvailable)
{
    public static UpdateCheckResult Failed(Version current) => new(current, null, false);
}

/// <summary>
/// Chiede a tramefuture.com qual è l'ultima versione pubblicata e la confronta con quella
/// in esecuzione. La domanda è una sola richiesta <c>HEAD</c>: la pagina di download serve
/// il file con il nome nell'intestazione <c>Content-Disposition</c>
/// (<c>TrameEditor-Setup-2.12.0.zip</c>), quindi la versione si legge senza scaricare un byte.
/// <para>
/// La fonte del controllo è di proposito la stessa da cui l'utente scaricherà: chiedere a
/// GitHub e mandare l'utente sul sito vorrebbe dire poter annunciare una versione che il
/// link non serve ancora.
/// </para>
/// <para>
/// Non viene inviato nulla sull'installazione — nessun identificativo, nessuna versione
/// nello User-Agent: al sito arriva solo l'indirizzo IP, come per una qualsiasi visita.
/// </para>
/// </summary>
public sealed class UpdateChecker(HttpClient? client = null)
{
    /// <summary>La pagina da aprire nel browser: presenta il programma e offre il download.</summary>
    public const string DownloadPageUrl = "https://www.tramefuture.com/download/trame-editor-last-release/";

    /// <summary>Recapito diretto del file, interrogato con HEAD per leggerne solo il nome.</summary>
    public const string FileUrl = DownloadPageUrl + "?wpdmdl=617";

    private static readonly Regex VersionInName = new(@"(\d+)\.(\d+)\.(\d+)", RegexOptions.Compiled);

    /// <summary>Largo di proposito: misurato sul sito vero, la prima richiesta costa quasi
    /// 4 secondi fra DNS e TLS a freddo, le successive mezzo secondo. Nessuno sta aspettando —
    /// il controllo gira in disparte a finestra già aperta — quindi tanto vale non troncare
    /// una risposta lenta e non dover riprovare domani.</summary>
    private readonly HttpClient _client = client ?? new HttpClient { Timeout = TimeSpan.FromSeconds(15) };

    public async Task<UpdateCheckResult> CheckAsync(Version current, CancellationToken token = default)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Head, FileUrl);
            request.Headers.UserAgent.ParseAdd("TrameEditor");
            using var response = await _client.SendAsync(request, token);
            if (!response.IsSuccessStatusCode)
                return UpdateCheckResult.Failed(current);

            var latest = ParseVersion(FileNameOf(response));
            if (latest is null)
                return UpdateCheckResult.Failed(current);

            return new UpdateCheckResult(current, latest, IsNewer(latest, current));
        }
        catch
        {
            // Rete assente, sito fermo, formato cambiato: si tace e si riprova domani.
            return UpdateCheckResult.Failed(current);
        }
    }

    /// <summary>Il nome del file annunciato dalla risposta. Se l'intestazione non si lascia
    /// interpretare si ripiega sul testo grezzo, che alcuni server scrivono senza virgolette.</summary>
    private static string? FileNameOf(HttpResponseMessage response)
    {
        var parsed = response.Content.Headers.ContentDisposition?.FileName;
        if (!string.IsNullOrWhiteSpace(parsed))
            return parsed.Trim('"');

        if (response.Content.Headers.TryGetValues("Content-Disposition", out var raw))
            return string.Join(" ", raw);
        return null;
    }

    public static Version? ParseVersion(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        var match = VersionInName.Match(text);
        return match.Success
            ? new Version(int.Parse(match.Groups[1].Value), int.Parse(match.Groups[2].Value),
                int.Parse(match.Groups[3].Value))
            : null;
    }

    /// <summary>Confronto sui soli tre numeri che l'utente vede: la quarta cifra che .NET
    /// aggiunge da sé (2.12.0 diventa 2.12.0.0) farebbe risultare diverse due versioni uguali.</summary>
    public static bool IsNewer(Version latest, Version current)
    {
        if (latest.Major != current.Major) return latest.Major > current.Major;
        if (latest.Minor != current.Minor) return latest.Minor > current.Minor;
        return Math.Max(latest.Build, 0) > Math.Max(current.Build, 0);
    }
}
