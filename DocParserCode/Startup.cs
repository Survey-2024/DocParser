using Azure.Identity;
using Microsoft.Azure.Functions.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

// This assembly declaration is required to register Startup
[assembly: FunctionsStartup(typeof(DocParserCode.Startup))]

namespace DocParserCode
{
    public class Startup : FunctionsStartup
    {
        public IConfiguration Configuration { get; set; }

        public override void ConfigureAppConfiguration(IFunctionsConfigurationBuilder builder)
        {
            var config = builder.ConfigurationBuilder.Build();

            var connectionString = config.GetConnectionString("AppConfig");

            builder.ConfigurationBuilder.AddAzureAppConfiguration(options =>
            {
                options.Connect(connectionString)
                                .ConfigureKeyVault(kv =>
                                {
                                    kv.SetCredential(new DefaultAzureCredential());
                                });
            });
        }

        public override void Configure(IFunctionsHostBuilder builder)
        {
            // TODO: utilize options pattern to bind configuration to class
            // Issue: Configuration.GetSection("KeyVault") is null
            // However, Configuration.GetSection("KeyVault:AiCognitiveKey") yields a result

            Configuration = builder.GetContext().Configuration;
            //builder.Services.Configure<KeyVaultOptions>(Configuration.GetSection("KeyVault"));

        }
    }
}
