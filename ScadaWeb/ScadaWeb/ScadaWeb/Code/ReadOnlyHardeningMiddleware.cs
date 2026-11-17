/*
 * Copyright 2026 Rapid Software LLC
 * 
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 * 
 *     http://www.apache.org/licenses/LICENSE-2.0
 * 
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 * 
 * 
 * Product  : Rapid SCADA / Arch SCADA
 * Module   : Webstation Application
 * Summary  : ASP.NET Core middleware enforcing Phase 5 read-only security controls
 * 
 * Author   : Mikhail Shiryaev / Arch SCADA Team
 * Created  : 2026
 * Modified : 2026
 */

#nullable enable

using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Scada.Web.Services;

namespace Scada.Web.Code
{
    /// <summary>
    /// Enforces Phase 5 read-only security controls and policy rules from ReadOnlyWebPolicy.json.
    /// </summary>
    public class ReadOnlyHardeningMiddleware
    {
        private const string DefaultHeaderKey = "X-Scada-Mode";
        private const string DefaultHeaderValue = "read-only";

        private readonly RequestDelegate _next;
        private readonly IWebContext? _webContext;
        private readonly ReadOnlyWebPolicy _policy;
        private readonly string _headerKey = DefaultHeaderKey;
        private readonly string _headerValue = DefaultHeaderValue;
        private readonly HashSet<string> _blockedMethods;
        private readonly HashSet<string> _authRoutes;
        private readonly List<CidrRange> _allowedCidrRanges;
        private readonly SlidingWindowRateLimiter _rateLimiter;

        /// <summary>
        /// Gets the active read-only web policy.
        /// </summary>
        public ReadOnlyWebPolicy Policy => _policy;

        /// <summary>
        /// Gets the internal rate limiter instance.
        /// </summary>
        public SlidingWindowRateLimiter RateLimiter => _rateLimiter;

        /// <summary>
        /// Initializes a new instance of the <see cref="ReadOnlyHardeningMiddleware"/> class.
        /// </summary>
        public ReadOnlyHardeningMiddleware(
            RequestDelegate next,
            IWebHostEnvironment? env = null,
            IWebContext? webContext = null,
            ReadOnlyWebPolicy? policy = null)
        {
            _next = next ?? throw new ArgumentNullException(nameof(next));
            _webContext = webContext;
            _policy = policy ?? LoadPolicy(env, webContext);
            _rateLimiter = new SlidingWindowRateLimiter();

            // Parse response header key and value
            string headerSpec = _policy.ReadOnlyEnforcement?.RequireReadOnlyHeader ?? $"{DefaultHeaderKey}: {DefaultHeaderValue}";
            int colonIndex = headerSpec.IndexOf(':');
            if (colonIndex > 0)
            {
                _headerKey = headerSpec[..colonIndex].Trim();
                _headerValue = headerSpec[(colonIndex + 1)..].Trim();
            }

            // Blocked HTTP methods
            var blockedMethodsList = _policy.ReadOnlyEnforcement?.BlockedHttpMethods ?? ["POST", "PUT", "PATCH", "DELETE"];
            _blockedMethods = new HashSet<string>(blockedMethodsList, StringComparer.OrdinalIgnoreCase);

            // Auth lifecycle routes exempt from method block
            var exemptList = _policy.ReadOnlyEnforcement?.AuthEndpointsExemptFromMethodBlock ??
            [
                "/Login",
                "/Logout",
                "/api/v1/auth/login",
                "/api/v1/auth/logout",
                "/api/v1/auth/refresh"
            ];
            _authRoutes = new HashSet<string>(exemptList, StringComparer.OrdinalIgnoreCase);
            _authRoutes.Add("/Login");
            _authRoutes.Add("/Logout");
            _authRoutes.Add("/api/v1/auth/login");
            _authRoutes.Add("/api/v1/auth/logout");
            _authRoutes.Add("/api/v1/auth/refresh");

            // Allowed CIDRs
            _allowedCidrRanges = new List<CidrRange>();
            var cidrList = _policy.NetworkBinding?.AllowedCidrRanges ??
            [
                "127.0.0.1/32",
                "::1/128",
                "10.20.0.0/16",
                "192.168.100.0/24"
            ];

            foreach (var cidr in cidrList)
            {
                if (CidrRange.TryParse(cidr, out var range) && range != null)
                {
                    _allowedCidrRanges.Add(range);
                }
            }

            // Ensure loopback ranges are always present
            if (!_allowedCidrRanges.Any(r => r.NetworkAddress.Equals(IPAddress.Loopback)))
            {
                _allowedCidrRanges.Add(new CidrRange(IPAddress.Loopback, 32));
            }
            if (!_allowedCidrRanges.Any(r => r.NetworkAddress.Equals(IPAddress.IPv6Loopback)))
            {
                _allowedCidrRanges.Add(new CidrRange(IPAddress.IPv6Loopback, 128));
            }
        }

