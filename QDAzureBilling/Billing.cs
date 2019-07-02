using Microsoft.IdentityModel.Clients.ActiveDirectory;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;



namespace QDAzureBilling
{

    public class Billing
    {

        private static readonly string MANAGEMENT_URL = "https://management.azure.com/";
        private static readonly string LOGIN_SERVICE_URL = "https://login.microsoftonline.com";

        private HttpClient _httpClient = new HttpClient();


        public string TenantName { get; set; }
        public string ClientId { get; set; }
        public string ClientSecret { get; set; }
        public string SubscriptionId { get; set; }
        public string RedirectUrl { get; set; }



        public async Task Authenticate(PlatformParameters pp = null)
        {
            _httpClient = new HttpClient();

            var authorityURL = new Uri(new Uri(LOGIN_SERVICE_URL), TenantName).ToString();

            AuthenticationResult result;
            var authenticationContext = new AuthenticationContext(authorityURL);  // ADAL

            if (ClientSecret == null)
            {
                // user interactive authentication
                // On NET4.5 --> PlatformParameters pp = new PlatformParameters(PromptBehavior.Auto);
                result = await authenticationContext.AcquireTokenAsync(MANAGEMENT_URL, ClientId, new Uri(RedirectUrl), pp);
            }
            else
            {
                // application authentication
                ClientCredential clientCred = new ClientCredential(ClientId, ClientSecret);
                result = await authenticationContext.AcquireTokenAsync(MANAGEMENT_URL, clientCred);
            }

            if (result == null)
                throw new ApplicationException("Cannot obtain token");

            string accessToken = result.AccessToken;

            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);   // <--- JWT token for all requests
        }



        public async Task<List<TimePeriod>> GetBillingPeriods(int n)
        {
            string billingPeriodsURL = $"https://management.azure.com/subscriptions/{SubscriptionId}/providers/Microsoft.Billing/billingPeriods?api-version=2017-04-24-preview&$top={n}";

            var request = new HttpRequestMessage(HttpMethod.Get, billingPeriodsURL);
            var responseMessage = await _httpClient.SendAsync(request);
            var billindPeriodRespBody = await responseMessage.Content.ReadAsStringAsync();
            responseMessage.EnsureSuccessStatusCode();

            //string billindPeriodRespBody = await _httpClient.GetStringAsync(billingPeriodsURL);

            dynamic objBillingPeriods = Newtonsoft.Json.JsonConvert.DeserializeObject(billindPeriodRespBody);

            var outList = new List<TimePeriod>();
            for (int i = 0; i < objBillingPeriods.value.Count; i++)
            {
                DateTime startDT = objBillingPeriods.value[i].properties.billingPeriodStartDate;
                DateTime endDT = objBillingPeriods.value[i].properties.billingPeriodEndDate;
                outList.Add(new TimePeriod { DateFrom = startDT, DateTo = endDT });
            }

            return outList;
        }



        public async Task<List<DailyCost>> GetDailyPeriodCosts(TimePeriod tperiod)
        {

            dynamic requestObj = new
            {
                type = "Usage",
                dataSet = new
                {
                    granularity = "Daily",
                    aggregation = new
                    {
                        totalCost = new
                        {
                            name = "PreTaxCost",
                            function = "Sum"
                        }
                    },
                    sorting = new[] { new { direction = "ascending", name = "UsageDate" } }
                },
                timeframe = "Custom",
                timePeriod = new
                {
                    from = tperiod.DateFrom,
                    to = tperiod.DateTo
                }
            };

            var jsonBody = Newtonsoft.Json.JsonConvert.SerializeObject(requestObj, Newtonsoft.Json.Formatting.Indented);

            string url = $"https://management.azure.com/subscriptions/{SubscriptionId}/providers/Microsoft.CostManagement/query?api-version=2019-03-01-preview&$top=10000";

            var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
            var responseMessage = await _httpClient.SendAsync(request);
            var respBody = await responseMessage.Content.ReadAsStringAsync();
            dynamic responseObj = Newtonsoft.Json.JsonConvert.DeserializeObject(respBody);


            int columnPreTaxCostIDX = -1;
            int columnUsageDateIDX = -1;
            int columnCurrencyIDX = -1;

            for (int i = 0; i < responseObj.properties.columns.Count; i++)
            {
                switch (responseObj.properties.columns[i].name.ToString())
                {
                    case "PreTaxCost": columnPreTaxCostIDX = i; break;
                    case "UsageDate": columnUsageDateIDX = i; break;
                    case "Currency": columnCurrencyIDX = i; break;
                }
            }

            var outList = new List<DailyCost>();
            for (int i = 0; i < responseObj.properties.rows.Count; i++)
            {
                outList.Add(
                    new DailyCost
                    {
                        DT = DateTime.ParseExact(responseObj.properties.rows[i][columnUsageDateIDX].ToString(), "yyyyMMdd", CultureInfo.InvariantCulture),
                        Value = responseObj.properties.rows[i][columnPreTaxCostIDX]
                    });
            }

            return outList;
        }



