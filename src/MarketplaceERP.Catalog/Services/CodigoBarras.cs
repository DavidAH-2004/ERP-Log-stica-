namespace MarketplaceERP.Catalog.Services;

/// <summary>
/// Cálculo y validación de códigos EAN-13.
/// Los códigos internos del ERP usan el prefijo de circulación restringida "20"
/// (reservado por GS1 para uso interno): "20" + consecutivo de 10 dígitos + dígito verificador.
/// </summary>
public static class CodigoBarras
{
    public const string PrefijoInterno = "20";
    public const long ConsecutivoMaximo = 9_999_999_999;

    private const int LargoEan13 = 13;
    private const int LargoConsecutivo = 10;

    /// <summary>Calcula el dígito verificador EAN-13 a partir de los 12 primeros dígitos.</summary>
    public static char CalcularDigitoVerificador(string doceDigitos)
    {
        if (doceDigitos is null || doceDigitos.Length != LargoEan13 - 1 || !SonDigitosAscii(doceDigitos))
            throw new ArgumentException("Se requieren exactamente 12 dígitos.", nameof(doceDigitos));

        var suma = 0;
        for (var i = 0; i < doceDigitos.Length; i++)
        {
            var digito = doceDigitos[i] - '0';
            // Posiciones impares (1ª, 3ª, …) pesan 1; pares pesan 3.
            suma += i % 2 == 0 ? digito : digito * 3;
        }

        return (char)('0' + (10 - suma % 10) % 10);
    }

    /// <summary>Indica si el texto es un EAN-13 válido. No recorta espacios.</summary>
    public static bool EsEan13Valido(string? codigo)
    {
        if (codigo is null || codigo.Length != LargoEan13 || !SonDigitosAscii(codigo))
            return false;

        return CalcularDigitoVerificador(codigo[..12]) == codigo[12];
    }

    /// <summary>Indica si es un EAN-13 válido generado por el ERP (prefijo interno).</summary>
    public static bool EsInterno(string? codigo)
        => EsEan13Valido(codigo) && codigo!.StartsWith(PrefijoInterno, StringComparison.Ordinal);

    /// <summary>Genera el código interno correspondiente a un consecutivo.</summary>
    public static string GenerarInterno(long consecutivo)
    {
        if (consecutivo < 1 || consecutivo > ConsecutivoMaximo)
            throw new ArgumentOutOfRangeException(nameof(consecutivo), consecutivo,
                $"El consecutivo debe estar entre 1 y {ConsecutivoMaximo}.");

        var doce = PrefijoInterno + consecutivo.ToString().PadLeft(LargoConsecutivo, '0');
        return doce + CalcularDigitoVerificador(doce);
    }

    /// <summary>Obtiene el consecutivo de un código interno.</summary>
    public static long ExtraerConsecutivo(string codigoInterno)
    {
        if (!EsInterno(codigoInterno))
            throw new ArgumentException("No es un código interno válido.", nameof(codigoInterno));

        return long.Parse(codigoInterno.AsSpan(PrefijoInterno.Length, LargoConsecutivo));
    }

    private static bool SonDigitosAscii(string texto)
    {
        foreach (var c in texto)
        {
            if (c is < '0' or > '9')
                return false;
        }
        return true;
    }
}
