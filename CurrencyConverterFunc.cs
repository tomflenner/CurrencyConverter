using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System.Net.Http;
using Microsoft.Extensions.Configuration;
using System.Collections.Generic;
using StackExchange.Redis;

namespace CurrencyConverter.Function
{
    public class ExchangeRateApiResponse
    {
        public string Result { get; set; }

        [JsonProperty("time_next_update_unix")]
        public uint UnixExpirationDate { get; set; }

        [JsonProperty("time_last_update_unix")]
        public uint UnixLastUpdateDate { get; set; }

        [JsonProperty("conversion_rates")]
        public Dictionary<string, decimal> ConvertionRates { get; set; }
    }

    public class CurrencyConverterFunc
    {
        private const string CACHE_KEY = "EUR";
        private readonly IConfiguration _configuration;

        public CurrencyConverterFunc(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        [FunctionName("CurrencyConverterFunc")]
        public async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", "post", Route = null)] HttpRequest req,
            ILogger log)
        {
            string targetCurrency = req.Query["currency"];

            if (string.IsNullOrEmpty(targetCurrency))
                return new BadRequestObjectResult("Missing the query parameter currency in HTTP GET Request");

            var cache = ConnectionMultiplexer.Connect(_configuration["RedisConnectionString"]).GetDatabase();

            var exchangeRateApiCachedData = await cache.StringGetAsync(CACHE_KEY);

            ExchangeRateApiResponse exchangeRateApiResponse = null;

            if (!exchangeRateApiCachedData.IsNullOrEmpty)
            {
                exchangeRateApiResponse = JsonConvert.DeserializeObject<ExchangeRateApiResponse>(exchangeRateApiCachedData.ToString());
            }
            else
            {
                var httpClient = new HttpClient();
                var exchangeRateApiUrl = $"https://v6.exchangerate-api.com/v6/{_configuration["ExchangeRateApiKey"]}/latest/EUR";
                var response = await httpClient.GetAsync(exchangeRateApiUrl);

                if (response.IsSuccessStatusCode)
                {
                    exchangeRateApiResponse = JsonConvert.DeserializeObject<ExchangeRateApiResponse>(await response.Content.ReadAsStringAsync());

                    DateTime specificDate = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(exchangeRateApiResponse.UnixExpirationDate);
                    DateTime currentDate = DateTime.UtcNow;

                    TimeSpan expiry = specificDate - currentDate;

                    await cache.StringSetAsync(CACHE_KEY, JsonConvert.SerializeObject(exchangeRateApiResponse), expiry);
                }
                else
                {
                    return new BadRequestObjectResult("Error, something went wrong");
                }
            }

            if (exchangeRateApiResponse.ConvertionRates is not null)
            {
                decimal targetCurrencyRate;
                if (exchangeRateApiResponse.ConvertionRates.TryGetValue(targetCurrency.ToUpper(), out targetCurrencyRate))
                {
                    return new OkObjectResult(new { CurrencyCode = targetCurrency.ToUpper(), CurrencyRate = targetCurrencyRate, UnixTimeLastUpdate = exchangeRateApiResponse.UnixLastUpdateDate });
                }
                else
                {
                    return new ObjectResult($"Currency {targetCurrency} not found") { StatusCode = StatusCodes.Status404NotFound };
                }
            }
            else
            {
                return new ObjectResult("No data found for exchange rate") { StatusCode = StatusCodes.Status404NotFound };
            }
        }
    }
}
