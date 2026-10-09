using System.ComponentModel;
using System.Text.Json;

namespace PayloadDrop;

public class QueueItem
{
    public string Path { get; set; } = "";
    public decimal Delay { get; set; } = 3;
    public string State { get; set; } = "Ready";
}
public record Settings(string Host, int Port, decimal DefaultDelay, List<QueueItem> Queue);

public sealed class MainForm : Form
{
    readonly Color bg = Color.FromArgb(15,17,21), panel = Color.FromArgb(23,26,32), muted = Color.FromArgb(155,165,182);
    readonly List<QueueItem> queue = [];
    readonly TextBox host = new() { Text = "192.168.137.215" };
    readonly NumericUpDown port = new() { Minimum = 1, Maximum = 65535, Value = 9021 };
    readonly NumericUpDown delay = new() { Minimum = 0, Maximum = 3600, DecimalPlaces = 1, Increment = .5m, Value = 3 };
    readonly DataGridView grid = new();
    readonly Label status = new() { Text = "Drop payloads below, arrange the order, then send." };
    readonly Label count = new();
    readonly ProgressBar progress = new() { Maximum = 1000 };
    readonly Button send = new() { Text = "Send pending" }, stop = new() { Text = "Stop", Enabled = false };
    readonly List<Control> editing = [];
    readonly string config = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SnipersPayloadDrop", "settings.json");
    CancellationTokenSource? running;
    bool loading, closing; int dragIndex = -1; Point dragStart;
    readonly string? preview;

