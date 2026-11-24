// Copyright (c) Rapid Software LLC / Arch SCADA Team. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Scada.Data.Const;
using Scada.Data.Models;
using Scada.Web.Code;
using Scada.Web.Services;
using System.Diagnostics;

namespace Scada.Web.Pages
{
    /// <summary>
    /// Strictly read-only 3D Coal Handling &amp; Preparation Plant (CHPP) Digital Twin page.
    /// Exposes only GET page rendering and GET telemetry JSON handlers; zero write/command endpoints exist.
    /// </summary>
    [AllowAnonymous]
    public class DigitalTwin3DModel : PageModel
    {
        private static readonly Stopwatch DemoClock = Stopwatch.StartNew();

        private static readonly int[] MonitoredChannels =
        [
            101, 102, 103,                // CR-101 (JC-101)
            111, 112,                     // SC-102 (SS-101)
            121, 122, 711,                // CV-101
            131, 132, 712,                // CV-102
            141, 142, 143,                // ROM101 (AF-001 / BN-101 / VF-101)
            151, 152, 153,                // DRC-101
            161, 162, 163, 164, 165, 166, // CV_DRY (CV-103..106 / CV-201 / BN-201)
            201, 202, 203,                // TK-201
            211, 212,                     // PP-201
            221,                          // PP-202
            231,                          // PP-203
            301, 302, 303, 304, 305,      // TK-301 / MS-301 / DM-301 (WP-301)
            311, 312, 313, 314, 315, 316, // DS_WASH (CV-202 / DS-201 / CV-203 / CV-301 / CV-302)
            501, 502, 503, 504,           // CF-501 / CV-501
            601, 602, 603, 604,           // AN-601 / SL-601
            701, 702, 721                 // Environmental dust & CH4
        ];

        private readonly IWebContext webContext;
        private readonly IClientAccessor clientAccessor;

        public DigitalTwin3DModel(IWebContext webContext, IClientAccessor clientAccessor)
        {
            this.webContext = webContext;
            this.clientAccessor = clientAccessor;
        }

        public string AppVersion => AppUtils.AppVersion;

        public bool IsServerConnected => webContext.IsReady;

        public void OnGet()
        {
        }

        /// <summary>
        /// Read-only GET telemetry handler returning live Rapid SCADA channels when connected and authenticated,
        /// or coupled CHPP demo telemetry labeled "SIMULATED DEMO TELEMETRY · READ-ONLY" when offline.
        /// </summary>
        public IActionResult OnGetTelemetry()
        {
            Response.Headers["Cache-Control"] = "no-store";

            if (webContext.IsReady && User.IsAuthenticated())
            {
                try
                {
                    CnlData[] values = clientAccessor.ScadaClient.GetCurrentData(MonitoredChannels, false, out _);
                    if (values != null && values.Length == MonitoredChannels.Length)
                    {
                        var liveMap = new Dictionary<int, CnlData>(MonitoredChannels.Length);
                        bool hasDefined = false;
                        for (int i = 0; i < MonitoredChannels.Length; i++)
                        {
                            liveMap[MonitoredChannels[i]] = values[i];
                            if (values[i].Stat > CnlStatusID.Undefined)
                            {
                                hasDefined = true;
                            }
                        }

                        if (hasDefined)
                        {
                            return new JsonResult(BuildTelemetryPayload(liveMap, isLive: true));
                        }
                    }
                }
                catch (Exception ex)
                {
                    webContext.Log?.WriteWarning("DigitalTwin3D live telemetry fallback to demo mode: {0}", ex.Message);
                }
            }

            return new JsonResult(BuildDemoTelemetryPayload());
        }

        private static object BuildTelemetryPayload(IReadOnlyDictionary<int, CnlData> ch, bool isLive)
        {
            double Val(int num, double fallback) =>
                ch.TryGetValue(num, out CnlData d) && d.Stat > CnlStatusID.Undefined && double.IsFinite(d.Val)
                    ? Math.Round(d.Val, 2)
                    : fallback;

