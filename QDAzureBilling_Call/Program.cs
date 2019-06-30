using Microsoft.IdentityModel.Clients.ActiveDirectory;
using QDAzureBilling;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microcharts;



namespace QDAzureBilling_Call
{
    class Program
    {
        static void Main(string[] args)
        {
            string baseHtml = File.ReadAllText("report_template.html");

            // dev
            if (false) { CreateSampleHtmlHistogram(); return; }
            if (false) { File.WriteAllBytes("out.png", CreateImgChart(600, 200, Enumerable.Range(10, 30).Select(x => (double)x + DateTime.UtcNow.Ticks % 100), "#266489")); return; }


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
            Console.WriteLine("\nAuthentication:");
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


            Console.WriteLine("\nExtra data for histogram:");
            List<DailyCost> dailyCostsHisto =
                qdBilling.GetDailyPeriodCosts(
                    new TimePeriod()
                    {
                        DateFrom = DateTime.UtcNow.AddDays(-60),
                        DateTo = DateTime.UtcNow
                    }).Result;
            string htmlHistogram = BuildHtmlHistogram(dailyCostsHisto, lastBillingPeriod.DateFrom);

            byte[] chart1Body = CreateImgChart(
                600, 200,
                dailyCostsHisto.Where(x => x.DT < lastBillingPeriod.DateFrom).Select(x => x.Value), "#266489",
                dailyCostsHisto.Where(x => x.DT >= lastBillingPeriod.DateFrom).Select(x => x.Value), "#90D585");
            File.WriteAllBytes("chart1.png", chart1Body);





            /// HTML Report
            var list2html = new List2HtmlTable()
            {
                THStyle = "border: 1px solid black; padding:6px; background-color:darkblue; color:white;",
                TDStyle = "border: 1px solid black; padding:6px;"
            };

            baseHtml = baseHtml.Replace("###TOTAL###", servicesCosts.Sum(x => x.Value).ToString("0.00"));

            baseHtml = baseHtml.Replace("###HISTOGRAM###", htmlHistogram);

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



        private static string BuildHtmlHistogram(List<DailyCost> dailyCostsHisto, DateTime cutDate)
        {
            double scaleFactor = 100.0 / dailyCostsHisto.Max(x => x.Value);

            var histoData = dailyCostsHisto.Select(item =>
                                new HtmlHistogram.BarData()
                                {
                                    Value = (int)(item.Value * scaleFactor),
                                    Width = 5,
                                    BarColor = item.DT < cutDate ? "blue" : "green"
                                });

            string html = new HtmlHistogram()
            {
                TableBackgroundColor = "lightgrey;",
                TableBorderColor = "lightgrey",
                TableBorderSize = 5
            }
            .Build(histoData, dailyCostsHisto.Max(x => x.Value).ToString("0.00"));

            return html;
        }



        private static byte[] CreateImgChart(int width, int height, IEnumerable<double> valuesA, string colorA, IEnumerable<double> valuesB = null, string colorB = null)
        {
            var entries = valuesA.Select(v => new Microcharts.Entry((float)v) { Color = SKColor.Parse(colorA) });
            if (valuesB != null)
            {
                entries = entries.Concat(valuesB.Select(v => new Microcharts.Entry((float)v) { Color = SKColor.Parse(colorB) }));
            }

            var chart = new Microcharts.LineChart()
            {
                LineMode = LineMode.Straight,
                PointSize = 2,
                Entries = entries,
                BackgroundColor = SKColor.Parse("#FFFFFF"),
                PointMode = Microcharts.PointMode.Circle,
                Margin = 15
            };

            SKBitmap bitmap = new SKBitmap(width, height);
            SKCanvas canvas = new SKCanvas(bitmap);
            chart.Draw(canvas, width, height);

            string max = entries.Max(x => x.Value).ToString("0.00");

            canvas.DrawText($"{max}_______", 10, 15, new SKPaint()
            {
                Style = SKPaintStyle.Fill,
                IsAntialias = true,
                Color = SKColors.DarkBlue,
                TextSize = 14,
                IsStroke = false,
                FakeBoldText = true
            });

            var image = SKImage.FromBitmap(bitmap);
            var data = image.Encode(SKEncodedImageFormat.Png, 100);
            return data.ToArray();
        }



        private static void CreateSampleHtmlHistogram()
        {
            var sampleData = new HtmlHistogram.BarData[100];
            for (int i = 0; i < sampleData.Length; i++)
            {
                sampleData[i] = new HtmlHistogram.BarData
                {
                    Value = (int)(100 + 40 * Math.Sin(i / 10.0)),
                    BarColor = i < 40 ? "blue" : "green",
                    Width = 5,
                    BackgroundColor = (i > 60 && i < 90) ? "orange" : ""
                };
            }

            var sampleHtml = new HtmlHistogram()
            {
                TableBackgroundColor = "lightgrey;",
                TableBorderColor = "lightgrey",
                TableBorderSize = 5
            }.Build(sampleData, sampleData.Max(x => x.Value).ToString());

            sampleHtml = "<html><body><hr/>" + sampleHtml + "<hr/></body></html>";

            File.WriteAllText("sample.html", sampleHtml);
        }

    }
}
