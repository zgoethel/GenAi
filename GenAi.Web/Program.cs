using GenAi.Backend.Services;
using GenAi.Backend.ViewModels;
using GenAi.Web.Components;
using Microsoft.Extensions.AI;

namespace GenAi.Web
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            // Add services to the container.
            builder.Services.AddRazorComponents()
                .AddInteractiveServerComponents();

            builder.Services.AddSingleton<OllamaService>();
            builder.Services.AddSingleton<WebUiService>();

            builder.Services.AddTransient<UniqueConversation>();
            builder.Services.AddTransient<GeneralbotConversation>();
            builder.Services.AddTransient<SalesbotConversation>();

            builder.Services.AddTransient<HomeViewModel>();

            builder.Services.AddControllersWithViews();
            builder.Services.AddRazorPages();
            builder.Services.AddDistributedMemoryCache();

            builder.Services.AddSession((options) =>
            {
                options.Cookie.Name = "GenAi.Session";
                options.Cookie.IsEssential = true;
                options.Cookie.HttpOnly = true;
                options.Cookie.SameSite = SameSiteMode.None;
                options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            });

            builder.Services.AddAuthentication()
                .AddCookie((options) =>
                {
                    options.Cookie.Name = "GenAi.Identity";
                    options.LoginPath = "/login";
                    options.AccessDeniedPath = "/login";
                    options.Cookie.IsEssential = true;
                    options.Cookie.HttpOnly = true;
                    options.Cookie.SameSite = SameSiteMode.None;
                    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
                });

            builder.Services.AddAuthorization();

            using var app = builder.Build();

            // Configure the HTTP request pipeline.
            if (!app.Environment.IsDevelopment())
            {
                app.UseExceptionHandler("/Error");
                // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
                app.UseHsts();
            }

            app.UseHttpsRedirection();
            app.UseStaticFiles();
            app.UseAntiforgery();
            app.UseRouting();
            app.UseAuthentication();
            app.UseAuthorization();
            app.UseSession();
            app.UseAntiforgery();
            app.MapControllers();
            app.MapRazorPages();

            app.MapRazorComponents<App>()
                .AddInteractiveServerRenderMode();

            app.Start();

            Task.Run(async () =>
            {
                for (;; await Task.Delay(TimeSpan.FromMinutes(4)))
                {
                    var log = app.Services.GetRequiredService<ILogger<Program>>();
                    var ollama = app.Services.GetRequiredService<OllamaService>();
                    
                    try
                    {
                        var prompt = "I say ping, you say pong. Ping";
                        log.LogDebug("[Keepalive prompt] {}", prompt);
                        var pong = await ollama.CreateAiResponse(
                            [
                                new ChatMessage(ChatRole.User, prompt)
                            ]);

                        log.LogDebug("[Keepalive response] {}", pong.Text?.Trim() ?? "null");
                    } catch (Exception ex)
                    {
                        log.LogError(ex, "Failed to complete model keepalive task");
                    }

                    try
                    {
                        var prompt = "I say ping, you say pong. Ping";
                        log.LogDebug("[Keepalive prompt] {}", prompt);
                        var pong = await ollama.CreateAiResponse(
                            [
                                new ChatMessage(ChatRole.User, prompt)
                            ],
                            endpointPrefix: "Cheap");

                        log.LogDebug("[Keepalive response] {}", pong.Text?.Trim() ?? "null");
                    }
                    catch (Exception ex)
                    {
                        log.LogError(ex, "Failed to complete model keepalive task");
                    }
                }
            });

            app.WaitForShutdown();
        }
    }
}