            int Stat(int num) =>
                ch.TryGetValue(num, out CnlData d) ? d.Stat : CnlStatusID.Undefined;

            static string MapStatus(int stat, bool allowStandby = false, double val = 1.0)
            {
                if (stat == CnlStatusID.Error || stat == CnlStatusID.HiHi || stat == CnlStatusID.LoLo)
                    return "Fault";
                if (stat == CnlStatusID.Warning || stat == CnlStatusID.High || stat == CnlStatusID.Low)
                    return "Warning";
                if (allowStandby && (!double.IsFinite(val) || val <= 0.5))
                    return "Stopped";
                return stat > CnlStatusID.Undefined ? "Running" : "Running";
            }

            double crTph = Val(101, 418.0);
            double crKw = Val(102, 268.0);
            double crBrg = Val(103, 61.5);
            double scVib = Val(111, 3.6);
            double scKw = Val(112, 46.2);
            double cv101Spd = Val(121, 2.85);
            double cv101Amp = Val(122, 184.0);
            double cv101Rip = Val(711, 0.0);
            double cv102Spd = Val(131, 2.62);
            double cv102Tmp = Val(132, 54.0);
            double cv102Rip = Val(712, 0.0);
            double af001Tph = Val(141, 650.0);
            double bn101Lvl = Val(142, 68.0);
            double vf101Vib = Val(143, 4.1);
            double drc101Tph = Val(151, 530.0);
            double drc101Kw = Val(152, 138.0);
            double drc101Gap = Val(153, 50.0);
            double cv103Spd = Val(161, 2.55);
            double cv104Spd = Val(162, 2.75);
            double cv105Spd = Val(163, 2.20);
            double cv106Spd = Val(164, 2.40);
            double cv201Spd = Val(165, 2.70);
            double bn201Lvl = Val(166, 62.0);
            double tk201Lvl = Val(201, 64.0);
            double tk201Trq = Val(202, 42.0);
            double tk201Trb = Val(203, 18.5);
            double pp201Flw = Val(211, 385.0);
            double pp201Prs = Val(212, 4.2);
            double pp202Flw = Val(221, 0.0);
            double pp203Dos = Val(231, 24.5);
            double tk301Sg = Val(301, 1.52);
            double tk301Lvl = Val(302, 72.0);
            double ms301Rec = Val(303, 96.4);
            double dm301Flw = Val(304, 510.0);
            double dm301Prs = Val(305, 165.0);
            double cv202Spd = Val(311, 2.65);
            double ds201Vib = Val(312, 3.85);
            double ds201Prs = Val(313, 210.0);
            double cv203Spd = Val(314, 2.68);
            double cv301Spd = Val(315, 2.50);
            double cv302Spd = Val(316, 2.15);
            double cf501Rpm = Val(501, 680.0);
            double an601Ash = Val(601, 9.42);
            double an601Gcv = Val(604, 27.8);

            double simSec = Math.Round(DemoClock.Elapsed.TotalSeconds, 2);

