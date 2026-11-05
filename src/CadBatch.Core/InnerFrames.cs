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
        // The sheet around an inner frame at the given scale (drawing units per paper millimetre). It is built as an exact
        // rectangle from the bottom side of the frame, its direction and its size: an inner frame drawn slightly out of
        // square (within the search tolerance) must not stop the search. Null when no sheet can be built.
        public static Rectangle2? Expand(Rectangle2 inner,double scale)
        {
            if(!Numeric.Positive(scale)||!Numeric.Positive(inner.Width)||!Numeric.Positive(inner.Height))return null;
            var c=inner.Corners;var u=(c[1]-c[0])*(1/inner.Width);var v=new Point2(-u.Y,u.X);
            double width=inner.Width+(LeftMm+MarginMm)*scale,height=inner.Height+2*MarginMm*scale;
            var start=c[0]-u*(LeftMm*scale)-v*(MarginMm*scale);
            var points=new[]{start,start+u*width,start+u*width+v*height,start+v*height};
            return Rectangle2.TryCreate(points,Math.Max(1e-6,Math.Max(width,height)*1e-9),out var sheet)?sheet:null;
        }
    }
}
