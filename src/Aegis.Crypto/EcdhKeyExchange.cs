using System.Security.Cryptography;
using Aegis.Common.Errors;

namespace Aegis.Crypto;

/// <summary>
/// ECDH key exchange for establishing shared secrets between client and server
/// Implements Perfect Forward Secrecy (PFS) mechanism
/// </summary>
public class EcdhKeyExchange : IDisposable
{
    private readonly ECDiffieHellman _privateKey;
    private readonly byte[] _publicKey;
    private readonly byte[] _publicKeyRaw;
    private const int KeySize = 256; // P-256 curve

    public byte[] PublicKey => (byte[])_publicKey.Clone();
    public byte[] PublicKeyRaw => (byte[])_publicKeyRaw.Clone();

    /// <summary>
    /// Generate new ECDH key pair
    /// </summary>
    public static EcdhKeyExchange GenerateKeyPair()
    {
        return new EcdhKeyExchange();
    }

    private EcdhKeyExchange()
    {
        _privateKey = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        _publicKey = _privateKey.ExportSubjectPublicKeyInfo();
        var parameters = _privateKey.ExportParameters(false);
        _publicKeyRaw = ExportRawPublicKey(parameters);
    }

    /// <summary>
    /// Compute shared secret from peer's public key
    /// </summary>
    public byte[] ComputeSharedSecret(ReadOnlySpan<byte> peerPublicKey)
    {
        try
        {
            using var peerKey = ImportPeerKey(peerPublicKey);

            // Use the raw ECDH shared secret and let HKDF derive session keys.
            return _privateKey.DeriveRawSecretAgreement(peerKey.PublicKey);
        }
        catch (Exception ex)
        {
            throw new CryptoError($"Failed to compute shared secret: {ex.Message}", ex);
        }
    }

    public void Dispose()
    {
        _privateKey?.Dispose();
        GC.SuppressFinalize(this);
    }

    private static ECDiffieHellman ImportPeerKey(ReadOnlySpan<byte> peerPublicKey)
    {
        if (peerPublicKey.Length == 65 && peerPublicKey[0] == 0x04)
        {
            var parameters = new ECParameters
            {
                Curve = ECCurve.NamedCurves.nistP256,
                Q = new ECPoint
                {
                    X = peerPublicKey.Slice(1, 32).ToArray(),
                    Y = peerPublicKey.Slice(33, 32).ToArray()
                }
            };

            var ecdh = ECDiffieHellman.Create();
            ecdh.ImportParameters(parameters);
            return ecdh;
        }

        var imported = ECDiffieHellman.Create();
        imported.ImportSubjectPublicKeyInfo(peerPublicKey, out _);
        return imported;
    }

    private static byte[] ExportRawPublicKey(ECParameters parameters)
    {
        if (parameters.Q.X == null || parameters.Q.Y == null)
            throw new CryptoError("ECDH public key coordinates are missing");

        var raw = new byte[65];
        raw[0] = 0x04;
        Buffer.BlockCopy(parameters.Q.X, 0, raw, 1, 32);
        Buffer.BlockCopy(parameters.Q.Y, 0, raw, 33, 32);
        return raw;
    }
}

/// <summary>
/// X3DH protocol implementation for establishing double ratchet sessions
/// Reference: https://signal.org/docs/specifications/x3dh/
/// </summary>
public class X3dhProtocol
{
    private const int PublicKeySize = 32;
    private const int SignatureSize = 64;

    /// <summary>
    /// Server generates identity key pair and signed pre-keys
    /// </summary>
    public class ServerPreKeyBundle
    {
        /// <summary>Server's long-term identity public key</summary>
        public byte[] IdentityKey { get; set; } = Array.Empty<byte>();

        /// <summary>One-time ephemeral pre-keys</summary>
        public List<byte[]> PreKeys { get; set; } = new();

        /// <summary>Signed ephemeral key</summary>
        public byte[] SignedPreKey { get; set; } = Array.Empty<byte>();

        /// <summary>Signature over signed pre-key</summary>
        public byte[] Signature { get; set; } = Array.Empty<byte>();

        /// <summary>Pre-key index to use</summary>
        public uint PreKeyId { get; set; }
    }

