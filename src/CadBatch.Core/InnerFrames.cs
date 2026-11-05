using System;
using System.Collections.Generic;

namespace CadBatch.Core
{
    // Many drawings have only the inner frame of ГОСТ Р 21.101 (ЕСКД ГОСТ 2.301): the sheet edge is not drawn, or is drawn
    // in pieces shared with the neighbouring sheet. The sheet is the inner frame plus 20 mm on the binding (left) side and
    // 5 mm on the others; the left side is the one along the bottom edge's start, as the title block sits bottom right.
    public static class InnerFrames
    {
        public const double LeftMm=20,MarginMm=5;
        public static (FormatDefinition? Format,double Scale) Match(Rectangle2 inner,IEnumerable<double> scales,IReadOnlyList<FormatDefinition> formats,double toleranceMm)
        {
            double best=double.MaxValue;FormatDefinition? match=null;double chosen=0;
            foreach(double s in scales)
            {
                double a=inner.Width/s+LeftMm+MarginMm,b=inner.Height/s+2*MarginMm;
                foreach(var f in formats)
                    foreach(bool swap in new[]{false,true})
                    {
                        double dw=Math.Abs(a-(swap?f.HeightMm:f.WidthMm)),dh=Math.Abs(b-(swap?f.WidthMm:f.HeightMm));
                        double error=dw/f.WidthMm+dh/f.HeightMm;
                        if(dw<=toleranceMm&&dh<=toleranceMm&&error<best){best=error;match=f;chosen=s;}
                    }
            }
            return (match,chosen);
        }
        // The sheet around an inner frame at the given scale (drawing units per paper millimetre).
        public static Rectangle2 Expand(Rectangle2 inner,double scale)
        {
            var c=inner.Corners;var u=(c[1]-c[0])*(1/inner.Width);var v=new Point2(-u.Y,u.X);
            double left=LeftMm*scale,margin=MarginMm*scale;
            var points=new[]{c[0]-u*left-v*margin,c[1]+u*margin-v*margin,c[2]+u*margin+v*margin,c[3]-u*left+v*margin};
            if(!Rectangle2.TryCreate(points,Math.Max(1e-9,inner.Width*1e-9),out var sheet))throw new InvalidOperationException("Не удалось построить лист по внутренней рамке.");
            return sheet!;
        }
    }
}
