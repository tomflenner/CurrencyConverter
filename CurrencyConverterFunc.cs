using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System.Net.Http;
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
        private readonly IConnectionMultiplexer _redis;

        public CurrencyConverterFunc(IConnectionMultiplexer redis)
        {
            _redis = redis;
        }

        [FunctionName("CurrencyConverterFunc")]
        public async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = null)] HttpRequest req,
            ILogger log)
        {
            string fromValueQueryParam = req.Query["fromValue"];
            string fromCurrencyQueryParam = req.Query["fromCurrency"];
            string toCurrencyQueryParam = req.Query["toCurrency"];

            if (string.IsNullOrEmpty(fromValueQueryParam))
                return new BadRequestObjectResult("Missing the query parameter fromValue in HTTP GET Request");

            if (string.IsNullOrEmpty(fromCurrencyQueryParam))
                return new BadRequestObjectResult("Missing the query parameter fromCurrency in HTTP GET Request");

            if (string.IsNullOrEmpty(toCurrencyQueryParam))
                return new BadRequestObjectResult("Missing the query parameter toCurrency in HTTP GET Request");

            decimal fromValue;

            if (!decimal.TryParse(fromValueQueryParam, out fromValue))
                return new ObjectResult(value: "Error while parsing fromValueQueryParam to decimal.") { StatusCode = StatusCodes.Status500InternalServerError };

            fromCurrencyQueryParam = fromCurrencyQueryParam.ToUpper();
            toCurrencyQueryParam = toCurrencyQueryParam.ToUpper();

            var cache = _redis.GetDatabase();

            var exchangeRateApiCachedData = await cache.StringGetAsync(fromCurrencyQueryParam);

            ExchangeRateApiResponse exchangeRateApiResponse = null;

            if (!exchangeRateApiCachedData.IsNullOrEmpty)
            {
                exchangeRateApiResponse = JsonConvert.DeserializeObject<ExchangeRateApiResponse>(exchangeRateApiCachedData.ToString());
            }
            else
            {
                var httpClient = new HttpClient();
                var exchangeRateApiUrl = $"https://v6.exchangerate-api.com/v6/{System.Environment.GetEnvironmentVariable("ExchangeRateApiKey", EnvironmentVariableTarget.Process)}/latest/{fromCurrencyQueryParam}";
                var response = await httpClient.GetAsync(exchangeRateApiUrl);

                if (response.IsSuccessStatusCode)
                {
                    exchangeRateApiResponse = JsonConvert.DeserializeObject<ExchangeRateApiResponse>(await response.Content.ReadAsStringAsync());

                    DateTime specificDate = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(exchangeRateApiResponse.UnixExpirationDate);
                    DateTime currentDate = DateTime.UtcNow;

                    TimeSpan expiry = specificDate - currentDate;

                    await cache.StringSetAsync(fromCurrencyQueryParam, JsonConvert.SerializeObject(exchangeRateApiResponse), expiry);
                }
                else
                {
                    return new BadRequestObjectResult("Error, something went wrong");
                }
            }

            if (exchangeRateApiResponse.ConvertionRates is not null)
            {
                decimal targetCurrencyRate;
                if (exchangeRateApiResponse.ConvertionRates.TryGetValue(toCurrencyQueryParam, out targetCurrencyRate))
                {
                    return new OkObjectResult(new
                    {
                        FromCurrencyCode = fromCurrencyQueryParam,
                        ToCurrencyCode = toCurrencyQueryParam,
                        CurrencyRate = targetCurrencyRate,
                        ConvertedValue = fromValue * targetCurrencyRate,
                        UnixTimeLastUpdate = exchangeRateApiResponse.UnixLastUpdateDate
                    });
                }
                else
                {
                    return new ObjectResult($"Currency {toCurrencyQueryParam} not found") { StatusCode = StatusCodes.Status404NotFound };
                }
            }
            else
            {
                return new ObjectResult("No data found for exchange rate") { StatusCode = StatusCodes.Status404NotFound };
            }
        }
    }
}
