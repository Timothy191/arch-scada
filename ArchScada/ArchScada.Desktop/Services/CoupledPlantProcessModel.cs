using System;
using System.Collections.Generic;
using System.Linq;
using ArchScada.Desktop.Models;

namespace ArchScada.Desktop.Services;

/// <summary>
/// First-principles coupled Coal Handling and Preparation Plant (CHPP) process model.
/// Maintains a 3-tick transport delay queue between dense-medium / flotation separation
/// and downstream dewatering, silo inventory, and PGNAA online quality analysis.
/// </summary>
public sealed class CoupledPlantProcessModel
{
    private readonly record struct TransportPacket(
        double FeedTph,
        double MediumSg,
        double CollectorDoseLpm,
        double CleanCoalYieldPercent,
        double ProductAshPercent);

    private readonly Queue<TransportPacket> _transportDelayQueue = new(4);
    private readonly object _sync = new();
    private const int TransportDelayTicks = 3;

    public CoupledPlantProcessModel()
    {
        for (int i = 0; i < TransportDelayTicks; i++)
        {
            _transportDelayQueue.Enqueue(new TransportPacket(
                FeedTph: 642.5,
                MediumSg: 1.52,
                CollectorDoseLpm: 14.8,
                CleanCoalYieldPercent: 83.8,
                ProductAshPercent: 9.4));
        }
    }

    public void StepCoupledPhysics(
        IEnumerable<TelemetryTag> tags,
        out double yieldPercent,
        out double productAshPercent,
        out double waterRecoveryPercent,
        out double specificEnergyKwhPerTon)
    {
        var dict = new Dictionary<string, TelemetryTag>(StringComparer.OrdinalIgnoreCase);
        if (tags is not null)
        {
            foreach (var tag in tags)
            {
                if (tag is not null && !string.IsNullOrWhiteSpace(tag.TagId))
                {
                    dict[tag.TagId] = tag;
                }
            }
        }
        StepCoupledPhysics(dict, out yieldPercent, out productAshPercent, out waterRecoveryPercent, out specificEnergyKwhPerTon);
    }

