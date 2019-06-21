using Microsoft.IdentityModel.Clients.ActiveDirectory;
using QDAzureBilling;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace QDAzureBilling_Call
{
    class Program
    {
        static void Main(string[] args)
        {
            Billing qdBilling = new Billing()
            {
                TenantName = ConfigurationManager.AppSettings["TenantName"],
                ClientId = ConfigurationManager.AppSettings["ClientId"],
                ClientSecret = ConfigurationManager.AppSettings["ClientSecret"],
                SubscriptionId = ConfigurationManager.AppSettings["SubscriptionId"],
                RedirectUrl = ConfigurationManager.AppSettings["RedirectUrl"]
            };

            // authentication using user credentails (show a windows pop-up)
            //var pp = new PlatformParameters(PromptBehavior.Auto);
            //qdBilling.Authenticate(pp).Wait();

            // authentication through AD application secrets
            qdBilling.Authenticate().Wait();

            Console.WriteLine("\nLast billing periods:");
            var billingPeriods = qdBilling.GetBillingPeriods(5).Result;
            billingPeriods.ForEach(p => Console.WriteLine($" > {p.DateFrom.ToShortDateString()} {p.DateTo.ToShortDateString()}"));

            Console.WriteLine("\n\nLast period details:");
            var lastBillingPeriod = billingPeriods.First();

            Console.WriteLine("\nServices:");
            List<ServiceCost> servicesCosts = qdBilling.GetServicesPeriodCosts(lastBillingPeriod).Result;
            servicesCosts.ForEach(item => Console.WriteLine($" > {item.ServiceName} : {item.Value.ToString("0.00")}"));
            Console.WriteLine($" * Total: {servicesCosts.Sum(x => x.Value)}");

            Console.WriteLine("\nDay by day:");
            List<DailyCost> dailyCosts = qdBilling.GetDailyPeriodCosts(lastBillingPeriod).Result;
            dailyCosts.ForEach(x => Console.WriteLine($" > {x.DT.ToShortDateString()} : {x.Value}"));
            Console.WriteLine($" * Total: {dailyCosts.Sum(x => x.Value)}");

        }
    }
}
