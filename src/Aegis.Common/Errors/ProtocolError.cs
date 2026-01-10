namespace Aegis.Common.Errors;

public class ProtocolError : Exception
{
    public ProtocolError(string message) : base(message) { }
    public ProtocolError(string message, Exception inner) : base(message, inner) { }
}

public class CryptoError : Exception
{
    public CryptoError(string message) : base(message) { }
    public CryptoError(string message, Exception inner) : base(message, inner) { }
}

public class TransportError : Exception
{
    public TransportError(string message) : base(message) { }
    public TransportError(string message, Exception inner) : base(message, inner) { }
}
