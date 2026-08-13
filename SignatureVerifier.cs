using System.Security.Cryptography;

namespace NexusDisplay;

internal static class SignatureVerifier
{
    public static bool VerifyEcdsaP256(string subjectPublicKeyInfoBase64,
                                      ReadOnlySpan<byte> payload,
                                      ReadOnlySpan<byte> signature)
    {
        try
        {
            using ECDsa key = ECDsa.Create();
            key.ImportSubjectPublicKeyInfo(Convert.FromBase64String(subjectPublicKeyInfoBase64), out int read);
            return read > 0 && key.KeySize == 256 &&
                   key.VerifyData(payload, signature, HashAlgorithmName.SHA256,
                       DSASignatureFormat.Rfc3279DerSequence);
        }
        catch (CryptographicException)
        {
            return false;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
