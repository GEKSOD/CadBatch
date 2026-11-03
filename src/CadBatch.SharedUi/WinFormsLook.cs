using System.Drawing;
using System.Windows.Forms;

namespace CadBatch.UI
{
    // Table editors that remain WinForms get the same palette, type and button hierarchy as the WPF shell.
    public static class WinFormsLook
    {
        private static readonly Color Ink=Color.FromArgb(29,29,31),Secondary=Color.FromArgb(110,110,115),Line=Color.FromArgb(229,229,234),Canvas=Color.FromArgb(245,245,247),Accent=Color.FromArgb(0,113,227),Soft=Color.FromArgb(232,242,253),Fill=Color.FromArgb(232,232,237);
        public static void Apply(Form form)
        {
            form.Font=new Font("Segoe UI",10f);form.BackColor=Canvas;form.ForeColor=Ink;form.ShowIcon=false;form.MinimizeBox=false;
            Style(form.Controls);
        }
        public static void Primary(Button button){Flat(button);button.BackColor=Accent;button.ForeColor=Color.White;button.FlatAppearance.MouseOverBackColor=Color.FromArgb(0,102,204);button.FlatAppearance.MouseDownBackColor=Color.FromArgb(0,92,184);button.Font=new Font("Segoe UI Semibold",10f);}
        private static void Style(Control.ControlCollection controls)
        {
            foreach(Control control in controls)
            {
                if(control is Button button&&button.BackColor!=Accent)Flat(button);
                else if(control is DataGridView grid)Grid(grid);
                else if(control is Label label)label.ForeColor=Secondary;
                else if(control is FlowLayoutPanel||control is Panel||control is TableLayoutPanel)control.BackColor=Canvas;
                Style(control.Controls);
            }
        }
        private static void Flat(Button button)
        {
            button.FlatStyle=FlatStyle.Flat;button.FlatAppearance.BorderSize=0;button.BackColor=Fill;button.ForeColor=Ink;button.FlatAppearance.MouseOverBackColor=Color.FromArgb(220,220,226);button.FlatAppearance.MouseDownBackColor=Color.FromArgb(209,209,214);
            button.Padding=new Padding(10,4,10,4);button.Margin=new Padding(4);button.Cursor=Cursors.Hand;button.MinimumSize=new Size(0,34);
        }
        public static void Grid(DataGridView grid)
        {
            grid.BackgroundColor=Color.White;grid.BorderStyle=BorderStyle.None;grid.GridColor=Line;grid.CellBorderStyle=DataGridViewCellBorderStyle.SingleHorizontal;grid.RowHeadersVisible=false;grid.EnableHeadersVisualStyles=false;
            grid.ColumnHeadersBorderStyle=DataGridViewHeaderBorderStyle.None;grid.ColumnHeadersHeight=38;grid.ColumnHeadersHeightSizeMode=DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
            grid.ColumnHeadersDefaultCellStyle.BackColor=Color.FromArgb(250,250,252);grid.ColumnHeadersDefaultCellStyle.ForeColor=Secondary;grid.ColumnHeadersDefaultCellStyle.Font=new Font("Segoe UI Semibold",9f);grid.ColumnHeadersDefaultCellStyle.Padding=new Padding(6,0,6,0);
            grid.DefaultCellStyle.SelectionBackColor=Soft;grid.DefaultCellStyle.SelectionForeColor=Ink;grid.DefaultCellStyle.ForeColor=Ink;grid.DefaultCellStyle.Padding=new Padding(6,0,6,0);grid.RowTemplate.Height=34;
        }
    }
}