        /// <summary>
        /// Invokes the middleware for an HTTP request.
        /// </summary>
        public async Task InvokeAsync(HttpContext context)
        {
            // 1. Mandatory Response Header: X-Scada-Mode: read-only on all responses
            context.Response.Headers[_headerKey] = _headerValue;
            context.Response.OnStarting(state =>
            {
                var ctx = (HttpContext)state;
                try
                {
                    ctx.Response.Headers[_headerKey] = _headerValue;
                }
                catch
                {
                    // Headers already committed; continue
                }
                return Task.CompletedTask;
            }, context);

            // 2. Resolve Client IP
            var clientIp = ResolveClientIp(context);

            // 3. CIDR Allowlist Enforcement
            if (_policy.NetworkBinding?.InternalOnly == true && !IsIpAllowed(clientIp))
            {
                LogWarning("CIDR access violation: Client IP {0} is outside allowed internal ranges. Path: {1}",
                    clientIp, context.Request.Path);

                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                context.Response.ContentType = "text/plain; charset=utf-8";
                await context.Response.WriteAsync("403 Forbidden: Client IP address is not permitted by SCADA Web internal network policy.");
                return;
            }

            // 4. HTTP Method Enforcement (Zero Remote Control)
            string method = context.Request.Method;
            string path = context.Request.Path.Value ?? string.Empty;

            if (IsBlockedMethod(method) && !IsAuthRoute(path))
            {
                LogWarning("Write attempt blocked by read-only policy: {0} {1} from IP {2}",
                    method, path, clientIp);

                int statusCode = _policy.ReadOnlyEnforcement?.BlockedMethodStatusCode ?? StatusCodes.Status403Forbidden;
                context.Response.StatusCode = statusCode > 0 ? statusCode : StatusCodes.Status403Forbidden;
                context.Response.ContentType = "text/plain; charset=utf-8";
                await context.Response.WriteAsync("403 Forbidden: SCADA Web is operating in read-only mode. Write operations are prohibited.");
                return;
            }

            // 5. Sliding-Window Rate Limiting
            if (_policy.RateLimiting?.Enabled == true)
            {
                bool isAuth = IsAuthRoute(path);
                int limit = isAuth
                    ? (_policy.RateLimiting.Rules?.AuthLogin?.RequestsPerMinutePerIp ?? 10)
                    : (_policy.RateLimiting.Rules?.ApiRead?.RequestsPerMinutePerIp ?? 120);

                if (limit > 0 && !_rateLimiter.TryAcquire(clientIp.ToString(), limit, TimeSpan.FromMinutes(1), out int retryAfterSeconds))
                {
                    LogWarning("Rate limit exceeded for IP {0} on path {1}. Limit: {2} req/min. Retry-After: {3}s",
                        clientIp, path, limit, retryAfterSeconds);

                    int statusCode = _policy.RateLimiting.StatusCodeOnLimit > 0
                        ? _policy.RateLimiting.StatusCodeOnLimit
                        : StatusCodes.Status429TooManyRequests;

                    context.Response.StatusCode = statusCode;

                    if (_policy.RateLimiting.IncludeRetryAfterHeader)
                    {
                        context.Response.Headers["Retry-After"] = retryAfterSeconds.ToString();
                    }

                    context.Response.ContentType = "text/plain; charset=utf-8";
                    await context.Response.WriteAsync($"429 Too Many Requests: Rate limit exceeded. Try again in {retryAfterSeconds} seconds.");
                    return;
                }
            }

            // 6. Proceed to downstream middleware
            await _next(context);
        }

