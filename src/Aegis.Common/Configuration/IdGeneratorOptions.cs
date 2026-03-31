namespace Aegis.Common.Configuration;

/// <summary>
/// Configuration for distributed ID generation (Snowflake-like algorithm)
/// </summary>
public class IdGeneratorOptions
{
    public const string SectionName = "IdGenerator";

    /// <summary>
    /// Node ID (0-1023) for this server instance.
    /// Must be unique across all instances in a distributed deployment.
    /// Used in Snowflake-like ID generation to prevent collisions across servers.
    /// </summary>
    public int NodeId { get; set; } = 1;
}
