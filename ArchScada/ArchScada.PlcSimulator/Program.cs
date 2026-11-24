using System.Buffers.Binary;
using System.Diagnostics;
using System.Net.Sockets;
using ArchScada.CoreIntegration;

namespace ArchScada.PlcSimulator;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        int httpPort = 5000;
        int tcpPort = 10000;
        int modbusPort = 1502;
        bool selfTest = false;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--http-port" when i + 1 < args.Length && int.TryParse(args[i + 1], out int parsedHttp):
                    httpPort = parsedHttp;
                    i++;
                    break;
                case "--tcp-port" when i + 1 < args.Length && int.TryParse(args[i + 1], out int parsedTcp):
                    tcpPort = parsedTcp;
                    i++;
                    break;
                case "--modbus-port" when i + 1 < args.Length && int.TryParse(args[i + 1], out int parsedModbus):
                    modbusPort = parsedModbus;
                    i++;
                    break;
                case "--self-test":
                    selfTest = true;
                    break;
                case "--help" or "-h":
                    PrintUsage();
                    return 0;
            }
        }

        if (selfTest)
        {
            return await RunSelfTestAsync().ConfigureAwait(false);
        }

        await using var host = new PlcSimulatorHost(httpPort, tcpPort, modbusPort);
        await host.StartAsync().ConfigureAwait(false);

        Console.WriteLine($"ArchScada.PlcSimulator started ({host.Model.ChannelCount} CHPP channels, read-only)");
        Console.WriteLine($"  HTTP REST:  http://127.0.0.1:{host.HttpPort}/api/v1/health");
        Console.WriteLine($"  Native TCP: 127.0.0.1:{host.TcpPort}");
        Console.WriteLine($"  Modbus TCP: 127.0.0.1:{host.ModbusPort} (Registers 0..99)");
        Console.WriteLine("Press Ctrl+C to stop.");

        using var stopSignal = new SemaphoreSlim(0, 1);
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            stopSignal.Release();
        };
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            if (stopSignal.CurrentCount == 0)
            {
                stopSignal.Release();
            }
        };

        await stopSignal.WaitAsync().ConfigureAwait(false);
        return 0;
    }

    public static async Task<int> RunSelfTestAsync()
    {
        var sw = Stopwatch.StartNew();
        await using var host = new PlcSimulatorHost(httpPort: 0, tcpPort: 0, modbusPort: 0);
        await host.StartAsync().ConfigureAwait(false);

        // 1. Verify ScadaServerClient HTTP REST health + tags
        using var httpClient = new ScadaServerClient(new ScadaServerOptions
        {
            BaseUrl = $"http://127.0.0.1:{host.HttpPort}",
            Timeout = TimeSpan.FromSeconds(2)
        });
        var health = await httpClient.GetHealthAsync().ConfigureAwait(false);
        if (!health.IsSuccess || health.Data is null || !health.Data.IsOnline || health.Data.ActiveChannelCount != 52)
        {
            Console.Error.WriteLine($"[FAIL] HTTP health check failed: {health.ErrorMessage}");
            return 1;
        }

        var tags = await httpClient.GetCurrentTagsAsync().ConfigureAwait(false);
        if (!tags.IsSuccess || tags.Data is null || tags.Data.Count != 52)
        {
            Console.Error.WriteLine($"[FAIL] HTTP current tags check failed: count={tags.Data?.Count}");
            return 1;
        }

        // 2. Verify NativeScadaServerAdapter binary TCP protocol (initial + cached poll)
        using var nativeAdapter = new NativeScadaServerAdapter("127.0.0.1", host.TcpPort, timeoutMs: 2000);
        var snapshots1 = nativeAdapter.PollChannelTags(out var status1);
        if (!nativeAdapter.IsOnline || snapshots1.Count != 52)
        {
            Console.Error.WriteLine($"[FAIL] Native TCP initial poll failed: {status1}");
            return 1;
        }

        var snapshots2 = nativeAdapter.PollChannelTags(out var status2);
        if (!nativeAdapter.IsOnline || snapshots2.Count != 52)
        {
            Console.Error.WriteLine($"[FAIL] Native TCP cached poll failed: {status2}");
            return 1;
        }

        // 3. Verify raw Modbus TCP Function 0x04 (Read Input Registers 0..51)
        using var modbusClient = new TcpClient();
        await modbusClient.ConnectAsync("127.0.0.1", host.ModbusPort).ConfigureAwait(false);
        using var modbusStream = modbusClient.GetStream();

        const ushort txId = 0x1234;
        const ushort startAddr = 0;
        const ushort regCount = 52;
        byte[] request = new byte[12];
        BinaryPrimitives.WriteUInt16BigEndian(request.AsSpan(0, 2), txId);
        BinaryPrimitives.WriteUInt16BigEndian(request.AsSpan(2, 2), 0);
        BinaryPrimitives.WriteUInt16BigEndian(request.AsSpan(4, 2), 6);
        request[6] = 1; // Unit ID
        request[7] = 0x04; // Read Input Registers
        BinaryPrimitives.WriteUInt16BigEndian(request.AsSpan(8, 2), startAddr);
        BinaryPrimitives.WriteUInt16BigEndian(request.AsSpan(10, 2), regCount);

        await modbusStream.WriteAsync(request).ConfigureAwait(false);
        await modbusStream.FlushAsync().ConfigureAwait(false);

        byte[] response = new byte[9 + regCount * 2];
        int totalRead = 0;
        while (totalRead < response.Length)
        {
            int read = await modbusStream.ReadAsync(response.AsMemory(totalRead, response.Length - totalRead)).ConfigureAwait(false);
            if (read == 0)
            {
                Console.Error.WriteLine("[FAIL] Modbus TCP stream closed prematurely.");
                return 1;
            }
            totalRead += read;
        }

        ushort respTx = BinaryPrimitives.ReadUInt16BigEndian(response.AsSpan(0, 2));
        byte respFunc = response[7];
        byte byteCount = response[8];
        ushort cr101Scaled = BinaryPrimitives.ReadUInt16BigEndian(response.AsSpan(9, 2));

        if (respTx != txId || respFunc != 0x04 || byteCount != regCount * 2 || cr101Scaled == 0)
        {
            Console.Error.WriteLine($"[FAIL] Invalid Modbus response: tx={respTx:X4}, func={respFunc:X2}, bytes={byteCount}, reg0={cr101Scaled}");
            return 1;
        }

        sw.Stop();
        Console.WriteLine(
            $"[PASS] Self-test succeeded in {sw.ElapsedMilliseconds} ms " +
            $"(HTTP=52 ch, NativeTCP=52 ch, Modbus0x04={regCount} regs, Reg0={cr101Scaled}).");
        return 0;
    }

    private static void PrintUsage()
    {
        Console.WriteLine("ArchScada.PlcSimulator [--http-port 5000] [--tcp-port 10000] [--modbus-port 1502] [--self-test]");
    }
}
