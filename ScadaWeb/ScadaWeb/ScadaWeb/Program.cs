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
 * Product  : Rapid SCADA
 * Module   : Webstation Application
 * Summary  : The Webstation application
 * 
 * Author   : Mikhail Shiryaev
 * Created  : 2021
 * Modified : 2025
 */

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.StaticFiles;
using Scada.Data.Const;
using Scada.Lang;
using Scada.Web.Authorization;
using Scada.Web.Code;
using Scada.Web.Services;
using System.Reflection;
using System.Security.Claims;

namespace Scada.Web
{
    /// <summary>
    /// The Webstation application.
    /// <para>Приложение Вебстанция.</para>
    /// </summary>
    public class Program
    {
        private static WebContext webContext;


        /// <summary>
        /// Initializes the application context.
        /// </summary>
        private static void InitContext()
        {
            webContext = new WebContext();

            if (webContext.Init())
            {
                webContext.StartProcessing();
                webContext.WaitForPlugins();
            }
        }

        /// <summary>
        /// Runs the web application.
        /// </summary>
        private static void RunWebHost(string[] args)
        {
            try
            {
                WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
                ConfigureServices(builder.Services);

                WebApplication app = builder.Build();
                ConfigureApplication(app);
                app.Run();
            }
            catch (Exception ex)
            {
                webContext.Log.WriteError(ex, Locale.IsRussian ?
                    "Не удалось запустить веб-узел" :
                    "Web host failed to start");
            }
        }

        /// <summary>
        /// Adds and configures the services.
        /// </summary>
        private static void ConfigureServices(IServiceCollection services)
        {
            services
                .AddControllers(options =>
                {
                    // set controller options here
                })
                .AddJsonOptions(options =>
                {
                    // set JSON options here
                });

            services
                .AddRazorPages(options =>
                {
                    options.Conventions.AuthorizeFolder(WebPath.Root);
                })
                .AddMvcOptions(options =>
                {
                    options.Filters.Add(typeof(ReadyResourceFilter));
                    webContext.PluginHolder.AddFilters(options.Filters);
                })
                .ConfigureApplicationPartManager(ConfigureApplicationParts);

            services
                .AddDataProtection()
                .AddKeyManagementOptions(options =>
                {
                    options.XmlRepository = new XmlRepository(webContext.Storage, webContext.Log);
                    options.XmlEncryptor = new XmlEncryptor(webContext.Log);
                });

            services
                .AddHealthChecks()
                    .AddCheck<ScadaServerHealthCheck>("scada_server", tags: new[] { "scada", "upstream" });

            services
                .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
                .AddCookie(options =>
                {
                    options.LoginPath = WebPath.LoginPage;
                    options.LogoutPath = WebPath.LogoutPage;
                    options.Events = new CookieAuthEvents
                    {
                        OnValidatePrincipal = async validateContext =>
                        {
                            if (validateContext.Properties.IssuedUtc.HasValue &&
                                DateTimeOffset.UtcNow - validateContext.Properties.IssuedUtc.Value > TimeSpan.FromHours(8))
                            {
                                validateContext.RejectPrincipal();
                                await validateContext.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                            }
                        }
                    };
                    options.Cookie.Name = "__Host-ScadaWebSession";
                    options.Cookie.HttpOnly = true;
                    options.Cookie.SameSite = SameSiteMode.Strict;
                    options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
                    options.ExpireTimeSpan = TimeSpan.FromMinutes(15);
                    options.SlidingExpiration = true;
                });

            services
                .AddAuthorizationBuilder()
                .AddPolicy(PolicyName.Administrators, policy =>
                    policy.RequireClaim(ClaimTypes.Role, RoleID.Administrator.ToString()))
                .AddPolicy(PolicyName.RequireViewAll, policy =>
                    policy.Requirements.Add(new ViewAllRequirement()))
                .AddPolicy(PolicyName.Restricted, policy =>
                    policy.Requirements.Add(new ObjRightRequirement()))
                .SetFallbackPolicy(new AuthorizationPolicyBuilder()
                    .RequireAuthenticatedUser()
                    .Build());

            services
                .Configure<ForwardedHeadersOptions>(options =>
                {
                    // required to use reverse proxy
                    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
                });

            services
                .AddHttpContextAccessor()
                .AddSession(options =>
                {
                    options.IdleTimeout = TimeSpan.FromMinutes(15);
                    options.Cookie.Name = "__Host-ScadaWebSession.Session";
                    options.Cookie.HttpOnly = true;
                    options.Cookie.SameSite = SameSiteMode.Strict;
                    options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
                    options.Cookie.IsEssential = true;
                })
                .AddSingleton(provider =>
                {
                    var env = provider.GetService<IWebHostEnvironment>();
                    return ReadOnlyHardeningMiddleware.LoadPolicy(env, webContext);
                })
                .AddSingleton<IWebContext>(webContext)
                .AddSingleton(webContext.Log)
                .AddScoped(UserContextFactory.GetUserContext)
                .AddScoped<IAuditLog, AuditLog>()
                .AddScoped<IClientAccessor, ClientAccessor>()
                .AddScoped<ILoginService, LoginService>()
                .AddScoped<IViewLoader, ViewLoader>()
                .AddScoped<IAuthorizationHandler, ViewAllHandler>()
                .AddScoped<IAuthorizationHandler, ObjRightHandler>();

            webContext.PluginHolder.AddServices(services);
        }

