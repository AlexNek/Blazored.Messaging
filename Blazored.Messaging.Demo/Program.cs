using Blazor.Messaging.Demo.Components;

using Microsoft.AspNetCore.Components;

namespace Blazor.Messaging.Demo
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

           
            // Add services to the container.
            builder.Services.AddRazorComponents()
                .AddInteractiveServerComponents();
            //builder.Services.AddSingleton<IMessagingService, MessagingService>();
            builder.Services.AddScoped<IMessagingService>(sp =>
                {
                    var synchronizationContext = SynchronizationContext.Current;

                    return new MessagingService(synchronizationContext, TimeSpan.FromSeconds(10));
                });

            // for prerendering check
            builder.Services.AddHttpContextAccessor();

            var app = builder.Build();

            // Configure the HTTP request pipeline.
            if (!app.Environment.IsDevelopment())
            {
                app.UseExceptionHandler("/Error");
                // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
                app.UseHsts();
            }

            app.UseHttpsRedirection();

            app.UseAntiforgery();

            app.MapStaticAssets();
            app.MapRazorComponents<App>()
                .AddInteractiveServerRenderMode();

            app.Run();
        }
    }
}
