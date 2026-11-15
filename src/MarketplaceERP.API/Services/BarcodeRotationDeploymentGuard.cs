namespace MarketplaceERP.API.Services;

/// <summary>
/// F472: release cutover protection. Rotations cannot be enabled by a typo,
/// independently of the alias reader, or on the current unapproved Production.
/// No connection or database mutation; startup fails before HTTP is opened.
/// </summary>
public static class BarcodeRotationDeploymentGuard
{
    public static void Validate(IConfiguration config, string? renderServiceName)
    {
        var lookup = ReadFlag(config, "Barcodes:EnableHistoricalAliasLookup");
        var rotation = ReadFlag(config, "Barcodes:EnableEanRotation");

        if (rotation && !lookup)
            throw new InvalidOperationException(
                "F472: la rotación requiere lectura histórica de EAN habilitada.");

        // This remains a deliberate NO-GO until physical reader/label testing,
        // data recovery and explicit #65/#70 Production authorization.
        if (rotation && string.Equals(renderServiceName,
                "marketplaceerp-api", StringComparison.Ordinal))
            throw new InvalidOperationException(
                "F472: rotación de EAN en producción bloqueada hasta aprobación del release.");
    }

    private static bool ReadFlag(IConfiguration config, string name)
    {
        var value = config[name];
        if (string.IsNullOrWhiteSpace(value)) return false;
        if (bool.TryParse(value, out var result)) return result;
        throw new InvalidOperationException($"F472: configuración de función inválida: {name}.");
    }
}
