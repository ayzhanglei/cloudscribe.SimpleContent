﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Http;
using cloudscribe.SimpleContent.Models;
using Microsoft.AspNetCore.DataProtection;
using cloudscribe.Core.SimpleContent.Integration;
using Microsoft.AspNetCore.Authentication.Cookies;
using System.Globalization;
using Microsoft.AspNetCore.Localization;

namespace example.WebApp
{
    public class Startup
    {
        public Startup(IHostingEnvironment env)
        {
            var builder = new ConfigurationBuilder()
                .SetBasePath(env.ContentRootPath)
                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
                .AddJsonFile($"appsettings.{env.EnvironmentName}.json", optional: true);

            //builder.AddJsonFile("app-tenants-users.json");
            //builder.AddJsonFile("font-awesome-icon-map.json");


            //builder.AddJsonFile("app-content-project-settings.json");

            // this file name is ignored by gitignore
            // so you can create it and use on your local dev machine
            // remember last config source added wins if it has the same settings
            builder.AddJsonFile("appsettings.local.overrides.json", optional: true);

            if (env.IsDevelopment())
            {
                // For more details on using the user secret store see http://go.microsoft.com/fwlink/?LinkID=532709
                builder.AddUserSecrets();
            }

            builder.AddEnvironmentVariables();
            Configuration = builder.Build();
            appBasePath = env.ContentRootPath;
            environment = env;
           
        }