        /// <summary>
        /// Loads plugins to integrate their pages and controllers into the web application.
        /// </summary>
        private static void ConfigureApplicationParts(ApplicationPartManager apm)
        {
            foreach (string fileName in
                Directory.EnumerateFiles(webContext.AppDirs.ExeDir, "Plg*.dll", SearchOption.TopDirectoryOnly))
            {
                if (webContext.PluginHolder.ContainsPlugin(
                    ScadaUtils.RemoveFileNameSuffixes(Path.GetFileName(fileName))))
                {
                    try
                    {
                        Assembly assembly = Assembly.LoadFrom(fileName);
                        apm.ApplicationParts.Add(new CompiledRazorAssemblyPart(assembly));
                        apm.ApplicationParts.Add(new AssemblyPart(assembly));
                    }
                    catch (Exception ex)
                    {
                        webContext.Log.WriteError(ex, Locale.IsRussian ?
                            "Ошибка при загрузке части приложения из файла {0}" :
                            "Error loading application part from file {0}", fileName);
                    }
                }
            }
        }

        /// <summary>
        /// Configures the HTTP request pipeline of the web application.
        /// </summary>
        private static void ConfigureApplication(WebApplication app)
        {
            app.UseForwardedHeaders();
            app.UsePathBase(app.Configuration["pathBase"]);

            if (app.Environment.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }
            else
            {
                app.UseExceptionHandler(WebPath.ErrorPage);
                app.UseHsts();
            }

            app.UseReadOnlyHardening();

            var contentTypeProvider = new FileExtensionContentTypeProvider();
            contentTypeProvider.Mappings[".glb"] = "model/gltf-binary";
            contentTypeProvider.Mappings[".gltf"] = "model/gltf+json";
            app.UseStaticFiles(new StaticFileOptions
            {
                ContentTypeProvider = contentTypeProvider
            });
            app.UseWhen(ctx => !ctx.Request.Path.StartsWithSegments("/api"), webApp =>
            {
                webApp.UseStatusCodePagesWithReExecute("/Errors/{0}");
            });
            app.UseRouting();
            app.UseAuthentication();
            app.UseAuthorization();
            app.UseSession();
            app.MapRazorPages();
            app.MapControllers();
            app.MapGet("/models3d/{fileName}", (string fileName, IWebHostEnvironment env) =>
            {
                string resolvedPath = ResolveGlbModelPath(fileName, env);
                return resolvedPath is not null
                    ? Results.File(resolvedPath, "model/gltf-binary", enableRangeProcessing: true)
                    : Results.NotFound();
            }).AllowAnonymous();
            // Swagger/OpenAPI metadata
            app.MapHealthChecks("/health");
        }

        /// <summary>
        /// Safely resolves a bundled or repository-exported .glb model file for read-only 3D Web Twin delivery.
        /// </summary>
        private static string ResolveGlbModelPath(string fileName, IWebHostEnvironment env)
        {
            if (string.IsNullOrWhiteSpace(fileName))
                return null;

            string safeName = Path.GetFileName(fileName);
            if (!string.Equals(safeName, fileName, StringComparison.Ordinal) ||
                !safeName.EndsWith(".glb", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            if (!string.IsNullOrWhiteSpace(env.WebRootPath))
            {
                string webRootCandidate = Path.Combine(env.WebRootPath, "models3d", safeName);
                if (File.Exists(webRootCandidate))
                    return webRootCandidate;
            }

            string baseWwwrootCandidate = Path.Combine(AppContext.BaseDirectory, "wwwroot", "models3d", safeName);
            if (File.Exists(baseWwwrootCandidate))
                return baseWwwrootCandidate;

            foreach (string startDir in new[] { env.ContentRootPath, AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
            {
                if (string.IsNullOrWhiteSpace(startDir))
                    continue;

                DirectoryInfo dir = new(startDir);
                for (int i = 0; i < 6 && dir is not null; i++)
                {
                    string repoCandidate = Path.Combine(dir.FullName, "ArchScada", "Assets", "3D", "Exported", safeName);
                    if (File.Exists(repoCandidate))
                        return repoCandidate;
                    dir = dir.Parent;
                }
            }

            return null;
        }


        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        public static void Main(string[] args)
        {
#if DEBUG
            System.Diagnostics.Debugger.Launch();
#endif

            InitContext();
            RunWebHost(args);
            webContext.FinalizeContext();
        }
    }
}
