namespace MarketplaceERP.Core.Entities;

/// <summary>
/// F369: hashed login identifier window shared by all API instances.
/// Never store a raw email, client IP, password or JWT in this table.
/// </summary>
public sealed class LoginThrottle
{
    public string AccountKey { get; set; } = string.Empty;
    public DateTime WindowStartUtc { get; set; }
    public int Requests { get; set; }
}