        private string appBasePath;
        public IHostingEnvironment environment { get; set; }
        public IConfigurationRoot Configuration { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            //services.AddGlimpse();

            string pathToCryptoKeys = appBasePath + System.IO.Path.DirectorySeparatorChar + "dp_keys" + System.IO.Path.DirectorySeparatorChar;
            services.AddDataProtection()
                .PersistKeysToFileSystem(new System.IO.DirectoryInfo(pathToCryptoKeys));
            
            services.Configure<ForwardedHeadersOptions>(options =>
            {
                options.ForwardedHeaders = Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedProto;
            });

            services.AddMemoryCache();
            // we currently only use session for alerts, so we can fire an alert on the next request
            // if session is disabled this feature fails quietly with no errors
            services.AddSession();

            ConfigureAuthPolicy(services);

            services.AddOptions();
            services.AddCloudscribeCoreNoDbStorage();
            services.AddCloudscribeLoggingNoDbStorage(Configuration);
            services.AddCloudscribeLogging();

            services.AddScoped<cloudscribe.Web.Navigation.Caching.ITreeCache, cloudscribe.Web.Navigation.Caching.NotCachedTreeCache>();

            //
            services.AddScoped<cloudscribe.Web.Navigation.INavigationNodePermissionResolver, cloudscribe.Web.Navigation.NavigationNodePermissionResolver>();
            services.AddScoped<cloudscribe.Web.Navigation.INavigationNodePermissionResolver, cloudscribe.SimpleContent.Web.Services.PagesNavigationNodePermissionResolver>();
            services.AddCloudscribeCore(Configuration);

            services.AddCloudscribeIdentity();

            services.AddNoDbStorageForSimpleContent();

            services.Configure<List<ProjectSettings>>(Configuration.GetSection("ContentProjects"));
            services.AddScoped<IProjectSettingsResolver, SiteProjectSettingsResolver>();
            services.AddScoped<IProjectSecurityResolver, ProjectSecurityResolver>();

            //services.Configure<List<CustomClaimMap>>(Configuration.GetSection("ClaimMaps"));
            //services.AddScoped<cloudscribe.Core.Identity.ICustomClaimProvider, CustomClaimProvider>();


            services.AddCloudscribeCoreIntegrationForSimpleContent();
            services.AddSimpleContent(Configuration);

            services.AddMetaWeblogForSimpleContent(Configuration.GetSection("MetaWeblogApiOptions"));

            services.AddSimpleContentRssSyndiction();

            services.AddLocalization(options => options.ResourcesPath = "GlobalResources");

            services.Configure<RequestLocalizationOptions>(options =>
            {
                var supportedCultures = new[]
                {
                    new CultureInfo("en-US"),
                    new CultureInfo("en-GB"),
                    new CultureInfo("fr-FR"),
                    new CultureInfo("fr"),
                };

                // State what the default culture for your application is. This will be used if no specific culture
                // can be determined for a given request.
                options.DefaultRequestCulture = new RequestCulture(culture: "en-US", uiCulture: "en-US");

                // You must explicitly state which cultures your application supports.
                // These are the cultures the app supports for formatting numbers, dates, etc.
                options.SupportedCultures = supportedCultures;

                // These are the cultures the app supports for UI strings, i.e. we have localized resources for.
                options.SupportedUICultures = supportedCultures;

                // You can change which providers are configured to determine the culture for requests, or even add a custom
                // provider with your own logic. The providers will be asked in order to provide a culture for each request,
                // and the first to provide a non-null result that is in the configured supported cultures list will be used.
                // By default, the following built-in providers are configured:
                // - QueryStringRequestCultureProvider, sets culture via "culture" and "ui-culture" query string values, useful for testing
                // - CookieRequestCultureProvider, sets culture via "ASPNET_CULTURE" cookie
                // - AcceptLanguageHeaderRequestCultureProvider, sets culture via the "Accept-Language" request header
                //options.RequestCultureProviders.Insert(0, new CustomRequestCultureProvider(async context =>
                //{
                //  // My custom request culture logic
                //  return new ProviderCultureResult("en");
                //}));
            });

            services.Configure<MvcOptions>(options =>
            {
                //  if(environment.IsProduction())
                //  {
                options.Filters.Add(new RequireHttpsAttribute());
                //   }


                options.CacheProfiles.Add("SiteMapCacheProfile",
                     new CacheProfile
                     {
                         Duration = 30
                     });

                options.CacheProfiles.Add("RssCacheProfile",
                     new CacheProfile
                     {
                         Duration = 100
                     });
            });

            services.AddMvc()
                .AddRazorOptions(options =>
                {
                    options.AddCloudscribeViewLocationFormats();

                    options.AddEmbeddedViewsForNavigation();
                    options.AddEmbeddedViewsForCloudscribeCore();
                    options.AddEmbeddedViewsForCloudscribeLogging();
                    options.AddEmbeddedViewsForSimpleContent();
                    options.AddEmbeddedViewsForCloudscribeCoreSimpleContentIntegration();

                    options.ViewLocationExpanders.Add(new cloudscribe.Core.Web.Components.SiteViewLocationExpander());
                })
                    ;


        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(
            IApplicationBuilder app,
            IHostingEnvironment env,
            ILoggerFactory loggerFactory,
            IOptions<cloudscribe.Core.Models.MultiTenantOptions> multiTenantOptionsAccessor,
            IServiceProvider serviceProvider,
            IOptions<RequestLocalizationOptions> localizationOptionsAccessor,
            cloudscribe.Logging.Web.ILogRepository logRepo
            )
        {
            loggerFactory.AddConsole(Configuration.GetSection("Logging"));
            loggerFactory.AddDebug();
            ConfigureLogging(loggerFactory, serviceProvider, logRepo);

            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
                app.UseBrowserLink();
            }
            else
            {
                app.UseExceptionHandler("/Home/Error");
            }
            
            app.UseForwardedHeaders();
            app.UseStaticFiles();

            // custom 404 and error page - this preserves the status code (ie 404)
            app.UseStatusCodePagesWithReExecute("/Home/Error/{0}");

            app.UseSession();

            app.UseRequestLocalization(localizationOptionsAccessor.Value);

            app.UseMultitenancy<cloudscribe.Core.Models.SiteSettings>();

            var multiTenantOptions = multiTenantOptionsAccessor.Value;

            app.UsePerTenant<cloudscribe.Core.Models.SiteSettings>((ctx, builder) =>
            {
                var tenant = ctx.Tenant;

                var shouldUseFolder = !multiTenantOptions.UseRelatedSitesMode
                                        && multiTenantOptions.Mode == cloudscribe.Core.Models.MultiTenantMode.FolderName
                                        && tenant.SiteFolderName.Length > 0;

                var externalCookieOptions = SetupOtherCookies(
                    cloudscribe.Core.Identity.AuthenticationScheme.External,
                    multiTenantOptions.UseRelatedSitesMode,
                    tenant);
                builder.UseCookieAuthentication(externalCookieOptions);

                var twoFactorRememberMeCookieOptions = SetupOtherCookies(
                    cloudscribe.Core.Identity.AuthenticationScheme.TwoFactorRememberMe,
                    multiTenantOptions.UseRelatedSitesMode,
                    tenant);
                builder.UseCookieAuthentication(twoFactorRememberMeCookieOptions);

                var twoFactorUserIdCookie = SetupOtherCookies(
                    cloudscribe.Core.Identity.AuthenticationScheme.TwoFactorUserId,
                    multiTenantOptions.UseRelatedSitesMode,
                    tenant);
                builder.UseCookieAuthentication(twoFactorUserIdCookie);

                var cookieEvents = new CookieAuthenticationEvents();
                var logger = loggerFactory.CreateLogger<cloudscribe.Core.Identity.SiteAuthCookieValidator>();
                var cookieValidator = new cloudscribe.Core.Identity.SiteAuthCookieValidator(logger);
                var appCookieOptions = SetupAppCookie(
                    cookieEvents,
                    cookieValidator,
                    cloudscribe.Core.Identity.AuthenticationScheme.Application,
                    multiTenantOptions.UseRelatedSitesMode,
                    tenant
                    );
                builder.UseCookieAuthentication(appCookieOptions);

                // known issue here is if a site is updated to populate the
                // social auth keys, it currently requires a restart so that the middleware gets registered
                // in order for it to work or for the social auth buttons to appear 
                builder.UseSocialAuth(ctx.Tenant, externalCookieOptions, shouldUseFolder);

            });

            UseMvc(app, multiTenantOptions.Mode == cloudscribe.Core.Models.MultiTenantMode.FolderName);

            CoreNoDbStartup.InitializeDataAsync(app.ApplicationServices).Wait();

        }



