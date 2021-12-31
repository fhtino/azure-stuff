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
using System.Drawing;

namespace SimpleAppInsightsHtmlReport
{

    // ------------------------------------------------
    // !!! Single CS file with all required classes !!!
    // ------------------------------------------------


    public class ReportBuilder
    {


        private ReportSetup _reportSetup;

        public List<string> Logs = new List<string>();

        public bool ErrorPresent { get; internal set; }


        private void DoLog(string msg) { Logs.Add($"{DateTime.UtcNow.ToString("O")} > {msg}"); }



        /// <summary>
        /// ...
        /// </summary>
        public async Task<Report> Exec(string htmlTemplateFilename)
        {
            using (var fs = File.OpenRead(htmlTemplateFilename))
            {
                return await Exec(fs);
            }
        }





        /// <summary>
        /// ...
        /// </summary>
        public async Task<Report> Exec(Stream inputStream)
        {
            var startDT = DateTime.UtcNow;

            Logs.Clear();
            ErrorPresent = false;

            DoLog("START");

            // Read Report HTML
            var xdoc = new XmlDocument();
            var xdocLocker = new object();
            xdoc.Load(inputStream);

            // Read ReportSetup section
            ReadReportSetupFromHtml(xdoc);

            // Setup Report object
            var report = new Report() { Images = new ConcurrentDictionary<string, byte[]>() };
            report.EmailSubject = _reportSetup.Title;
            report.EmailTOList = _reportSetup.EmailTOList;


            // Process nodes using palellel calls to improve performance
            //   Note: Parallel.ForEach<XmlElement>(nodeList, new ParallelOptions() { }, async (node) => ...
            //         does not work as expected. As far as I have understood, I cannot create an "async" Parallel.For

            var nodeList = xdoc.SelectNodes("//AppInsightData").Cast<XmlElement>();

            Func<XmlElement, Task> ProcessNode = async (XmlElement node) =>
            {
                DoLog("Process element");

                string htmlFragment = "[UNDEF]";

                try
                {
                    var xsAppInsightData = new XmlSerializer(typeof(AppInsightData));
                    var aid = (AppInsightData)xsAppInsightData.Deserialize(new StringReader(node.OuterXml));
                    aid.CleanUp();

                    // setup Application Insights client
                    string apiKey = _reportSetup.AppInsightsConnections[aid.ConnectionID].ApiKey;
                    string appID = _reportSetup.AppInsightsConnections[aid.ConnectionID].AppID;
                    var appInsightsClient = new ApplicationInsightsDataClient(new ApiKeyClientCredentials(apiKey));

                    // Get data from Application Insights API                
                    var queryResult = await appInsightsClient.Query.ExecuteAsync(appID, aid.Query);
                    var tableResult = queryResult.Tables[0];

                    // Add a fake Now="0" at the end if values are missing at the end of the sequence
                    if (aid.BackwardCheckPeriods.HasValue &&
                        aid.BackwardCheckPeriods.Value > 0 &&
                        tableResult.Rows.Count > aid.BackwardCheckPeriods.Value)
                    {
                        if ((DateTime.UtcNow).Subtract((DateTime)tableResult.Rows[^1][0]) >
                            ((DateTime)tableResult.Rows[^1][0]).Subtract((DateTime)tableResult.Rows[^(aid.BackwardCheckPeriods.Value + 1)][0]))
                        {
                            tableResult.Rows.Add(new List<object>() { DateTime.UtcNow, 0.0 });
                        }
                    }

                    // Build Html/Images                   
                    if (tableResult.Rows.Count == 0)
                    {
                        htmlFragment = "[Error: missing data]";
                    }
                    else if (aid.OutMode == "TR")
                    {
                        string[,] matrix = AppInsightTableToMatrix(tableResult, aid.ToStrList);
                        var columnsNames = tableResult.Columns.Select(x => x.Name).ToArray();
                        htmlFragment = Matrix2Html(matrix, columnsNames, aid.AlignList, aid.THeadStyle);
                    }
                    else if (aid.OutMode == "IMG" || aid.OutMode == "IMG-EMB")
                    {
                        byte[] imgBody = CreateImgChart(
                            aid.ImgWidth, aid.ImgHeight, aid.ImgColor, aid.ImgColorShadow,
                            tableResult.Rows.Select(row => (DateTime)row[0]).ToArray(),
                            tableResult.Rows.Select(row => Double.Parse(row[1].ToString(), CultureInfo.InvariantCulture)).ToArray());

                        if (String.IsNullOrEmpty(aid.ImgFileName))
                            aid.ImgFileName = $"img_{Guid.NewGuid().ToString()}.png";

                        report.Images[aid.ImgFileName] = imgBody;

                        htmlFragment = aid.OutMode == "IMG" ?
                                        $"<img src=\"{aid.ImgFileName}\" />" :
                                        $"<img src=\"data:image/png;base64,{Convert.ToBase64String(imgBody)}\" />";
                    }
                    else
                    {
                        throw new ApplicationException("Unknown OutMode");
                    }
                }
                catch (Exception ex)
                {
                    DoLog($"ERROR: {ex.ToString()}");
                    htmlFragment = $"ERROR: {ex.Message}";
                    ErrorPresent = true;
                }

                // Replace <AppInsightData> tag with generated HTML
                lock (xdocLocker)
                {
                    XmlDocumentFragment docFrag = xdoc.CreateDocumentFragment();
                    docFrag.InnerXml = htmlFragment;
                    node.ParentNode.ReplaceChild(docFrag, node);
                }
            };

            await Task.WhenAll(nodeList.Select(n => ProcessNode(n)));

            ReplaceTag(xdoc, "DateUtcNow", DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"));
            ReplaceTag(xdoc, "ElapsedTime", DateTime.UtcNow.Subtract(startDT).TotalSeconds.ToString("0.00"));

            report.HTML = xdoc.OuterXml;

            DoLog("END");

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
                                             string colorRGB,
                                             string shadowColorRGB,
                                             DateTime[] timeX,
                                             double[] valuesY)
        {
            //int numOfItems = values.Length;
            //double[] dataX = Enumerable.Range(0, values.Length).Select(n => (double)n).ToArray();
            //double[] dataX = Enumerable.Range(-numOfItems, numOfItems).Select(n => (double)n).ToArray();

            double[] dataX = timeX.Select(x => x.ToOADate()).ToArray();

            // Plot area
            var plot = new ScottPlot.Plot(width, height);

            // Chart "shade"
            if (!String.IsNullOrEmpty(shadowColorRGB))
            {
                var fillChart = plot.AddFill(dataX, valuesY, color: ColorTranslator.FromHtml(shadowColorRGB));
                fillChart.LineWidth = 0;
            }

            // Draw gray areas for missing values
            // (I use the double of the diff to avoid some "border" cases).
            for (int i = 1; i < timeX.Length - 1; i++)
            {
                var diff1 = timeX[i].Subtract(timeX[i - 1]);
                var diff2 = timeX[i + 1].Subtract(timeX[i]);
                if (diff2 > 2 * diff1)
                {
                    var x = plot.AddHorizontalSpan(
                                    timeX[i].ToOADate(), timeX[i + 1].ToOADate(),
                                    color: System.Drawing.Color.LightGray);
                }
            }

            // Main chart
            plot.AddScatter(dataX, valuesY, color: ColorTranslator.FromHtml(colorRGB));

            // Max line
            var maxLine = plot.AddHorizontalLine(valuesY.Max());
            maxLine.Color = System.Drawing.Color.DarkRed;
            maxLine.LineWidth = 1;
            maxLine.PositionLabel = true;
            maxLine.PositionLabelBackground = maxLine.Color;

            // Set axis limits and format
            plot.SetAxisLimitsY(0, valuesY.Max() * 1.1);
            plot.XAxis.DateTimeFormat(true);
            //plot.XAxis.ManualTickSpacing(1, ScottPlot.Ticks.DateTimeUnit.Day);
            //plot.XAxis.TickLabelStyle(rotation: 90);
            //plot.XAxis.Ticks(false);

            return plot.GetImageBytes();
        }


        private void ReadReportSetupFromHtml(XmlDocument xdoc)
        {
            DoLog("Read ReportSetup tag");

            XmlElement reportSetupXE = xdoc.SelectSingleNode("/html/head/ReportSetup") as XmlElement;
            if (reportSetupXE is null)
                throw new ApplicationException("Cannot fint ReportSetup xml tag");

            _reportSetup = new ReportSetup();
            _reportSetup.Title = reportSetupXE["Report"].GetAttribute("Title");
            _reportSetup.EmailTOList = reportSetupXE["Email"].GetAttribute("ToList").Split("#").ToList();
            _reportSetup.AppInsightsConnections = new Dictionary<string, AppInsightsConfig>();

            foreach (var connectionItem in reportSetupXE.SelectNodes("AppInsightsConnections/Connection").Cast<XmlElement>())
            {
                _reportSetup.AppInsightsConnections.Add(
                    connectionItem.GetAttribute("ID"),
                    new AppInsightsConfig()
                    {
                        AppID = connectionItem.GetAttribute("AppInsightID"),
                        ApiKey = connectionItem.GetAttribute("AppInsightApiKey")
                    });
            }

            // remove "ReportSetup" section from HTML
            reportSetupXE.ParentNode.RemoveChild(reportSetupXE);
        }




        // ------------------------------------------------------------------------------------------------
        // ------------------------------------------------------------------------------------------------
        // ------------------------------------------------------------------------------------------------
        // ------------------------------------------------------------------------------------------------
        // ------------------------------------------------------------------------------------------------
        // ------------------------------------------------------------------------------------------------



        public class Report
        {
            public string HTML { get; set; }
            public ConcurrentDictionary<string, byte[]> Images { get; set; }
            public List<string> EmailTOList { get; set; }
            public string EmailSubject { get; set; }


            public MailMessage ConvertToEmail(string fromEmail)
            {
                // Prepare images bodies
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

                // Build email message
                var alternateView = AlternateView.CreateAlternateViewFromString(xdoc.OuterXml, null, MediaTypeNames.Text.Html);
                linkedResources.ForEach(alternateView.LinkedResources.Add);
                var emailMsg = new MailMessage()
                {
                    From = new MailAddress(fromEmail),
                    Subject = EmailSubject.Replace("#DT#", DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss")),
                    IsBodyHtml = true,
                    AlternateViews = { alternateView },
                };
                EmailTOList.ForEach(emailMsg.To.Add);

                return emailMsg;
            }

        }

        public class AppInsightsConfig
        {
            public string AppID { get; set; }
            public string ApiKey { get; set; }

        }


        public class ReportSetup
        {
            public string Title { get; set; }
            public List<string> EmailTOList { get; set; }
            public Dictionary<string, AppInsightsConfig> AppInsightsConnections { get; set; }
        }



        public class AppInsightData
        {
            public string ConnectionID { get; set; }
            public string Query { get; set; }
            public string ToStr { get; set; }
            public string Align { get; set; }
            public string OutMode { get; set; }
            public string THeadStyle { get; set; }
            public string ImgFileName { get; set; }
            public int ImgWidth { get; set; }
            public int ImgHeight { get; set; }
            public string ImgColor { get; set; }
            public string ImgColorShadow { get; set; }
            public int? BackwardCheckPeriods { get; set; }

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
