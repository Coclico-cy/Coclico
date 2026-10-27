namespace Coclico.Modules.Diagnostics.Services;

using System.Management;

internal static class WmiHelper
{
    public static bool TryQuery(
        string scope,
        string query,
        out IReadOnlyList<IDictionary<string, object?>> rows)
    {
        List<IDictionary<string, object?>> result = [];
        try
        {
            using ManagementObjectSearcher searcher = new(scope, query);
            foreach (ManagementBaseObject item in searcher.Get())
            {
                Dictionary<string, object?> row = new(StringComparer.OrdinalIgnoreCase);
                foreach (PropertyData property in item.Properties)
                {
                    row[property.Name] = property.Value;
                }

                result.Add(row);
            }

            rows = result;
            return true;
        }
        catch (Exception ex) when (ex is ManagementException or InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            Coclico.Services.LoggingService.LogException(ex, $"WmiHelper.Query({query})");
            rows = [];
            return false;
        }
    }

    public static string? GetString(IDictionary<string, object?> row, string property)
        => row.TryGetValue(property, out object? value) && value is string text && text.Length > 0
            ? text
            : null;

    public static long? GetInt64(IDictionary<string, object?> row, string property)
        => row.TryGetValue(property, out object? value) && value is not null && !Convert.IsDBNull(value)
            ? TryConvertInt64(value)
            : null;

    public static double? GetDouble(IDictionary<string, object?> row, string property)
        => row.TryGetValue(property, out object? value) && value is not null && !Convert.IsDBNull(value)
            ? TryConvertDouble(value)
            : null;

    private static long? TryConvertInt64(object value)
    {
        try
        {
            return Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture);
        }
        catch (Exception ex) when (ex is not System.OutOfMemoryException)
        {
            return null;
        }
    }

    private static double? TryConvertDouble(object value)
    {
        try
        {
            return Convert.ToDouble(value, System.Globalization.CultureInfo.InvariantCulture);
        }
        catch (Exception ex) when (ex is not System.OutOfMemoryException)
        {
            return null;
        }
    }
}
