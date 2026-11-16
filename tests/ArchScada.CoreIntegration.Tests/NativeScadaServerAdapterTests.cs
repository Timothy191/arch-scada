using System.Net;
using System.Net.Sockets;
using ArchScada.CoreIntegration;
using Scada.Data.Const;
using Scada.Data.Models;

namespace ArchScada.CoreIntegration.Tests;

public sealed class NativeScadaServerAdapterTests
{
    [Fact]
    public void ChannelToTagMap_MapsAll34ChppChannelsFromCnlXml()
    {
        Assert.Equal(34, NativeScadaServerAdapter.ChannelToTagMap.Count);
        Assert.Equal(34, NativeScadaServerAdapter.ChannelNumbers.Count);

        Assert.Equal("CR101_TPH", NativeScadaServerAdapter.ChannelToTagMap[101]);
        Assert.Equal("CR101_KW", NativeScadaServerAdapter.ChannelToTagMap[102]);
        Assert.Equal("CR101_BRG", NativeScadaServerAdapter.ChannelToTagMap[103]);
        Assert.Equal("TK201_LVL", NativeScadaServerAdapter.ChannelToTagMap[201]);
        Assert.Equal("TK201_TRQ", NativeScadaServerAdapter.ChannelToTagMap[202]);
        Assert.Equal("TK201_TRB", NativeScadaServerAdapter.ChannelToTagMap[203]);
        Assert.Equal("TK301_SG", NativeScadaServerAdapter.ChannelToTagMap[301]);
        Assert.Equal("CF501_MST", NativeScadaServerAdapter.ChannelToTagMap[504]);
        Assert.Equal("AN601_ASH", NativeScadaServerAdapter.ChannelToTagMap[601]);
        Assert.Equal("AN601_MST", NativeScadaServerAdapter.ChannelToTagMap[602]);

        using var adapter = new NativeScadaServerAdapter();
        Assert.Equal("127.0.0.1", adapter.ConnectionOptions.Host);
        Assert.Equal(10000, adapter.ConnectionOptions.Port);
        Assert.True(adapter.TryGetTagId(101, out var cr101Tph));
        Assert.Equal("CR101_TPH", cr101Tph);
        Assert.True(adapter.TryGetChannelNumber("AN601_ASH", out var an601Cnl));
        Assert.Equal(601, an601Cnl);
    }

    [Fact]
    public void PollChannelTags_ReturnsGracefulOfflineStaleStatus_WhenScadaServerIsUnreachable()
    {
        int unusedPort;
        using (var listener = new TcpListener(IPAddress.Loopback, 0))
        {
            listener.Start();
            unusedPort = ((IPEndPoint)listener.LocalEndpoint).Port;
        }

        using var adapter = new NativeScadaServerAdapter(
            host: "127.0.0.1",
            port: unusedPort,
            timeoutMs: 250);

        var snapshots = adapter.PollChannelTags(out var statusMessage);

        Assert.False(adapter.IsOnline);
        Assert.True(adapter.IsOffline);
        Assert.False(string.IsNullOrWhiteSpace(statusMessage));
        Assert.Contains("Offline", statusMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(34, snapshots.Count);
        Assert.All(snapshots, s => Assert.Equal("Stale", s.Quality));
    }

    [Fact]
    public void PollChannelTags_MapsLiveChannelDataToTagValueSnapshots_WhenServerResponds()
    {
        using var adapter = new NativeScadaServerAdapter(
            connectionOptions: NativeScadaServerAdapter.CreateDefaultConnectionOptions(),
            customDataFetcher: cnlNums =>
            {
                var result = new CnlData[cnlNums.Length];
                for (var i = 0; i < cnlNums.Length; i++)
                {
                    result[i] = cnlNums[i] switch
                    {
                        101 => new CnlData(645.2, CnlStatusID.Defined),
                        201 => new CnlData(71.8, CnlStatusID.Defined),
                        301 => new CnlData(1.53, CnlStatusID.Defined),
                        601 => new CnlData(9.2, CnlStatusID.Defined),
                        _ => new CnlData(10.0, CnlStatusID.Defined)
                    };
                }

                return result;
            });

        var snapshots = adapter.PollChannelTags(out var statusMessage);

        Assert.True(adapter.IsOnline);
        Assert.Contains("Online", statusMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(34, snapshots.Count);

        var cr101 = snapshots.Single(s => s.TagId == "CR101_TPH");
        Assert.Equal(645.2, cr101.Value);
        Assert.Equal("Good", cr101.Quality);

        var tk301 = snapshots.Single(s => s.TagId == "TK301_SG");
        Assert.Equal(1.53, tk301.Value);
        Assert.Equal("Good", tk301.Quality);
    }
}
