namespace ArchScada.PlcSimulator;

public sealed class FaultSimulationOptions
{
    public double PacketDropProbability { get; set; } = 0.0;
    public int MinJitterMs { get; set; } = 0;
    public int MaxJitterMs { get; set; } = 0;
    public double StaleDataProbability { get; set; } = 0.0;
    public double InvalidDataProbability { get; set; } = 0.0;
    public bool ConnectionDrop { get; set; } = false;

    public bool HasFaults => PacketDropProbability > 0 || MaxJitterMs > 0 || StaleDataProbability > 0 || InvalidDataProbability > 0 || ConnectionDrop;
}
