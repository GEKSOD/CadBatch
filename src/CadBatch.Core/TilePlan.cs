using System;
using System.Collections.Generic;

namespace CadBatch.Core
{
    public sealed class TilePlan
    {
        public double SheetWidthMm {get;private set;}
        public double SheetHeightMm {get;private set;}
        public double MarginMm {get;private set;}
        public double OverlapMm {get;private set;}
        public double ContentWidthMm {get;private set;}
        public double ContentHeightMm {get;private set;}
        public double Scale {get;private set;}
        public int Columns {get;private set;}
        public int Rows {get;private set;}
        public int Count=>Columns*Rows;
        public double UsableWidthMm=>SheetWidthMm-2*MarginMm;
        public double UsableHeightMm=>SheetHeightMm-2*MarginMm;
        public double X(int column)=>column*(UsableWidthMm-OverlapMm);
        public double Y(int row)=>row*(UsableHeightMm-OverlapMm);
        public static TilePlan Create(double sourceWidthMm,double sourceHeightMm,double sheetWidthMm,double sheetHeightMm,double marginMm,double overlapMm,double scale=1)
        {
            if(!Numeric.Positive(sourceWidthMm)||!Numeric.Positive(sourceHeightMm)||!Numeric.Positive(sheetWidthMm)||!Numeric.Positive(sheetHeightMm)||
                !Numeric.NonNegative(marginMm)||!Numeric.NonNegative(overlapMm)||!Numeric.Positive(scale))throw new ArgumentException("Некорректные размеры разбивки.");
            double width=sheetWidthMm-2*marginMm,height=sheetHeightMm-2*marginMm;
            if(width<=0||height<=0||overlapMm>=width||overlapMm>=height)throw new ArgumentException("Поля или перекрытие больше полезной области листа.");
            double columns=Math.Max(1,Math.Ceiling((sourceWidthMm*scale-overlapMm)/(width-overlapMm)-1e-9));
            double rows=Math.Max(1,Math.Ceiling((sourceHeightMm*scale-overlapMm)/(height-overlapMm)-1e-9));
            if(!Numeric.Positive(columns)||!Numeric.Positive(rows)||columns*rows>1000)throw new ArgumentException("Разбивка превышает 1000 страниц. Проверьте масштаб и бумагу.");
            return new TilePlan{SheetWidthMm=sheetWidthMm,SheetHeightMm=sheetHeightMm,MarginMm=marginMm,OverlapMm=overlapMm,ContentWidthMm=sourceWidthMm*scale,ContentHeightMm=sourceHeightMm*scale,Scale=scale,Columns=(int)columns,Rows=(int)rows};
        }
    }
}
