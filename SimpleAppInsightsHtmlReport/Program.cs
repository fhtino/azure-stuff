using Microsoft.Extensions.Configuration;
using System;
using System.IO;
using System.Xml;
using System.Linq;
using System.Net.Mail;
using System.Net;
using System.Net.Mime;
using System.Collections.Generic;


namespace SimpleAppInsightsHtmlReport
{
    class Program
    {
        static void Main(string[] args)
        {
            var configuration = new ConfigurationBuilder()
                .AddJsonFile("appsettings.local.json", optional: true, reloadOnChange: true)
                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
                .Build();

            // we can read Application Insight ID and Key from an external file or from html template
            //var cfg = new ReportBuilder.AppInsightsConfig()
            //{
            //    AppID = configuration.GetValue<string>("AppID"),
            //    ApiKey = configuration.GetValue<string>("ApiKey")
            //};
            ReportBuilder.AppInsightsConfig cfg = null;

            // Build report
            var report = new ReportBuilder().Exec("../../../template2.donotcommit.html", cfg).Result;

            // Save html and images
            System.IO.File.WriteAllText("report.html", report.HTML);
            foreach (var img in report.Images)
            {
                System.IO.File.WriteAllBytes(img.Key, img.Value);
            }

            // Send email
            if (!String.IsNullOrEmpty(configuration["SmtpFrom"]))
            {
                MailMessage emailMsg = report.ConvertToEmail(configuration["SmtpFrom"]);

                using (SmtpClient smtpClient = new SmtpClient(configuration["SmtpHostName"], int.Parse(configuration["SmtpPort"])))
                {
                    smtpClient.EnableSsl = true;
                    smtpClient.Credentials = new NetworkCredential(configuration["SmtpUsername"], configuration["SmtpPassword"]);
                    smtpClient.Send(emailMsg);
                }
            }
        }

    }
}
