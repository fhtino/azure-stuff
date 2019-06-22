using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace QDAzureBilling_Call
{

    /*
    <table style="border-collapse: collapse; background-color:lightgray; border:5px solid lightgray; ">
        <tr>
            <td style="vertical-align: bottom;                            "><div style="width:5px; height:150px; background-color:red;   ">&nbsp;</div></td>
            <td style="vertical-align: bottom;                            "><div style="width:5px; height:160px; background-color:red;   ">&nbsp;</div></td>
            <td style="vertical-align: bottom;                            "><div style="width:5px; height:80px;  background-color:red;   ">&nbsp;</div></td>
            <td style="vertical-align: bottom;                            "><div style="width:5px; height:100px; background-color:green; ">&nbsp;</div></td>
            <td style="vertical-align: bottom; background-color:lightblue;"><div style="width:5px; height:110px; background-color:green; ">&nbsp;</div></td>
            <td style="vertical-align: bottom; background-color:lightblue;"><div style="width:5px; height:80px;  background-color:green; ">&nbsp;</div></td>
            <td style="vertical-align: bottom; background-color:lightblue;"><div style="width:5px; height:80px;  background-color:green; ">&nbsp;</div></td>
            <td style="vertical-align: bottom; background-color:lightblue;"><div style="width:5px; height:80px;  background-color:green; ">&nbsp;</div></td>
            <td style="vertical-align: bottom; background-color:lightblue;"><div style="width:5px; height:60px;  background-color:green; ">&nbsp;</div></td>
            <td style="vertical-align: bottom; background-color:lightblue;"><div style="width:5px; height:70px;  background-color:green; ">&nbsp;</div></td>            
            <td>
                <table style="border-collapse:collapse; margin-left:10px; height:160px;">
                    <tr><td style="vertical-align:top;">160</td></tr>
                    <tr><td style="vertical-align:bottom;">0</td></tr>
                </table>
            </td>
        </tr>
    </table>
    */


    public class HtmlHistogram
    {

        public class BarData
        {
            public int Value { get; set; }
            public int Width { get; set; }
            public string BarColor { get; set; }
            public string BackgroundColor { get; set; }
        }


        public string TableBackgroundColor { get; set; }
        public int? TableBorderSize { get; set; }
        public string TableBorderColor { get; set; }


        public string Build(IEnumerable<BarData> data, string maxRealValue)
        {
            var sb = new StringBuilder();

            sb.Append("<table style='border-collapse:collapse; ");
            if (TableBackgroundColor != null) sb.Append("background-color:" + TableBackgroundColor + ";");
            if (TableBorderSize.HasValue) sb.Append("border:" + TableBorderSize.Value + "px solid " + TableBorderColor + ";");
            sb.AppendLine("'>");

            sb.AppendLine("<tr>");

            foreach (var item in data)
            {
                sb.Append("<td style='vertical-align:bottom; ");
                if (item.BackgroundColor != null) sb.AppendLine("background-color:" + item.BackgroundColor + ";");
                sb.Append("'>");

                sb.Append("<div style='");
                sb.Append("width:" + item.Width + "px; height:" + item.Value + "px; ");
                if (item.BarColor != null) sb.Append("background-color:" + item.BarColor + ";");
                sb.Append("'>&nbsp;</div>");

                sb.Append("</td>");
                sb.AppendLine();
            }

            int max = data.Max(x => x.Value);
            sb.AppendLine("<td>");
            sb.AppendLine("<table style='border-collapse:collapse; margin-left:10px; height:" + max + "px;'>");
            sb.AppendLine("<tr><td style='vertical-align:top;'>" + maxRealValue + "</td></tr>");
            sb.AppendLine("<tr><td style='vertical-align:bottom;'>0</td></tr>");
            sb.AppendLine("</table>");
            sb.AppendLine("</td>");

            sb.AppendLine("</tr>");

            sb.AppendLine("</table>");

            return sb.ToString();
        }

    }
}
