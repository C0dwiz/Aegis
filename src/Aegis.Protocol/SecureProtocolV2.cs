using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace Aegis.Protocol;

public enum V2HandshakeStage : byte
{
    ClientHello = 1,
    ServerHello = 2,
    ClientFinish = 3
}

public readonly record struct ApiCredentials(int ApiId, string ApiHash);

public readonly record struct SessionTrafficKeys(
    byte[] ClientToServerKey,
    byte[] ServerToClientKey,
    byte[] AckKey);

public sealed record ClientHelloV2(
    int ApiId,
    string AppHash,
    byte[] ClientEphemeralPublicKey,
    byte[] ClientNonce,
    long ClientUnixTimeMs,
    string TransportHint);

public sealed record ServerHelloV2(
    byte[] ServerEphemeralPublicKey,
    byte[] ServerNonce,
    byte[] Cookie,
    long ServerUnixTimeMs,
    long KeyId,
    byte[] Signature);

public sealed record ClientFinishV2(byte[] Cookie, byte[] Proof);

public static class ApiCredentialIssuer
{
    private const int AppHashBytes = 32;

    public static string GenerateApiHash()
    {
        Span<byte> raw = stackalloc byte[AppHashBytes];
        RandomNumberGenerator.Fill(raw);
        return Convert.ToHexString(raw).ToLowerInvariant();
    }

    public static bool VerifyApiHash(string expected, string actual)
    {
        if (string.IsNullOrWhiteSpace(expected) || string.IsNullOrWhiteSpace(actual))
        {
            return false;
        }

        var left = Encoding.UTF8.GetBytes(expected);
        var right = Encoding.UTF8.GetBytes(actual);
        return CryptographicOperations.FixedTimeEquals(left, right);
    }
}

public static class V2KeySchedule
{
    public static SessionTrafficKeys DeriveSessionKeys(
        ReadOnlySpan<byte> sharedSecret,
        ReadOnlySpan<byte> clientNonce,
        ReadOnlySpan<byte> serverNonce,
        ReadOnlySpan<byte> transcriptHash)
    {
        var salt = new byte[clientNonce.Length + serverNonce.Length];
        clientNonce.CopyTo(salt);
        serverNonce.CopyTo(salt.AsSpan(clientNonce.Length));

        var hsSecret = HkdfSha256(sharedSecret, salt, "aegis-v2/hs"u8, 32);

        var infoPrefix = new byte[transcriptHash.Length + 1];
        transcriptHash.CopyTo(infoPrefix);

        infoPrefix[infoPrefix.Length - 1] = 0x01;
        var c2s = HkdfSha256(hsSecret, ReadOnlySpan<byte>.Empty, infoPrefix, 32);

        infoPrefix[infoPrefix.Length - 1] = 0x02;
        var s2c = HkdfSha256(hsSecret, ReadOnlySpan<byte>.Empty, infoPrefix, 32);

        infoPrefix[infoPrefix.Length - 1] = 0x03;
        var ack = HkdfSha256(hsSecret, ReadOnlySpan<byte>.Empty, infoPrefix, 32);

        CryptographicOperations.ZeroMemory(hsSecret);
        return new SessionTrafficKeys(c2s, s2c, ack);
    }

    public static byte[] ComputeClientFinishProof(
        ReadOnlySpan<byte> handshakeSecret,
        ReadOnlySpan<byte> transcriptHash)
    {
        var material = new byte[transcriptHash.Length + 6];
        transcriptHash.CopyTo(material);
        Encoding.ASCII.GetBytes("finish").CopyTo(material, transcriptHash.Length);

        using var hmac = new HMACSHA256(handshakeSecret.ToArray());
        return hmac.ComputeHash(material);
    }

