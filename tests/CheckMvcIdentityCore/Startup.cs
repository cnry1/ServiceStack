﻿using IdentityDemo.Data;
using IdentityDemo.Models;
using IdentityDemo.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Funq;
using ServiceStack;
using ServiceStack.Auth;

namespace IdentityDemo
{
    public class Startup
    {
        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        // This method gets called by the runtime. Use this method to add services to the container.
        public IConfiguration Configuration { get; }

        public void ConfigureServices(IServiceCollection services)
        {
            services.AddDbContext<ApplicationDbContext>(options =>
                options.UseSqlServer(Configuration.GetConnectionString("DefaultConnection")));

            services.AddIdentity<ApplicationUser, IdentityRole>()
                .AddEntityFrameworkStores<ApplicationDbContext>()
                .AddDefaultTokenProviders();

            services.Configure<IdentityOptions>(options =>
            {
                // Password settings
                // options.Password.RequireDigit = true;
                // options.Password.RequiredLength = 8;
                // options.Password.RequireNonAlphanumeric = false;
                // options.Password.RequireUppercase = true;
                // options.Password.RequireLowercase = false;
                // options.Password.RequiredUniqueChars = 6;

                options.Password.RequireDigit = false;
                options.Password.RequiredLength = 4;
                options.Password.RequireNonAlphanumeric = false;
                options.Password.RequireUppercase = false;
                options.Password.RequireLowercase = false;
                options.Password.RequiredUniqueChars = 2;


                // Lockout settings
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(30);
                options.Lockout.MaxFailedAccessAttempts = 10;
                options.Lockout.AllowedForNewUsers = true;

                // User settings
                options.User.RequireUniqueEmail = true;
            });

            services.ConfigureApplicationCookie(options =>
            {
                // Cookie settings
                options.Cookie.HttpOnly = true;
                options.Cookie.Expiration = TimeSpan.FromDays(150);
                // If the LoginPath isn't set, ASP.NET Core defaults 
                // the path to /Account/Login.
                options.LoginPath = "/Account/Login";
                // If the AccessDeniedPath isn't set, ASP.NET Core defaults 
                // the path to /Account/AccessDenied.
                options.AccessDeniedPath = "/Account/AccessDenied";
                options.SlidingExpiration = true;
            });

            // Add application services.
            services.AddTransient<IEmailSender, EmailSender>();

            services.AddMvc();
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IHostingEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
                app.UseBrowserLink();
                app.UseDatabaseErrorPage();
            }
            else
            {
                app.UseExceptionHandler("/Home/Error");
            }

            app.UseStaticFiles();

            app.UseAuthentication();

            app.UseServiceStack(new AppHost {
                AppSettings = new NetCoreAppSettings(Configuration)
            });

            app.UseMvc(routes =>
            {
                routes.MapRoute(
                    name: "default",
                    template: "{controller=Home}/{action=Index}/{id?}");
            });

            CreateRoles(app).Wait();
        }
        
        private async Task CreateRoles(IApplicationBuilder app)
        {
            var scopeFactory = app.ApplicationServices.GetRequiredService<IServiceScopeFactory>();

            using (var scope = scopeFactory.CreateScope())
            {
                //initializing custom roles 
                var RoleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
                var UserManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
                string[] roleNames = { "Admin", "Manager", "Member" };
                IdentityResult roleResult;

                foreach (var roleName in roleNames)
                {
                    var roleExist = await RoleManager.RoleExistsAsync(roleName);
                    if (!roleExist)
                    {
                        //create the roles and seed them to the database: Question 1
                        roleResult = await RoleManager.CreateAsync(new IdentityRole(roleName));
                    }
                }
            
                var user = await UserManager.FindByEmailAsync("test@gmail.com");
                if (user != null)
                {
                    foreach (var role in roleNames)
                    {
                        //here we tie the new user to the role
                        await UserManager.AddToRoleAsync(user, role);
                    }
                }
            }
        }
    }
    
    public class AppHost : AppHostBase
    {
        public AppHost() : base("IdentityDemo", typeof(HelloService).Assembly) { }

        public override void Configure(Container container)
        {
            var authRepo = new InMemoryAuthRepository();
            container.Register<IAuthRepository>(c => authRepo);
            authRepo.CreateUserAuth(new UserAuth {
                UserName = "test",
                Email = "test@gmail.com",
                DisplayName = "ServiceStack User",
                Roles = new List<string> { "Member" },
            }, "test");
            
            Plugins.Add(new AuthFeature(() => new AuthUserSession(), 
                new IAuthProvider[] {
                    new CredentialsAuthProvider(AppSettings), 
                    new NetCoreIdentityAuthProvider(AppSettings) 
                    {
                        PopulateSessionFilter = (session, principal, req) => 
                        {
                            //Example of populating ServiceStack Session Roles for EF Identity DB
                            var userManager = req.TryResolve<UserManager<ApplicationUser>>();
                            var user = userManager.FindByIdAsync(session.Id).Result;
                            var roles = userManager.GetRolesAsync(user).Result;
                            session.Roles = roles.ToList();
                        }
                    }, 
                }));
            
            SetConfig(new HostConfig {
                AdminAuthSecret = "secret"
            });
        }
    }

    [Route("/hello")]
    public class Hello : IReturn<HelloResponse>
    {
        public string Name { get; set; }
    }

    [Route("/hello-member")]
    public class HelloMember : IReturn<HelloResponse>
    {
        public string Name { get; set; }
    }

    [Route("/hello-manager")]
    public class HelloManager : IReturn<HelloResponse>
    {
        public string Name { get; set; }
    }

    [Route("/hello-admin")]
    public class HelloAdmin : IReturn<HelloResponse>
    {
        public string Name { get; set; }
    }

    public class HelloResponse
    {
        public string Result { get; set; }
    }

    [Route("/hello-auth")]
    public class HelloAuth : IReturn<HelloResponse>
    {
        public string Name { get; set; }
    }

    public class HelloService : Service
    {
        public object Any(Hello request) => new HelloResponse { Result = $"Hello, {request.Name}!" };

        [Authenticate]
        public object Any(HelloAuth request) => new HelloResponse { Result = $"Hello Auth, {request.Name}!" };

        [RequiredRole("Member")]
        public object Any(HelloMember request) => new HelloResponse { Result = $"Hello Member, {request.Name}!" };

        [RequiredRole("Manager")]
        public object Any(HelloManager request) => new HelloResponse { Result = $"Hello Manager, {request.Name}!" };

        [RequiredRole("Admin")]
        public object Any(HelloAdmin request) => new HelloResponse { Result = $"Hello Admin, {request.Name}!" };
    }
}
