using System.Buffers.Binary;
using System.Net.Sockets;
using System.Xml.Linq;
using ArchScada.PlcSimulator;

namespace ArchScada.CoreIntegration.Tests;

public sealed class PlcSimulatorIntegrationTests
{
    [Fact]
    public async Task PlcSimulatorHost_ServesHttpHealthNativeTcp52ChannelsAndModbusTcp0x04EndToEnd()
    {
        await using var host = new PlcSimulatorHost(httpPort: 0, tcpPort: 0, modbusPort: 0);
        await host.StartAsync();

        // 1. HTTP REST endpoints via ScadaServerClient
        using var serverClient = new ScadaServerClient(new ScadaServerOptions
        {
            BaseUrl = $"http://127.0.0.1:{host.HttpPort}",
            Timeout = TimeSpan.FromSeconds(2)
        });

        var health = await serverClient.GetHealthAsync();
        Assert.True(health.IsSuccess);
        Assert.NotNull(health.Data);
        Assert.True(health.Data.IsOnline);
        Assert.Equal(52, health.Data.ActiveChannelCount);

        var tags = await serverClient.GetCurrentTagsAsync();
        Assert.True(tags.IsSuccess);
        Assert.NotNull(tags.Data);
        Assert.Equal(52, tags.Data.Count);
        Assert.Contains(tags.Data, t => t.TagId == "AF001_TPH" && t.Value > 500.0);
        Assert.Contains(tags.Data, t => t.TagId == "DRC101_TPH" && t.Value > 400.0);
        Assert.Contains(tags.Data, t => t.TagId == "CV302_SPD" && t.Value > 1.5);

        var config = await serverClient.GetPlantConfigAsync();
        Assert.True(config.IsSuccess);
        Assert.NotNull(config.Data);
        Assert.Equal(52, config.Data.Tags.Count);

        // 2. Native Rapid SCADA binary TCP protocol via NativeScadaServerAdapter
        using var adapter = new NativeScadaServerAdapter("127.0.0.1", host.TcpPort, timeoutMs: 2000);
        var snapshots1 = adapter.PollChannelTags(out var status1);
        Assert.True(adapter.IsOnline, status1);
        Assert.Equal(52, snapshots1.Count);
        Assert.All(snapshots1, s => Assert.Equal("Good", s.Quality));

        var snapshots2 = adapter.PollChannelTags(out var status2);
        Assert.True(adapter.IsOnline, status2);
        Assert.Equal(52, snapshots2.Count);

        var cr101 = snapshots2.Single(s => s.TagId == "CR101_TPH");
        Assert.InRange(cr101.Value, 550.0, 750.0);
        var ds201 = snapshots2.Single(s => s.TagId == "DS201_PRS");
        Assert.InRange(ds201.Value, 180.0, 240.0);

        // 3. Modbus TCP Function 0x04 (Read Input Registers) and 0x03 (Read Holding Registers)
        using var modbusClient = new TcpClient();
        await modbusClient.ConnectAsync("127.0.0.1", host.ModbusPort);
        using var stream = modbusClient.GetStream();

        byte[] readInputReq = BuildModbusReadRequest(txId: 0x0042, unitId: 1, functionCode: 0x04, startAddr: 0, quantity: 52);
        await stream.WriteAsync(readInputReq);
        byte[] readInputResp = await ReadModbusBytesAsync(stream, 9 + 52 * 2);

        Assert.Equal(0x00, readInputResp[0]);
        Assert.Equal(0x42, readInputResp[1]);
        Assert.Equal(0x04, readInputResp[7]);
        Assert.Equal(52 * 2, readInputResp[8]);

        ushort reg0 = BinaryPrimitives.ReadUInt16BigEndian(readInputResp.AsSpan(9, 2)); // CR101_TPH * 10
        Assert.InRange(reg0, 5500, 7500);

        byte[] readHoldingReq = BuildModbusReadRequest(txId: 0x0043, unitId: 1, functionCode: 0x03, startAddr: 52, quantity: 5);
        await stream.WriteAsync(readHoldingReq);
        byte[] readHoldingResp = await ReadModbusBytesAsync(stream, 9 + 5 * 2);

        Assert.Equal(0x03, readHoldingResp[7]);
        Assert.Equal(10, readHoldingResp[8]);
        ushort reg52ChannelCount = BinaryPrimitives.ReadUInt16BigEndian(readHoldingResp.AsSpan(9, 2));
        ushort reg53RunState = BinaryPrimitives.ReadUInt16BigEndian(readHoldingResp.AsSpan(11, 2));
        Assert.Equal((ushort)52, reg52ChannelCount);
        Assert.Equal((ushort)1, reg53RunState);
    }