    /// <summary>
    /// Client initiates X3DH with server's pre-key bundle
    /// </summary>
    public class ClientInitMessage
    {
        /// <summary>Client's identity public key</summary>
        public byte[] IdentityKey { get; set; } = Array.Empty<byte>();

        /// <summary>Client's ephemeral public key</summary>
        public byte[] EphemeralKey { get; set; } = Array.Empty<byte>();

        /// <summary>Index of one-time pre-key used</summary>
        public uint OneTimePreKeyId { get; set; }

        /// <summary>Initial message encrypted with derived session key</summary>
        public byte[] InitialMessage { get; set; } = Array.Empty<byte>();
    }

    /// <summary>
    /// Perform client-side X3DH key exchange
    /// </summary>
    public static byte[] ClientInitiateKeyExchange(
        ServerPreKeyBundle serverBundle,
        EcdhKeyExchange clientIdentityKey,
        EcdhKeyExchange clientEphemeralKey,
        EcdhKeyExchange clientOneTimeKey)
    {
        var sharedSecrets = new List<byte[]>();

        // DH1: Client ephemeral vs Server signed pre-key
        sharedSecrets.Add(clientEphemeralKey.ComputeSharedSecret(serverBundle.SignedPreKey));

        // DH2: Client identity vs Server signed pre-key
        sharedSecrets.Add(clientIdentityKey.ComputeSharedSecret(serverBundle.SignedPreKey));

        // DH3: Client ephemeral vs Server one-time pre-key
        if (serverBundle.PreKeys.Count > 0)
        {
            sharedSecrets.Add(clientEphemeralKey.ComputeSharedSecret(
                serverBundle.PreKeys[(int)(serverBundle.PreKeyId % serverBundle.PreKeys.Count)]));
        }

        // DH4: Client identity vs Server one-time pre-key
        if (serverBundle.PreKeys.Count > 0)
        {
            sharedSecrets.Add(clientIdentityKey.ComputeSharedSecret(
                serverBundle.PreKeys[(int)(serverBundle.PreKeyId % serverBundle.PreKeys.Count)]));
        }

        // Concatenate all shared secrets and derive final key using KDF
        var combinedSecret = CombineSecrets(sharedSecrets);
        return DeriveSessionKey(combinedSecret);
    }

    /// <summary>
    /// Perform server-side X3DH key exchange
    /// </summary>
    public static byte[] ServerRespondKeyExchange(
        ClientInitMessage clientInit,
        EcdhKeyExchange serverIdentityKey,
        EcdhKeyExchange serverSignedPreKey,
        List<EcdhKeyExchange> serverOneTimeKeys)
    {
        var sharedSecrets = new List<byte[]>();

        // DH1: Server signed pre-key vs Client ephemeral
        sharedSecrets.Add(serverSignedPreKey.ComputeSharedSecret(clientInit.EphemeralKey));

        // DH2: Server signed pre-key vs Client identity
        sharedSecrets.Add(serverSignedPreKey.ComputeSharedSecret(clientInit.IdentityKey));

        // DH3: Server one-time pre-key vs Client ephemeral
        if (clientInit.OneTimePreKeyId < serverOneTimeKeys.Count)
        {
            sharedSecrets.Add(serverOneTimeKeys[(int)clientInit.OneTimePreKeyId]
                .ComputeSharedSecret(clientInit.EphemeralKey));
        }

        // DH4: Server one-time pre-key vs Client identity
        if (clientInit.OneTimePreKeyId < serverOneTimeKeys.Count)
        {
            sharedSecrets.Add(serverOneTimeKeys[(int)clientInit.OneTimePreKeyId]
                .ComputeSharedSecret(clientInit.IdentityKey));
        }

        var combinedSecret = CombineSecrets(sharedSecrets);
        return DeriveSessionKey(combinedSecret);
    }

    private static byte[] CombineSecrets(List<byte[]> secrets)
    {
        var combined = new List<byte>();
        foreach (var secret in secrets)
        {
            combined.AddRange(secret);
        }
        return combined.ToArray();
    }

    private static byte[] DeriveSessionKey(byte[] combinedSecret)
    {
        // Use KDF (Key Derivation Function) to derive session key
        using var hkdf = new Rfc5869DeriveBytes(combinedSecret,
            Array.Empty<byte>(),
            "X3DH"u8.ToArray(),
            32);

        return hkdf.GetBytes(32);
    }
}
