using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;

namespace ArchScada.ControlBoundary.Tests;

public sealed class Phase5WebPolicyAndSystemdTests
{
    private static readonly string[] SystemdServiceFiles =
    [
        "archscada-server.service",
        "archscada-comm.service",
        "archscada-desktop.service"
    ];

    private static string ResolveRepoPath(params string[] relativeParts)
    {
        var current = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(current))
        {
            var candidate = Path.Combine([current, .. relativeParts]);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            var parent = Directory.GetParent(current);
            if (parent == null || parent.FullName == current)
            {
                break;
            }

            current = parent.FullName;
        }

        var fallback = Path.GetFullPath(Path.Combine([Directory.GetCurrentDirectory(), .. relativeParts]));
        if (File.Exists(fallback))
        {
            return fallback;
        }

        throw new FileNotFoundException($"Could not locate repository file: {Path.Combine(relativeParts)}");
    }

    private static JsonDocument LoadReadOnlyWebPolicyDocument()
    {
        var policyPath = ResolveRepoPath("ArchScada", "Config", "ReadOnlyWebPolicy.json");
        Assert.True(File.Exists(policyPath), $"ReadOnlyWebPolicy.json not found at {policyPath}");
        var json = File.ReadAllText(policyPath);
        return JsonDocument.Parse(json);
    }

    // ---------------------------------------------------------------------------
    // ReadOnlyWebPolicy.json: Core Policy & Zero Remote Control Enforcement
    // ---------------------------------------------------------------------------

    [Fact]
    public void ReadOnlyWebPolicy_ParsesCleanly_AndHasValidHeaderMetadata()
    {
        using var doc = LoadReadOnlyWebPolicyDocument();
        var root = doc.RootElement;

        Assert.True(root.TryGetProperty("policyVersion", out var versionElement));
        Assert.Equal("1.0.0", versionElement.GetString());

        Assert.True(root.TryGetProperty("phase", out var phaseElement));
        Assert.Contains("Phase 5", phaseElement.GetString(), StringComparison.OrdinalIgnoreCase);

        Assert.True(root.TryGetProperty("serviceName", out var serviceElement));
        Assert.Equal("ScadaWeb", serviceElement.GetString());
    }

    [Fact]
    public void ReadOnlyWebPolicy_EnforcesZeroRemoteControl_AndNoPlcWrites()
    {
        using var doc = LoadReadOnlyWebPolicyDocument();
        var root = doc.RootElement;

        Assert.True(root.TryGetProperty("readOnlyEnforcement", out var ro));
        Assert.True(ro.GetProperty("enabled").GetBoolean());
        Assert.Equal("strict-read-only", ro.GetProperty("mode").GetString());
        Assert.False(ro.GetProperty("allowRemoteControl").GetBoolean(), "allowRemoteControl must be false.");
        Assert.False(ro.GetProperty("allowPlcWrites").GetBoolean(), "allowPlcWrites must be false.");
        Assert.False(ro.GetProperty("allowSetpointChanges").GetBoolean(), "allowSetpointChanges must be false.");
        Assert.False(ro.GetProperty("allowAlarmAcknowledgeToPlc").GetBoolean(), "allowAlarmAcknowledgeToPlc must be false.");
    }

    [Fact]
    public void ReadOnlyWebPolicy_BlocksWriteHttpMethods_WithStatusCode403()
    {
        using var doc = LoadReadOnlyWebPolicyDocument();
        var ro = doc.RootElement.GetProperty("readOnlyEnforcement");

        var blockedMethods = ro.GetProperty("blockedHttpMethods")
            .EnumerateArray()
            .Select(e => e.GetString())
            .ToList();

        Assert.Contains("POST", blockedMethods);
        Assert.Contains("PUT", blockedMethods);
        Assert.Contains("PATCH", blockedMethods);
        Assert.Contains("DELETE", blockedMethods);

        var allowedMethods = ro.GetProperty("allowedHttpMethods")
            .EnumerateArray()
            .Select(e => e.GetString())
            .ToList();

        Assert.DoesNotContain("POST", allowedMethods);
        Assert.DoesNotContain("PUT", allowedMethods);
        Assert.DoesNotContain("PATCH", allowedMethods);
        Assert.DoesNotContain("DELETE", allowedMethods);
        Assert.Contains("GET", allowedMethods);
        Assert.Contains("HEAD", allowedMethods);
        Assert.Contains("OPTIONS", allowedMethods);

        Assert.Equal(403, ro.GetProperty("blockedMethodStatusCode").GetInt32());
    }

    [Fact]
    public void ReadOnlyWebPolicy_RequiresReadOnlyHeader_AndExemptsOnlyAuthLifecycle()
    {
        using var doc = LoadReadOnlyWebPolicyDocument();
        var ro = doc.RootElement.GetProperty("readOnlyEnforcement");

        Assert.Equal("X-Scada-Mode: read-only", ro.GetProperty("requireReadOnlyHeader").GetString());

        var exemptEndpoints = ro.GetProperty("authEndpointsExemptFromMethodBlock")
            .EnumerateArray()
            .Select(e => e.GetString())
            .ToList();

        Assert.Contains("/api/v1/auth/login", exemptEndpoints);
        Assert.Contains("/api/v1/auth/logout", exemptEndpoints);
        Assert.Contains("/api/v1/auth/refresh", exemptEndpoints);

        // Ensure no operational or control endpoints are exempt
        foreach (var endpoint in exemptEndpoints)
        {
            Assert.StartsWith("/api/v1/auth/", endpoint, StringComparison.OrdinalIgnoreCase);
        }
    }

    // ---------------------------------------------------------------------------
    // ReadOnlyWebPolicy.json: Network Binding, TLS, and CORS
    // ---------------------------------------------------------------------------

    [Fact]
    public void ReadOnlyWebPolicy_NetworkBinding_IsLoopbackOnlyAndInternalOnly()
    {
        using var doc = LoadReadOnlyWebPolicyDocument();
        var nb = doc.RootElement.GetProperty("networkBinding");

        Assert.True(nb.GetProperty("internalOnly").GetBoolean(), "internalOnly must be true.");
        Assert.False(nb.GetProperty("allowPublicInternetIngress").GetBoolean(), "allowPublicInternetIngress must be false.");
        Assert.Equal(8443, nb.GetProperty("internalPort").GetInt32());

        var bindAddresses = nb.GetProperty("bindAddresses")
            .EnumerateArray()
            .Select(e => e.GetString())
            .ToList();

        Assert.Single(bindAddresses);
        Assert.Equal("127.0.0.1", bindAddresses[0]);
        Assert.DoesNotContain("0.0.0.0", bindAddresses);
        Assert.DoesNotContain("*", bindAddresses);

        var allowedCidrs = nb.GetProperty("allowedCidrRanges")
            .EnumerateArray()
            .Select(e => e.GetString())
            .ToList();

        Assert.Contains("127.0.0.1/32", allowedCidrs);
        Assert.Contains("10.20.0.0/16", allowedCidrs);
        Assert.Contains("192.168.100.0/24", allowedCidrs);
    }

    [Fact]
    public void ReadOnlyWebPolicy_TlsAndCors_EnforcesTls13AndDisallowsWildcardOrigins()
    {
        using var doc = LoadReadOnlyWebPolicyDocument();
        var nb = doc.RootElement.GetProperty("networkBinding");

        var tls = nb.GetProperty("tls");
        Assert.True(tls.GetProperty("required").GetBoolean());
        Assert.Equal("TLSv1.3", tls.GetProperty("minVersion").GetString());
        Assert.True(tls.GetProperty("hstsEnabled").GetBoolean());

        var cors = nb.GetProperty("cors");
        Assert.True(cors.GetProperty("enabled").GetBoolean());
        Assert.False(cors.GetProperty("allowWildcardOrigins").GetBoolean());

        var allowedOrigins = cors.GetProperty("allowedOrigins")
            .EnumerateArray()
            .Select(e => e.GetString())
            .ToList();

        Assert.Contains("https://scada.internal.plant.local", allowedOrigins);
        Assert.DoesNotContain("*", allowedOrigins);
    }

    // ---------------------------------------------------------------------------
    // ReadOnlyWebPolicy.json: RBAC Roles and Zero Write Permissions
    // ---------------------------------------------------------------------------

    [Fact]
    public void ReadOnlyWebPolicy_Rbac_DenyByDefault_AndNoRoleGrantsWriteOrPlcPrivileges()
    {
        using var doc = LoadReadOnlyWebPolicyDocument();
        var rbac = doc.RootElement.GetProperty("rbac");

        Assert.True(rbac.GetProperty("denyByDefault").GetBoolean());
        Assert.Equal("Viewer", rbac.GetProperty("defaultRole").GetString());

        var roles = rbac.GetProperty("roles");

        foreach (var roleProperty in roles.EnumerateObject())
        {
            var roleName = roleProperty.Name;
            var roleObj = roleProperty.Value;

            var permissions = roleObj.GetProperty("permissions")
                .EnumerateArray()
                .Select(p => p.GetString()!)
                .ToList();

            foreach (var perm in permissions)
            {
                Assert.False(
                    perm.EndsWith(":write", StringComparison.OrdinalIgnoreCase) ||
                    perm.Contains("write", StringComparison.OrdinalIgnoreCase),
                    $"Role '{roleName}' must not contain write permission: '{perm}'");

                Assert.False(
                    perm.StartsWith("plc:", StringComparison.OrdinalIgnoreCase) ||
                    perm.Contains("plc", StringComparison.OrdinalIgnoreCase),
                    $"Role '{roleName}' must not contain PLC write/control permission: '{perm}'");

                Assert.False(
                    perm.Contains("control:", StringComparison.OrdinalIgnoreCase),
                    $"Role '{roleName}' must not contain control permission: '{perm}'");

                Assert.False(
                    perm.Contains("setpoint:", StringComparison.OrdinalIgnoreCase),
                    $"Role '{roleName}' must not contain setpoint permission: '{perm}'");

                Assert.True(
                    perm.EndsWith(":read", StringComparison.OrdinalIgnoreCase) ||
                    perm.EndsWith(":export", StringComparison.OrdinalIgnoreCase),
                    $"Permission '{perm}' in role '{roleName}' is neither a read nor an export scope.");
            }
        }
    }

    [Fact]
    public void ReadOnlyWebPolicy_Rbac_StandardRoles_MatchDocumentedScopes()
    {
        using var doc = LoadReadOnlyWebPolicyDocument();
        var roles = doc.RootElement.GetProperty("rbac").GetProperty("roles");

        string[] requiredRoles = ["Viewer", "OperatorReadOnly", "EngineerReadOnly", "Auditor"];
        foreach (var role in requiredRoles)
        {
            Assert.True(roles.TryGetProperty(role, out _), $"Role '{role}' is missing from RBAC definitions.");
        }

        // Viewer: restricted areas, no export, no diagnostics
        var viewer = roles.GetProperty("Viewer");
        Assert.False(viewer.GetProperty("canExportCsv").GetBoolean());
        Assert.False(viewer.GetProperty("canViewDiagnostics").GetBoolean());
        var viewerAreas = viewer.GetProperty("allowedAreas").EnumerateArray().Select(a => a.GetString()).ToList();
        Assert.DoesNotContain("*", viewerAreas);

        // OperatorReadOnly: all areas, no export, no diagnostics
        var operatorRole = roles.GetProperty("OperatorReadOnly");
        Assert.False(operatorRole.GetProperty("canExportCsv").GetBoolean());
        Assert.False(operatorRole.GetProperty("canViewDiagnostics").GetBoolean());
        var operatorAreas = operatorRole.GetProperty("allowedAreas").EnumerateArray().Select(a => a.GetString()).ToList();
        Assert.Contains("*", operatorAreas);

        // EngineerReadOnly: all areas, export enabled, diagnostics enabled
        var engineer = roles.GetProperty("EngineerReadOnly");
        Assert.True(engineer.GetProperty("canExportCsv").GetBoolean());
        Assert.True(engineer.GetProperty("canViewDiagnostics").GetBoolean());

        // Auditor: all areas, export enabled, diagnostics enabled
        var auditor = roles.GetProperty("Auditor");
        Assert.True(auditor.GetProperty("canExportCsv").GetBoolean());
        Assert.True(auditor.GetProperty("canViewDiagnostics").GetBoolean());
    }

    // ---------------------------------------------------------------------------
    // ReadOnlyWebPolicy.json: Rate Limiting, Session, and Cookie Hardening
    // ---------------------------------------------------------------------------

    [Fact]
    public void ReadOnlyWebPolicy_RateLimiting_Returns429_AndEnforcesAllCategories()
    {
        using var doc = LoadReadOnlyWebPolicyDocument();
        var rl = doc.RootElement.GetProperty("rateLimiting");

        Assert.True(rl.GetProperty("enabled").GetBoolean());
        Assert.Equal("sliding-window", rl.GetProperty("algorithm").GetString());
        Assert.Equal(429, rl.GetProperty("statusCodeOnLimit").GetInt32());
        Assert.True(rl.GetProperty("includeRetryAfterHeader").GetBoolean());

        var rules = rl.GetProperty("rules");

        var apiRead = rules.GetProperty("apiRead");
        Assert.Equal(120, apiRead.GetProperty("requestsPerMinutePerIp").GetInt32());
        Assert.Equal(180, apiRead.GetProperty("requestsPerMinutePerSession").GetInt32());
        Assert.Equal(30, apiRead.GetProperty("burst").GetInt32());

        var stream = rules.GetProperty("telemetryStream");
        Assert.Equal(3, stream.GetProperty("maxConcurrentConnectionsPerSession").GetInt32());
        Assert.Equal(10, stream.GetProperty("maxConcurrentConnectionsPerIp").GetInt32());

        var auth = rules.GetProperty("authLogin");
        Assert.Equal(10, auth.GetProperty("requestsPerMinutePerIp").GetInt32());
        Assert.Equal(15, auth.GetProperty("lockoutMinutesAfterFailures").GetInt32());
        Assert.Equal(5, auth.GetProperty("maxFailedAttempts").GetInt32());

        var export = rules.GetProperty("historianExport");
        Assert.Equal(20, export.GetProperty("requestsPerHourPerSession").GetInt32());
    }

    [Fact]
    public void ReadOnlyWebPolicy_SessionPolicy_EnforcesHostCookieAndSecurityFlags()
    {
        using var doc = LoadReadOnlyWebPolicyDocument();
        var sp = doc.RootElement.GetProperty("sessionPolicy");

        Assert.Equal(15, sp.GetProperty("idleTimeoutMinutes").GetInt32());
        Assert.Equal(8, sp.GetProperty("absoluteMaxLifetimeHours").GetInt32());
        Assert.Equal(2, sp.GetProperty("maxConcurrentSessionsPerUser").GetInt32());
        Assert.True(sp.GetProperty("revokeOnLogout").GetBoolean());
        Assert.True(sp.GetProperty("revokeOnRoleChange").GetBoolean());

        var cookie = sp.GetProperty("cookie");
        Assert.Equal("__Host-ScadaWebSession", cookie.GetProperty("name").GetString());
        Assert.True(cookie.GetProperty("httpOnly").GetBoolean());
        Assert.True(cookie.GetProperty("secure").GetBoolean());
        Assert.Equal("Strict", cookie.GetProperty("sameSite").GetString());
    }

    [Fact]
    public void ReadOnlyWebPolicy_AuditLogging_LogsSecurityEventsWithRequiredRetention()
    {
        using var doc = LoadReadOnlyWebPolicyDocument();
        var audit = doc.RootElement.GetProperty("auditLogging");

        Assert.True(audit.GetProperty("enabled").GetBoolean());
        Assert.True(audit.GetProperty("logBlockedWriteAttempts").GetBoolean());
        Assert.True(audit.GetProperty("logAuthEvents").GetBoolean());
        Assert.True(audit.GetProperty("logRateLimitViolations").GetBoolean());
        Assert.True(audit.GetProperty("logSessionTimeouts").GetBoolean());
        Assert.True(audit.GetProperty("retentionDays").GetInt32() >= 365);
    }

    // ---------------------------------------------------------------------------
    // Systemd Service Files Compliance
    // ---------------------------------------------------------------------------

    [Theory]
    [InlineData("archscada-server.service")]
    [InlineData("archscada-comm.service")]
    [InlineData("archscada-desktop.service")]
    public void SystemdService_EnforcesNoNewPrivileges(string serviceName)
    {
        var servicePath = ResolveRepoPath("deploy", "systemd", serviceName);
        Assert.True(File.Exists(servicePath), $"Service file not found: {servicePath}");

        var content = File.ReadAllText(servicePath);
        Assert.True(
            Regex.IsMatch(content, @"^\s*NoNewPrivileges\s*=\s*true\b", RegexOptions.Multiline),
            $"{serviceName} is missing 'NoNewPrivileges=true' directive.");
    }

    [Theory]
    [InlineData("archscada-server.service")]
    [InlineData("archscada-comm.service")]
    [InlineData("archscada-desktop.service")]
    public void SystemdService_EnforcesProtectSystemStrict(string serviceName)
    {
        var servicePath = ResolveRepoPath("deploy", "systemd", serviceName);
        Assert.True(File.Exists(servicePath), $"Service file not found: {servicePath}");

        var content = File.ReadAllText(servicePath);
        Assert.True(
            Regex.IsMatch(content, @"^\s*ProtectSystem\s*=\s*strict\b", RegexOptions.Multiline),
            $"{serviceName} is missing 'ProtectSystem=strict' directive.");
    }

    [Theory]
    [InlineData("archscada-server.service")]
    [InlineData("archscada-comm.service")]
    [InlineData("archscada-desktop.service")]
    public void SystemdService_EnforcesArchScadaControlModeReadOnly(string serviceName)
    {
        var servicePath = ResolveRepoPath("deploy", "systemd", serviceName);
        Assert.True(File.Exists(servicePath), $"Service file not found: {servicePath}");

        var content = File.ReadAllText(servicePath);
        Assert.True(
            Regex.IsMatch(content, @"^\s*Environment\s*=.*?\bARCHSCADA_CONTROL_MODE\s*=\s*READ_ONLY\b", RegexOptions.Multiline),
            $"{serviceName} is missing 'Environment=... ARCHSCADA_CONTROL_MODE=READ_ONLY' directive.");
    }

    [Theory]
    [InlineData("archscada-server.service")]
    [InlineData("archscada-comm.service")]
    [InlineData("archscada-desktop.service")]
    public void SystemdService_EnforcesKernelAndSuidIsolationDirectives(string serviceName)
    {
        var servicePath = ResolveRepoPath("deploy", "systemd", serviceName);
        Assert.True(File.Exists(servicePath), $"Service file not found: {servicePath}");

        var content = File.ReadAllText(servicePath);

        string[] requiredDirectives =
        [
            @"PrivateTmp\s*=\s*true",
            @"ProtectControlGroups\s*=\s*true",
            @"ProtectKernelModules\s*=\s*true",
            @"ProtectKernelTunables\s*=\s*true",
            @"RestrictSUIDSGID\s*=\s*true"
        ];

        foreach (var directive in requiredDirectives)
        {
            Assert.True(
                Regex.IsMatch(content, $@"^\s*{directive}\b", RegexOptions.Multiline),
                $"{serviceName} is missing security isolation directive: '{directive}'");
        }
    }

    // ---------------------------------------------------------------------------
    // Documentation & Policy Cross-Verification
    // ---------------------------------------------------------------------------

    [Fact]
    public void HardeningDoc_AndPolicyFile_AreMutuallyConsistent()
    {
        var docPath = ResolveRepoPath("docs", "scada", "04-scadaweb-readonly-hardening.md");
        Assert.True(File.Exists(docPath), $"Hardening document not found at {docPath}");

        var docContent = File.ReadAllText(docPath);

        // Doc must reference the config file
        Assert.Contains("ReadOnlyWebPolicy.json", docContent);

        // Doc must specify loopback binding and TLS 1.3
        Assert.Contains("127.0.0.1:8443", docContent);
        Assert.Contains("TLS 1.3", docContent);

        // Doc must mandate the read-only header
        Assert.Contains("X-Scada-Mode: read-only", docContent);

        // Doc must specify HTTP status codes 403 and 429
        Assert.Contains("403", docContent);
        Assert.Contains("429", docContent);

        // Doc must specify the cookie name
        Assert.Contains("__Host-ScadaWebSession", docContent);
    }
}
