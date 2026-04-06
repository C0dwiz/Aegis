using MessagePack;
using MessagePack.Resolvers;

namespace Aegis.Handlers;

/// <summary>
/// Binary payload serializer based on MessagePack.
/// Uses ContractlessStandardResolver so no [MessagePackObject] attributes are required.
/// </summary>
public static class PayloadSerializer
{
    private static readonly MessagePackSerializerOptions Options =
        MessagePackSerializerOptions.Standard
            .WithResolver(ContractlessStandardResolver.Instance);

    public static byte[] Serialize<T>(T value) =>
        MessagePackSerializer.Serialize(value, Options);

    public static T? Deserialize<T>(byte[] data) =>
        MessagePackSerializer.Deserialize<T>(data, Options);

    public static T? Deserialize<T>(ReadOnlyMemory<byte> data) =>
        MessagePackSerializer.Deserialize<T>(data, Options);
}
