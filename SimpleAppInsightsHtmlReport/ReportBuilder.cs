using Microcharts;
using Microsoft.Azure.ApplicationInsights;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Serialization;

namespace SimpleAppInsightsHtmlReport
{

    // !!! Single CS file with all required classes !!!


    public class ReportBuilder
    {

        // ---------------------------------------------------------
        public class Report
        {
            public string HTML { get; set; }
            public Dictionary<string, byte[]> Images { get; set; }
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
        // ---------------------------------------------------------



        /// <summary>
        /// ...
        /// </summary>
        public Report Exec(string htmlTemplateFilename, AppInsightsConfig appInsCfg)
        {
            var report = new Report() { Images = new Dictionary<string, byte[]>() };

            var xdoc = new XmlDocument();
            xdoc.Load(htmlTemplateFilename);

            if (appInsCfg == null)
            {
                var appInsightsConfigNode = xdoc.SelectSingleNode("//AppInsightsConfig");
                if (appInsightsConfigNode != null)
                {
                    appInsCfg = (AppInsightsConfig)new XmlSerializer(typeof(AppInsightsConfig))
                                .Deserialize(new StringReader(appInsightsConfigNode.OuterXml));
                    appInsightsConfigNode.ParentNode.RemoveChild(appInsightsConfigNode);
                }
            }

            // setup Application Insights client
            var apikeyAuth = new ApiKeyClientCredentials(appInsCfg.ApiKey);
            var appInsightsClient = new ApplicationInsightsDataClient(apikeyAuth);
            appInsightsClient.AppId = appInsCfg.AppID;

            // Process nodes
            var nodeList = xdoc.SelectNodes("//AppInsightData");

            foreach (XmlElement node in nodeList)
            {
                var xsAppInsightData = new XmlSerializer(typeof(AppInsightData));
                var aid = (AppInsightData)xsAppInsightData.Deserialize(new StringReader(node.OuterXml));
                aid.CleanUp();

                // Get data from Application Insights API
                var queryResult = appInsightsClient.Query(aid.Query);
                var tableResult = queryResult.Tables[0];
                string[,] matrix = AppInsightTableToMatrix(tableResult, aid.ToStrList);

                string htmlFragment;

                // Build Html/Images
                if (aid.OutMode == "TR")
                {
                    var columnsNames = tableResult.Columns.Select(x => x.Name).ToArray();
                    htmlFragment = Matrix2Html(matrix, columnsNames, aid.AlignList, aid.THeadStyle);
                }
                else if (aid.OutMode == "IMG")   // CID  ???
                {
                    byte[] imgBody = CreateImgChart(
                        aid.ImgWidth, aid.ImgHeight, aid.ImgColor, true,
                        tableResult.Rows.Select(row => Double.Parse(row[1], CultureInfo.InvariantCulture)).ToArray());

                    report.Images.Add(aid.ImgFileName, imgBody);
                    htmlFragment = $"<img src=\"{aid.ImgFileName}\" />";
                }
                else
                {
                    throw new ApplicationException("Unknown OutMode");
                }

                XmlDocumentFragment docFrag = xdoc.CreateDocumentFragment();
                docFrag.InnerXml = htmlFragment;
                var parentNode = node.ParentNode;
                parentNode.ReplaceChild(docFrag, node);
            }


            ReplaceTag(xdoc, "DateUtcNow", DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"));

            report.HTML = xdoc.OuterXml;

            return report;
        }



        /// <summary>
        /// ...
        /// </summary>
        private string[,] AppInsightTableToMatrix(Microsoft.Azure.ApplicationInsights.Models.Table table,
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
                    string fieldValue = table.Rows[r][c];
                    string toStringRule = toStringRules[c];

                    string body;

                    switch (table.Columns[c].Type)
                    {
                        case "string":
                            body = fieldValue;
                            break;
                        case "datetime":
                            var dt = DateTime.Parse(fieldValue, CultureInfo.InvariantCulture);
                            body = String.IsNullOrEmpty(toStringRule) ? dt.ToString() : dt.ToString(toStringRule);
                            break;
                        case "long":
                            body = Int64.Parse(fieldValue, CultureInfo.InvariantCulture).ToString();
                            break;
                        case "real":
                            var x = Double.Parse(fieldValue, CultureInfo.InvariantCulture);
                            body = String.IsNullOrEmpty(toStringRule) ? x.ToString() : x.ToString(toStringRule);
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
        /// Simple chart using Microcharts library (based on SkiaSharp)
        /// </summary>

        public static byte[] CreateImgChart(int width,
                                            int height,
                                            string colorStr,
                                            bool showValueLabels,
                                            double[] values)
        {
            var color = SKColor.Parse(colorStr);

            var entries = new List<Microcharts.Entry>();
            for (int i = 0; i < values.Length; i++)
            {
                var entry = new Microcharts.Entry((float)values[i]);
                entry.Color = color;
                entries.Add(entry);
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

            // My "max"
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
            var imageData = image.Encode(SKEncodedImageFormat.Png, 100);
            return imageData.ToArray();
        }
    }

}
