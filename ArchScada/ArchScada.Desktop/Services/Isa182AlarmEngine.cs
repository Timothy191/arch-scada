using System;
using System.Collections.Generic;
using System.Linq;
using ArchScada.Desktop.Models;

namespace ArchScada.Desktop.Services;

public enum IsaAlarmState
{
    Normal,
    UnacknowledgedActive,
    AcknowledgedActive,
    UnacknowledgedReturnToNormal,
    Shelved,
    Suppressed
}

public sealed class AlarmLimitRule
{
    public string TagId { get; init; } = string.Empty;
    public string EquipmentId { get; init; } = string.Empty;
    public string Area { get; init; } = string.Empty;
    public double? HighHigh { get; init; }
    public double? High { get; init; }
    public double? Low { get; init; }
    public double? LowLow { get; init; }
    public double Deadband { get; init; } = 0.5;
    public int OnDelayTicks { get; init; } = 2;

    public AlarmLimitRule()
    {
    }

    public AlarmLimitRule(
        string tagId,
        string equipmentId,
        string area,
        double? highHigh,
        double? high,
        double? low,
        double? lowLow,
        double deadband = 0.5,
        int onDelayTicks = 2)
    {
        TagId = tagId;
        EquipmentId = equipmentId;
        Area = area;
        HighHigh = highHigh;
        High = high;
        Low = low;
        LowLow = lowLow;
        Deadband = deadband;
        OnDelayTicks = Math.Max(1, onDelayTicks);
    }
}

public sealed class Isa182AlarmEngine
{
    private sealed class RuleRuntimeState
    {
        public IsaAlarmState State { get; set; } = IsaAlarmState.Normal;
        public int ConsecutiveViolationTicks { get; set; }
        public DateTimeOffset? ShelvedUntilUtc { get; set; }
        public bool IsSuppressed { get; set; }
        public AlarmSeverity LastSeverity { get; set; } = AlarmSeverity.Medium;
        public string LastMessage { get; set; } = string.Empty;
        public bool IsCurrentlyViolating { get; set; }
    }

    private readonly Dictionary<string, AlarmLimitRule> _rulesByTag = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, RuleRuntimeState> _runtimeByAlarmId = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<AlarmItem> _managedAlarms = new();

    public bool IsHornActive { get; private set; }

    public IReadOnlyCollection<AlarmLimitRule> Rules => _rulesByTag.Values;

    public int ShelvedAlarmCount => _runtimeByAlarmId.Values.Count(s => s.State == IsaAlarmState.Shelved);

    public Isa182AlarmEngine(bool seedDefaultRules = true)
    {
        if (seedDefaultRules)
        {
            SeedDefaultChppRules();
        }
    }

    public void RegisterRule(AlarmLimitRule rule)
    {
        if (rule is null || string.IsNullOrWhiteSpace(rule.TagId))
        {
            throw new ArgumentException("Alarm rule must specify a valid TagId.", nameof(rule));
        }

        _rulesByTag[rule.TagId] = rule;
        var alarmId = GetAlarmIdForTag(rule.TagId);
        if (!_runtimeByAlarmId.ContainsKey(alarmId))
        {
            _runtimeByAlarmId[alarmId] = new RuleRuntimeState();
        }
    }

    public static string GetAlarmIdForTag(string tagId) => $"ALM-{tagId.ToUpperInvariant()}";

    public IsaAlarmState GetState(string alarmId)
    {
        if (string.IsNullOrWhiteSpace(alarmId))
        {
            return IsaAlarmState.Normal;
        }

        if (_runtimeByAlarmId.TryGetValue(alarmId, out var runtime))
        {
            if (runtime.State == IsaAlarmState.Shelved &&
                runtime.ShelvedUntilUtc.HasValue &&
                DateTimeOffset.UtcNow >= runtime.ShelvedUntilUtc.Value)
            {
                runtime.ShelvedUntilUtc = null;
                runtime.State = runtime.IsCurrentlyViolating
                    ? IsaAlarmState.UnacknowledgedActive
                    : IsaAlarmState.Normal;
            }

            return runtime.State;
        }

        var normalizedId = GetAlarmIdForTag(alarmId);
        return _runtimeByAlarmId.TryGetValue(normalizedId, out var byTagRuntime)
            ? byTagRuntime.State
            : IsaAlarmState.Normal;
    }