    public MainForm(string? previewPath = null)
    {
        preview = previewPath; Text = "Snipers • Payload Drop";
        ClientSize = new Size(960,640); MinimumSize = new Size(820,530); StartPosition = FormStartPosition.CenterScreen;
        BackColor = bg; ForeColor = Color.White; Font = new Font("Segoe UI", 10); AllowDrop = true;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(24), ColumnCount = 1, RowCount = 7 };
        foreach (var h in new float[] { 72,62,46,0,32,32,56 }) layout.RowStyles.Add(h == 0 ? new RowStyle(SizeType.Percent,100) : new RowStyle(SizeType.Absolute,h));
        Controls.Add(layout);
        var heading = new Panel { Dock = DockStyle.Fill };
        heading.Controls.Add(new Label { Text = "PAYLOAD DROP", Font = new Font("Segoe UI Semibold",24), AutoSize = true, Location = new Point(0,0) });
        heading.Controls.Add(new Label { Text = "DROP. ORDER. SEND.", ForeColor = muted, AutoSize = true, Location = new Point(2,44) }); layout.Controls.Add(heading);
        var connect = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
        AddField(connect,"Console IP / hostname",host,250); AddField(connect,"Payload port",port,125); AddField(connect,"Default wait after send (s)",delay,210);
        layout.Controls.Add(connect); editing.AddRange([host,port,delay]);
        var tools = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
        AddButton(tools,"Add files",()=> { using var d = new OpenFileDialog { Multiselect = true, Filter = "Payloads (*.elf;*.bin)|*.elf;*.bin|All files (*.*)|*.*" }; if (d.ShowDialog(this)==DialogResult.OK) AddFiles(d.FileNames); });
        AddButton(tools,"Move up",()=>MoveRow(-1)); AddButton(tools,"Move down",()=>MoveRow(1));
        AddButton(tools,"Remove",Remove); AddButton(tools,"Clear",()=>{ queue.Clear();RefreshQueue();Save(); });
        AddButton(tools,"Mark all ready",()=>{foreach(var item in queue)item.State="Ready";RefreshQueue();Save();}); layout.Controls.Add(tools);
        grid.Dock=DockStyle.Fill;grid.BackgroundColor=panel;grid.BorderStyle=BorderStyle.None;grid.AllowDrop=true;
        grid.AllowUserToAddRows=false;grid.AllowUserToDeleteRows=false;grid.AllowUserToResizeRows=false;grid.RowHeadersVisible=false;
        grid.MultiSelect=false;grid.SelectionMode=DataGridViewSelectionMode.FullRowSelect;grid.AutoGenerateColumns=false;
        grid.EnableHeadersVisualStyles=false;grid.ColumnHeadersHeight=40;grid.RowTemplate.Height=43;
        grid.ColumnHeadersDefaultCellStyle=new DataGridViewCellStyle { BackColor=Color.FromArgb(32,36,44),ForeColor=muted,Font=new Font(Font,FontStyle.Bold),SelectionBackColor=panel };
        grid.DefaultCellStyle=new DataGridViewCellStyle { BackColor=panel,ForeColor=Color.White,SelectionBackColor=Color.FromArgb(46,60,80),SelectionForeColor=Color.White,Padding=new Padding(8,0,4,0) };
        grid.GridColor=Color.FromArgb(41,46,55);
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name="order",HeaderText="#",Width=45,ReadOnly=true });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name="payload",HeaderText="Payload",AutoSizeMode=DataGridViewAutoSizeColumnMode.Fill,ReadOnly=true });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name="size",HeaderText="Size",Width=105,ReadOnly=true });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name="wait",HeaderText="Wait (s)",Width=100 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name="state",HeaderText="Status",Width=175,ReadOnly=true });
        foreach(DataGridViewColumn col in grid.Columns)col.SortMode=DataGridViewColumnSortMode.NotSortable;
        grid.CellValidating+=(s,e)=>{ if(e.ColumnIndex!=3||e.RowIndex<0)return; if(!decimal.TryParse(e.FormattedValue?.ToString(),out var v)||v<0||v>3600){e.Cancel=true;status.Text="Wait must be between 0 and 3600 seconds.";} };
        grid.CellEndEdit+=(s,e)=>{if(e.ColumnIndex==3&&e.RowIndex>=0&&decimal.TryParse(grid.Rows[e.RowIndex].Cells[3].Value?.ToString(),out var v)){queue[e.RowIndex].Delay=v;Save();}};
        grid.KeyDown+=(s,e)=>{if(running!=null||grid.IsCurrentCellInEditMode)return;if(e.KeyCode==Keys.Delete){Remove();e.Handled=true;}if(e.Control&&(e.KeyCode==Keys.Up||e.KeyCode==Keys.Down)){MoveRow(e.KeyCode==Keys.Up?-1:1);e.Handled=true;}};
        grid.MouseDown+=(s,e)=>{dragIndex=grid.HitTest(e.X,e.Y).RowIndex;dragStart=e.Location;};
        grid.MouseMove+=(s,e)=>{if(running==null&&e.Button==MouseButtons.Left&&dragIndex>=0&&!grid.IsCurrentCellInEditMode&&(Math.Abs(e.X-dragStart.X)>8||Math.Abs(e.Y-dragStart.Y)>8)){int from=dragIndex;dragIndex=-1;grid.DoDragDrop(from,DragDropEffects.Move);}};
        grid.DragOver+=DragOverQueue;grid.DragDrop+=DropQueue;DragOver+=DragOverQueue;DragDrop+=DropQueue;
        layout.Controls.Add(grid);count.Dock=DockStyle.Fill;count.ForeColor=muted;count.TextAlign=ContentAlignment.MiddleLeft;layout.Controls.Add(count);
        progress.Dock=DockStyle.Fill;progress.Margin=new Padding(0,4,0,8);layout.Controls.Add(progress);
        var bottom = new TableLayoutPanel { Dock=DockStyle.Fill,ColumnCount=3 };
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,135));bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,85));
        status.Dock=DockStyle.Fill;status.ForeColor=muted;bottom.Controls.Add(status);StyleButton(send);StyleButton(stop);send.BackColor=Color.FromArgb(52,91,145);send.Dock=DockStyle.Fill;stop.Dock=DockStyle.Fill;
        bottom.Controls.Add(send);bottom.Controls.Add(stop);layout.Controls.Add(bottom);
        send.Click+=async(s,e)=>await SendQueue();stop.Click+=(s,e)=>running?.Cancel();
        host.Leave+=(s,e)=>Save();port.ValueChanged+=(s,e)=>Save();delay.ValueChanged+=(s,e)=>Save();
        FormClosing+=(s,e)=>{if(running!=null){e.Cancel=true;closing=true;running.Cancel();status.Text="Stopping transfer…";}else Save();};
        if(preview==null)LoadSettings();else{queue.Add(new(){Path="TacHen.elf",Delay=15});queue.Add(new(){Path="ShadowMount.elf",Delay=3});queue.Add(new(){Path="ps5debug.elf",Delay=0});}
        RefreshQueue();
        Shown+=(s,e)=>{if(preview!=null){var timer=new System.Windows.Forms.Timer{Interval=400};timer.Tick+=(a,b)=>{timer.Stop();using var bitmap=new Bitmap(Width,Height);DrawToBitmap(bitmap,new Rectangle(0,0,Width,Height));bitmap.Save(preview);timer.Dispose();Close();};timer.Start();
            Tag=timer;}};
    }
    void AddField(FlowLayoutPanel row,string label,Control control,int width)
    {
        var p=new Panel{Width=width,Height=58,Margin=new Padding(0,0,14,0)};
        p.Controls.Add(new Label{Text=label,ForeColor=muted,AutoSize=true});control.SetBounds(0,23,width-4,29);control.BackColor=panel;control.ForeColor=Color.White;p.Controls.Add(control);row.Controls.Add(p);
    }
    void StyleButton(Button b){b.FlatStyle=FlatStyle.Flat;b.FlatAppearance.BorderColor=Color.FromArgb(66,74,89);b.BackColor=panel;b.ForeColor=Color.White;b.Height=33;b.Margin=new Padding(0,0,8,0);b.Cursor=Cursors.Hand;}
    void AddButton(FlowLayoutPanel row,string text,Action action){var b=new Button{Text=text,AutoSize=true};StyleButton(b);b.Click+=(s,e)=>action();row.Controls.Add(b);editing.Add(b);}
    public void AddFiles(IEnumerable<string> paths)
    {
        if(running!=null)return;grid.EndEdit();var errors=new List<string>();int added=0;
        foreach(var path in paths){try{var f=Transfer.Inspect(path);if(queue.Any(x=>string.Equals(x.Path,f.Path,StringComparison.OrdinalIgnoreCase)))continue;queue.Add(new(){Path=f.Path,Delay=delay.Value});added++;}catch(Exception e){errors.Add(System.IO.Path.GetFileName(path)+": "+e.Message);}}
        RefreshQueue();Save();status.Text=$"Added {added} payload(s). Drag rows or use Move up / Move down.";
        if(errors.Count>0)MessageBox.Show(this,string.Join("\n",errors.Take(8)),"Files not added",MessageBoxButtons.OK,MessageBoxIcon.Information);
    }
    void RefreshQueue(int selected=-1)
    {
        loading=true;grid.Rows.Clear();foreach(var item in queue){string size="Missing";try{size=FormatSize(new FileInfo(item.Path).Length);}catch{}int i=grid.Rows.Add(grid.Rows.Count+1,System.IO.Path.GetFileName(item.Path),size,item.Delay,item.State);grid.Rows[i].Cells[1].ToolTipText=item.Path;}
        if(selected>=0&&selected<grid.Rows.Count){grid.ClearSelection();grid.Rows[selected].Selected=true;grid.CurrentCell=grid.Rows[selected].Cells[1];}
        count.Text=queue.Count==0?"Drop .elf / .bin files here • multiple files welcome":$"{queue.Count} payload(s) • sent top to bottom • edit each row’s wait time • Sent means transferred, not execution confirmed";
        loading=false;
    }
    static string FormatSize(long bytes)=>bytes>=1048576?$"{bytes/1048576d:0.0} MiB":$"{bytes/1024d:0.0} KiB";
    int Selected=>grid.CurrentRow?.Index??-1;
    void MoveRow(int direction){grid.EndEdit();int i=Selected,j=i+direction;if(i<0||j<0||j>=queue.Count)return;var item=queue[i];queue.RemoveAt(i);queue.Insert(j,item);RefreshQueue(j);Save();}
    void Remove(){grid.EndEdit();int i=Selected;if(i<0)return;queue.RemoveAt(i);RefreshQueue(Math.Min(i,queue.Count-1));Save();}
    void DragOverQueue(object? s,DragEventArgs e){e.Effect=running!=null?DragDropEffects.None:e.Data?.GetDataPresent(DataFormats.FileDrop)==true?DragDropEffects.Copy:e.Data?.GetDataPresent(typeof(int))==true?DragDropEffects.Move:DragDropEffects.None;}
    void DropQueue(object? s,DragEventArgs e)
    {
        if(running!=null)return;if(e.Data?.GetData(DataFormats.FileDrop) is string[] files){AddFiles(files);return;}
        if(e.Data?.GetData(typeof(int)) is not int from||from<0||from>=queue.Count)return;grid.EndEdit();var point=grid.PointToClient(new Point(e.X,e.Y));int to=grid.HitTest(point.X,point.Y).RowIndex;if(to<0)to=queue.Count-1;var item=queue[from];queue.RemoveAt(from);queue.Insert(to,item);RefreshQueue(to);Save();
    }
    async Task SendQueue()
    {
        if(running!=null||!grid.EndEdit())return;
        string address=host.Text.Trim();if(Uri.CheckHostName(address)==UriHostNameType.Unknown){status.Text="Enter a valid console IP address or hostname.";return;}
        var items=queue.Where(x=>x.State=="Ready").ToArray();if(items.Length==0){status.Text="Nothing pending. Add files or mark the queue ready to send again.";return;}
        PayloadFile[] files;try{files=items.Select(x=>Transfer.Inspect(x.Path)).ToArray();}catch(Exception e){status.Text="Queue not started: "+e.Message;return;}
        int targetPort=(int)port.Value;Save();running=new();var token=running.Token;foreach(var c in editing)c.Enabled=false;send.Enabled=false;stop.Enabled=true;grid.ReadOnly=true;
        QueueItem? current=null;bool transferred=false;
        try
        {
            for(int i=0;i<items.Length;i++)
            {
                token.ThrowIfCancellationRequested();current=items[i];transferred=false;current.State="Connecting…";RefreshQueue(queue.IndexOf(current));long last=0;
                await Transfer.Send(files[i],address,targetPort,p=>{long now=Environment.TickCount64;if(now-last<80&&p.Sent!=p.Total)return;last=now;progress.Value=(int)Math.Clamp(p.Sent*1000d/p.Total,0,1000);current.State=$"Sending {p.Sent*100d/p.Total:0}%";grid.Rows[queue.IndexOf(current)].Cells[4].Value=current.State;status.Text=$"{i+1}/{items.Length} • {System.IO.Path.GetFileName(current.Path)} • {FormatSize(p.Sent)} / {FormatSize(p.Total)}";},token);
                transferred=true;current.State="Sent";RefreshQueue(queue.IndexOf(current));
                if(i+1<items.Length){var until=DateTime.UtcNow.AddSeconds((double)current.Delay);while(DateTime.UtcNow<until){status.Text=$"Sent {System.IO.Path.GetFileName(current.Path)} • next payload in {Math.Ceiling((until-DateTime.UtcNow).TotalSeconds)}s";await Task.Delay(100,token);}}
            }
            status.Text=$"Sent {items.Length} payload(s) in order. Check the console for startup notifications.";
        }
        catch(Exception e)
        {
            if(current!=null&&!transferred)current.State=e is OperationCanceledException?"Stopped / check console":"Failed / check console";
            status.Text=e is OperationCanceledException?"Queue stopped. No automatic retries; remaining payloads are still ready.":"Queue stopped: "+e.Message;
        }
        finally{running.Dispose();running=null;foreach(var c in editing)c.Enabled=true;send.Enabled=true;stop.Enabled=false;grid.ReadOnly=false;RefreshQueue();Save();if(closing)Close();}
    }
    void Save(){if(loading||preview!=null)return;try{Directory.CreateDirectory(System.IO.Path.GetDirectoryName(config)!);var temp=config+".tmp";File.WriteAllText(temp,JsonSerializer.Serialize(new Settings(host.Text,(int)port.Value,delay.Value,queue)));File.Move(temp,config,true);}catch{status.Text="Settings could not be saved. Sending is still available.";}}
    void LoadSettings(){loading=true;try{if(File.Exists(config)){var s=JsonSerializer.Deserialize<Settings>(File.ReadAllText(config));if(s!=null){host.Text=s.Host;port.Value=Math.Clamp(s.Port,1,65535);delay.Value=Math.Clamp(s.DefaultDelay,0,3600);foreach(var item in s.Queue??[])if(!string.IsNullOrWhiteSpace(item.Path)){item.Delay=Math.Clamp(item.Delay,0,3600);item.State="Ready";queue.Add(item);}}}}catch{status.Text="Saved queue could not be read. Drop files to start a new one.";}finally{loading=false;}}
}
