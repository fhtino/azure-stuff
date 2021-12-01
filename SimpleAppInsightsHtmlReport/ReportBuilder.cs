using Microsoft.Azure.ApplicationInsights.Query;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Mail;
using System.Net.Mime;
using System.Text;
using System.Xml;
using System.Xml.Serialization;
using System.Threading.Tasks;
using System.Collections.Concurrent;


namespace SimpleAppInsightsHtmlReport
{

    // ------------------------------------------------
    // !!! Single CS file with all required classes !!!
    // ------------------------------------------------


    public class ReportBuilder
    {


        public async Task<Report> Exec(string htmlTemplateFilename, AppInsightsConfig appInsCfg)
        {
            using (var fs = File.OpenRead(htmlTemplateFilename))
            {
                return await Exec(fs, appInsCfg);
            }
        }


        /// <summary>
        /// ...
        /// </summary>
        public async Task<Report> Exec(Stream inputStream, AppInsightsConfig appInsCfg)
        {
            var startDT = DateTime.UtcNow;

            var report = new Report() { Images = new ConcurrentDictionary<string, byte[]>() };

            var xdoc = new XmlDocument();
            xdoc.Load(inputStream);

            // read meta from head and cleanup them
            var nodeElements = xdoc.SelectNodes("/html/head/meta[starts-with(@name,'Report_')]").Cast<XmlElement>().ToList();
            //report.MetaValues = nodeElements.ToDictionary(x => x.GetAttribute("name"), x => x.GetAttribute("content"));
            report.MetaValues = new ConcurrentDictionary<string, string>(
                                        nodeElements.Select(n => new KeyValuePair<string, string>(n.GetAttribute("name"), n.GetAttribute("content"))));


            nodeElements.ForEach(x => { x.ParentNode.RemoveChild(x); });

            // try to read Application Insights ID and Key from template
            if (appInsCfg == null)
            {
                appInsCfg = new AppInsightsConfig()
                {
                    AppID = report.MetaValues["Report_AppInsightID"],
                    ApiKey = report.MetaValues["Report_AppInsightApiKey"]
                };
            }

            // Process nodes using palellel calls to improve performance
            //   Note: Parallel.ForEach<XmlElement>(nodeList, new ParallelOptions() { }, async (node) => ...
            //         does not work as expected. As far as I have understood, I cannot create an "async" Parallel.For

            var nodeList = xdoc.SelectNodes("//AppInsightData").Cast<XmlElement>();

            Func<XmlElement, Task> ProcessNode = async (XmlElement node) =>
            {
                var xsAppInsightData = new XmlSerializer(typeof(AppInsightData));
                var aid = (AppInsightData)xsAppInsightData.Deserialize(new StringReader(node.OuterXml));
                aid.CleanUp();

                // setup Application Insights client
                var apikeyAuth = new ApiKeyClientCredentials(appInsCfg.ApiKey);
                var appInsightsClient = new ApplicationInsightsDataClient(apikeyAuth);

                // Get data from Application Insights API                
                var queryResult = await appInsightsClient.Query.ExecuteAsync(appInsCfg.AppID, aid.Query);
                var tableResult = queryResult.Tables[0];
                string[,] matrix = AppInsightTableToMatrix(tableResult, aid.ToStrList);

                string htmlFragment;

                // Build Html/Images
                if (aid.OutMode == "TR")
                {
                    var columnsNames = tableResult.Columns.Select(x => x.Name).ToArray();
                    htmlFragment = Matrix2Html(matrix, columnsNames, aid.AlignList, aid.THeadStyle);
                }
                else if (aid.OutMode == "IMG" || aid.OutMode == "IMG-EMB")
                {
                    byte[] imgBody = CreateImgChart(
                        aid.ImgWidth, aid.ImgHeight, aid.ImgColor, true,
                        tableResult.Rows.Select(row => Double.Parse(row[1].ToString(), CultureInfo.InvariantCulture)).ToArray());

                    //report.Images.add(aid.ImgFileName, imgBody);
                    report.Images[aid.ImgFileName] = imgBody;

                    htmlFragment = aid.OutMode == "IMG" ?
                        $"<img src=\"{aid.ImgFileName}\" />" :
                        $"<img src=\"data:image/png;base64,{Convert.ToBase64String(imgBody)}\" />";
                }
                else
                {
                    throw new ApplicationException("Unknown OutMode");
                }

                XmlDocumentFragment docFrag = xdoc.CreateDocumentFragment();
                docFrag.InnerXml = htmlFragment;
                var parentNode = node.ParentNode;
                parentNode.ReplaceChild(docFrag, node);
            };

            await Task.WhenAll(nodeList.Select(n => ProcessNode(n)));

            ReplaceTag(xdoc, "DateUtcNow", DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"));
            ReplaceTag(xdoc, "ElapsedTime", DateTime.UtcNow.Subtract(startDT).TotalSeconds.ToString("0.00"));

            report.HTML = xdoc.OuterXml;

            return report;
        }



        /// <summary>
        /// ...
        /// </summary>
        private string[,] AppInsightTableToMatrix(Microsoft.Azure.ApplicationInsights.Query.Models.Table table,
                                                  List<string> toStringRules)
        {
            // App Insighrs types (???) :  string, int, long, real, timespan, datetime, bool, guid, dynamic 

            // add missing "tostring" rules
            while (toStringRules.Count < table.Columns.Count)
            {
                toStringRules.Add("");
            }

            // build data matrix
            var matrix = new string[table.Rows.Count, table.Columns.Count];
            for (int r = 0; r < table.Rows.Count; r++)
            {
                for (int c = 0; c < table.Columns.Count; c++)
                {
                    object fieldValue = table.Rows[r][c];
                    string toStringRule = toStringRules[c];

                    string body;

                    switch (table.Columns[c].Type)
                    {
                        case "string":
                            body = fieldValue.ToString();
                            break;
                        case "datetime":
                            DateTime dt = (DateTime)fieldValue;
                            body = String.IsNullOrEmpty(toStringRule) ? dt.ToString() : dt.ToString(toStringRule);
                            break;
                        case "long":
                            long longValue = (long)fieldValue;
                            body = String.IsNullOrEmpty(toStringRule) ? longValue.ToString() : longValue.ToString(toStringRule);
                            break;
                        case "real":
                            // Sometime (rarely) the cell is filled with a long value instead of a double value.
                            // Not clear if it's library bug or an Application Insights API bug (to be investigated... in the future... maybe)
                            double realValue;
                            try { realValue = (double)fieldValue; }
                            catch (InvalidCastException) { realValue = (double)(long)fieldValue; }
                            body = String.IsNullOrEmpty(toStringRule) ? realValue.ToString() : realValue.ToString(toStringRule);
                            break;
                        case "int":
                            long intValue = (long)fieldValue;  // ??? I don't know wht "int" is mapped as long
                            body = String.IsNullOrEmpty(toStringRule) ? intValue.ToString() : intValue.ToString(toStringRule);
                            break;
                        default:
                            body = "[" + fieldValue + "]";
                            break;
                    }

                    matrix[r, c] = body;
                }
            }

            return matrix;
        }


        /// <summary>
        /// ...
        /// </summary>
        private string Matrix2Html(string[,] matrix, string[] columnNames, List<string> aligns, string THeadStyle)
        {
            while (aligns.Count < matrix.GetLength(1))
            {
                aligns.Add("");
            }

            for (int c = 0; c < matrix.GetLength(1); c++)
            {
                aligns[c] = (aligns[c] + "").Trim().ToLower();
                switch (aligns[c])
                {
                    case "l": aligns[c] = "left"; break;
                    case "r": aligns[c] = "right"; break;
                    case "c": aligns[c] = "center"; break;
                    default: aligns[c] = "left"; break;
                }
            }

            var sb = new StringBuilder();

            if (columnNames != null)
            {
                sb.AppendLine($"<thead style=\"{THeadStyle}\">");
                sb.Append("<tr>");
                for (int c = 0; c < columnNames.Length; c++)
                {
                    sb.Append($"<td align=\"{aligns[c]}\">{columnNames[c]}</td>");
                }
                sb.AppendLine("</tr>");
                sb.AppendLine("</thead>");
            }

            sb.AppendLine("<tbody>");
            for (int r = 0; r < matrix.GetLength(0); r++)
            {
                sb.Append("<tr>");
                for (int c = 0; c < matrix.GetLength(1); c++)
                {
                    sb.Append($"<td align=\"{aligns[c]}\">{matrix[r, c]}</td>");
                }
                sb.AppendLine("</tr>");
            }
            sb.AppendLine("</tbody>");

            return sb.ToString();
        }


        /// <summary>
        /// ...
        /// </summary>
        private void ReplaceTag(XmlDocument xdoc, string tagName, string value)
        {
            foreach (XmlNode node in xdoc.SelectNodes("//" + tagName))
            {
                var newNode = xdoc.CreateElement("span");
                newNode.InnerText = value;
                node.ParentNode.ReplaceChild(newNode, node);
            }
        }


        /// <summary>
        /// Simple chart using ScottPlot
        /// </summary>
        private static byte[] CreateImgChart(int width,
                                           int height,
                                           string colorStr,
                                           bool showValueLabels,
                                           double[] values)
        {
            var plot = new ScottPlot.Plot(width, height);
            plot.AddScatter(Enumerable.Range(0, values.Length).Select(n => (double)n).ToArray(), values, color: System.Drawing.Color.DarkBlue);

            var maxLine = plot.AddHorizontalLine(values.Max());
            maxLine.Color = System.Drawing.Color.DarkRed;
            maxLine.LineWidth = 1;
            maxLine.PositionLabel = true;
            maxLine.PositionLabelBackground = maxLine.Color;            

            return plot.GetImageBytes();
        }



        // ------------------------------------------------------------------------------------------------
        // ------------------------------------------------------------------------------------------------
        // ------------------------------------------------------------------------------------------------

        public class Report
        {
            public string HTML { get; set; }
            public ConcurrentDictionary<string, byte[]> Images { get; set; }
            public ConcurrentDictionary<string, string> MetaValues { get; set; }

            public MailMessage ConvertToEmail(string fromEmail)
            {
                string[] toEmailList = MetaValues["Report_ToEmailList"].Split('#');

                var linkedResources = new List<LinkedResource>();
                var xdoc = new XmlDocument();
                xdoc.LoadXml(this.HTML);
                foreach (XmlElement imgNode in xdoc.SelectNodes("//img"))
                {
                    string imgSrc = imgNode.GetAttribute("src");
                    if (!imgSrc.StartsWith("data:"))
                    {
                        byte[] imgBody = this.Images[imgSrc];
                        var linkedIMG = new LinkedResource(new MemoryStream(imgBody), "image/png");
                        linkedResources.Add(linkedIMG);
                        imgNode.SetAttribute("src", "CID:" + linkedIMG.ContentId);
                    }
                }

                var alternateView = AlternateView.CreateAlternateViewFromString(xdoc.OuterXml, null, MediaTypeNames.Text.Html);
                linkedResources.ForEach(alternateView.LinkedResources.Add);
                var emailMsg = new MailMessage()
                {
                    From = new MailAddress(fromEmail),
                    Subject = MetaValues["Report_EmailSubject"].Replace("#DT#", DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss")),
                    IsBodyHtml = true,
                    AlternateViews = { alternateView },
                };
                toEmailList.ToList().ForEach(emailMsg.To.Add);

                return emailMsg;
            }

        }

        public class AppInsightsConfig
        {
            public string AppID { get; set; }
            public string ApiKey { get; set; }

        }

        public class AppInsightData
        {
            public string Query { get; set; }
            public string ToStr { get; set; }
            public string Align { get; set; }
            public string OutMode { get; set; }
            public string THeadStyle { get; set; }
            public string ImgFileName { get; set; }
            public int ImgWidth { get; set; }
            public int ImgHeight { get; set; }
            public string ImgColor { get; set; }

            [XmlIgnore]
            public List<string> ToStrList { get; set; }

            [XmlIgnore]
            public List<string> AlignList { get; set; }

            public void CleanUp()
            {
                Query = Query.Trim(' ', '\r', '\n');
                ToStrList = (ToStr + "").Split('#').Select(x => x.Trim()).ToList();
                AlignList = (Align + "").Split('#').Select(x => x.Trim()).ToList();
            }
        }

        // ------------------------------------------------------------------------------------------------
        // ------------------------------------------------------------------------------------------------
        // ------------------------------------------------------------------------------------------------        

    }

}
