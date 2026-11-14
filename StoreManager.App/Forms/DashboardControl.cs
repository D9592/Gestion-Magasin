using StoreManager.App.Controls;
using StoreManager.App.Models;
using StoreManager.App.Services;

namespace StoreManager.App.Forms;

internal sealed class DashboardControl : UserControl
{
    private readonly UserSession _actor;
    private readonly ReportingService _reporting;
    private readonly Action<string,string> _openSales;
    private readonly Action<string> _openStock;
    private readonly Label _date=Ui.Label("Chargement de la date métier…",10,false,Ui.Muted);
    private readonly Label _loading=Ui.Label("Chargement des indicateurs…",10,false,Ui.Muted);
    private readonly Panel _metrics=new(){Dock=DockStyle.Fill,Height=120,BackColor=Ui.Canvas};
    private readonly DashboardChart _trend=new(DashboardChartKind.Trend){Dock=DockStyle.Fill};
    private readonly DashboardChart _top=new(DashboardChartKind.TopProducts){Dock=DockStyle.Fill};
    private readonly DashboardChart _payments=new(DashboardChartKind.Payments){Dock=DockStyle.Fill};
    private readonly DataGridView _alerts=StockManagementForm.MakeGridPublic();

    public DashboardControl(UserSession actor,ReportingService reporting,Action<string,string> openSales,Action<string> openStock)
    {
        _actor=actor;_reporting=reporting;_openSales=openSales;_openStock=openStock;Dock=DockStyle.Top;Height=900;BackColor=Ui.Canvas;Font=new Font("Segoe UI",10);AutoScaleMode=AutoScaleMode.Font;
        var layout=new TableLayoutPanel{Dock=DockStyle.Top,AutoSize=true,ColumnCount=1,RowCount=7,BackColor=Ui.Canvas,Padding=new Padding(0,0,14,20)};layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,285));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,285));layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,220));layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));Controls.Add(layout);
        var heading=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=2,AutoSize=true,Margin=new Padding(0,0,0,14)};heading.Controls.Add(Ui.Label($"Bonjour {_actor.Name}",20,true),0,0);heading.Controls.Add(_date,0,1);layout.Controls.Add(heading,0,0);
        layout.Controls.Add(_metrics,0,1);
        _loading.Margin=new Padding(0,0,0,6);layout.Controls.Add(_loading,0,6);
        var chartRow1=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2,RowCount=1,Margin=new Padding(0,0,0,14)};chartRow1.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,62));chartRow1.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,38));chartRow1.Controls.Add(Card("Ventes validées · 7 derniers jours",_trend),0,0);chartRow1.Controls.Add(Card("Modes de paiement · aujourd'hui",_payments),1,0);layout.Controls.Add(chartRow1,0,2);
        layout.Controls.Add(Card("Produits les plus vendus · 7 derniers jours",_top),0,3);
        var alertHeader=new FlowLayoutPanel{Dock=DockStyle.Fill,AutoSize=true,Padding=new Padding(0,0,0,8),WrapContents=false};alertHeader.Controls.Add(Ui.Label("Alertes de stock",14,true));var all=Ui.Button("Voir les stocks");all.Click+=(_,_)=>_openStock("");alertHeader.Controls.Add(all);layout.Controls.Add(alertHeader,0,4);
        _alerts.Height=210;_alerts.Dock=DockStyle.Fill;_alerts.CellFormatting+=FormatAlert;_alerts.CellDoubleClick+=(_,_)=>OpenSelectedAlert();_alerts.DataError+=(_,e)=>e.ThrowException=false;layout.Controls.Add(_alerts,0,5);
        Load+=async(_,_)=>await LoadDashboardAsync();
    }

    private static Control Card(string title,Control content)
    {
        var panel=new TableLayoutPanel{Dock=DockStyle.Fill,BackColor=Color.White,Padding=new Padding(16),Margin=new Padding(0,0,12,0),ColumnCount=1,RowCount=2};panel.RowStyles.Add(new RowStyle(SizeType.Absolute,32));panel.RowStyles.Add(new RowStyle(SizeType.Percent,100));var heading=Ui.Label(title,11,true);heading.Dock=DockStyle.Fill;content.Dock=DockStyle.Fill;panel.Controls.Add(heading,0,0);panel.Controls.Add(content,0,1);return panel;
    }

    private async Task LoadDashboardAsync()
    {
        try
        {
            var data=await Task.Run(()=>_reporting.Dashboard(_actor));if(IsDisposed||Disposing)return;var date=DateOnly.ParseExact(data.Statistics.BusinessDate,"yyyy-MM-dd");_date.Text=$"{date.ToDateTime(TimeOnly.MinValue).ToString("dddd d MMMM yyyy",System.Globalization.CultureInfo.GetCultureInfo("fr-FR"))} · Fuseau du magasin";
            BuildMetrics(data.Statistics);_trend.SetTrend(data.SalesTrend);_top.SetTopProducts(data.TopProducts);_payments.SetPayments(data.PaymentModes);
            _alerts.DataSource=data.Alerts.ToList();SetHeader(nameof(StockAlert.Reference),"RÉFÉRENCE");SetHeader(nameof(StockAlert.Name),"PRODUIT");SetHeader(nameof(StockAlert.Quantity),"QUANTITÉ");SetHeader(nameof(StockAlert.Threshold),"SEUIL");SetHeader(nameof(StockAlert.Status),"STATUT");Hide(nameof(StockAlert.ProductId));
            _loading.Text=data.Alerts.Count==0?"Aucune alerte active. Les produits archivés sont exclus.":$"{data.Alerts.Count} produit(s) actif(s) à surveiller · statistiques de vente selon votre rôle.";
        }
        catch(Exception ex){if(!IsDisposed&&!Disposing){_loading.ForeColor=Ui.Red;_loading.Text=$"Impossible de charger le tableau de bord : {ex.Message}";}}
    }

    private void BuildMetrics(DailyStoreStatistics stats)
    {
        _metrics.Controls.Clear();var flow=new FlowLayoutPanel{Dock=DockStyle.Fill,AutoSize=true,WrapContents=true,FlowDirection=FlowDirection.LeftToRight,BackColor=Ui.Canvas};
        flow.Controls.Add(new DashboardMetric("Ventes du jour",$"{stats.SalesTotal:N0} XAF",Ui.Blue,()=>OpenSales(stats.BusinessDate)));
        flow.Controls.Add(new DashboardMetric("Ventes",stats.SaleCount.ToString("N0"),Ui.Green,()=>OpenSales(stats.BusinessDate)));
        flow.Controls.Add(new DashboardMetric("Unités vendues",stats.UnitsSold.ToString("N0"),Ui.Blue,()=>OpenSales(stats.BusinessDate)));
        flow.Controls.Add(new DashboardMetric("En rupture",stats.OutOfStockCount.ToString("N0"),Ui.Red,()=>_openStock("En rupture")));
        flow.Controls.Add(new DashboardMetric("Stock faible",stats.LowStockCount.ToString("N0"),Ui.Orange,()=>_openStock("Stock faible")));
        _metrics.Controls.Add(flow);
    }
    private void OpenSales(string date)=>_openSales(date,date);
    private void OpenSelectedAlert(){if(_alerts.CurrentRow?.DataBoundItem is StockAlert alert)_openStock(alert.Status);}
    private void SetHeader(string property,string title){if(_alerts.Columns.Contains(property)&&_alerts.Columns[property] is{} column)column.HeaderText=title;}
    private void Hide(string property){if(_alerts.Columns.Contains(property)&&_alerts.Columns[property] is{} column)column.Visible=false;}
    private static void FormatAlert(object? sender,DataGridViewCellFormattingEventArgs e)
    {
        if(sender is not DataGridView grid||e.RowIndex<0||grid.Rows[e.RowIndex].DataBoundItem is not StockAlert alert)return;var color=alert.Status=="En rupture"?(Color.FromArgb(254,226,226),Ui.Red):(Color.FromArgb(255,247,237),Ui.Orange);e.CellStyle!.BackColor=color.Item1;e.CellStyle!.ForeColor=color.Item2;e.CellStyle!.SelectionBackColor=color.Item1;e.CellStyle!.SelectionForeColor=color.Item2;
    }
}