    public void SilenceHorn()
    {
        IsHornActive = false;
    }

    public void ShelveAlarm(string alarmId, TimeSpan duration)
    {
        if (string.IsNullOrWhiteSpace(alarmId))
        {
            return;
        }

        var effectiveDuration = duration <= TimeSpan.Zero ? TimeSpan.FromMinutes(15) : duration;
        if (!_runtimeByAlarmId.TryGetValue(alarmId, out var runtime))
        {
            runtime = new RuleRuntimeState();
            _runtimeByAlarmId[alarmId] = runtime;
        }

        runtime.ShelvedUntilUtc = DateTimeOffset.UtcNow.Add(effectiveDuration);
        runtime.State = IsaAlarmState.Shelved;
        RecomputeHornState();
    }

    public void UnshelveAlarm(string alarmId)
    {
        if (string.IsNullOrWhiteSpace(alarmId) || !_runtimeByAlarmId.TryGetValue(alarmId, out var runtime))
        {
            return;
        }

        runtime.ShelvedUntilUtc = null;
        runtime.State = runtime.IsCurrentlyViolating
            ? IsaAlarmState.UnacknowledgedActive
            : IsaAlarmState.Normal;
        RecomputeHornState();
    }

    public void SuppressAlarm(string alarmId, bool suppressed)
    {
        if (string.IsNullOrWhiteSpace(alarmId))
        {
            return;
        }

        if (!_runtimeByAlarmId.TryGetValue(alarmId, out var runtime))
        {
            runtime = new RuleRuntimeState();
            _runtimeByAlarmId[alarmId] = runtime;
        }

        runtime.IsSuppressed = suppressed;
        runtime.State = suppressed
            ? IsaAlarmState.Suppressed
            : (runtime.IsCurrentlyViolating ? IsaAlarmState.UnacknowledgedActive : IsaAlarmState.Normal);
        RecomputeHornState();
    }

    public void AcknowledgeAlarm(string alarmId, IList<AlarmItem>? alarmCollection = null)
    {
        if (string.IsNullOrWhiteSpace(alarmId))
        {
            return;
        }

        var targetList = alarmCollection ?? _managedAlarms;
        var alarm = targetList.FirstOrDefault(a => string.Equals(a.Id, alarmId, StringComparison.OrdinalIgnoreCase));
        if (alarm is not null)
        {
            alarm.IsAcknowledged = true;
            alarm.AcknowledgedAt = DateTime.Now;
        }

        if (_runtimeByAlarmId.TryGetValue(alarmId, out var runtime))
        {
            if (runtime.State == IsaAlarmState.UnacknowledgedActive)
            {
                runtime.State = IsaAlarmState.AcknowledgedActive;
            }
            else if (runtime.State == IsaAlarmState.UnacknowledgedReturnToNormal)
            {
                runtime.State = IsaAlarmState.Normal;
                if (alarm is not null)
                {
                    targetList.Remove(alarm);
                }
            }
        }

        RecomputeHornState();
    }

    public void AcknowledgeAll(IList<AlarmItem>? alarmCollection = null)
    {
        var targetList = alarmCollection ?? _managedAlarms;
        var ids = _runtimeByAlarmId.Keys.Union(targetList.Select(a => a.Id), StringComparer.OrdinalIgnoreCase).ToList();
        foreach (var id in ids)
        {
            AcknowledgeAlarm(id, targetList);
        }

        IsHornActive = false;
    }

