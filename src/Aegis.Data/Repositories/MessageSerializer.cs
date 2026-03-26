using System;
using System.Text.Json;
using MessagePack;
using MessagePack.Resolvers;
using Tenray.ZoneTree.Serializers;
using Aegis.Data.Entities;

namespace Aegis.Data.Repositories;

public class MessageSerializer : ISerializer<Message>
{
    private static readonly MessagePackSerializerOptions Options =
        MessagePackSerializerOptions.Standard
            .WithResolver(ContractlessStandardResolver.Instance);

    public Message Deserialize(byte[] bytes)
    {
        // MessagePack frames start with 0x80-0x9F (fixmap) or 0xDE/0xDF (map).
        // JSON always starts with '{' (0x7B). Use this to migrate legacy records.
        if (bytes.Length > 0 && bytes[0] == (byte)'{' || bytes[0] == (byte)'[')
        {
            return JsonSerializer.Deserialize<Message>(bytes)
                ?? throw new InvalidOperationException("Failed to deserialize legacy JSON message");
        }

        return MessagePackSerializer.Deserialize<Message>(bytes, Options);
    }

    public Message Deserialize(Memory<byte> bytes)
    {
        throw new NotImplementedException();
    }

    public byte[] Serialize(Message value)
    {
        return MessagePackSerializer.Serialize(value, Options);
    }

    public Memory<byte> Serialize(in Message entry)
    {
        throw new NotImplementedException();
    }
}
