using Microsoft.Extensions.Configuration;
using System;
using System.IO;
using System.Xml;
using System.Linq;
using System.Net.Mail;
using System.Net;
using System.Net.Mime;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SimpleAppInsightsHtmlReport
{
    class Program
    {
        static async Task Main(string[] args)
        {
            var configuration = new ConfigurationBuilder()
                .AddJsonFile("appsettings.local.json", optional: true, reloadOnChange: true)
                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
                .Build();

            // Build the report
            var reportBuilder = new ReportBuilder();
            var report = await reportBuilder.Exec("../../../template3.donotcommit.html");
            reportBuilder.Logs.ForEach(Console.WriteLine);
            Console.WriteLine(reportBuilder.ErrorPresent);

            // Save html and images to local hard-disk
            System.IO.File.WriteAllText("report.html", report.HTML);
            foreach (var img in report.Images)
            {
                System.IO.File.WriteAllBytes(img.Key, img.Value);
            }
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("report.html") { UseShellExecute = true });
          
            return;

            // Send the email
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
