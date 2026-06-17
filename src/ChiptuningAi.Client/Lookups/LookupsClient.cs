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
}