        /// <summary>
        /// Determines whether the specified HTTP method is blocked under the read-only policy.
        /// </summary>
        public bool IsBlockedMethod(string method)
        {
            if (string.IsNullOrWhiteSpace(method))
                return false;

            return _blockedMethods.Contains(method);
        }

        /// <summary>
        /// Determines whether the specified request path corresponds to an exempt auth lifecycle route.
        /// </summary>
        public bool IsAuthRoute(string path)
        {
            if (string.IsNullOrEmpty(path))
                return false;

            string trimmed = path.TrimEnd('/');
            if (string.IsNullOrEmpty(trimmed))
                trimmed = "/";

            foreach (var route in _authRoutes)
            {
                string norm = route.TrimEnd('/');
                if (string.Equals(trimmed, norm, StringComparison.OrdinalIgnoreCase) ||
                    trimmed.StartsWith(norm + "/", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Determines whether the specified IP address is permitted under the internal CIDR allowlist.
        /// </summary>
        public bool IsIpAllowed(IPAddress ipAddress)
        {
            if (ipAddress == null)
                return false;

            if (ipAddress.IsIPv4MappedToIPv6)
                ipAddress = ipAddress.MapToIPv4();

            if (IPAddress.IsLoopback(ipAddress))
                return true;

            foreach (var range in _allowedCidrRanges)
            {
                if (range.Contains(ipAddress))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Resolves the client IP address from the HTTP connection or proxy headers.
        /// </summary>
        public static IPAddress ResolveClientIp(HttpContext context)
        {
            var remoteIp = context.Connection.RemoteIpAddress;
            if (remoteIp == null && context.Request.Headers.TryGetValue("X-Forwarded-For", out var forwardedFor))
            {
                string? firstIp = forwardedFor.ToString().Split(',').FirstOrDefault()?.Trim();
                if (!string.IsNullOrEmpty(firstIp) && IPAddress.TryParse(firstIp, out var parsed))
                {
                    remoteIp = parsed;
                }
            }

            if (remoteIp == null)
            {
                remoteIp = IPAddress.Loopback;
            }

            if (remoteIp.IsIPv4MappedToIPv6)
            {
                remoteIp = remoteIp.MapToIPv4();
            }

            return remoteIp;
        }

        private void LogWarning(string format, params object[] args)
        {
            _webContext?.Log?.WriteWarning(format, args);
        }

        /// <summary>
        /// Loads the read-only web policy from disk or falls back to strict Phase 5 defaults.
        /// </summary>
        public static ReadOnlyWebPolicy LoadPolicy(IWebHostEnvironment? env = null, IWebContext? webContext = null)
        {
            string? policyPath = FindPolicyFilePath(env, webContext);
            if (!string.IsNullOrEmpty(policyPath) && File.Exists(policyPath))
            {
                try
                {
                    string json = File.ReadAllText(policyPath);
                    var options = new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true,
                        ReadCommentHandling = JsonCommentHandling.Skip,
                        AllowTrailingCommas = true
                    };

                    var policy = JsonSerializer.Deserialize<ReadOnlyWebPolicy>(json, options);
                    if (policy != null)
                    {
                        webContext?.Log?.WriteInfo("Loaded ReadOnlyWebPolicy from {0}", policyPath);
                        return policy;
                    }
                }
                catch (Exception ex)
                {
                    webContext?.Log?.WriteError(ex, "Failed to load ReadOnlyWebPolicy from {0}. Applying strict defaults.", policyPath);
                }
            }

            return CreateDefaultPolicy();
        }

        private static string? FindPolicyFilePath(IWebHostEnvironment? env, IWebContext? webContext)
        {
            var candidates = new List<string>();

            string? envPath = Environment.GetEnvironmentVariable("SCADA_READONLY_POLICY_PATH");
            if (!string.IsNullOrWhiteSpace(envPath))
                candidates.Add(envPath);

            if (webContext?.AppDirs?.ConfigDir != null)
            {
                candidates.Add(Path.Combine(webContext.AppDirs.ConfigDir, "ReadOnlyWebPolicy.json"));
            }

            if (env != null && !string.IsNullOrWhiteSpace(env.ContentRootPath))
            {
                candidates.Add(Path.Combine(env.ContentRootPath, "ArchScada", "Config", "ReadOnlyWebPolicy.json"));
                candidates.Add(Path.Combine(env.ContentRootPath, "Config", "ReadOnlyWebPolicy.json"));
                candidates.Add(Path.Combine(env.ContentRootPath, "..", "..", "ArchScada", "Config", "ReadOnlyWebPolicy.json"));
            }

            string baseDir = AppContext.BaseDirectory;
            candidates.Add(Path.Combine(baseDir, "Config", "ReadOnlyWebPolicy.json"));
            candidates.Add(Path.Combine(baseDir, "ArchScada", "Config", "ReadOnlyWebPolicy.json"));

            try
            {
                var dir = new DirectoryInfo(baseDir);
                for (int i = 0; i < 6 && dir != null; i++)
                {
                    candidates.Add(Path.Combine(dir.FullName, "ArchScada", "Config", "ReadOnlyWebPolicy.json"));
                    candidates.Add(Path.Combine(dir.FullName, "Config", "ReadOnlyWebPolicy.json"));
                    dir = dir.Parent;
                }
            }
            catch
            {
                // Ignore directory walk errors
            }

            string currentDir = Directory.GetCurrentDirectory();
            candidates.Add(Path.Combine(currentDir, "ArchScada", "Config", "ReadOnlyWebPolicy.json"));
            candidates.Add(Path.Combine(currentDir, "Config", "ReadOnlyWebPolicy.json"));

            foreach (var candidate in candidates)
            {
                try
                {
                    string fullPath = Path.GetFullPath(candidate);
                    if (File.Exists(fullPath))
                        return fullPath;
                }
                catch
                {
                    // Ignore format exceptions
                }
            }

            return null;
        }

        /// <summary>
        /// Creates the strict Phase 5 fallback policy when ReadOnlyWebPolicy.json is absent.
        /// </summary>
        public static ReadOnlyWebPolicy CreateDefaultPolicy()
        {
            return new ReadOnlyWebPolicy
            {
                PolicyVersion = "1.0.0",
                Phase = "Phase 5 - ScadaWeb Read-Only Hardening",
                ServiceName = "ScadaWeb",
                ReadOnlyEnforcement = new ReadOnlyEnforcementPolicy
                {
                    Enabled = true,
                    Mode = "strict-read-only",
                    AllowRemoteControl = false,
                    AllowPlcWrites = false,
                    AllowSetpointChanges = false,
                    AllowAlarmAcknowledgeToPlc = false,
                    AllowedHttpMethods = ["GET", "HEAD", "OPTIONS"],
                    BlockedHttpMethods = ["POST", "PUT", "PATCH", "DELETE"],
                    BlockedMethodStatusCode = 403,
                    AuthEndpointsExemptFromMethodBlock =
                    [
                        "/Login",
                        "/Logout",
                        "/api/v1/auth/login",
                        "/api/v1/auth/logout",
                        "/api/v1/auth/refresh"
                    ],
                    RequireReadOnlyHeader = "X-Scada-Mode: read-only"
                },
                NetworkBinding = new NetworkBindingPolicy
                {
                    InternalOnly = true,
                    BindAddresses = ["127.0.0.1"],
                    InternalPort = 8443,
                    AllowPublicInternetIngress = false,
                    AllowedCidrRanges =
                    [
                        "127.0.0.1/32",
                        "::1/128",
                        "10.20.0.0/16",
                        "192.168.100.0/24"
                    ]
                },
                RateLimiting = new RateLimitingPolicy
                {
                    Enabled = true,
                    Algorithm = "sliding-window",
                    StatusCodeOnLimit = 429,
                    IncludeRetryAfterHeader = true,
                    Rules = new RateLimitingRules
                    {
                        ApiRead = new ApiReadRule
                        {
                            RequestsPerMinutePerIp = 120,
                            RequestsPerMinutePerSession = 180,
                            Burst = 30
                        },
                        AuthLogin = new AuthLoginRule
                        {
                            RequestsPerMinutePerIp = 10,
                            LockoutMinutesAfterFailures = 15,
                            MaxFailedAttempts = 5
                        }
                    }
                },
                SessionPolicy = new SessionPolicyConfig
                {
                    IdleTimeoutMinutes = 15,
                    AbsoluteMaxLifetimeHours = 8,
                    SlidingExpiration = true,
                    Cookie = new CookieConfig
                    {
                        HttpOnly = true,
                        Secure = true,
                        SameSite = "Strict",
                        Name = "__Host-ScadaWebSession"
                    },
                    MaxConcurrentSessionsPerUser = 2,
                    RevokeOnLogout = true,
                    RevokeOnRoleChange = true
                },
                AuditLogging = new AuditLoggingPolicy
                {
                    Enabled = true,
                    LogBlockedWriteAttempts = true,
                    LogAuthEvents = true,
                    LogRateLimitViolations = true,
                    LogSessionTimeouts = true,
                    RetentionDays = 365
                }
            };
        }
    }

    /// <summary>
    /// Thread-safe sliding-window rate limiter per client IP.
    /// </summary>
    public sealed class SlidingWindowRateLimiter
    {
        private readonly ConcurrentDictionary<string, Queue<long>> _ipTimestamps = new();
        private long _lastCleanupTicks = Environment.TickCount64;

        /// <summary>
        /// Attempts to acquire a request token within the sliding window.
        /// </summary>
        public bool TryAcquire(string clientIp, int maxRequests, TimeSpan window, out int retryAfterSeconds)
        {
            if (maxRequests <= 0)
            {
                retryAfterSeconds = (int)Math.Max(1, Math.Ceiling(window.TotalSeconds));
                return false;
            }

            long nowTicks = Environment.TickCount64;
            long windowTicks = (long)window.TotalMilliseconds;
            long cutoffTicks = nowTicks - windowTicks;

            PeriodicCleanup(cutoffTicks);

            var queue = _ipTimestamps.GetOrAdd(clientIp, _ => new Queue<long>());
            lock (queue)
            {
                while (queue.Count > 0 && queue.Peek() <= cutoffTicks)
                {
                    queue.Dequeue();
                }

                if (queue.Count >= maxRequests)
                {
                    long oldest = queue.Peek();
                    long elapsedSinceOldest = nowTicks - oldest;
                    long remainingMs = Math.Max(0, windowTicks - elapsedSinceOldest);
                    retryAfterSeconds = Math.Max(1, (int)Math.Ceiling(remainingMs / 1000.0));
                    return false;
                }

                queue.Enqueue(nowTicks);
                retryAfterSeconds = 0;
                return true;
            }
        }

        /// <summary>
        /// Resets all rate limiting state.
        /// </summary>
        public void Reset()
        {
            _ipTimestamps.Clear();
        }

        private void PeriodicCleanup(long cutoffTicks)
        {
            long now = Environment.TickCount64;
            if (now - _lastCleanupTicks < 60000)
                return;

            _lastCleanupTicks = now;
            foreach (var kvp in _ipTimestamps)
            {
                lock (kvp.Value)
                {
                    while (kvp.Value.Count > 0 && kvp.Value.Peek() <= cutoffTicks)
                    {
                        kvp.Value.Dequeue();
                    }
                    if (kvp.Value.Count == 0)
                    {
                        _ipTimestamps.TryRemove(kvp.Key, out _);
                    }
                }
            }
        }
    }

    /// <summary>
    /// Represents an IPv4 or IPv6 CIDR subnet range.
    /// </summary>
    public sealed class CidrRange
    {
        public IPAddress NetworkAddress { get; }
        public int PrefixLength { get; }

        public CidrRange(IPAddress networkAddress, int prefixLength)
        {
            NetworkAddress = networkAddress ?? throw new ArgumentNullException(nameof(networkAddress));
            PrefixLength = prefixLength;
        }

        public static bool TryParse(string? cidr, out CidrRange? range)
        {
            range = null;
            if (string.IsNullOrWhiteSpace(cidr))
                return false;

            string trimmed = cidr.Trim();
            int slashIndex = trimmed.IndexOf('/');
            if (slashIndex <= 0 || slashIndex == trimmed.Length - 1)
            {
                if (IPAddress.TryParse(trimmed, out var singleIp))
                {
                    int prefix = singleIp.AddressFamily == AddressFamily.InterNetworkV6 ? 128 : 32;
                    range = new CidrRange(singleIp, prefix);
                    return true;
                }
                return false;
            }

            string ipPart = trimmed[..slashIndex];
            string prefixPart = trimmed[(slashIndex + 1)..];

            if (!IPAddress.TryParse(ipPart, out var ip))
                return false;

            if (!int.TryParse(prefixPart, out int prefixLength) || prefixLength < 0)
                return false;

            int maxPrefix = ip.AddressFamily == AddressFamily.InterNetworkV6 ? 128 : 32;
            if (prefixLength > maxPrefix)
                return false;

            range = new CidrRange(ip, prefixLength);
            return true;
        }

        public bool Contains(IPAddress address)
        {
            if (address == null)
                return false;

            if (address.IsIPv4MappedToIPv6)
                address = address.MapToIPv4();

            var netAddr = NetworkAddress;
            if (netAddr.IsIPv4MappedToIPv6)
                netAddr = netAddr.MapToIPv4();

            if (address.AddressFamily != netAddr.AddressFamily)
                return false;

            byte[] addressBytes = address.GetAddressBytes();
            byte[] networkBytes = netAddr.GetAddressBytes();

            int fullBytes = PrefixLength / 8;
            int remainingBits = PrefixLength % 8;

            for (int i = 0; i < fullBytes && i < addressBytes.Length; i++)
            {
                if (addressBytes[i] != networkBytes[i])
                    return false;
            }

            if (remainingBits > 0 && fullBytes < addressBytes.Length)
            {
                byte mask = (byte)~(0xFF >> remainingBits);
                if ((addressBytes[fullBytes] & mask) != (networkBytes[fullBytes] & mask))
                    return false;
            }

            return true;
        }
    }

    /// <summary>
    /// Model representations for ReadOnlyWebPolicy.json.
    /// </summary>
    public class ReadOnlyWebPolicy
    {
        public string PolicyVersion { get; set; } = "1.0.0";
        public string Phase { get; set; } = "Phase 5 - ScadaWeb Read-Only Hardening";
        public string ServiceName { get; set; } = "ScadaWeb";
        public ReadOnlyEnforcementPolicy ReadOnlyEnforcement { get; set; } = new();
        public NetworkBindingPolicy NetworkBinding { get; set; } = new();
        public RateLimitingPolicy RateLimiting { get; set; } = new();
        public SessionPolicyConfig SessionPolicy { get; set; } = new();
        public AuditLoggingPolicy AuditLogging { get; set; } = new();
    }

    public class ReadOnlyEnforcementPolicy
    {
        public bool Enabled { get; set; } = true;
        public string Mode { get; set; } = "strict-read-only";
        public bool AllowRemoteControl { get; set; } = false;
        public bool AllowPlcWrites { get; set; } = false;
        public bool AllowSetpointChanges { get; set; } = false;
        public bool AllowAlarmAcknowledgeToPlc { get; set; } = false;
        public List<string> AllowedHttpMethods { get; set; } = ["GET", "HEAD", "OPTIONS"];
        public List<string> BlockedHttpMethods { get; set; } = ["POST", "PUT", "PATCH", "DELETE"];
        public int BlockedMethodStatusCode { get; set; } = 403;
        public List<string> AuthEndpointsExemptFromMethodBlock { get; set; } =
        [
            "/Login",
            "/Logout",
            "/api/v1/auth/login",
            "/api/v1/auth/logout",
            "/api/v1/auth/refresh"
        ];
        public string RequireReadOnlyHeader { get; set; } = "X-Scada-Mode: read-only";
    }

    public class NetworkBindingPolicy
    {
        public bool InternalOnly { get; set; } = true;
        public List<string> BindAddresses { get; set; } = ["127.0.0.1"];
        public int InternalPort { get; set; } = 8443;
        public bool AllowPublicInternetIngress { get; set; } = false;
        public List<string> AllowedCidrRanges { get; set; } =
        [
            "127.0.0.1/32",
            "::1/128",
            "10.20.0.0/16",
            "192.168.100.0/24"
        ];
    }

    public class RateLimitingPolicy
    {
        public bool Enabled { get; set; } = true;
        public string Algorithm { get; set; } = "sliding-window";
        public int StatusCodeOnLimit { get; set; } = 429;
        public bool IncludeRetryAfterHeader { get; set; } = true;
        public RateLimitingRules Rules { get; set; } = new();
    }

    public class RateLimitingRules
    {
        public ApiReadRule ApiRead { get; set; } = new();
        public AuthLoginRule AuthLogin { get; set; } = new();
    }

    public class ApiReadRule
    {
        public int RequestsPerMinutePerIp { get; set; } = 120;
        public int RequestsPerMinutePerSession { get; set; } = 180;
        public int Burst { get; set; } = 30;
    }

    public class AuthLoginRule
    {
        public int RequestsPerMinutePerIp { get; set; } = 10;
        public int LockoutMinutesAfterFailures { get; set; } = 15;
        public int MaxFailedAttempts { get; set; } = 5;
    }

    public class SessionPolicyConfig
    {
        public int IdleTimeoutMinutes { get; set; } = 15;
        public int AbsoluteMaxLifetimeHours { get; set; } = 8;
        public bool SlidingExpiration { get; set; } = true;
        public CookieConfig Cookie { get; set; } = new();
        public int MaxConcurrentSessionsPerUser { get; set; } = 2;
        public bool RevokeOnLogout { get; set; } = true;
        public bool RevokeOnRoleChange { get; set; } = true;
    }

    public class CookieConfig
    {
        public bool HttpOnly { get; set; } = true;
        public bool Secure { get; set; } = true;
        public string SameSite { get; set; } = "Strict";
        public string Name { get; set; } = "__Host-ScadaWebSession";
    }

    public class AuditLoggingPolicy
    {
        public bool Enabled { get; set; } = true;
        public bool LogBlockedWriteAttempts { get; set; } = true;
        public bool LogAuthEvents { get; set; } = true;
        public bool LogRateLimitViolations { get; set; } = true;
        public bool LogSessionTimeouts { get; set; } = true;
        public int RetentionDays { get; set; } = 365;
    }

    /// <summary>
    /// Extension methods for registering ReadOnlyHardeningMiddleware in the ASP.NET Core pipeline.
    /// </summary>
    public static class ReadOnlyHardeningMiddlewareExtensions
    {
        /// <summary>
        /// Adds the Phase 5 ReadOnlyHardeningMiddleware to the application pipeline.
        /// </summary>
        public static IApplicationBuilder UseReadOnlyHardening(this IApplicationBuilder app)
        {
            return app.UseMiddleware<ReadOnlyHardeningMiddleware>();
        }
    }
}
