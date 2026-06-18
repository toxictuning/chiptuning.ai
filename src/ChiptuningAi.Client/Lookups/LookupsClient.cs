namespace ChiptuningAi.Client.Lookups;

/// <summary>
/// Retrieves autocomplete lookup values.
/// No authentication required by the server, but the standard auth header is sent when a token is available.
/// Access via <see cref="ChiptuningAiClient.Lookups"/>.
/// </summary>
public sealed class LookupsClient
{
    private readonly ChiptuningAiClient _client;

    internal LookupsClient(ChiptuningAiClient client) => _client = client;

    /// <summary>
    /// Returns all active values for the given lookup type.
    /// Valid types: VehicleClass, VehicleMake, VehicleModel, EngineType, ECUType, ECUMake, ECUModel, ReadHardware, ReadMode.
    /// </summary>
    public Task<string[]> GetAsync(string type, CancellationToken cancellationToken = default)
        => _client.GetAsync<string[]>($"/api/lookups/{Uri.EscapeDataString(type)}", cancellationToken);

    /// <summary>
    /// Returns distinct field values from actual uploaded files, filtered by parent field selections.
    /// Use this to implement cascade dropdowns.
    /// Supported fields: VehicleMake, VehicleModel, VehicleVariant, ECUMake, ECUModel.
    /// </summary>
    public Task<string[]> GetCascadeAsync(
        string field,
        string? vehicleClass = null,
        string? vehicleMake  = null,
        string? vehicleModel = null,
        string? ecuType      = null,
        string? ecuMake      = null,
        CancellationToken cancellationToken = default)
    {
        var qs = new System.Text.StringBuilder($"/api/lookups/cascade?field={Uri.EscapeDataString(field)}");
        if (!string.IsNullOrEmpty(vehicleClass)) qs.Append($"&vehicleClass={Uri.EscapeDataString(vehicleClass)}");
        if (!string.IsNullOrEmpty(vehicleMake))  qs.Append($"&vehicleMake={Uri.EscapeDataString(vehicleMake)}");
        if (!string.IsNullOrEmpty(vehicleModel)) qs.Append($"&vehicleModel={Uri.EscapeDataString(vehicleModel)}");
        if (!string.IsNullOrEmpty(ecuType))      qs.Append($"&ecuType={Uri.EscapeDataString(ecuType)}");
        if (!string.IsNullOrEmpty(ecuMake))      qs.Append($"&ecuMake={Uri.EscapeDataString(ecuMake)}");
        return _client.GetAsync<string[]>(qs.ToString(), cancellationToken);
    }
}