        private void UseMvc(IApplicationBuilder app, bool useFolders)
        {
            app.UseMvc(routes =>
            {
                if (useFolders)
                {
                    routes.AddBlogRoutesForSimpleContent(new cloudscribe.Core.Web.Components.SiteFolderRouteConstraint());
                }

                routes.AddBlogRoutesForSimpleContent();
                
                if (useFolders)
                {
                    routes.MapRoute(
                        name: "folderdefault",
                        template: "{sitefolder}/{controller}/{action}/{id?}",
                        defaults: new { controller = "Home", action = "Index" },
                        constraints: new { name = new cloudscribe.Core.Web.Components.SiteFolderRouteConstraint() }
                        );

                    routes.AddDefaultPageRouteForSimpleContent(new cloudscribe.Core.Web.Components.SiteFolderRouteConstraint());
                }

                routes.MapRoute(
                    name: "errorhandler",
                    template: "{controller}/{action}/{statusCode}"
                    );

                routes.MapRoute(
                    name: "def",
                    template: "{controller}/{action}"
                    );

                routes.AddDefaultPageRouteForSimpleContent();
                
            });
        }

        private void ConfigureAuthPolicy(IServiceCollection services)
        {
            //https://docs.asp.net/en/latest/security/authorization/policies.html

            services.AddAuthorization(options =>
            {
                options.AddPolicy(
                    "ServerAdminPolicy",
                    authBuilder =>
                    {
                        authBuilder.RequireRole("ServerAdmins");
                    });

                options.AddPolicy(
                    "CoreDataPolicy",
                    authBuilder =>
                    {
                        authBuilder.RequireRole("ServerAdmins");
                    });

                options.AddPolicy(
                    "AdminPolicy",
                    authBuilder =>
                    {
                        authBuilder.RequireRole("ServerAdmins", "Administrators");
                    });

                options.AddPolicy(
                    "UserManagementPolicy",
                    authBuilder =>
                    {
                        authBuilder.RequireRole("ServerAdmins", "Administrators");
                    });

                options.AddPolicy(
                    "RoleAdminPolicy",
                    authBuilder =>
                    {
                        authBuilder.RequireRole("Role Administrators", "Administrators");
                    });

                options.AddPolicy(
                    "SystemLogPolicy",
                    authBuilder =>
                    {
                        authBuilder.RequireRole("ServerAdmins");
                    });


                options.AddPolicy(
                    "BlogEditPolicy",
                    authBuilder =>
                    {
                        //authBuilder.RequireClaim("blogId");
                        authBuilder.RequireRole("Administrators");
                    }
                 );

                options.AddPolicy(
                    "PageEditPolicy",
                    authBuilder =>
                    {
                        authBuilder.RequireRole("Administrators");
                    });

                // add other policies here 

            });

        }

