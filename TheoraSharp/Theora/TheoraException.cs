namespace TheoraSharp.Theora;

public class TheoraException : Exception
{
    private static long serialVersionUID = 1L;
    private int error;
    
    public int ErrorCode => error;

    public TheoraException()
    {
    }

    public TheoraException(String str, int error)
    {
        this.error = error;
    }
}