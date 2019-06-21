using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;



namespace QDAzureBilling_Call
{
    public class List2HtmlTable
    {
        public bool ShowHeader { get; set; } = true;
        public string TDStyle { get; set; }
        public string THStyle { get; set; }


        public string Exec<T>(IEnumerable<T> objects)
        {
            var sb = new StringBuilder();

            if (objects != null && objects.Any())
            {
                sb.AppendLine("<table style='border-collapse: collapse;'>");

                if (ShowHeader)
                    sb.Append(this.BuildColumnHeader(objects.First().GetType().GetProperties().Select(p => p.Name).ToList()));

                objects.ToList().ForEach(item => sb.Append(this.BuildRow(item)));

                sb.AppendLine("</table>");
            }

            return sb.ToString();
        }


        private string BuildColumnHeader<T>(List<T> propertiesList)
        {
            StringBuilder sb = new StringBuilder();

            if (propertiesList != null)
            {
                sb.AppendLine(" <tr>");
                propertiesList.ForEach(propValue =>
                {
                    sb.Append("  <th style='" + THStyle + "'>");
                    sb.Append(Convert.ToString(propValue));
                    sb.AppendLine("</th>");
                });
                sb.AppendLine(" </tr>");
            };

            return sb.ToString();
        }


        private string BuildRow<T>(T obj)
        {
            StringBuilder sb = new StringBuilder();

            if (obj != null)
            {
                sb.AppendLine(" <tr>");
                obj.GetType()
                    .GetProperties()
                    .ToList()
                    .ForEach(prop =>
                    {
                        sb.AppendLine(
                            "  " +
                            "<td style='" + TDStyle + "'>" +
                            Convert.ToString(prop.GetValue(obj, null)) +
                            "</td>");
                    }

            );
                sb.AppendLine(" </tr>");
            }

            return sb.ToString();
        }

    }
}