internal sealed class DashboardMetric : Panel
{
    public DashboardMetric(string title,string value,Color accent,Action action)
    {
        Width=204;Height=104;BackColor=Color.White;Margin=new Padding(0,0,10,10);Padding=new Padding(14);Cursor=Cursors.Hand;AccessibleRole=AccessibleRole.PushButton;AccessibleName=$"{title}: {value}";TabStop=true;
        var titleLabel=Ui.Label(title,9,true,Ui.Muted);titleLabel.Location=new Point(14,13);var valueLabel=new Label{Text=value,Location=new Point(14,43),AutoSize=true,Font=new Font("Segoe UI",17,FontStyle.Bold),ForeColor=accent};Controls.Add(titleLabel);Controls.Add(valueLabel);Click+=(_,_)=>action();foreach(Control child in Controls)child.Click+=(_,_)=>action();MouseEnter+=(_,_)=>BackColor=Color.FromArgb(239,246,255);MouseLeave+=(_,_)=>BackColor=Color.White;KeyDown+=(_,e)=>{if(e.KeyCode is Keys.Enter or Keys.Space){action();e.Handled=true;}};Paint+=(_,e)=>{using var pen=new Pen(Ui.Border);e.Graphics.DrawRectangle(pen,0,0,Width-1,Height-1);};
    }
}

