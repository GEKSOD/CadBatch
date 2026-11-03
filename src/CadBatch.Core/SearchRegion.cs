using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;

namespace CadBatch.Core
{
    [DataContract] public sealed class SearchRegion
    {
        [DataMember] public string DocumentPath {get;set;}="";
        [DataMember] public string Layout {get;set;}="";
        [DataMember] public double[][] Corners {get;set;}=Array.Empty<double[]>();
        public List<Point2> Points=>Corners.Select(p=>new Point2(p[0],p[1])).ToList();
        public void Validate(){if(Corners==null||Corners.Length<3||Corners.Any(p=>p==null||p.Length!=2||!new Point2(p[0],p[1]).IsFinite))throw new ArgumentException("Некорректная область поиска.");}
    }
    public static class PolygonVisibility
    {
        private static bool OnEdge(Point2 p,Point2 a,Point2 b,double tolerance=.00001){var d=b-a;return d.Length>0&&Math.Abs(Point2.Cross(p-a,d))<=tolerance*d.Length&&Point2.Dot(p-a,p-b)<=tolerance*tolerance;}
        public static bool Contains(IReadOnlyList<Point2> polygon,Point2 point)
        {
            bool inside=false;for(int i=0,j=polygon.Count-1;i<polygon.Count;j=i++)
            {
                var a=polygon[j];var b=polygon[i];if(OnEdge(point,a,b))return true;
                if((a.Y>point.Y)!=(b.Y>point.Y)&&point.X<(b.X-a.X)*(point.Y-a.Y)/(b.Y-a.Y)+a.X)inside=!inside;
            }return inside;
        }
        private static bool Crosses(Point2 a,Point2 b,Point2 c,Point2 d)
        {
            double t1=Point2.Cross(b-a,c-a),t2=Point2.Cross(b-a,d-a),u1=Point2.Cross(d-c,a-c),u2=Point2.Cross(d-c,b-c);
            return t1*t2< -1e-12&&u1*u2< -1e-12;
        }
        public static bool FullyVisible(IReadOnlyList<Point2> frame,IReadOnlyList<Point2> boundary,bool inverted=false)
        {
            if(frame.Count<3||boundary.Count<3)return false;
            if(inverted?frame.Any(p=>Contains(boundary,p)):frame.Any(p=>!Contains(boundary,p)))return false;
            for(int i=0;i<frame.Count;i++)for(int j=0;j<boundary.Count;j++)if(Crosses(frame[i],frame[(i+1)%frame.Count],boundary[j],boundary[(j+1)%boundary.Count]))return false;
            if(!inverted&&boundary.Any(p=>Contains(frame,p)&&!Enumerable.Range(0,frame.Count).Any(i=>OnEdge(p,frame[i],frame[(i+1)%frame.Count]))))return false;
            return !inverted||!boundary.Any(p=>Contains(frame,p));
        }
    }
}
