using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace TrameEditor.Core.Shell;

/// <summary>Una voce del menu che appare col tasto destro.</summary>
/// <param name="Association">Dove si aggancia: <c>SystemFileAssociations\.pdf</c>
/// per un tipo di file, <c>Directory</c> per le cartelle.</param>
/// <param name="Name">Nome della chiave: deve restare stabile fra le versioni.</param>
/// <param name="Label">Quello che legge l'utente.</param>
/// <param name="Verb">L'azione che TrameEditor eseguirà sul file scelto.</param>
public sealed record ShellVerb(string Association, string Name, string Label, StartupVerb Verb);

/// <summary>
/// Le voci di TrameEditor nel menu contestuale di Esplora risorse.
/// </summary>
/// <remarks>
/// <para>Tutto sotto <b>HKEY_CURRENT_USER</b>: nessun diritto di amministratore,
/// nessun effetto sugli altri utenti del computer. Sono <b>voci di menu</b>, non
/// associazioni: il programma predefinito con cui si aprono i PDF non cambia.</para>
/// <para>Su Windows 11 le voci compaiono sotto <b>"Mostra altre opzioni"</b> (il
/// menu classico). Il menu breve nuovo accetta solo estensioni impacchettate in
/// MSIX, che questo programma non usa: dirlo è più onesto che far cercare
/// all'utente una voce che non c'è.</para>
/// </remarks>
public static class ExplorerIntegration
{
    /// <summary>Il ramo standard: <c>HKCU\Software\Classes</c>.</summary>
    public const string DefaultRoot = @"Software\Classes";

    /// <summary>Prefisso delle chiavi, per riconoscere le nostre e non toccare altro.</summary>
    private const string Prefix = "TrameEditor.";

    public static IReadOnlyList<ShellVerb> Verbs { get; } =
    [
        new(@"SystemFileAssociations\.pdf", Prefix + "apri", "Apri con TrameEditor", StartupVerb.Open),
        new(@"SystemFileAssociations\.pdf", Prefix + "pdfa", "Converti in PDF/A (archiviazione)", StartupVerb.ConvertToPdfA),
        new(@"SystemFileAssociations\.pdf", Prefix + "redact", "Anonimizza con TrameEditor", StartupVerb.Redact),
        new(@"SystemFileAssociations\.p7m", Prefix + "apri", "Apri con TrameEditor (firme e contenuto)", StartupVerb.Open),
        new(@"SystemFileAssociations\.xml", Prefix + "apri", "Apri con TrameEditor (fattura leggibile)", StartupVerb.Open),
        new(@"SystemFileAssociations\.txt", Prefix + "apri", "Apri con TrameEditor", StartupVerb.Open),
        new(@"SystemFileAssociations\.md", Prefix + "apri", "Apri con TrameEditor", StartupVerb.Open),
        new("Directory", Prefix + "cerca", "Cerca nei PDF di questa cartella", StartupVerb.SearchFolder),
        new("Directory", Prefix + "estrai", "Estrai dai file firmati (.p7m)", StartupVerb.ExtractSigned),
    ];

    /// <summary>Il percorso di registro di una voce, sotto la radice indicata.</summary>
    public static string KeyPath(ShellVerb verb, string root = DefaultRoot) =>
        $@"{root}\{verb.Association}\shell\{verb.Name}";

    /// <summary>
    /// La riga di comando della voce. Le virgolette servono: senza, un percorso
    /// con gli spazi arriva spezzato in più argomenti.
    /// </summary>
    public static string CommandLine(ShellVerb verb, string executablePath)
    {
        var option = StartupArguments.SwitchFor(verb.Verb);
        return option.Length == 0
            ? $"\"{executablePath}\" \"%1\""
            : $"\"{executablePath}\" {option} \"%1\"";
    }

    /// <summary>Scrive le voci nel registro dell'utente corrente.</summary>
    public static void Install(string executablePath, string root = DefaultRoot)
    {
        foreach (var verb in Verbs)
        {
            using var key = Registry.CurrentUser.CreateSubKey(KeyPath(verb, root));
            key.SetValue(null, verb.Label);
            key.SetValue("Icon", $"\"{executablePath}\",0");
            using var command = key.CreateSubKey("command");
            command.SetValue(null, CommandLine(verb, executablePath));
        }

        NotifyShell();
    }

    /// <summary>Toglie le voci. Cancella solo le chiavi con il nostro prefisso.</summary>
    public static void Uninstall(string root = DefaultRoot)
    {
        foreach (var verb in Verbs)
        {
            try
            {
                Registry.CurrentUser.DeleteSubKeyTree(KeyPath(verb, root), throwOnMissingSubKey: false);
            }
            catch (UnauthorizedAccessException)
            {
                // una chiave che non si lascia togliere non deve bloccare le altre
            }
        }

        NotifyShell();
    }

    /// <summary>Vero se le voci risultano installate e puntano a questo eseguibile.</summary>
    public static bool IsInstalled(string executablePath, string root = DefaultRoot)
    {
        foreach (var verb in Verbs)
        {
            using var command = Registry.CurrentUser.OpenSubKey(KeyPath(verb, root) + @"\command");
            if (command?.GetValue(null) as string != CommandLine(verb, executablePath))
                return false;
        }

        return true;
    }

    /// <summary>Vero se c'è almeno una nostra voce, anche di una versione precedente.</summary>
    public static bool IsPresent(string root = DefaultRoot)
    {
        foreach (var verb in Verbs)
        {
            using var key = Registry.CurrentUser.OpenSubKey(KeyPath(verb, root));
            if (key is not null)
                return true;
        }

        return false;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern void SHChangeNotify(int eventId, uint flags, IntPtr item1, IntPtr item2);

    /// <summary>Avvisa Windows che le associazioni sono cambiate: senza,
    /// le voci compaiono solo dopo aver riavviato Esplora risorse.</summary>
    private static void NotifyShell()
    {
        try
        {
            const int AssocChanged = 0x08000000;
            const uint Flush = 0x1000;
            SHChangeNotify(AssocChanged, Flush, IntPtr.Zero, IntPtr.Zero);
        }
        catch (DllNotFoundException)
        {
            // fuori da Windows non c'è niente da avvisare
        }
    }
}
