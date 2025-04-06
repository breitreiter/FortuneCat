using System.Diagnostics;
using OpenTelemetry.Instrumentation.AspNetCore;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Grafana.OpenTelemetry;

namespace FortuneService
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            if (Debugger.IsAttached)
            {
                builder.Services.AddCors(p => p.AddPolicy("corsapp", builder =>
                {
                    builder.WithOrigins("*").AllowAnyMethod().AllowAnyHeader();
                }));
            }
            else
            {
                builder.Services.AddCors(p => p.AddPolicy("corsapp", builder =>
                {
                    builder.WithOrigins("https://dreamlands.org").AllowAnyMethod().AllowAnyHeader();
                }));
            }

            // Add services to the container.

            SetupOtel(builder);

            builder.Services.AddSingleton<ScreenContents>();
            builder.Services.AddHostedService<AiGenerator>();

            builder.Services.AddControllers();
            // Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
            builder.Services.AddEndpointsApiExplorer();
            builder.Services.AddSwaggerGen();

            var app = builder.Build();

            // Configure the HTTP request pipeline.
            if (app.Environment.IsDevelopment())
            {
                app.UseSwagger();
                app.UseSwaggerUI();
            }


            app.UseHttpsRedirection();

            app.UseAuthorization();

            app.UseCors("corsapp");

            app.MapControllers();

            app.Run();
        }

        private static void SetupOtel(WebApplicationBuilder appBuilder)
        {
            //// Build a resource configuration action to set service information.
            Action<ResourceBuilder> configureResource = r => r.AddService(
                serviceName: "FortuneCat",
                serviceVersion: typeof(Program).Assembly.GetName().Version?.ToString() ?? "unknown",
                serviceInstanceId: Environment.MachineName);

            // Create a service to expose ActivitySource, and Metric Instruments
            // for manual instrumentation
            appBuilder.Services.AddSingleton<Instrumentation>();

            appBuilder.Services.AddOpenTelemetry()
                .WithTracing(builder =>
                {
                    // Tracing

                    // Ensure the TracerProvider subscribes to any custom ActivitySources.
                    builder
                        .AddSource(Instrumentation.ActivitySourceName)
                        .SetSampler(new AlwaysOnSampler())
                        .AddHttpClientInstrumentation()
                        .AddAspNetCoreInstrumentation()
                        .ConfigureResource(configureResource);

                    // Use IConfiguration binding for AspNetCore instrumentation options.
                    appBuilder.Services.Configure<AspNetCoreTraceInstrumentationOptions>(appBuilder.Configuration.GetSection("AspNetCoreInstrumentation"));
                })
                .WithMetrics(builder =>
                {
                    builder.AddMeter(Instrumentation.MeterName)
                            .AddRuntimeInstrumentation()
                            .AddHttpClientInstrumentation()
                            .AddAspNetCoreInstrumentation()
                            .ConfigureResource(configureResource);
                }
                )
                .UseGrafana();

            appBuilder.Logging.AddOpenTelemetry(logging => logging.UseGrafana());

            Pyroscope.Profiler.Instance.SetExceptionTrackingEnabled(true);

        }
    }

}
