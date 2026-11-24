using System.Text;
using SpaceKatMonitor;

namespace SpaceKat.FusionS80;

internal static class Program
{
    private static int Main(string[] args)
    {
        Console.OutputEncoding = new UTF8Encoding(false);
        try
        {
            if (args is ["--self-test"])
            {
                SelfTest.Run();
                return 0;
            }

            if (args is not ["--input", var name] || !Console.IsInputRedirected ||
                !name.StartsWith(FusionNativeBridge.NamePrefix, StringComparison.Ordinal) ||
                !Guid.TryParseExact(name[FusionNativeBridge.NamePrefix.Length..], "N", out _))
                throw new ArgumentException("请从 SpaceKat 小助手开启 Fusion 原生六轴。");

            using var input = new FusionNativeBridge(name, writer: false);
            using var stopping = new CancellationTokenSource();
            // The pipe follows the helper's lifetime, including when navigation is paused.
            new Thread(() =>
            {
                try { Console.In.Read(); }
                catch (IOException) { }
                try { stopping.Cancel(); }
                catch (ObjectDisposedException) { }
            }) { IsBackground = true }.Start();

            var reader = new MotionReader(input);
            new S80Server("", reader.Read, input).Run(stopping.Token);
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error.Message);
            return 1;
        }
    }
}

internal readonly record struct MotionAxes(int X, int Y, int Z, int Rx, int Ry, int Rz)
{
    public bool IsZero => this == default;
}

internal readonly record struct MotionSample(MotionAxes Axes, long ReceivedAt);

internal sealed class MotionReader(FusionNativeBridge input)
{
    private readonly float[] axes = new float[6];

    public MotionSample? Read(long now)
    {
        if (!input.TryRead(now, axes, out long received))
            return null;

        // Match the prototype's native axis signs. Gains and dead zone are already applied by the helper.
        return new MotionSample(new MotionAxes(Scale(axes[0]), Scale(-axes[1]), Scale(-axes[2]),
            Scale(axes[3]), Scale(-axes[4]), Scale(-axes[5])), received);
    }

    private static int Scale(float axis) => (int)Math.Round(Math.Clamp(axis, -3f, 3f) * 96);
}