        private void ConfigureLogging(
            ILoggerFactory loggerFactory,
            IServiceProvider serviceProvider
            , cloudscribe.Logging.Web.ILogRepository logRepo
            )
        {
            // a customizable filter for logging
            LogLevel minimumLevel;
            if (environment.IsProduction())
            {
                minimumLevel = LogLevel.Warning;
            }
            else
            {
                minimumLevel = LogLevel.Information;
            }


            // add exclusions to remove noise in the logs
            var excludedLoggers = new List<string>
            {
                "Microsoft.AspNetCore.StaticFiles.StaticFileMiddleware",
                "Microsoft.AspNetCore.Hosting.Internal.WebHost",
            };

            Func<string, LogLevel, bool> logFilter = (string loggerName, LogLevel logLevel) =>
            {
                if (logLevel < minimumLevel)
                {
                    return false;
                }

                if (excludedLoggers.Contains(loggerName))
                {
                    return false;
                }

                return true;
            };

            loggerFactory.AddDbLogger(serviceProvider, logFilter, logRepo);
        }

        

        private CookieAuthenticationOptions SetupAppCookie(
            CookieAuthenticationEvents cookieEvents,
            cloudscribe.Core.Identity.SiteAuthCookieValidator siteValidator,
            string scheme,
            bool useRelatedSitesMode,
            cloudscribe.Core.Models.SiteSettings tenant
            )
        {
            var options = new CookieAuthenticationOptions();
            if (useRelatedSitesMode)
            {
                options.AuthenticationScheme = scheme;
                options.CookieName = scheme;
                options.CookiePath = "/";
            }
            else
            {
                options.AuthenticationScheme = $"{scheme}-{tenant.SiteFolderName}";
                options.CookieName = $"{scheme}-{tenant.SiteFolderName}";
                options.CookiePath = "/" + tenant.SiteFolderName;
                cookieEvents.OnValidatePrincipal = siteValidator.ValidatePrincipal;
            }

            var tenantPathBase = string.IsNullOrEmpty(tenant.SiteFolderName)
                ? PathString.Empty
                : new PathString("/" + tenant.SiteFolderName);

            options.LoginPath = tenantPathBase + "/account/login";
            options.LogoutPath = tenantPathBase + "/account/logoff";
            options.AccessDeniedPath = tenantPathBase + "/account/accessdenied";

            options.Events = cookieEvents;

            options.AutomaticAuthenticate = true;
            options.AutomaticChallenge = false;


            return options;
        }

        private CookieAuthenticationOptions SetupOtherCookies(
            string scheme,
            bool useRelatedSitesMode,
            cloudscribe.Core.Models.SiteSettings tenant
            )
        {
            var options = new CookieAuthenticationOptions();
            if (useRelatedSitesMode)
            {
                options.AuthenticationScheme = scheme;
                options.CookieName = scheme;
                options.CookiePath = "/";
            }
            else
            {
                options.AuthenticationScheme = $"{scheme}-{tenant.SiteFolderName}";
                options.CookieName = $"{scheme}-{tenant.SiteFolderName}";
                options.CookiePath = "/" + tenant.SiteFolderName;
            }

            options.AutomaticAuthenticate = false;

            return options;

        }
    }
}
