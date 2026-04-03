using Aegis.DomainRules;
using Aegis.Protocol;
using Aegis.Common.Errors;
using System.Reflection;
using Xunit;

namespace Aegis.Tests;

public class ProtocolSecurityFuzzTests
{
    private static readonly MethodInfo? FuzzTransitionMethod =
        Type.GetType("Aegis.DomainRules.ProtocolStateMachine, Aegis.DomainRules")?
            .GetMethod("fuzzTransition", BindingFlags.Public | BindingFlags.Static);

    [Fact]
    [Trait("Category", "Fuzz")]
    public void MessageEncoder_Decode_FuzzInputs_NoUnexpectedCrashes()
    {
        var random = new Random(1337);

        for (var i = 0; i < 4000; i++)
        {
            var length = random.Next(0, 512);
            var data = new byte[length];
            random.NextBytes(data);

            try
            {
                _ = MessageEncoder.Decode(data);
            }
            catch (Exception ex) when (
                ex is ProtocolError ||
                ex is ArgumentOutOfRangeException ||
                ex is OverflowException)
            {
                // Expected path for malformed random frames.
            }
        }
    }

    [Fact]
    [Trait("Category", "Fuzz")]
    public void HandshakeStateMachine_FuzzTransitions_NoExceptions()
    {
        Assert.NotNull(FuzzTransitionMethod);

        var random = new Random(2026);

        for (var i = 0; i < 2000; i++)
        {
            var stateCode = random.Next(0, 6);
            var eventCode = random.Next(0, 5);
            var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + random.Next(-120_000, 120_000);
            var clientTimeMs = nowMs + random.Next(-180_000, 180_000);
            var userId = (ulong)random.NextInt64(0, 10_000);

            _ = (bool?)FuzzTransitionMethod!.Invoke(
                null,
                new object[] { stateCode, eventCode, nowMs, clientTimeMs, userId });
        }
    }
}