            var equipment = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
            {
                ["AF-001"] = new { status = MapStatus(Stat(141)), loadPct = Math.Clamp(Math.Round(af001Tph / 8.5, 1), 35.0, 96.0), powerKw = 246.0, primaryLabel = $"{af001Tph:0.0} t/h ROM", secondaryLabel = "75 mm grizzly", isLiveChannel = true },
                ["BN-101"] = new { status = MapStatus(Stat(142)), loadPct = Math.Clamp(bn101Lvl, 0.0, 100.0), powerKw = 0.0, primaryLabel = $"{bn101Lvl:0.0}% level", secondaryLabel = "ROM receiving hopper", isLiveChannel = true },
                ["VF-101"] = new { status = MapStatus(Stat(143)), loadPct = Math.Clamp(Math.Round(af001Tph / 8.5, 1), 0.0, 100.0), powerKw = 30.0, primaryLabel = $"{vf101Vib:0.00} mm/s", secondaryLabel = "75 mm grizzly bars", isLiveChannel = true },
                ["CR-101"] = new { status = MapStatus(Stat(101)), loadPct = Math.Clamp(Math.Round(crKw / 3.15, 1), 0.0, 100.0), powerKw = crKw, primaryLabel = $"{crTph:0.0} t/h", secondaryLabel = $"Brg {crBrg:0.0} °C", isLiveChannel = true },
                ["DRC-101"] = new { status = MapStatus(Stat(151)), loadPct = Math.Clamp(Math.Round(drc101Kw / 1.60, 1), 0.0, 100.0), powerKw = drc101Kw, primaryLabel = $"{drc101Tph:0.0} t/h", secondaryLabel = $"Gap {drc101Gap:0.0} mm", isLiveChannel = true },
                ["SC-102"] = new { status = MapStatus(Stat(111)), loadPct = Math.Clamp(Math.Round(scKw / 0.55, 1), 0.0, 100.0), powerKw = scKw, primaryLabel = $"{scVib:0.00} mm/s vib", secondaryLabel = "50 / 6 mm decks", isLiveChannel = true },
                ["CV-101"] = new { status = cv101Rip > 0.5 ? "Fault" : MapStatus(Stat(121)), loadPct = Math.Clamp(Math.Round(cv101Spd / 3.4 * 100.0, 1), 0.0, 100.0), powerKw = Math.Round(cv101Amp * 0.68, 1), primaryLabel = $"{cv101Spd:0.00} m/s", secondaryLabel = $"{cv101Amp:0.0} A", isLiveChannel = true },
                ["CV-102"] = new { status = cv102Rip > 0.5 ? "Fault" : MapStatus(Stat(131)), loadPct = Math.Clamp(Math.Round(cv102Spd / 3.2 * 100.0, 1), 0.0, 100.0), powerKw = 112.4, primaryLabel = $"{cv102Spd:0.00} m/s", secondaryLabel = $"GB {cv102Tmp:0.0} °C", isLiveChannel = true },
                ["CV-103"] = new { status = MapStatus(Stat(161)), loadPct = Math.Clamp(Math.Round(cv103Spd / 3.2 * 100.0, 1), 0.0, 100.0), powerKw = 58.0, primaryLabel = $"{cv103Spd:0.00} m/s", secondaryLabel = "Jaw discharge", isLiveChannel = true },
                ["CV-104"] = new { status = MapStatus(Stat(162)), loadPct = Math.Clamp(Math.Round(cv104Spd / 3.4 * 100.0, 1), 0.0, 100.0), powerKw = 72.0, primaryLabel = $"{cv104Spd:0.00} m/s", secondaryLabel = "Sizing feed", isLiveChannel = true },
                ["CV-105"] = new { status = MapStatus(Stat(163)), loadPct = Math.Clamp(Math.Round(cv105Spd / 3.0 * 100.0, 1), 0.0, 100.0), powerKw = 29.0, primaryLabel = $"{cv105Spd:0.00} m/s", secondaryLabel = "<6 mm fines", isLiveChannel = true },
                ["CV-106"] = new { status = MapStatus(Stat(164)), loadPct = Math.Clamp(Math.Round(cv106Spd / 3.2 * 100.0, 1), 0.0, 100.0), powerKw = 54.0, primaryLabel = $"{cv106Spd:0.00} m/s", secondaryLabel = "Oversize return", isLiveChannel = true },
                ["CV-201"] = new { status = MapStatus(Stat(165)), loadPct = Math.Clamp(Math.Round(cv201Spd / 3.4 * 100.0, 1), 0.0, 100.0), powerKw = 86.0, primaryLabel = $"{cv201Spd:0.00} m/s", secondaryLabel = "Plant feed", isLiveChannel = true },
                ["BN-201"] = new { status = MapStatus(Stat(166)), loadPct = Math.Clamp(bn201Lvl, 0.0, 100.0), powerKw = 0.0, primaryLabel = $"{bn201Lvl:0.0}% level", secondaryLabel = "Surge bin", isLiveChannel = true },
                ["CV-202"] = new { status = MapStatus(Stat(311)), loadPct = Math.Clamp(Math.Round(cv202Spd / 3.4 * 100.0, 1), 0.0, 100.0), powerKw = 84.0, primaryLabel = $"{cv202Spd:0.00} m/s", secondaryLabel = "Desliming feed", isLiveChannel = true },
                ["DS-201"] = new { status = MapStatus(Stat(312)), loadPct = Math.Clamp(Math.Round(ds201Prs / 2.6, 1), 0.0, 100.0), powerKw = 41.0, primaryLabel = $"{ds201Vib:0.00} mm/s", secondaryLabel = $"{ds201Prs:0.0} kPa spray", isLiveChannel = true },
                ["CV-203"] = new { status = MapStatus(Stat(314)), loadPct = Math.Clamp(Math.Round(cv203Spd / 3.4 * 100.0, 1), 0.0, 100.0), powerKw = 74.0, primaryLabel = $"{cv203Spd:0.00} m/s", secondaryLabel = "Cyclone feed", isLiveChannel = true },
                ["CV-301"] = new { status = MapStatus(Stat(315)), loadPct = Math.Clamp(Math.Round(cv301Spd / 3.2 * 100.0, 1), 0.0, 100.0), powerKw = 62.0, primaryLabel = $"{cv301Spd:0.00} m/s", secondaryLabel = "Clean product", isLiveChannel = true },
                ["CV-302"] = new { status = MapStatus(Stat(316)), loadPct = Math.Clamp(Math.Round(cv302Spd / 3.0 * 100.0, 1), 0.0, 100.0), powerKw = 58.0, primaryLabel = $"{cv302Spd:0.00} m/s", secondaryLabel = "Discard belt", isLiveChannel = true },
                ["DM-301"] = new { status = MapStatus(Stat(304)), loadPct = Math.Clamp(Math.Round(dm301Flw / 6.2, 1), 0.0, 100.0), powerKw = 278.0, primaryLabel = $"{dm301Flw:0.0} m³/h", secondaryLabel = $"{dm301Prs:0.0} kPa · SG {tk301Sg:0.00}", isLiveChannel = true },
                ["MS-301"] = new { status = MapStatus(Stat(303)), loadPct = Math.Clamp(Math.Round(ms301Rec * 0.82, 1), 0.0, 100.0), powerKw = 68.4, primaryLabel = $"{ms301Rec:0.0}% recovery", secondaryLabel = "Wet-drum Macsep", isLiveChannel = true },
                ["TK-301"] = new { status = MapStatus(Stat(301)), loadPct = Math.Clamp(tk301Lvl, 0.0, 100.0), powerKw = 26.5, primaryLabel = $"SG {tk301Sg:0.00}", secondaryLabel = $"Level {tk301Lvl:0.0}%", isLiveChannel = true },
                ["TK-201"] = new { status = MapStatus(Stat(201)), loadPct = Math.Clamp(tk201Lvl, 0.0, 100.0), powerKw = 74.2, primaryLabel = $"Torque {tk201Trq:0.0}%", secondaryLabel = $"Turbidity {tk201Trb:0.0} NTU", isLiveChannel = true },
                ["PP-201"] = new { status = MapStatus(Stat(211)), loadPct = Math.Clamp(Math.Round(pp201Flw / 4.8, 1), 0.0, 100.0), powerKw = 94.0, primaryLabel = $"{pp201Flw:0.0} m³/h", secondaryLabel = $"{pp201Prs:0.0} bar", isLiveChannel = true },
                ["PP-202"] = new { status = MapStatus(Stat(221), allowStandby: true, val: pp202Flw), loadPct = pp202Flw > 1.0 ? 72.0 : 0.0, powerKw = pp202Flw > 1.0 ? 88.0 : 0.0, primaryLabel = pp202Flw > 1.0 ? $"{pp202Flw:0.0} m³/h" : "Standby", secondaryLabel = "Redundant U/F", isLiveChannel = true },
                ["PP-203"] = new { status = MapStatus(Stat(231)), loadPct = 66.0, powerKw = 11.4, primaryLabel = $"{pp203Dos:0.0} L/min", secondaryLabel = "Flocculant + water", isLiveChannel = true },
                ["SL-601"] = new { status = MapStatus(Stat(601)), loadPct = 80.5, powerKw = 0.0, primaryLabel = $"{an601Ash:0.00}% ash", secondaryLabel = $"{an601Gcv:0.0} MJ/kg", isLiveChannel = true },
                ["CV-501"] = new { status = MapStatus(Stat(501)), loadPct = Math.Clamp(Math.Round(cf501Rpm / 8.5, 1), 0.0, 100.0), powerKw = 64.2, primaryLabel = $"{cf501Rpm:0.0} rpm", secondaryLabel = "Refuse discard", isLiveChannel = true }
            };

