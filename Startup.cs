using System;
using Microsoft.Azure.Functions.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;

[assembly: FunctionsStartup(typeof(CurrencyConverter.Startup))]

namespace CurrencyConverter
{
    public class Startup : FunctionsStartup
    {
        public override void Configure(IFunctionsHostBuilder builder)
        {
            var muxer = ConnectionMultiplexer.Connect(System.Environment.GetEnvironmentVariable("RedisConnectionString", EnvironmentVariableTarget.Process));
            builder.Services.AddSingleton<IConnectionMultiplexer>(muxer);
        }
    }
}