    public void StepCoupledPhysics(
        IReadOnlyDictionary<string, TelemetryTag> tags,
        out double yieldPercent,
        out double productAshPercent,
        out double waterRecoveryPercent,
        out double specificEnergyKwhPerTon)
    {
        lock (_sync)
        {
            var now = DateTime.Now;

            double feedTph = Math.Clamp(GetTagValue(tags, "CR101_TPH", 642.5), 0.0, 2500.0);
            double mediumSg = Math.Clamp(GetTagValue(tags, "TK301_SG", 1.52), 1.0, 2.5);
            double collectorDose = Math.Clamp(GetTagValue(tags, "PP401_DOS", 14.8), 0.0, 100.0);
            double flocculantDose = Math.Clamp(GetTagValue(tags, "PP203_DOS", 24.5), 0.0, 150.0);
            double centrifugeAmps = Math.Clamp(GetTagValue(tags, "CF501_AMP", 242.0), 0.0, 1000.0);
            double loadoutTph = Math.Clamp(GetTagValue(tags, "LD601_TPH", 1850.0), 0.0, 6000.0);

            // 1. Instantaneous separation physics in Dense Medium Cyclone (TK301_SG) + Froth Flotation (PP401_DOS)
            // Higher SG cut-point increases yield but pulls more near-gravity middlings ash into product.
            // Collector dose improves fine coal combustible recovery up to saturation.
            double sgDelta = mediumSg - 1.52;
            double collectorDelta = collectorDose - 14.8;

            double instantaneousYield = Math.Clamp(
                83.8 + (sgDelta * 26.5) + (collectorDelta * 0.34) - Math.Max(0, feedTph - 680.0) * 0.012,
                72.0,
                91.5);

            double instantaneousAsh = Math.Clamp(
                9.4 + (sgDelta * 14.2) + Math.Max(0.0, collectorDelta - 1.5) * 0.18 + (feedTph - 642.5) * 0.0025,
                6.2,
                15.8);

            _transportDelayQueue.Enqueue(new TransportPacket(
                FeedTph: feedTph,
                MediumSg: mediumSg,
                CollectorDoseLpm: collectorDose,
                CleanCoalYieldPercent: Math.Round(instantaneousYield, 2),
                ProductAshPercent: Math.Round(instantaneousAsh, 2)));

            while (_transportDelayQueue.Count > TransportDelayTicks)
            {
                _transportDelayQueue.Dequeue();
            }

            var delayed = _transportDelayQueue.Peek();
            yieldPercent = delayed.CleanCoalYieldPercent;
            productAshPercent = delayed.ProductAshPercent;

            // Update Flotation combustible recovery tag if present
            double flotationRecovery = Math.Clamp(86.2 + collectorDelta * 0.55 - (feedTph - 642.5) * 0.008, 68.0, 96.5);
            SetTagValue(tags, "FC401_REC", flotationRecovery, now);

            // 2. Thickener clarity (TK201_TRB) and closed-loop Water Recovery from flocculant dose (PP203_DOS) & feed tonnage (CR101_TPH)
            // Higher solids loading increases turbidity; higher flocculant dose settles fines and lowers NTU turbidity.
            double solidsLoadFactor = (feedTph - 640.0) / 100.0;
            double flocEffect = (flocculantDose - 24.5) * 0.72;
            double targetTurbidity = Math.Clamp(18.4 + solidsLoadFactor * 4.2 - flocEffect, 6.0, 48.0);
            double currentTurbidity = Math.Clamp(GetTagValue(tags, "TK201_TRB", 18.4), 0.0, 100.0);
            double nextTurbidity = Math.Clamp(currentTurbidity + (targetTurbidity - currentTurbidity) * 0.45, 0.0, 100.0);
            SetTagValue(tags, "TK201_TRB", nextTurbidity, now);

            waterRecoveryPercent = Math.Round(
                Math.Clamp(90.6 - (nextTurbidity * 0.18) + (flocculantDose - 20.0) * 0.09, 78.0, 95.5),
                2);

            // 3. Dewatering Cake Moisture (CF501_MOIST) and Clean Coal Silo Level (SL601_LVL) from lagged upstream feed
            double cleanCoalTph = delayed.FeedTph * (delayed.CleanCoalYieldPercent / 100.0);
            double targetCakeMoisture = Math.Clamp(
                9.6 + ((cleanCoalTph - 538.0) * 0.011) - ((centrifugeAmps - 240.0) * 0.015),
                6.2,
                16.5);
            double currentMoisture = Math.Clamp(GetTagValue(tags, "CF501_MOIST", 9.8), 4.0, 20.0);
            double nextMoisture = Math.Clamp(currentMoisture + (targetCakeMoisture - currentMoisture) * 0.5, 4.0, 20.0);
            SetTagValue(tags, "CF501_MOIST", nextMoisture, now);

            // Update PGNAA Online Analyzer tags (AN601_ASH, AN601_MOIST, AN601_CV) after 3-tick conveyor transport lag
            SetTagValue(tags, "AN601_ASH", productAshPercent, now);
            double pgnaaMoisture = Math.Clamp(nextMoisture - 0.8, 4.5, 15.5);
            SetTagValue(tags, "AN601_MOIST", pgnaaMoisture, now);
            double calorificValue = Math.Clamp(31.4 - (productAshPercent * 0.24) - (pgnaaMoisture * 0.16), 20.0, 31.5);
            SetTagValue(tags, "AN601_CV", calorificValue, now);

            // Clean Coal Silo mass balance (5000 t capacity; scaled per simulation tick)
            double currentSiloLevel = Math.Clamp(GetTagValue(tags, "SL601_LVL", 64.5), 0.0, 100.0);
            double effectiveOutflowTph = loadoutTph * 0.28; // Intermittent batch train loading average factor
            double netSiloRateTph = cleanCoalTph - effectiveOutflowTph;
            double nextSiloLevel = Math.Clamp(currentSiloLevel + (netSiloRateTph / 5000.0) * 2.5, 5.0, 98.0);
            SetTagValue(tags, "SL601_LVL", nextSiloLevel, now);
            SetTagValue(tags, "SL601_TON", nextSiloLevel * 50.0, now);

            // Specific energy (kWh/t) coupling crusher motor power and plant base load vs feed tonnage
            double crusherKw = Math.Clamp(GetTagValue(tags, "CR101_KW", 315.4), 0.0, 2000.0);
            double screenKw = Math.Clamp(GetTagValue(tags, "SC102_KW", 48.2), 0.0, 500.0);
            double estimatedTotalPowerKw = crusherKw + screenKw + 1120.0 + Math.Max(0.0, feedTph - 600.0) * 0.45;
            specificEnergyKwhPerTon = feedTph > 10.0 && double.IsFinite(estimatedTotalPowerKw)
                ? Math.Round(estimatedTotalPowerKw / feedTph, 2)
                : 0.0;
        }
    }

    private static double GetTagValue(IReadOnlyDictionary<string, TelemetryTag> tags, string tagId, double fallback)
    {
        return tags is not null &&
               tags.TryGetValue(tagId, out var tag) &&
               tag is not null &&
               double.IsFinite(tag.Value)
            ? tag.Value
            : fallback;
    }

    private static void SetTagValue(IReadOnlyDictionary<string, TelemetryTag> tags, string tagId, double value, DateTime timestamp)
    {
        if (!double.IsFinite(value))
            return;

        if (tags is not null && tags.TryGetValue(tagId, out var tag) && tag is not null)
        {
            double min = Math.Min(tag.MinLimit, tag.MaxLimit);
            double max = Math.Max(tag.MinLimit, tag.MaxLimit);
            if (!double.IsFinite(min) || !double.IsFinite(max))
                return;
            double clamped = Math.Clamp(value, min, max);
            tag.Value = Math.Round(clamped, 2);
            tag.Timestamp = timestamp;
        }
    }
}