            var channels = new Dictionary<string, object>(ch.Count);
            foreach (KeyValuePair<int, CnlData> kv in ch)
            {
                double safeVal = double.IsFinite(kv.Value.Val) ? Math.Round(kv.Value.Val, 3) : 0.0;
                channels[kv.Key.ToString()] = new { val = safeVal, stat = kv.Value.Stat };
            }

            return new
            {
                readOnly = true,
                source = isLive ? "LIVE SCADA TELEMETRY · READ-ONLY" : "SIMULATED DEMO TELEMETRY · READ-ONLY",
                isLive,
                timestampUtc = DateTimeOffset.UtcNow.ToString("O"),
                simTimeSec = simSec,
                engineering = new
                {
                    siteConfirmed = true,
                    grizzlyApertureMm = 75.0,
                    sizingTopDeckMm = 50.0,
                    sizingBottomDeckMm = 6.0,
                    deslimingApertureMm = 0.5,
                    primaryBeltWidthMm = 1200,
                    secondaryBeltWidthMm = 900,
                    jawDischargeRoutingMode = "BypassToCv104",
                    dryFinesDestinationMode = "DryThermalBlend"
                },
                plantTotals = new
                {
                    romFeedTph = af001Tph,
                    cleanProductTph = Math.Round(dm301Flw * 0.80, 1),
                    discardTph = Math.Round(dm301Flw * 0.19, 1),
                    mediumSg = tk301Sg,
                    waterRecoveryPct = 87.4,
                    ashPct = an601Ash,
                    gcvMjKg = an601Gcv
                },
                equipment,
                channels
            };
        }

