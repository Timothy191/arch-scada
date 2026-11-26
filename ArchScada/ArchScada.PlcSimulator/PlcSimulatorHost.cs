using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using ArchScada.CoreIntegration;
using Scada;
using Scada.Data.Const;
using Scada.Data.Models;
using Scada.Protocol;

namespace ArchScada.PlcSimulator;

/// <summary>
/// In-process multi-protocol CHPP PLC &amp; ScadaServer simulator host supporting:
/// 1. HTTP REST endpoints (<c>/api/v1/health</c>, <c>/api/v1/tags/current</c>, <c>/api/v1/config/plant</c>)
/// 2. Rapid SCADA native binary TCP protocol (<c>ScadaClient</c>)
/// 3. Standard Modbus TCP server (Function Codes <c>0x03</c> and <c>0x04</c> on registers <c>0..99</c>).
/// </summary>
public sealed class PlcSimulatorHost : IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly int _requestedHttpPort;
    private readonly int _requestedTcpPort;
    private readonly int _requestedModbusPort;

    private TcpListener? _httpListener;
    private TcpListener? _tcpListener;
    private TcpListener? _modbusListener;
    private CancellationTokenSource? _cts;
    private readonly List<Task> _backgroundTasks = [];
    private long _nextSessionId = 1000;
    private long _nextCnlListId;
    private readonly Random _random = new();

    public ChppTelemetryModel Model { get; } = new();
    public FaultSimulationOptions Faults { get; } = new();

    public int HttpPort { get; private set; }
    public int TcpPort { get; private set; }
    public int ModbusPort { get; private set; }

    public PlcSimulatorHost(int httpPort = 5000, int tcpPort = 10000, int modbusPort = 1502)
    {
        _requestedHttpPort = httpPort;
        _requestedTcpPort = tcpPort;
        _requestedModbusPort = modbusPort;
    }

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_cts is not null)
        {
            throw new InvalidOperationException("Simulator host has already been started.");
        }

        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        _httpListener = new TcpListener(IPAddress.Loopback, _requestedHttpPort);
        _httpListener.Start();
        HttpPort = ((IPEndPoint)_httpListener.LocalEndpoint).Port;

        _tcpListener = new TcpListener(IPAddress.Loopback, _requestedTcpPort);
        _tcpListener.Start();
        TcpPort = ((IPEndPoint)_tcpListener.LocalEndpoint).Port;

        _modbusListener = new TcpListener(IPAddress.Loopback, _requestedModbusPort);
        _modbusListener.Start();
        ModbusPort = ((IPEndPoint)_modbusListener.LocalEndpoint).Port;

        var token = _cts.Token;
        _backgroundTasks.Add(Task.Run(() => RunSimulationLoopAsync(token), CancellationToken.None));
        _backgroundTasks.Add(Task.Run(() => AcceptLoopAsync(_httpListener, HandleHttpClientAsync, token), CancellationToken.None));
        _backgroundTasks.Add(Task.Run(() => AcceptLoopAsync(_tcpListener, HandleNativeScadaClientAsync, token), CancellationToken.None));
        _backgroundTasks.Add(Task.Run(() => AcceptLoopAsync(_modbusListener, HandleModbusClientAsync, token), CancellationToken.None));

        return Task.CompletedTask;
    }

    private async Task RunSimulationLoopAsync(CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            while (!ct.IsCancellationRequested)
            {
                Model.Step(sw.Elapsed.TotalSeconds);
                await Task.Delay(500, ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown
        }
    }

    private static async Task AcceptLoopAsync(
        TcpListener listener,
        Func<TcpClient, CancellationToken, Task> clientHandler,
        CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            TcpClient? client = null;
            try
            {
                client = await listener.AcceptTcpClientAsync(ct).ConfigureAwait(false);
                client.NoDelay = true;
                _ = Task.Run(() => HandleClientSafelyAsync(client, clientHandler, ct), CancellationToken.None);
            }
            catch (OperationCanceledException)
            {
                client?.Dispose();
                break;
            }
            catch (ObjectDisposedException)
            {
                client?.Dispose();
                break;
            }
            catch (SocketException) when (ct.IsCancellationRequested)
            {
                client?.Dispose();
                break;
            }
            catch
            {
                client?.Dispose();
            }
        }
    }

    private static async Task HandleClientSafelyAsync(
        TcpClient client,
        Func<TcpClient, CancellationToken, Task> handler,
        CancellationToken ct)
    {
        using (client)
        {
            try
            {
                await handler(client, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Expected on shutdown
            }
            catch (IOException)
            {
                // Client disconnected
            }
            catch (SocketException)
            {
                // Client disconnected
            }
        }
    }

    private async Task<bool> ApplyFaultsAsync(CancellationToken ct)
    {
        if (!Faults.HasFaults) return false;

        if (Faults.ConnectionDrop && _random.NextDouble() < 0.05)
        {
            throw new SocketException((int)SocketError.ConnectionReset);
        }

        if (Faults.PacketDropProbability > 0 && _random.NextDouble() < Faults.PacketDropProbability)
        {
            return true; // Drop packet
        }

        if (Faults.MaxJitterMs > 0)
        {
            int delay = _random.Next(Faults.MinJitterMs, Faults.MaxJitterMs);
            if (delay > 0)
            {
                await Task.Delay(delay, ct).ConfigureAwait(false);
            }
        }

        return false;
    }

    private async Task HandleHttpClientAsync(TcpClient client, CancellationToken ct)
    {
        if (await ApplyFaultsAsync(ct).ConfigureAwait(false)) return;

        using var stream = client.GetStream();
        var buffer = new byte[16384];
        int totalRead = 0;

        while (!ct.IsCancellationRequested && totalRead < buffer.Length)
        {
            int read = await stream.ReadAsync(buffer.AsMemory(totalRead, buffer.Length - totalRead), ct).ConfigureAwait(false);
            if (read == 0)
            {
                return;
            }

            totalRead += read;
            if (Encoding.ASCII.GetString(buffer, 0, totalRead).Contains("\r\n\r\n", StringComparison.Ordinal))
            {
                break;
            }
        }

        string requestText = Encoding.ASCII.GetString(buffer, 0, totalRead);
        int lineEnd = requestText.IndexOf("\r\n", StringComparison.Ordinal);
        string requestLine = lineEnd > 0 ? requestText[..lineEnd] : requestText;
        string[] parts = requestLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        string method = parts.Length > 0 ? parts[0] : "GET";
        string rawTarget = parts.Length > 1 ? parts[1] : "/";
        int queryIdx = rawTarget.IndexOf('?');
        string path = queryIdx >= 0 ? rawTarget[..queryIdx] : rawTarget;

        int statusCode;
        string reason;
        byte[] bodyBytes;

        if (!string.Equals(method, "GET", StringComparison.OrdinalIgnoreCase))
        {
            statusCode = 405;
            reason = "Method Not Allowed";
            bodyBytes = Encoding.UTF8.GetBytes("""{"error":"Read-only simulator"}""");
        }
        else if (string.Equals(path, "/api/v1/health", StringComparison.OrdinalIgnoreCase))
        {
            statusCode = 200;
            reason = "OK";
            var health = new ServerHealthStatus
            {
                IsOnline = true,
                ServerVersion = "6.5.0-sim",
                ServerTimeUtc = DateTimeOffset.UtcNow,
                ActiveChannelCount = Model.ChannelCount
            };
            bodyBytes = JsonSerializer.SerializeToUtf8Bytes(health, JsonOptions);
        }
        else if (string.Equals(path, "/api/v1/tags/current", StringComparison.OrdinalIgnoreCase))
        {
            statusCode = 200;
            reason = "OK";
            bodyBytes = JsonSerializer.SerializeToUtf8Bytes(Model.GetAllTagSnapshots(), JsonOptions);
        }
        else if (string.Equals(path, "/api/v1/config/plant", StringComparison.OrdinalIgnoreCase))
        {
            statusCode = 200;
            reason = "OK";
            bodyBytes = JsonSerializer.SerializeToUtf8Bytes(Model.GetPlantConfigSnapshot(), JsonOptions);
        }
        else
        {
            statusCode = 404;
            reason = "Not Found";
            bodyBytes = Encoding.UTF8.GetBytes("""{"error":"Not Found"}""");
        }

        string header = $"HTTP/1.1 {statusCode} {reason}\r\n" +
                        "Content-Type: application/json; charset=utf-8\r\n" +
                        $"Content-Length: {bodyBytes.Length}\r\n" +
                        "Connection: close\r\n\r\n";
        byte[] headerBytes = Encoding.ASCII.GetBytes(header);

        await stream.WriteAsync(headerBytes, ct).ConfigureAwait(false);
        await stream.WriteAsync(bodyBytes, ct).ConfigureAwait(false);
        await stream.FlushAsync(ct).ConfigureAwait(false);
    }

    private async Task HandleNativeScadaClientAsync(TcpClient client, CancellationToken ct)
    {
        if (await ApplyFaultsAsync(ct).ConfigureAwait(false)) return;

        using var stream = client.GetStream();
        var inBuf = new byte[65536];
        var outBuf = new byte[65536];
        long sessionId = Interlocked.Increment(ref _nextSessionId);
        bool isLoggedIn = false;
        var cachedLists = new ConcurrentDictionary<long, int[]>();

        const int fullHeaderSize = ProtocolUtils.HeaderLength + 2;

        while (!ct.IsCancellationRequested)
        {
            bool headerOk = await ReadExactAsync(stream, inBuf, 0, fullHeaderSize, ct).ConfigureAwait(false);
            if (!headerOk)
            {
                break;
            }

            ushort txId = BitConverter.ToUInt16(inBuf, 0);
            int dataLen = BitConverter.ToInt32(inBuf, 2);
            long reqSessionId = BitConverter.ToInt64(inBuf, 6);
            ushort funcId = BitConverter.ToUInt16(inBuf, 14);

            if (dataLen < 10 || dataLen + 6 > inBuf.Length)
            {
                break;
            }

            int remaining = dataLen - 10;
            if (remaining > 0 && !await ReadExactAsync(stream, inBuf, fullHeaderSize, remaining, ct).ConfigureAwait(false))
            {
                break;
            }

            var request = new DataPacket
            {
                TransactionID = txId,
                DataLength = dataLen,
                SessionID = reqSessionId,
                FunctionID = funcId,
                Buffer = inBuf
            };

            var response = new ResponsePacket(request, outBuf)
            {
                SessionID = sessionId
            };

            bool closeAfterSend = false;

            switch (funcId)
            {
                case FunctionID.GetSessionInfo:
                {
                    int idx = ProtocolUtils.ArgumentIndex;
                    BinaryConverter.CopyUInt16(ProtocolUtils.ProtocolVersion, outBuf, ref idx);
                    BinaryConverter.CopyString("ArchScada.PlcSimulator 6.5", outBuf, ref idx);
                    BinaryConverter.CopyString(ProtocolUtils.GetServerStamp(sessionId, secretKey: null), outBuf, ref idx);
                    response.BufferLength = idx;
                    break;
                }

                case FunctionID.Login:
                {
                    int rIdx = ProtocolUtils.ArgumentIndex;
                    _ = BinaryConverter.GetString(inBuf, ref rIdx);
                    _ = BinaryConverter.GetString(inBuf, ref rIdx);
                    _ = BinaryConverter.GetString(inBuf, ref rIdx);
                    _ = BinaryConverter.GetInt32(inBuf, ref rIdx);

                    isLoggedIn = true;
                    int idx = ProtocolUtils.ArgumentIndex;
                    BinaryConverter.CopyBool(true, outBuf, ref idx);
                    BinaryConverter.CopyInt32(1, outBuf, ref idx);
                    BinaryConverter.CopyInt32(RoleID.Application, outBuf, ref idx);
                    BinaryConverter.CopyString(string.Empty, outBuf, ref idx);
                    response.BufferLength = idx;
                    break;
                }

                case FunctionID.GetStatus:
                {
                    int idx = ProtocolUtils.ArgumentIndex;
                    BinaryConverter.CopyBool(true, outBuf, ref idx);
                    BinaryConverter.CopyBool(isLoggedIn, outBuf, ref idx);
                    response.BufferLength = idx;
                    break;
                }

                case FunctionID.TerminateSession:
                {
                    closeAfterSend = true;
                    break;
                }

                case FunctionID.GetCurrentData:
                {
                    if (!isLoggedIn)
                    {
                        response.SetError(ErrorCode.AccessDenied);
                        break;
                    }

                    int rIdx = ProtocolUtils.ArgumentIndex;
                    long cnlListId = BinaryConverter.GetInt64(inBuf, ref rIdx);
                    int[] cnlNums;

                    if (cnlListId > 0)
                    {
                        if (!cachedLists.TryGetValue(cnlListId, out cnlNums!))
                        {
                            cnlListId = 0;
                            cnlNums = Array.Empty<int>();
                        }
                    }
                    else
                    {
                        bool useCache = BinaryConverter.GetBool(inBuf, ref rIdx);
                        cnlNums = BinaryConverter.GetIntArray(inBuf, ref rIdx);
                        if (useCache && cnlNums.Length > 0)
                        {
                            cnlListId = Interlocked.Increment(ref _nextCnlListId);
                            cachedLists[cnlListId] = cnlNums;
                        }
                    }

                    CnlData[] cnlData = Model.GetChannelDataArray(cnlNums);
                    int idx = ProtocolUtils.ArgumentIndex;
                    BinaryConverter.CopyInt64(cnlListId, outBuf, ref idx);
                    BinaryConverter.CopyCnlDataArray(cnlData, outBuf, ref idx);
                    response.BufferLength = idx;
                    break;
                }

                default:
                {
                    response.SetError(ErrorCode.IllegalFunction);
                    break;
                }
            }

            if (response.ErrorCode == ErrorCode.NoError)
            {
                response.Encode();
            }

            await stream.WriteAsync(outBuf.AsMemory(0, response.BufferLength), ct).ConfigureAwait(false);
            await stream.FlushAsync(ct).ConfigureAwait(false);

            if (closeAfterSend)
            {
                break;
            }
        }
    }

    private async Task HandleModbusClientAsync(TcpClient client, CancellationToken ct)
    {
        if (await ApplyFaultsAsync(ct).ConfigureAwait(false)) return;

        using var stream = client.GetStream();
        var mbap = new byte[7];
        var pduBuf = new byte[260];

        while (!ct.IsCancellationRequested)
        {
            if (!await ReadExactAsync(stream, mbap, 0, 7, ct).ConfigureAwait(false))
            {
                break;
            }

            ushort txId = BinaryPrimitives.ReadUInt16BigEndian(mbap.AsSpan(0, 2));
            ushort protoId = BinaryPrimitives.ReadUInt16BigEndian(mbap.AsSpan(2, 2));
            ushort length = BinaryPrimitives.ReadUInt16BigEndian(mbap.AsSpan(4, 2));
            byte unitId = mbap[6];

            if (protoId != 0 || length < 2 || length - 1 > pduBuf.Length)
            {
                break;
            }

            int pduLen = length - 1;
            if (!await ReadExactAsync(stream, pduBuf, 0, pduLen, ct).ConfigureAwait(false))
            {
                break;
            }

            byte funcCode = pduBuf[0];
            byte[] responseFrame;

            if (funcCode is 0x03 or 0x04 && pduLen >= 5)
            {
                ushort startAddr = BinaryPrimitives.ReadUInt16BigEndian(pduBuf.AsSpan(1, 2));
                ushort quantity = BinaryPrimitives.ReadUInt16BigEndian(pduBuf.AsSpan(3, 2));

                if (quantity is 0 or > ChppTelemetryModel.ModbusRegisterCount)
                {
                    responseFrame = BuildModbusException(txId, unitId, funcCode, exceptionCode: 0x03);
                }
                else if (startAddr + quantity > ChppTelemetryModel.ModbusRegisterCount)
                {
                    responseFrame = BuildModbusException(txId, unitId, funcCode, exceptionCode: 0x02);
                }
                else
                {
                    ushort[] regs = Model.ReadModbusRegisters(startAddr, quantity);
                    byte byteCount = (byte)(regs.Length * 2);
                    responseFrame = new byte[7 + 2 + byteCount];
                    BinaryPrimitives.WriteUInt16BigEndian(responseFrame.AsSpan(0, 2), txId);
                    BinaryPrimitives.WriteUInt16BigEndian(responseFrame.AsSpan(2, 2), 0);
                    BinaryPrimitives.WriteUInt16BigEndian(responseFrame.AsSpan(4, 2), (ushort)(3 + byteCount));
                    responseFrame[6] = unitId;
                    responseFrame[7] = funcCode;
                    responseFrame[8] = byteCount;

                    for (var i = 0; i < regs.Length; i++)
                    {
                        BinaryPrimitives.WriteUInt16BigEndian(responseFrame.AsSpan(9 + i * 2, 2), regs[i]);
                    }
                }
            }
            else
            {
                responseFrame = BuildModbusException(txId, unitId, funcCode, exceptionCode: 0x01);
            }

            await stream.WriteAsync(responseFrame, ct).ConfigureAwait(false);
            await stream.FlushAsync(ct).ConfigureAwait(false);
        }
    }

    private static byte[] BuildModbusException(ushort txId, byte unitId, byte funcCode, byte exceptionCode)
    {
        var frame = new byte[9];
        BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(0, 2), txId);
        BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(2, 2), 0);
        BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(4, 2), 3);
        frame[6] = unitId;
        frame[7] = (byte)(funcCode | 0x80);
        frame[8] = exceptionCode;
        return frame;
    }

    private static async Task<bool> ReadExactAsync(NetworkStream stream, byte[] buffer, int offset, int count, CancellationToken ct)
    {
        int total = 0;
        while (total < count)
        {
            int read = await stream.ReadAsync(buffer.AsMemory(offset + total, count - total), ct).ConfigureAwait(false);
            if (read == 0)
            {
                return false;
            }

            total += read;
        }

        return true;
    }

    public async ValueTask DisposeAsync()
    {
        if (_cts is null)
        {
            return;
        }

        await _cts.CancelAsync().ConfigureAwait(false);

        _httpListener?.Stop();
        _tcpListener?.Stop();
        _modbusListener?.Stop();

        try
        {
            await Task.WhenAll(_backgroundTasks).ConfigureAwait(false);
        }
        catch
        {
            // Suppress cancellation/socket exceptions during shutdown.
        }

        _backgroundTasks.Clear();
        _cts.Dispose();
        _cts = null;
    }
}
