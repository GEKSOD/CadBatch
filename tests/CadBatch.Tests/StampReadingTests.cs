using System;
using System.Collections.Generic;
using System.Linq;
using CadBatch.Core;

internal static partial class Program
{
    private static void RunStampReadingTests()
    {
        // An A3 frame at the origin, scale 1: the 185 × 55 title block starts at (230, 5). Positions are those of the
        // texts in a real drawing (0502.1-2024, ВК), in title block millimetres.
        DrawingText At(DetectedFrame f,double x,double y,string text)=>new DrawingText{DocumentKey=f.Source.DocumentKey,Layout=f.Layout,Center=new Point2(230+x,5+y),Text=text};
        DetectedFrame Frame(string handle,double dx=0)=>Detect(Candidate(handle,dx,0,420,297)).Frames.Single();
        IEnumerable<DrawingText> Main(DetectedFrame f,string number,string title,string code)=>new[]
        {
            At(f,125,50,@"{\W0.9;"+code+"}"),At(f,125,37.5,"«Капитальный ремонт основного здания»"),At(f,5,32.5,"Изм."),At(f,25,32.5,"Лист"),At(f,60,32.5,"Дата"),
            At(f,142.5,27.5,"Стадия"),At(f,157.5,27.5,"Лист"),At(f,175,27.5,"Листов"),At(f,8.7,27.4,"Разработал"),At(f,101.6,21.7,"Здание детского сада"),
            At(f,142.5,20,"Р"),At(f,157.7,19.7,number),At(f,172.7,19.7,"5"),At(f,100,7.4,@"\pxqc;{\C7;"+title+"}"),At(f,160,7.5,"ИП Онищенко А.И.")
        };
        Run("Stamp: the sheet title is the bottom cell, not the building name above it",()=>
        {
            var f=Frame("S1");StampMetadata.Apply(new[]{f},Main(f,"1","Общие данные","0502.1-2024-ВК"),new StampMetadataSettings());
            Assert(f.Title=="Общие данные"&&f.Metadata["Object"]=="Здание детского сада"&&f.Code=="0502.1-2024-ВК");
        });
        Run("Stamp: a sheet number on the cell border is read, captions are not",()=>
        {
            var f=Frame("S2");StampMetadata.Apply(new[]{f},Main(f,"1","Общие данные","АР"),new StampMetadataSettings());
            Assert(f.SheetNumber=="1"&&f.Metadata["SheetCount"]=="5"&&f.Metadata["Stage"]=="Р");
        });
        Run("Stamp: code pieces join without a space before a dot",()=>
        {
            Assert(StampMetadata.Join(new[]{"0502.1-2024-ВК",".СО"})=="0502.1-2024-ВК.СО"&&StampMetadata.Join(new[]{"План","1 этажа"})=="План 1 этажа"&&StampMetadata.Join(new[]{"АР-","01"})=="АР-01");
            Assert(StampMetadata.Clean(@"\pxqc;{\C7;Схемы системы К1}")=="Схемы системы К1"&&StampMetadata.Clean("Ø%%c100  мм")=="Ø⌀100 мм");
        });
        Run("Stamp: following sheets (form 6) give their number and the title of the first sheet",()=>
        {
            var first=Frame("F1");var next=Frame("F2",1000);
            var texts=Main(first,"1","Спецификация оборудования, изделий и материалов","0502.1-2024-ВК.СО").ToList();
            DrawingText N(double x,double y,string t)=>new DrawingText{DocumentKey=next.Source.DocumentKey,Layout=next.Layout,Center=new Point2(1230+x,5+y),Text=t};
            texts.AddRange(new[]{N(88.5,55.7,"шт."),N(109.1,55.2,"2"),N(88.6,48.9,"шт."),N(175.3,12,"Лист"),N(117.5,7.5,@"{\W0.9;0502.1-2024-ВК.СО}"),N(176,5.2,"2"),
                N(5,2.2,"Изм. "),N(13.3,2,"Кол.уч"),N(22.5,2.2,"Nдок."),N(32.2,2.1,"Лист"),N(45.2,2.2,"Подпись"),N(57.5,2.1,"Дата")});
            StampMetadata.Apply(new[]{next,first},texts,new StampMetadataSettings());
            Assert(next.SheetNumber=="2"&&next.Code=="0502.1-2024-ВК.СО"&&next.Title=="Спецификация оборудования, изделий и материалов"&&next.OriginalTitle==next.Title);
            Assert(first.SheetNumber=="1"&&first.Title=="Спецификация оборудования, изделий и материалов");
        });
        Run("Stamp: an empty title cell falls back to the building name",()=>
        {
            var f=Frame("E1");StampMetadata.Apply(new[]{f},new[]{At(f,100,22.5,"Здание лаборатории"),At(f,125,50,"1001-26-АР2")},new StampMetadataSettings());
            Assert(f.Title=="Здание лаборатории"&&f.SheetNumber=="");
        });
        Run("Stamp: old default zones are upgraded, edited zones are kept",()=>
        {
            var old=new StampMetadataSettings{Zones=new List<StampFieldZone>{new StampFieldZone("Title",65,15,70,15),new StampFieldZone("Code",65,45,120,10),new StampFieldZone("SheetNumber",150,20,15,10),new StampFieldZone("SheetCount",165,20,20,10),new StampFieldZone("Revision",0,35,10,10)}};
            Assert(old.Upgrade()&&StampMetadataSettings.SameZones(old.Zones,StampMetadataSettings.SpdsZones()));
            var mine=new StampMetadataSettings{Zones=new List<StampFieldZone>{new StampFieldZone("Title",60,10,80,20)}};Assert(!mine.Upgrade()&&mine.Zones[0].X==60);
            var profile=new ProjectProfile{Metadata=old};old.Zones=new List<StampFieldZone>{new StampFieldZone("Title",65,15,70,15),new StampFieldZone("Code",65,45,120,10),new StampFieldZone("SheetNumber",150,20,15,10),new StampFieldZone("SheetCount",165,20,20,10),new StampFieldZone("Revision",0,35,10,10)};
            profile.UpgradeCatalog();Assert(profile.Metadata!.Zones[0].Y==0);
        });
    }
}