internal enum DashboardChartKind { Trend,TopProducts,Payments }

internal sealed class DashboardChart : Control
{
    private readonly DashboardChartKind _kind;private IReadOnlyList<SalesTrendPoint> _trend=[];private IReadOnlyList<TopProductPoint> _top=[];private IReadOnlyList<PaymentModePoint> _payments=[];
    public DashboardChart(DashboardChartKind kind){_kind=kind;DoubleBuffered=true;BackColor=Color.White;MinimumSize=new Size(300,200);}
    public void SetTrend(IReadOnlyList<SalesTrendPoint> data){_trend=data;Invalidate();}
    public void SetTopProducts(IReadOnlyList<TopProductPoint> data){_top=data;Invalidate();}
    public void SetPayments(IReadOnlyList<PaymentModePoint> data){_payments=data;Invalidate();}
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);e.Graphics.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;var rect=ClientRectangle;rect.Inflate(-10,-8);if(_kind==DashboardChartKind.Trend)DrawTrend(e.Graphics,rect);else if(_kind==DashboardChartKind.TopProducts)DrawTop(e.Graphics,rect);else DrawPayments(e.Graphics,rect);
    }
    private void DrawTrend(Graphics g,Rectangle r)
    {
        if(_trend.Count==0){Empty(g,r,"Aucune vente validée sur les 7 derniers jours.");return;}var plot=new Rectangle(r.Left+46,r.Top+12,Math.Max(10,r.Width-62),Math.Max(20,r.Height-50));var max=Math.Max(1,_trend.Max(x=>x.Amount));using var grid=new Pen(Ui.Border);using var labelFont=new Font("Segoe UI",8);using var labelBrush=new SolidBrush(Ui.Muted);
        for(var i=0;i<4;i++){var y=plot.Top+i*plot.Height/3;g.DrawLine(grid,plot.Left,y,plot.Right,y);var amount=max*(3-i)/3;g.DrawString($"{amount:N0}",labelFont,labelBrush,r.Left,y-8);}
        var points=_trend.Select((item,i)=>new PointF(plot.Left+(_trend.Count==1?plot.Width/2:i*plot.Width/(_trend.Count-1)),plot.Bottom-(float)item.Amount/max*plot.Height)).ToArray();using var line=new Pen(Ui.Blue,3);if(points.Length>1)g.DrawLines(line,points);using var brush=new SolidBrush(Ui.Blue);foreach(var p in points)g.FillEllipse(brush,p.X-4,p.Y-4,8,8);
        for(var i=0;i<_trend.Count;i++){var date=DateOnly.ParseExact(_trend[i].BusinessDate,"yyyy-MM-dd");var text=date.ToString("dd/MM");var x=plot.Left+(_trend.Count==1?plot.Width/2:i*plot.Width/(_trend.Count-1));g.DrawString(text,labelFont,labelBrush,x-18,plot.Bottom+7);}
    }
    private void DrawTop(Graphics g,Rectangle r)
    {
        if(_top.Count==0){Empty(g,r,"Aucun produit vendu sur les 7 derniers jours.");return;}var labelFont=new Font("Segoe UI",8);var brush=new SolidBrush(Ui.Muted);var max=Math.Max(1,_top.Max(x=>x.Quantity));var rowHeight=Math.Max(20,r.Height/_top.Count);
        using var format=new StringFormat{Trimming=StringTrimming.EllipsisCharacter};for(var i=0;i<_top.Count;i++){var y=r.Top+i*rowHeight;var name=_top[i].Reference+" · "+_top[i].Name;var barLeft=r.Left+Math.Max(90,r.Width-195);var textBounds=new RectangleF(r.Left,y+1,Math.Max(70,barLeft-r.Left-10),rowHeight);g.DrawString(name,labelFont,brush,textBounds,format);var barWidth=Math.Max(8,(int)((r.Right-barLeft-42)*(double)_top[i].Quantity/max));using var bar=new SolidBrush(i==0?Ui.Blue:Color.FromArgb(147,197,253));g.FillRectangle(bar,barLeft,y+4,barWidth,14);g.DrawString(_top[i].Quantity.ToString("N0"),labelFont,brush,barLeft+barWidth+6,y+2);}
        labelFont.Dispose();brush.Dispose();
    }
    private void DrawPayments(Graphics g,Rectangle r)
    {
        if(_payments.Count==0){Empty(g,r,"Aucune vente validée aujourd'hui.");return;}var colors=new[]{Ui.Blue,Ui.Green,Ui.Orange};var total=_payments.Sum(x=>x.Amount);var size=Math.Min(r.Height-16,r.Width/2);var pie=new Rectangle(r.Left+6,r.Top+4,size,size);float start=-90;
        for(var i=0;i<_payments.Count;i++){var sweep=(float)(_payments[i].Amount/(double)total*360);using var b=new SolidBrush(colors[i%colors.Length]);g.FillPie(b,pie,start,sweep);start+=sweep;}
        using var font=new Font("Segoe UI",9);using var textBrush=new SolidBrush(Ui.Ink);var x=pie.Right+15;var y=r.Top+20;
        for(var i=0;i<_payments.Count;i++){using var swatch=new SolidBrush(colors[i%colors.Length]);g.FillRectangle(swatch,x,y+3,12,12);var label=PaymentLabel(_payments[i].PaymentMode);g.DrawString($"{label} · {_payments[i].Amount:N0} XAF",font,textBrush,x+20,y);y+=31;}
    }
    private static string PaymentLabel(string value)=>value switch{"Especes"=>"Espèces","MobileMoney"=>"Mobile Money","Carte"=>"Carte",_=>value};
    private static void Empty(Graphics g,Rectangle r,string message){using var font=new Font("Segoe UI",9);using var brush=new SolidBrush(Ui.Muted);using var format=new StringFormat{Alignment=StringAlignment.Center,LineAlignment=StringAlignment.Center};var bounds=new RectangleF(r.Left,r.Top+r.Height/2-14,r.Width,30);g.DrawString(message,font,brush,bounds,format);}
}
