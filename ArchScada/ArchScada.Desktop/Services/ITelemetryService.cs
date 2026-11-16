using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using ArchScada.Desktop.Models;

namespace ArchScada.Desktop.Services;

public interface ITelemetryService
{
    ObservableCollection<EquipmentItem> Equipment { get; }
    ObservableCollection<AlarmItem> Alarms { get; }
    ObservableCollection<TelemetryTag> AllTags { get; }

    bool IsServerConnected { get; }
    bool IsOfflineDemoMode { get; }
    string ConnectionModeLabel { get; }

    double TotalFeedTonnageTph { get; }
    double TotalPowerDrawKw { get; }
    double WaterRecoveryPercent { get; }
    double MediumSpecificGravity { get; }
    int ActiveAlarmCount { get; }
    int UnacknowledgedAlarmCount { get; }
    int StaleTagCount { get; }

    event EventHandler? TelemetryUpdated;

    void Start(bool offlineDemoMode);
    void Stop();
    void AcknowledgeAlarm(string alarmId);
    void AcknowledgeAllAlarms();
    IReadOnlyList<double> GetTagHistory(string tagId);
}
