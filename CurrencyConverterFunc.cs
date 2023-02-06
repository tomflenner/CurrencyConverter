using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Net.Http;
using Microsoft.Extensions.Configuration;
using System.Collections.Generic;
using Newtonsoft.Json.Converters;

namespace CurrencyConverter.Function
{
    public class ExchangeRateApiResponse
    {
        public string Result { get; set; }
        [JsonProperty("time_next_update_unix")]
        public uint UnixExpirationDate { get; set; }
        [JsonProperty("conversion_rates")]
        public Dictionary<string, decimal> ConvertionRates { get; set; }
    }

    public class CurrencyConverterFunc
    {
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
            var httpClient = new HttpClient();
            var exchangeRateApiUrl = $"https://v6.exchangerate-api.com/v6/{_configuration["ExchangeRateApiKey"]}/latest/EUR";
            var response = await httpClient.GetAsync(exchangeRateApiUrl);

            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                var apiContentResponse = JsonConvert.DeserializeObject<ExchangeRateApiResponse>(content);
                return new OkObjectResult(apiContentResponse);
            }
            else
            {
                return new BadRequestObjectResult("Error, something went wrong");
            }
        }
    }
}
