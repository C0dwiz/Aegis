namespace Aegis.Handlers;

public interface IAntiSpamClient
{
    Task<bool> CheckMessageAsync(ulong connectionId, byte[] message);
}

public class AntiSpamClient : IAntiSpamClient
{
    // Протокольный интерфейс для внешнего сервиса антиспама (реализован на Haskell)
    public async Task<bool> CheckMessageAsync(ulong connectionId, byte[] message)
    {
        // В настоящее время реализуется вызов gRPC/Thrift к внешнему сервису
        // антиспама (реализованному на Haskell КОТОРЫЙ Я ПОТОМ НАПИШУ)
        await Task.Delay(1); // TODO: надо сделать правильный вызов gRPC/Thrift
        
        // Для тестирования в настоящее время все сообщения разрешаются
        return true;
    }
}
