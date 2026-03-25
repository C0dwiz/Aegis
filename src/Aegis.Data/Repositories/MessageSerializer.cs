using System;
using System.Text.Json;
using Tenray.ZoneTree.Serializers;
using Aegis.Data.Entities;

namespace Aegis.Data.Repositories;

public class MessageSerializer : ISerializer<Message>
{
    public Message Deserialize(byte[] bytes)
    {
        return JsonSerializer.Deserialize<Message>(bytes)!;
    }

    public Message Deserialize(Memory<byte> bytes)
    {
        throw new NotImplementedException();
    }

    public byte[] Serialize(Message value)
    {
        return JsonSerializer.SerializeToUtf8Bytes(value);
    }

    public Memory<byte> Serialize(in Message entry)
    {
        throw new NotImplementedException();
    }
}
