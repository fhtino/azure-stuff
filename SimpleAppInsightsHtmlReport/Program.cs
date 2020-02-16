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

            var cfg = new ReportBuilder.AppInsightsConfig()
            {
                AppID = configuration.GetValue<string>("AppID"),
                ApiKey = configuration.GetValue<string>("ApiKey")
            };

            var report = new ReportBuilder().Exec("../../../template1.html", cfg);

            // save html and images
            System.IO.File.WriteAllText("report.html", report.HTML);
            foreach (var img in report.Images)
            {
                System.IO.File.WriteAllBytes(img.Key, img.Value);
            }

            if (!String.IsNullOrEmpty(configuration["SmtpFrom"]))
            {
                PrepareAndSendEmail(report,
                                    "This is a test",
                                    configuration["SmtpFrom"],
                                    new[] { configuration["SmtpTo"] },
                                    configuration["SmtpHostName"],
                                    int.Parse(configuration["SmtpPort"]),
                                    configuration["SmtpUsername"],
                                    configuration["SmtpPassword"]);
            }
        }



        private static void PrepareAndSendEmail(
                                ReportBuilder.Report report,
                                string subject, string fromEmail, string[] toEmailList,
                                string SmtpHostName, int SmtpPort, string SmtpUsername, string SmtpPassword)
        {
            // prepare
            var linkedResources = new List<LinkedResource>();
            var xdoc = new XmlDocument();
            xdoc.LoadXml(report.HTML);
            foreach (XmlElement imgNode in xdoc.SelectNodes("//img"))
            {
                string imgSrc = imgNode.GetAttribute("src");
                byte[] imgBody = report.Images[imgSrc];
                var linkedIMG = new LinkedResource(new MemoryStream(imgBody), "image/png");
                linkedResources.Add(linkedIMG);
                imgNode.SetAttribute("src", "CID:" + linkedIMG.ContentId);
            }

            var alternateView = AlternateView.CreateAlternateViewFromString(xdoc.OuterXml, null, MediaTypeNames.Text.Html);
            linkedResources.ForEach(alternateView.LinkedResources.Add);
            var emailMsg = new MailMessage()
            {
                From = new MailAddress(fromEmail),
                Subject = subject,
                IsBodyHtml = true,
                AlternateViews = { alternateView },
            };
            toEmailList.ToList().ForEach(emailMsg.To.Add);

            // send
            using (SmtpClient smtpClient = new SmtpClient(SmtpHostName, SmtpPort))
            {
                smtpClient.EnableSsl = true;
                smtpClient.Credentials = new NetworkCredential(SmtpUsername, SmtpPassword);
                smtpClient.Send(emailMsg);
            }
        }


    }
}