        public async Task<List<ServiceCost>> GetServicesPeriodCosts(TimePeriod tperiod)
        {
            dynamic requestObj = new
            {
                type = "Usage",
                dataSet = new
                {
                    granularity = "None",
                    aggregation = new
                    {
                        totalCost = new
                        {
                            name = "PreTaxCost",
                            function = "Sum"
                        }
                    },
                    grouping = new[] { new { type = "Dimension", name = "ServiceName" } },
                },
                timeframe = "Custom",
                timePeriod = new
                {
                    from = tperiod.DateFrom,
                    to = tperiod.DateTo
                }
            };

            var jsonBody = Newtonsoft.Json.JsonConvert.SerializeObject(requestObj, Newtonsoft.Json.Formatting.Indented);

            string costURL = $"https://management.azure.com/subscriptions/{SubscriptionId}/providers/Microsoft.CostManagement/query?api-version=2019-01-01&$top=10000";

            var request = new HttpRequestMessage(HttpMethod.Post, costURL);
            request.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
            var responseMessage = await _httpClient.SendAsync(request);
            var respBody = await responseMessage.Content.ReadAsStringAsync();
            dynamic responseObj = Newtonsoft.Json.JsonConvert.DeserializeObject(respBody);


            int columnPreTaxCostIDX = -1;
            int columnServiceNameIDX = -1;
            int columnCurrencyIDX = -1;

            for (int i = 0; i < responseObj.properties.columns.Count; i++)
            {
                switch (responseObj.properties.columns[i].name.ToString())
                {
                    case "PreTaxCost": columnPreTaxCostIDX = i; break;
                    case "ServiceName": columnServiceNameIDX = i; break;
                    case "Currency": columnCurrencyIDX = i; break;
                }
            }

            var outList = new List<ServiceCost>();
            for (int i = 0; i < responseObj.properties.rows.Count; i++)
            {
                outList.Add(
                    new ServiceCost
                    {
                        Value = responseObj.properties.rows[i][columnPreTaxCostIDX],
                        ServiceName = responseObj.properties.rows[i][columnServiceNameIDX],
                        Currency = responseObj.properties.rows[i][columnCurrencyIDX],
                    });
            }

            // OLD version. Direct on array positions.
            //foreach (var row in responseObj.properties.rows)
            //{                
            //    costs.Details.Add(new ServiceCost() { Value = row[0], ServiceName = row[1], Currency = row[2] });
            //}

            return outList;
        }




        // --------------------------------------------------------------------------------------------
        // --------------------------------------------------------------------------------------------
        // --------------------------------------------------------------------------------------------

        public void Experiments_GetCredit()
        {
            throw new ApplicationException("Does not work");

            string url = "https://s2.billing.ext.azure.com/api/Billing/RemainingCredits";

            // Request:
            //    "mimeType": "application/json",
            //"text": "[{\"subscriptionId\":\"?????????????\",\"quotaId\":\"MSDN_2014-09-01\"}]"

            // Response:
            //  "text": "[{\"subscriptionId\":\"??????????????????????????\",\"currency\":\"EUR\",\"total\":130.0,\"remaining\":94.24,\"showUpgradeNotification\":false}]"

            string jsonBody = "[{\"subscriptionId\":\"" + SubscriptionId + "\", \"quotaId\":\"MSDN_2014-09-01\"}]";


            var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
            var responseMessage = _httpClient.SendAsync(request).Result;
            var respBody = responseMessage.Content.ReadAsStringAsync().Result;
            dynamic responseObj = Newtonsoft.Json.JsonConvert.DeserializeObject(respBody);
        }


    }
}
