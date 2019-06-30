using Microcharts;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;


namespace QDAzureBilling_Call
{

    public class ImgHistogram
    {

        public static byte[] CreateImgChart(int width, int height, IEnumerable<double> valuesA, string colorA, IEnumerable<double> valuesB = null, string colorB = null)
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

    }

}
