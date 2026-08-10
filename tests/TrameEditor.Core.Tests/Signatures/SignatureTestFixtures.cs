using System.Text;
using Org.BouncyCastle.Asn1.X509;
using Org.BouncyCastle.Cms;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Operators;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Math;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.X509;

namespace TrameEditor.Core.Tests.Signatures;

/// <summary>
/// Certificati e buste firmate costruiti sul momento: i test sulle firme non
/// devono dipendere da file esterni che scadono o che non si possono versionare.
/// </summary>
internal static class SignatureTestFixtures
{
    internal sealed record Identity(X509Certificate Certificate, AsymmetricKeyParameter PrivateKey);

    internal static Identity CreateIdentity(string commonName,
        DateTime? notBefore = null, DateTime? notAfter = null)
    {
        var random = new SecureRandom();
        var generator = new RsaKeyPairGenerator();
        generator.Init(new KeyGenerationParameters(random, 2048));
        var keys = generator.GenerateKeyPair();

        var name = new X509Name($"CN={commonName}, O=Prova, C=IT");
        var certificateGenerator = new X509V3CertificateGenerator();
        certificateGenerator.SetSerialNumber(BigInteger.ValueOf(DateTime.UtcNow.Ticks & 0x7FFFFFFF));
        certificateGenerator.SetIssuerDN(name);
        certificateGenerator.SetSubjectDN(name);
        certificateGenerator.SetNotBefore(notBefore ?? DateTime.UtcNow.AddDays(-1));
        certificateGenerator.SetNotAfter(notAfter ?? DateTime.UtcNow.AddYears(1));
        certificateGenerator.SetPublicKey(keys.Public);

        var signatureFactory = new Asn1SignatureFactory("SHA256WITHRSA", keys.Private, random);
        return new Identity(certificateGenerator.Generate(signatureFactory), keys.Private);
    }

    /// <summary>Costruisce una busta .p7m che racchiude i byte indicati.</summary>
    internal static byte[] CreateP7m(byte[] content, Identity identity)
    {
        var generator = new CmsSignedDataGenerator();
        generator.AddSigner(identity.PrivateKey, identity.Certificate, CmsSignedGenerator.DigestSha256);
        generator.AddCertificate(identity.Certificate);
        return generator.Generate(new CmsProcessableByteArray(content), encapsulate: true).GetEncoded();
    }

    internal static byte[] TextBytes(string text) => Encoding.UTF8.GetBytes(text);
}
