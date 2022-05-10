namespace TheoraSharp.Theora;

public class Comment
{
    public string[] user_comments;
    public string vendor;

    public void clear()
    {
        vendor = null;
        user_comments = null;
    }
}