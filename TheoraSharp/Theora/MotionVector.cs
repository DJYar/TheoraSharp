namespace TheoraSharp.Theora;

public class MotionVector : Coordinate
{
    public static MotionVector Null => new (0, 0);

    public MotionVector()
    {
    }

    public MotionVector(int x, int y) : base(x, y)
    {
    }
}