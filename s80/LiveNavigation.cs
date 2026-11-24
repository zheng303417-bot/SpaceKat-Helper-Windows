namespace SpaceKat.FusionS80;

internal sealed class LiveNavigation
{
    public const int FrameInterval = 16;
    private const int SampleTimeout = 150;

    private S80Session? current;
    private long eligibleSince;
    private bool moving;
    private bool reportedMotion;

    public void Update(S80Session? target, MotionSample? sample, uint message, long now)
    {
        if (current != target)
        {
            Stop(message);
            current = target;
            eligibleSince = now;
        }

        if (current is null)
            return;

        if (sample is not { } frame || frame.ReceivedAt <= eligibleSince || now - frame.ReceivedAt >= SampleTimeout ||
            frame.Axes.IsZero)
        {
            Stop(message);
            return;
        }

        if (current.SendMotion(message, frame.Axes, FrameInterval, limitBacklog: true) && !reportedMotion)
        {
            reportedMotion = true;
            Console.WriteLine($"已向 {current.Request.Application} 发送实时六轴运动。");
        }
        moving = true;
    }

    public void Detach(S80Session session, uint message)
    {
        if (session != current)
            return;

        Stop(message);
        current = null;
    }

    public void Stop(uint message)
    {
        if (!moving)
            return;

        current?.SendZero(message, FrameInterval);
        moving = false;
    }
}
