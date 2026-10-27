namespace CkanUI.Sfs.Cli;

internal static class Program
{
    public static int Main(string[] args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("用法: ckanui-sfs <format|validate|diff> <file>");
            return 2;
        }

        Console.Error.WriteLine("E1 里程碑尚未实现：" + string.Join(' ', args));
        return 2;
    }
}