    public IReadOnlyList<AlarmItem> Evaluate(IEnumerable<TelemetryTag> tags, IList<AlarmItem>? activeAlarms = null)
    {
        var targetAlarms = activeAlarms ?? _managedAlarms;
        if (tags is null)
        {
            return targetAlarms.ToList();
        }

        var nowUtc = DateTimeOffset.UtcNow;

        foreach (var tag in tags)
        {
            if (tag is null || string.IsNullOrWhiteSpace(tag.TagId) || !double.IsFinite(tag.Value))
            {
                continue;
            }

            if (!_rulesByTag.TryGetValue(tag.TagId, out var rule))
            {
                continue;
            }

            var alarmId = GetAlarmIdForTag(rule.TagId);
            if (!_runtimeByAlarmId.TryGetValue(alarmId, out var runtime))
            {
                runtime = new RuleRuntimeState();
                _runtimeByAlarmId[alarmId] = runtime;
            }

            // Check shelve expiration
            if (runtime.State == IsaAlarmState.Shelved &&
                runtime.ShelvedUntilUtc.HasValue &&
                nowUtc >= runtime.ShelvedUntilUtc.Value)
            {
                runtime.ShelvedUntilUtc = null;
                runtime.State = IsaAlarmState.Normal;
            }

            if (runtime.IsSuppressed)
            {
                runtime.State = IsaAlarmState.Suppressed;
                continue;
            }

            if (runtime.State == IsaAlarmState.Shelved)
            {
                continue;
            }

            bool wasViolating = runtime.IsCurrentlyViolating;
            bool isViolating = EvaluateViolationWithDeadband(rule, tag.Value, wasViolating, out var severity, out var limitLabel, out var thresholdValue);

            if (isViolating)
            {
                runtime.ConsecutiveViolationTicks++;
                if (runtime.ConsecutiveViolationTicks >= Math.Max(1, rule.OnDelayTicks))
                {
                    runtime.IsCurrentlyViolating = true;
                    runtime.LastSeverity = severity;
                    runtime.LastMessage = $"{rule.EquipmentId} {tag.Name} {limitLabel} limit exceeded ({tag.Value:0.##} {tag.Unit} vs {thresholdValue:0.##} {tag.Unit})";

                    var existing = targetAlarms.FirstOrDefault(a => string.Equals(a.Id, alarmId, StringComparison.OrdinalIgnoreCase));
                    if (existing is null)
                    {
                        var created = new AlarmItem
                        {
                            Id = alarmId,
                            Timestamp = DateTime.Now,
                            Severity = severity,
                            Area = rule.Area,
                            EquipmentId = rule.EquipmentId,
                            Message = runtime.LastMessage,
                            IsAcknowledged = false
                        };
                        targetAlarms.Insert(0, created);
                        runtime.State = IsaAlarmState.UnacknowledgedActive;
                        IsHornActive = true;
                    }
                    else
                    {
                        if (existing.IsAcknowledged && runtime.State == IsaAlarmState.UnacknowledgedActive)
                        {
                            runtime.State = IsaAlarmState.AcknowledgedActive;
                        }
                        else if (!existing.IsAcknowledged && runtime.State != IsaAlarmState.UnacknowledgedActive)
                        {
                            runtime.State = IsaAlarmState.UnacknowledgedActive;
                            IsHornActive = true;
                        }
                    }
                }
            }
            else
            {
                runtime.ConsecutiveViolationTicks = 0;
                runtime.IsCurrentlyViolating = false;

                var existing = targetAlarms.FirstOrDefault(a => string.Equals(a.Id, alarmId, StringComparison.OrdinalIgnoreCase));
                if (runtime.State == IsaAlarmState.UnacknowledgedActive)
                {
                    if (existing is not null && existing.IsAcknowledged)
                    {
                        runtime.State = IsaAlarmState.Normal;
                        targetAlarms.Remove(existing);
                    }
                    else
                    {
                        runtime.State = IsaAlarmState.UnacknowledgedReturnToNormal;
                    }
                }
                else if (runtime.State == IsaAlarmState.AcknowledgedActive)
                {
                    runtime.State = IsaAlarmState.Normal;
                    if (existing is not null)
                    {
                        targetAlarms.Remove(existing);
                    }
                }
                else if (runtime.State == IsaAlarmState.UnacknowledgedReturnToNormal && existing is not null && existing.IsAcknowledged)
                {
                    runtime.State = IsaAlarmState.Normal;
                    targetAlarms.Remove(existing);
                }
            }
        }

        // Sync any externally-seeded alarms in targetAlarms
        foreach (var alarm in targetAlarms)
        {
            if (alarm is null || string.IsNullOrWhiteSpace(alarm.Id))
            {
                continue;
            }

            if (!_runtimeByAlarmId.TryGetValue(alarm.Id, out var state))
            {
                state = new RuleRuntimeState
                {
                    IsCurrentlyViolating = true,
                    LastSeverity = alarm.Severity,
                    LastMessage = alarm.Message,
                    State = alarm.IsAcknowledged
                        ? IsaAlarmState.AcknowledgedActive
                        : IsaAlarmState.UnacknowledgedActive
                };
                _runtimeByAlarmId[alarm.Id] = state;
            }
            else if (alarm.IsAcknowledged && state.State == IsaAlarmState.UnacknowledgedActive)
            {
                state.State = IsaAlarmState.AcknowledgedActive;
            }
        }

        RecomputeHornState();
        return targetAlarms.ToList();
    }

