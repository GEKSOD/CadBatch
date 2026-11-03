using System;
using System.Globalization;
using System.Linq;

namespace CadBatch.Core
{
    /// <summary>Rejects device, paper-size or orientation substitutions after native media matching.</summary>
    public static class ValidatedPaper
    {
        public static void EnsureUnchanged(string requestedDevice,double requestedWidth,double requestedHeight,
            string actualDevice,double actualRawWidth,double actualRawHeight,bool quarterTurn)
        {
            if(!Numeric.Positive(requestedWidth)||!Numeric.Positive(requestedHeight) ||
                !Numeric.Positive(actualRawWidth)||!Numeric.Positive(actualRawHeight))
                throw new ArgumentException("AutoCAD вернул некорректный размер бумаги.");
            if(string.IsNullOrWhiteSpace(requestedDevice)||string.IsNullOrWhiteSpace(actualDevice) ||
                !string.Equals(DeviceName(requestedDevice),DeviceName(actualDevice),StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("AutoCAD заменил устройство печати: «"+requestedDevice+"» → «"+actualDevice+"». Вывод остановлен.");
            double width=quarterTurn?actualRawHeight:actualRawWidth;
            double height=quarterTurn?actualRawWidth:actualRawHeight;
            const double toleranceMm=0.5;
            if(Math.Abs(width-requestedWidth)>toleranceMm || Math.Abs(height-requestedHeight)>toleranceMm)
                throw new InvalidOperationException(string.Format(CultureInfo.CurrentCulture,
                    "AutoCAD заменил размер или ориентацию бумаги: запрошено {0:0.###} × {1:0.###} мм, получено {2:0.###} × {3:0.###} мм. Вывод остановлен.",
                    requestedWidth,requestedHeight,width,height));
        }
        private static string DeviceName(string value)=>value.Replace('\\','/').Split('/').Last();
    }
}