    private static byte[] HkdfSha256(
        ReadOnlySpan<byte> ikm,
        ReadOnlySpan<byte> salt,
        ReadOnlySpan<byte> info,
        int length)
    {
        var realSalt = salt.IsEmpty ? new byte[32] : salt.ToArray();
        using var extract = new HMACSHA256(realSalt);
        var prk = extract.ComputeHash(ikm.ToArray());

        var output = new byte[length];
        Span<byte> t = stackalloc byte[32];
        var produced = 0;
        byte counter = 1;

        while (produced < length)
        {
            using var expand = new HMACSHA256(prk);
            var input = new byte[t.Length + info.Length + 1];
            t.CopyTo(input);
            info.CopyTo(input.AsSpan(t.Length));
            input[input.Length - 1] = counter;

            var block = expand.ComputeHash(input);
            block.AsSpan().CopyTo(t);

            var toCopy = Math.Min(block.Length, length - produced);
            block.AsSpan(0, toCopy).CopyTo(output.AsSpan(produced));
            produced += toCopy;
            counter++;
        }

        CryptographicOperations.ZeroMemory(prk);
        CryptographicOperations.ZeroMemory(t);
        return output;
    }
}

public static class TlLikeCodec
{
    public static byte[] SerializeClientHello(ClientHelloV2 hello)
    {
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true);

        bw.Write(0xA2A3A401u); // constructor id
        bw.Write(hello.ApiId);
        WriteString(bw, hello.AppHash);
        WriteBytes(bw, hello.ClientEphemeralPublicKey);
        WriteBytes(bw, hello.ClientNonce);
        bw.Write(hello.ClientUnixTimeMs);
        WriteString(bw, hello.TransportHint);

        bw.Flush();
        return ms.ToArray();
    }

    public static ClientHelloV2 DeserializeClientHello(ReadOnlySpan<byte> payload)
    {
        using var ms = new MemoryStream(payload.ToArray());
        using var br = new BinaryReader(ms, Encoding.UTF8, leaveOpen: true);

        var constructor = br.ReadUInt32();
        if (constructor != 0xA2A3A401u)
        {
            throw new InvalidOperationException("Unexpected constructor id for ClientHelloV2");
        }

        var apiId = br.ReadInt32();
        var appHash = ReadString(br);
        var clientPub = ReadBytes(br);
        var clientNonce = ReadBytes(br);
        var clientTime = br.ReadInt64();
        var transportHint = ReadString(br);

        return new ClientHelloV2(apiId, appHash, clientPub, clientNonce, clientTime, transportHint);
    }

    private static void WriteString(BinaryWriter bw, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value ?? string.Empty);
        bw.Write(bytes.Length);
        bw.Write(bytes);
        WritePadding(bw, bytes.Length);
    }

    private static string ReadString(BinaryReader br)
    {
        var len = br.ReadInt32();
        if (len < 0 || len > 1_048_576)
        {
            throw new InvalidOperationException("Invalid string length");
        }

        var bytes = br.ReadBytes(len);
        SkipPadding(br, len);
        return Encoding.UTF8.GetString(bytes);
    }

    private static void WriteBytes(BinaryWriter bw, byte[] value)
    {
        var data = value ?? Array.Empty<byte>();
        bw.Write(data.Length);
        bw.Write(data);
        WritePadding(bw, data.Length);
    }

    private static byte[] ReadBytes(BinaryReader br)
    {
        var len = br.ReadInt32();
        if (len < 0 || len > 1_048_576)
        {
            throw new InvalidOperationException("Invalid byte-array length");
        }

        var bytes = br.ReadBytes(len);
        SkipPadding(br, len);
        return bytes;
    }

    private static void WritePadding(BinaryWriter bw, int payloadLength)
    {
        var pad = (4 - (payloadLength % 4)) % 4;
        if (pad == 0)
        {
            return;
        }

        Span<byte> zeros = stackalloc byte[4];
        bw.Write(zeros.Slice(0, pad));
    }

    private static void SkipPadding(BinaryReader br, int payloadLength)
    {
        var pad = (4 - (payloadLength % 4)) % 4;
        if (pad > 0)
        {
            _ = br.ReadBytes(pad);
        }
    }
}

public sealed class ReplayWindow
{
    private readonly SortedSet<ulong> _seen = [];
    private readonly int _capacity;

    public ReplayWindow(int capacity = 4096)
    {
        if (capacity < 128)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity));
        }

        _capacity = capacity;
    }

    public bool TryAccept(ulong seq)
    {
        lock (_seen)
        {
            if (_seen.Contains(seq))
            {
                return false;
            }

            _seen.Add(seq);
            while (_seen.Count > _capacity)
            {
                _seen.Remove(_seen.Min);
            }

            return true;
        }
    }
}