    private void RecomputeHornState()
    {
        if (!_runtimeByAlarmId.Values.Any(r =>
                r.State == IsaAlarmState.UnacknowledgedActive ||
                r.State == IsaAlarmState.UnacknowledgedReturnToNormal))
        {
            IsHornActive = false;
        }
    }

    private static bool EvaluateViolationWithDeadband(
        AlarmLimitRule rule,
        double value,
        bool wasViolating,
        out AlarmSeverity severity,
        out string limitLabel,
        out double thresholdValue)
    {
        double db = Math.Max(0.0, rule.Deadband);

        if (rule.HighHigh.HasValue)
        {
            double limit = wasViolating ? rule.HighHigh.Value - db : rule.HighHigh.Value;
            if (value >= limit)
            {
                severity = AlarmSeverity.Critical;
                limitLabel = "HH";
                thresholdValue = rule.HighHigh.Value;
                return true;
            }
        }

        if (rule.LowLow.HasValue)
        {
            double limit = wasViolating ? rule.LowLow.Value + db : rule.LowLow.Value;
            if (value <= limit)
            {
                severity = AlarmSeverity.Critical;
                limitLabel = "LL";
                thresholdValue = rule.LowLow.Value;
                return true;
            }
        }

        if (rule.High.HasValue)
        {
            double limit = wasViolating ? rule.High.Value - db : rule.High.Value;
            if (value >= limit)
            {
                severity = AlarmSeverity.High;
                limitLabel = "HI";
                thresholdValue = rule.High.Value;
                return true;
            }
        }

        if (rule.Low.HasValue)
        {
            double limit = wasViolating ? rule.Low.Value + db : rule.Low.Value;
            if (value <= limit)
            {
                severity = AlarmSeverity.Medium;
                limitLabel = "LO";
                thresholdValue = rule.Low.Value;
                return true;
            }
        }

        severity = AlarmSeverity.Low;
        limitLabel = "NORMAL";
        thresholdValue = 0.0;
        return false;
    }

    private void SeedDefaultChppRules()
    {
        RegisterRule(new AlarmLimitRule("CV102_TMP", "CV-102", "Crushing & Conveyors", highHigh: 88.0, high: 82.0, low: null, lowLow: null, deadband: 1.2, onDelayTicks: 2));
        RegisterRule(new AlarmLimitRule("CR101_BRG", "CR-101", "Crushing & Conveyors", highHigh: 86.0, high: 78.0, low: null, lowLow: null, deadband: 1.5, onDelayTicks: 2));
        RegisterRule(new AlarmLimitRule("TK201_TRQ", "TK-201", "Thickener Circuit", highHigh: 78.0, high: 55.0, low: null, lowLow: null, deadband: 2.0, onDelayTicks: 2));
        RegisterRule(new AlarmLimitRule("TK201_TRB", "TK-201", "Thickener Circuit", highHigh: 35.0, high: 24.0, low: null, lowLow: null, deadband: 1.0, onDelayTicks: 2));
        RegisterRule(new AlarmLimitRule("TK301_SG", "TK-301", "Magnetite Preparation", highHigh: 1.72, high: 1.64, low: 1.38, lowLow: 1.30, deadband: 0.02, onDelayTicks: 2));
        RegisterRule(new AlarmLimitRule("CF501_MOIST", "CF-501", "Dewatering & Tailings", highHigh: 13.5, high: 11.5, low: null, lowLow: null, deadband: 0.3, onDelayTicks: 2));
        RegisterRule(new AlarmLimitRule("AN601_ASH", "AN-601", "ROM, Loadout & PGNAA", highHigh: 12.5, high: 10.8, low: null, lowLow: null, deadband: 0.25, onDelayTicks: 2));
        RegisterRule(new AlarmLimitRule("SL601_LVL", "SL-601", "ROM, Loadout & PGNAA", highHigh: 92.0, high: 85.0, low: 15.0, lowLow: 8.0, deadband: 1.5, onDelayTicks: 2));
    }
}
