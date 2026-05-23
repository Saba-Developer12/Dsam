using System.Globalization;

namespace Dsam.Data.Sqlite;

internal static class AddressStorage
{
    public static string Format(ulong address) =>
        address.ToString("X16", CultureInfo.InvariantCulture);

    public static ulong Parse(string address) =>
        ulong.Parse(address, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
}
