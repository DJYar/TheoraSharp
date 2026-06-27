namespace TheoraSharp.Vorbis;

public class JOrbisException : Exception
{
    public JOrbisException()
    {
    }

    public JOrbisException(string message)
        : base("JOrbis: " + message)
    {
    }

    public JOrbisException(string message, Exception innerException)
        : base("JOrbis: " + message, innerException)
    {
    }
}
