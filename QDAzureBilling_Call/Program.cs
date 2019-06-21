using Microsoft.IdentityModel.Clients.ActiveDirectory;
using QDAzureBilling;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace QDAzureBilling_Call
{
    class Program
    {
        static void Main(string[] args)
        {
            string baseHtml = File.ReadAllText("report_template.html");


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


            /// HTML Report
            var list2html = new List2HtmlTable()
            {
                THStyle = "border: 1px solid black; padding:6px; background-color:darkblue; color:white;",
                TDStyle = "border: 1px solid black; padding:6px;"
            };

            baseHtml = baseHtml.Replace("###TOTAL###", servicesCosts.Sum(x => x.Value).ToString("0.00"));

            baseHtml = baseHtml.Replace("###PERIOD###", lastBillingPeriod.DateFrom.ToShortDateString() + " - " +
                                                        lastBillingPeriod.DateTo.ToShortDateString());

            baseHtml = baseHtml.Replace("###SERVICE_TABLE###", list2html.Exec(
                servicesCosts
                .OrderByDescending(x => x.Value)
                .Select(x => new { Service = x.ServiceName, Value = x.Value.ToString("0.00") })));

            baseHtml = baseHtml.Replace("###DAILY_TABLE###", list2html.Exec(
                dailyCosts
                .OrderByDescending(x => x.DT)
                .Select(x => new { Date = x.DT.ToShortDateString(), Value = x.Value.ToString("0.00") })));

            File.WriteAllText("out.html", baseHtml);

        }
    }
}
