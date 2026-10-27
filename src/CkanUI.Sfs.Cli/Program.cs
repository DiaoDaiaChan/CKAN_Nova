using System.Text;

namespace CkanUI.Sfs.Cli;

internal static class Program
{
    public static int Main(string[] args)
    {
        try
        {
            Console.OutputEncoding = Encoding.UTF8;
        }
        catch (IOException)
        {
            // 重定向/无控制台时忽略：不影响退出码语义。
        }

        return SfsCli.Run(args, Console.Out, Console.Error);
    }
}
