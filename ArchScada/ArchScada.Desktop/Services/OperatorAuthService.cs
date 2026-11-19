using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Configuration;

namespace ArchScada.Desktop.Services;

public enum OperatorRole
{
    Viewer,
    Operator,
    ShiftSupervisor,
    MaintenanceEngineer
}

public sealed record ShiftHandoverEntry(
    DateTime Timestamp,
    string OperatorId,
    OperatorRole Role,
    string Note)
{
    public string TimeDisplay => Timestamp.ToString("yyyy-MM-dd HH:mm:ss");
    public string RoleDisplay => Role.ToString();
}

public sealed record OperatorCredentialConfig
{
    public string Username { get; init; } = string.Empty;
    public string Pin { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string Role { get; init; } = "Viewer";
}

public sealed class OperatorCredentialsConfig
{
    public bool SeedDefaults { get; init; } = true;
    public List<OperatorCredentialConfig> Users { get; init; } = new();
}

public partial class OperatorAuthService : ObservableObject
{
    private sealed record SeededCredential(string Username, string Pin, string DisplayName, OperatorRole Role);

    private readonly Dictionary<string, SeededCredential> _credentials = new(StringComparer.OrdinalIgnoreCase);

    [ObservableProperty]
    public partial string CurrentOperatorId { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string CurrentDisplayName { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial OperatorRole CurrentRole { get; private set; } = OperatorRole.Viewer;

    [ObservableProperty]
    public partial bool IsAuthenticated { get; private set; } = false;

    [ObservableProperty]
    public partial bool IsSessionLocked { get; private set; }

    public ObservableCollection<ShiftHandoverEntry> HandoverLog { get; } = new();

    public ObservableCollection<ShiftHandoverEntry> ShiftHandoverNotes => HandoverLog;

    public bool CanAcknowledgeAlarms =>
        IsAuthenticated && !IsSessionLocked &&
        CurrentRole is OperatorRole.Operator or OperatorRole.ShiftSupervisor or OperatorRole.MaintenanceEngineer;

    public bool CanChangeOptimizationMode =>
        IsAuthenticated && !IsSessionLocked &&
        CurrentRole is OperatorRole.Operator or OperatorRole.ShiftSupervisor or OperatorRole.MaintenanceEngineer;

    public bool CanShelveAlarms =>
        IsAuthenticated && !IsSessionLocked &&
        CurrentRole is OperatorRole.ShiftSupervisor or OperatorRole.MaintenanceEngineer;

    public event EventHandler? AuthStateChanged;

    public OperatorAuthService()
    {
        LoadSeededCredentials();
        SeedInitialHandoverEntries();
        // Default to operator1 if credentials are available
        var first = _credentials.Values.FirstOrDefault();
        if (first is not null)
        {
            CurrentOperatorId = first.Username;
            CurrentDisplayName = first.DisplayName;
            CurrentRole = first.Role;
            IsAuthenticated = true;
        }
    }

    public OperatorAuthService(IConfiguration configuration) : this()
    {
        var credsConfig = configuration.GetSection("OperatorCredentials").Get<OperatorCredentialsConfig>();
        if (credsConfig != null)
        {
            _credentials.Clear();
            foreach (var user in credsConfig.Users)
            {
                var role = ParseRole(user.Role);
                _credentials[user.Username] = new SeededCredential(user.Username, user.Pin, user.DisplayName, role);
            }

            // Re-apply default operator if available
            var first = _credentials.Values.FirstOrDefault();
            if (first is not null)
            {
                CurrentOperatorId = first.Username;
                CurrentDisplayName = first.DisplayName;
                CurrentRole = first.Role;
                IsAuthenticated = true;
            }
        }
    }

    private void LoadSeededCredentials()
    {
        _credentials["operator1"] = new("operator1", "1234", "J. Mokoena (CCR Operator)", OperatorRole.Operator);
        _credentials["supervisor1"] = new("supervisor1", "2468", "S. Nkosi (Shift Supervisor)", OperatorRole.ShiftSupervisor);
        _credentials["engineer1"] = new("engineer1", "9999", "T. van Wyk (Control & Instrumentation Eng)", OperatorRole.MaintenanceEngineer);
        _credentials["viewer1"] = new("viewer1", "0000", "Control Room Read-Only Observer", OperatorRole.Viewer);
    }

    private static OperatorRole ParseRole(string role)
    {
        return role switch
        {
            "Operator" => OperatorRole.Operator,
            "ShiftSupervisor" => OperatorRole.ShiftSupervisor,
            "MaintenanceEngineer" => OperatorRole.MaintenanceEngineer,
            _ => OperatorRole.Viewer
        };
    }

    public bool Login(string username, string pin)
    {
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(pin))
        {
            return false;
        }

        if (!_credentials.TryGetValue(username.Trim(), out var account))
        {
            return false;
        }

        if (!string.Equals(account.Pin, pin.Trim(), StringComparison.Ordinal))
        {
            return false;
        }

        CurrentOperatorId = account.Username;
        CurrentDisplayName = account.DisplayName;
        CurrentRole = account.Role;
        IsAuthenticated = true;
        IsSessionLocked = false;

        NotifyPermissionsChanged();
        return true;
    }

    public void Logout()
    {
        CurrentOperatorId = "viewer1";
        CurrentDisplayName = "Control Room Read-Only Observer";
        CurrentRole = OperatorRole.Viewer;
        IsAuthenticated = false;
        IsSessionLocked = false;

        NotifyPermissionsChanged();
    }

    public void LockSession()
    {
        IsSessionLocked = true;
        NotifyPermissionsChanged();
    }

    public ShiftHandoverEntry? AddShiftHandoverNote(string note)
    {
        if (string.IsNullOrWhiteSpace(note))
        {
            return null;
        }

        var entry = new ShiftHandoverEntry(
            Timestamp: DateTime.Now,
            OperatorId: CurrentOperatorId,
            Role: CurrentRole,
            Note: note.Trim());

        HandoverLog.Insert(0, entry);
        while (HandoverLog.Count > 50)
        {
            HandoverLog.RemoveAt(HandoverLog.Count - 1);
        }

        return entry;
    }

    private void NotifyPermissionsChanged()
    {
        OnPropertyChanged(nameof(CanAcknowledgeAlarms));
        OnPropertyChanged(nameof(CanChangeOptimizationMode));
        OnPropertyChanged(nameof(CanShelveAlarms));
        AuthStateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void SeedInitialHandoverEntries()
    {
        HandoverLog.Add(new ShiftHandoverEntry(
            DateTime.Now.AddMinutes(-35),
            "supervisor1",
            OperatorRole.ShiftSupervisor,
            "DMC medium density setpoint held at 1.52 SG; PGNAA ash tracking 9.4% within export contract spec."));
        HandoverLog.Add(new ShiftHandoverEntry(
            DateTime.Now.AddHours(-2),
            "operator1",
            OperatorRole.Operator,
            "CV-102 gearbox temperature monitored under ISA-18.2 HI limit; standby pump PP-202 verified available."));
    }
}
