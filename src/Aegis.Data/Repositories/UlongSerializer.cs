using System;
using Tenray.ZoneTree.Serializers;

namespace Aegis.Data.Repositories;

public class UlongSerializer : ISerializer<ulong>
{
    public ulong Deserialize(byte[] bytes)
    {
        return BitConverter.ToUInt64(bytes, 0);
    }

    public ulong Deserialize(Memory<byte> bytes)
    {
        throw new NotImplementedException();
    }

    public byte[] Serialize(ulong value)
    {
        return BitConverter.GetBytes(value);
    }

    public Memory<byte> Serialize(in ulong entry)
    {
        throw new NotImplementedException();
    }
}
