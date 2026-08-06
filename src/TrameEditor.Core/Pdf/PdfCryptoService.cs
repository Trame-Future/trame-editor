using System.Text;
using iText.Kernel.Exceptions;
using iText.Kernel.Pdf;
using Path = System.IO.Path;

namespace TrameEditor.Core.Pdf;

/// <summary>Apertura di PDF protetti da password e protezione di PDF (AES-256).</summary>
public static class PdfCryptoService
{
    public static bool IsPasswordProtected(string path)
    {
        var reader = new PdfReader(path);
        try
        {
            using var document = new PdfDocument(reader);
            return false;
        }
        catch (BadPasswordException)
        {
            CloseQuietly(reader);
            return true;
        }
    }

    private static void CloseQuietly(PdfReader reader)
    {
        try
        {
            reader.Close();
        }
        catch
        {
            // già chiuso o mai aperto
        }
    }

    /// <summary>Scrive una copia decifrata del PDF. Password errata ⇒
    /// <see cref="BadPasswordException"/>.</summary>
    public static void Decrypt(string sourcePath, string targetPath, string password)
    {
        // Convalida della password in sola lettura, prima di aprire qualunque
        // file di output: una password errata non deve lasciare handle aperti.
        var probe = CreateReader(sourcePath, password);
        try
        {
            using (new PdfDocument(probe))
            {
            }
        }
        catch
        {
            CloseQuietly(probe);
            throw;
        }

        WriteAtomic(targetPath, tempPath =>
        {
            using var document = new PdfDocument(CreateReader(sourcePath, password),
                new PdfWriter(tempPath));
        });
    }

    private static PdfReader CreateReader(string sourcePath, string password)
    {
        var reader = new PdfReader(sourcePath,
            new ReaderProperties().SetPassword(Encoding.UTF8.GetBytes(password)));
        reader.SetUnethicalReading(true);
        return reader;
    }

    /// <summary>Scrive una copia protetta con password (AES-256, tutti i permessi).</summary>
    public static void Encrypt(string sourcePath, string targetPath, string password)
    {
        WriteAtomic(targetPath, tempPath =>
        {
            var passwordBytes = Encoding.UTF8.GetBytes(password);
            var writerProperties = new WriterProperties().SetStandardEncryption(
                passwordBytes, passwordBytes,
                EncryptionConstants.ALLOW_PRINTING | EncryptionConstants.ALLOW_COPY |
                EncryptionConstants.ALLOW_MODIFY_CONTENTS,
                EncryptionConstants.ENCRYPTION_AES_256);
            using var document = new PdfDocument(new PdfReader(sourcePath),
                new PdfWriter(tempPath, writerProperties));
        });
    }

    private static void WriteAtomic(string targetPath, Action<string> writeTo)
    {
        var fullTarget = Path.GetFullPath(targetPath);
        var directory = Path.GetDirectoryName(fullTarget)
            ?? throw new ArgumentException($"Percorso senza cartella: {targetPath}", nameof(targetPath));
        var tempPath = Path.Combine(directory, $".{Path.GetFileName(fullTarget)}.{Guid.NewGuid():N}.tmp");
        try
        {
            writeTo(tempPath);
            if (File.Exists(fullTarget))
                File.Replace(tempPath, fullTarget, destinationBackupFileName: null);
            else
                File.Move(tempPath, fullTarget);
        }
        finally
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
    }
}
