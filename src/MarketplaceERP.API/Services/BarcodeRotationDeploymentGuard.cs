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

        // F477: service name missing or renamed is NOT authorization.
        // Only isolated staging with payments Disabled may enable rotation.
        if (rotation &&
            !(string.Equals(renderServiceName, StagingDatabaseTargetGuard.ServiceName,
                StringComparison.Ordinal) &&
              string.Equals(config["Deployment:Target"],
                StagingDatabaseTargetGuard.TargetMarker, StringComparison.Ordinal) &&
              string.Equals(config["Payments:Provider"], "Disabled",
                StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException(
                "F477: rotación solo permitida en staging aislado con pagos deshabilitados.");
    }

    private static bool ReadFlag(IConfiguration config, string name)
    {
        var value = config[name];
        if (string.IsNullOrWhiteSpace(value)) return false;
        if (bool.TryParse(value, out var result)) return result;
        throw new InvalidOperationException($"F472: configuración de función inválida: {name}.");
    }
}
