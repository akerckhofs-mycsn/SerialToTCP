using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using Serilog.Events;
using Serilog.Sinks.Elasticsearch;
using Serilog.Sinks.SystemConsole.Themes;

namespace SerialToTCP
{
    public class Program
    {
        public static void Main(string[] args) {
            CreateHostBuilder(args).Build().Run();
        }
        
        public static Action<HostBuilderContext, LoggerConfiguration> ConfigureLogger =>
            (hostingContext, loggerConfiguration) => {

                var env = hostingContext.HostingEnvironment;

                var appName = env.ApplicationName.EndsWith("Gateway") ?
                     $"{env.ApplicationName}.{hostingContext.Configuration["Logging:Zone"]}" : env.ApplicationName;

                loggerConfiguration.MinimumLevel.Information()
                .Enrich.FromLogContext()
                .Enrich.WithProperty("ApplicationName", appName)
                .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
                .MinimumLevel.Override("System.Net.Http.HttpCient", LogEventLevel.Warning)
                .MinimumLevel.Override("Microsoft.Hosting.Lifetime", LogEventLevel.Information)
                .WriteTo.Console(theme: AnsiConsoleTheme.Code);

                if(hostingContext.HostingEnvironment.IsDevelopment())
                {
                    loggerConfiguration
                        .MinimumLevel.Override("Biostoom", LogEventLevel.Debug)
                        .MinimumLevel.Override("Akka", LogEventLevel.Debug);
                }

                var elasticUrl = hostingContext.Configuration["Logging:ElasticUrl"];
                var userid = hostingContext.Configuration["Logging:UserId"];
                var password = hostingContext.Configuration["Logging:Password"];

                if(!string.IsNullOrEmpty(elasticUrl))
                {

                    var options = new ElasticsearchSinkOptions(new Uri(elasticUrl))
                    {
                        AutoRegisterTemplate = true,
                        IndexFormat = "sitetraffic-logs-{0:yyyyMMdd}",
                        MinimumLogEventLevel = LogEventLevel.Debug, //the minimumLevels set above still apply
                        ConnectionTimeout = TimeSpan.FromSeconds(260)
                    };

                    if(!string.IsNullOrEmpty(userid) && !string.IsNullOrEmpty(password))
                    {
                        options.ModifyConnectionSettings = x => x.BasicAuthentication(userid, password);
                    }

                    loggerConfiguration.WriteTo.Elasticsearch(options);
                }
            };

        public static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .ConfigureServices((hostContext, services) => { services.AddHostedService<Worker>(); })
                .UseWindowsService()
                .UseSerilog(ConfigureLogger);
    }
}