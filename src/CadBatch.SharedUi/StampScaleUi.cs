using System;
using System.Globalization;
using System.Threading.Tasks;
using CadBatch.Core;

namespace CadBatch.UI
{
    public sealed partial class BatchWindow
    {
        private void RefreshStampHint()=>stampHint.Text="Верхняя линия: "+(profile.Search.Stamp?.ReferenceWidthMm??185).ToString(CultureInfo.CurrentCulture)+" мм • параметры в меню «Поиск»";
        private void MarkScaleDirty()
        {
            if(frames.Count==0)return;scaleNeedsSearch=true;status.Text="Параметры масштаба изменены. Нажмите «Найти рамки» перед выводом.";
        }
        private void RequireFreshScale()
        {
            if(scaleNeedsSearch)throw new InvalidOperationException("Параметры масштаба изменены. Нажмите «Найти рамки», чтобы пересчитать форматы и масштаб.");
        }
        private void EditStampScale()
        {
            var current=profile.Search.Stamp??new StampScaleSettings();
            var values=AskValues("Геометрия штампа для масштаба",("Длина всей верхней линии на бумаге, мм",current.ReferenceWidthMm.ToString(CultureInfo.CurrentCulture)),
                ("Высота штампа, мм",current.HeightMm.ToString(CultureInfo.CurrentCulture)),("Отступ от правого края бумаги, мм",current.RightMarginMm.ToString(CultureInfo.CurrentCulture)),
                ("Отступ от нижнего края бумаги, мм",current.BottomMarginMm.ToString(CultureInfo.CurrentCulture)),("Допуск геометрии штампа, мм бумаги",current.ToleranceMm.ToString(CultureInfo.CurrentCulture)));
            if(values==null)return;var settings=current.Copy();settings.ReferenceWidthMm=Number(values[0]);settings.HeightMm=Number(values[1]);settings.RightMarginMm=Number(values[2]);settings.BottomMarginMm=Number(values[3]);settings.ToleranceMm=Number(values[4]);
            // A stamp is about 185 mm wide on paper; 18500 is that width in drawing units at 1:100.
            double factor=StampScaleSettings.UnitsFactor(settings.ReferenceWidthMm);
            if(factor>1&&ModernDialog.Confirm(DialogHandle,"Размеры штампа в миллиметрах бумаги","Ширина штампа "+settings.ReferenceWidthMm.ToString(CultureInfo.CurrentCulture)+" похожа на размер в единицах чертежа. На бумаге штамп по ГОСТ шириной 185 мм.\n\nРазделить ширину и высоту на "+factor.ToString(CultureInfo.CurrentCulture)+"? Получится "+(settings.ReferenceWidthMm/factor).ToString("0.##",CultureInfo.CurrentCulture)+" × "+(settings.HeightMm/factor).ToString("0.##",CultureInfo.CurrentCulture)+" мм.","Разделить",DialogKind.Question,"Оставить как есть"))
            {settings.ReferenceWidthMm/=factor;settings.HeightMm/=factor;}
            settings.Validate();
            var modes=AskChecks("Режим масштаба",new[]{("Включить определение по штампу",stampScale.Checked),("Только подтверждённый штамп; без запасного списка масштабов",settings.OnlyStamp)});if(modes==null)return;
            settings.Enabled=modes[0];settings.OnlyStamp=modes[1];profile.Search.Stamp=settings;stampScale.Checked=settings.Enabled;RefreshStampHint();MarkScaleDirty();
        }
        private async Task MeasureStampAsync(bool twoPoints)
        {
            var settings=profile.Search.Stamp??new StampScaleSettings();settings.Validate();
            var measured=await host.MeasureStampScaleAsync(settings.ReferenceWidthMm,twoPoints);if(measured==null){status.Text="Определение масштаба отменено.";return;}
            string scale=measured.Scale.ToString("G12",CultureInfo.CurrentCulture);
            string plan="Источник: "+measured.Source+"\r\nИзмеренная длина: "+measured.Length.ToString("G12",CultureInfo.CurrentCulture)+" единиц DWG\r\nБумажная длина: "+settings.ReferenceWidthMm+" мм\r\n\r\nМасштаб геометрии = "+measured.Length.ToString("G12",CultureInfo.CurrentCulture)+" / "+settings.ReferenceWidthMm+" = "+scale+
                "\r\n\r\nПроверьте, что указана вся верхняя линия штампа, а не короткий участок ячейки.\r\nЗначение будет записано в поле «Масштабы». После этого выполните поиск рамок.";
            if(!ShowText("Масштаб по верхней линии штампа",plan,"Использовать масштаб"))return;
            scales.Text=scale;stampScale.Checked=false;profile.Search.Scales=new System.Collections.Generic.List<double>{measured.Scale};if(profile.Search.Stamp!=null)profile.Search.Stamp.Enabled=false;
            MarkScaleDirty();status.Text="По линии штампа получен масштаб "+scale+". Нажмите «Найти рамки».";
        }
    }
}