    [Fact]
    public async Task PlcSimulatorProgram_RunSelfTestAsync_CompletesWithZeroExitCode()
    {
        int exitCode = await Program.RunSelfTestAsync();
        Assert.Equal(0, exitCode);
    }

    [Fact]
    public void BaseXmlCnlXml_MatchesNativeScadaServerAdapter52ChannelsExactly()
    {
        var cnlXmlPath = ResolveRepoFile("Projects", "ArchCoalPlant", "BaseXML", "Cnl.xml");
        var doc = XDocument.Load(cnlXmlPath);
        var channels = doc.Root!.Elements("Cnl")
            .Select(el => (
                CnlNum: int.Parse(el.Element("CnlNum")!.Value),
                TagCode: el.Element("TagCode")!.Value,
                CnlTypeId: int.Parse(el.Element("CnlTypeID")!.Value)))
            .ToList();

        Assert.Equal(52, channels.Count);
        Assert.All(channels, c => Assert.Equal(1, c.CnlTypeId));

        foreach (var (cnlNum, tagCode, _) in channels)
        {
            Assert.True(NativeScadaServerAdapter.ChannelToTagMap.TryGetValue(cnlNum, out var mappedTag),
                $"Missing CnlNum {cnlNum} ({tagCode}) in NativeScadaServerAdapter.");
            Assert.Equal(tagCode, mappedTag);
        }
    }

    private static byte[] BuildModbusReadRequest(ushort txId, byte unitId, byte functionCode, ushort startAddr, ushort quantity)
    {
        var req = new byte[12];
        BinaryPrimitives.WriteUInt16BigEndian(req.AsSpan(0, 2), txId);
        BinaryPrimitives.WriteUInt16BigEndian(req.AsSpan(2, 2), 0);
        BinaryPrimitives.WriteUInt16BigEndian(req.AsSpan(4, 2), 6);
        req[6] = unitId;
        req[7] = functionCode;
        BinaryPrimitives.WriteUInt16BigEndian(req.AsSpan(8, 2), startAddr);
        BinaryPrimitives.WriteUInt16BigEndian(req.AsSpan(10, 2), quantity);
        return req;
    }

    private static async Task<byte[]> ReadModbusBytesAsync(NetworkStream stream, int expectedLength)
    {
        var buf = new byte[expectedLength];
        int total = 0;
        while (total < expectedLength)
        {
            int read = await stream.ReadAsync(buf.AsMemory(total, expectedLength - total));
            Assert.True(read > 0, "Modbus stream closed prematurely.");
            total += read;
        }

        return buf;
    }

    private static string ResolveRepoFile(params string[] relativeParts)
    {
        foreach (var start in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
        {
            var dir = new DirectoryInfo(start);
            while (dir is not null)
            {
                var candidate = Path.Combine(new[] { dir.FullName }.Concat(relativeParts).ToArray());
                if (File.Exists(candidate))
                {
                    return candidate;
                }

                dir = dir.Parent;
            }
        }

        throw new FileNotFoundException($"Could not locate {Path.Combine(relativeParts)}");
    }
}
