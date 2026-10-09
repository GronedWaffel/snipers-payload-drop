namespace PayloadDrop;
static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        if (args.Length == 2 && args[0] == "--self-test") return SelfTest.Run(args[1]).GetAwaiter().GetResult();
        ApplicationConfiguration.Initialize();
        var form = new MainForm(args.Length == 2 && args[0] == "--preview" ? args[1] : null);
        if (args.Length > 0 && !args[0].StartsWith("--")) form.AddFiles(args);
        Application.Run(form); return 0;
    }
}