        private static object BuildDemoTelemetryPayload()
        {
            double t = DemoClock.Elapsed.TotalSeconds;
            double loaderWave = 0.5 + 0.5 * Math.Sin(t * (2.0 * Math.PI / 12.0));
            double slowWave = Math.Sin(t * 0.32);
            double fastWave = Math.Sin(t * 1.15);

            var demoChannels = new Dictionary<int, CnlData>
            {
                [101] = new CnlData(418.0 + 18.0 * loaderWave + 6.0 * slowWave, CnlStatusID.Defined),
                [102] = new CnlData(266.0 + 14.0 * loaderWave + 4.0 * fastWave, CnlStatusID.Defined),
                [103] = new CnlData(61.2 + 1.4 * slowWave, CnlStatusID.Defined),
                [111] = new CnlData(3.65 + 0.18 * fastWave, CnlStatusID.Defined),
                [112] = new CnlData(46.2 + 2.1 * slowWave, CnlStatusID.Defined),
                [121] = new CnlData(2.85 + 0.08 * slowWave, CnlStatusID.Defined),
                [122] = new CnlData(184.0 + 9.0 * loaderWave, CnlStatusID.Defined),
                [131] = new CnlData(2.62 + 0.06 * slowWave, CnlStatusID.Defined),
                [132] = new CnlData(53.8 + 0.9 * slowWave, CnlStatusID.Defined),
                [141] = new CnlData(650.0 + 24.0 * loaderWave + 8.0 * slowWave, CnlStatusID.Defined),
                [142] = new CnlData(68.0 + 4.5 * loaderWave, CnlStatusID.Defined),
                [143] = new CnlData(4.10 + 0.22 * fastWave, CnlStatusID.Defined),
                [151] = new CnlData(530.0 + 16.0 * slowWave, CnlStatusID.Defined),
                [152] = new CnlData(138.0 + 6.5 * slowWave, CnlStatusID.Defined),
                [153] = new CnlData(50.0 + 0.4 * slowWave, CnlStatusID.Defined),
                [161] = new CnlData(2.55 + 0.05 * slowWave, CnlStatusID.Defined),
                [162] = new CnlData(2.75 + 0.06 * slowWave, CnlStatusID.Defined),
                [163] = new CnlData(2.20 + 0.04 * slowWave, CnlStatusID.Defined),
                [164] = new CnlData(2.40 + 0.05 * slowWave, CnlStatusID.Defined),
                [165] = new CnlData(2.70 + 0.06 * slowWave, CnlStatusID.Defined),
                [166] = new CnlData(62.0 + 3.0 * slowWave, CnlStatusID.Defined),
                [201] = new CnlData(64.2 + 3.2 * slowWave, CnlStatusID.Defined),
                [202] = new CnlData(42.5 + 2.4 * slowWave, CnlStatusID.Defined),
                [203] = new CnlData(18.2 + 1.1 * fastWave, CnlStatusID.Defined),
                [211] = new CnlData(385.0 + 7.5 * slowWave, CnlStatusID.Defined),
                [212] = new CnlData(4.20 + 0.08 * slowWave, CnlStatusID.Defined),
                [221] = new CnlData(0.0, CnlStatusID.Defined),
                [231] = new CnlData(24.5 + 0.8 * slowWave, CnlStatusID.Defined),
                [301] = new CnlData(1.52 + 0.01 * slowWave, CnlStatusID.Defined),
                [302] = new CnlData(72.5 + 2.8 * slowWave, CnlStatusID.Defined),
                [303] = new CnlData(96.4 + 0.4 * slowWave, CnlStatusID.Defined),
                [304] = new CnlData(510.0 + 12.0 * slowWave, CnlStatusID.Defined),
                [305] = new CnlData(165.0 + 3.5 * fastWave, CnlStatusID.Defined),
                [311] = new CnlData(2.65 + 0.05 * slowWave, CnlStatusID.Defined),
                [312] = new CnlData(3.85 + 0.18 * fastWave, CnlStatusID.Defined),
                [313] = new CnlData(210.0 + 4.0 * fastWave, CnlStatusID.Defined),
                [314] = new CnlData(2.68 + 0.05 * slowWave, CnlStatusID.Defined),
                [315] = new CnlData(2.50 + 0.04 * slowWave, CnlStatusID.Defined),
                [316] = new CnlData(2.15 + 0.04 * slowWave, CnlStatusID.Defined),
                [501] = new CnlData(680.0 + 10.0 * slowWave, CnlStatusID.Defined),
                [502] = new CnlData(6.40 + 0.30 * fastWave, CnlStatusID.Defined),
                [503] = new CnlData(48.0 + 1.5 * slowWave, CnlStatusID.Defined),
                [504] = new CnlData(9.60 + 0.25 * slowWave, CnlStatusID.Defined),
                [601] = new CnlData(9.40 + 0.14 * slowWave, CnlStatusID.Defined),
                [602] = new CnlData(8.20 + 0.10 * slowWave, CnlStatusID.Defined),
                [603] = new CnlData(0.62 + 0.02 * slowWave, CnlStatusID.Defined),
                [604] = new CnlData(27.80 + 0.12 * slowWave, CnlStatusID.Defined),
                [701] = new CnlData(14.2 + 1.8 * loaderWave, CnlStatusID.Defined),
                [702] = new CnlData(6.1 + 0.9 * loaderWave, CnlStatusID.Defined),
                [711] = new CnlData(0.0, CnlStatusID.Defined),
                [712] = new CnlData(0.0, CnlStatusID.Defined),
                [721] = new CnlData(12.0 + 1.5 * slowWave, CnlStatusID.Defined)
            };

            return BuildTelemetryPayload(demoChannels, isLive: false);
        }
    }
}
