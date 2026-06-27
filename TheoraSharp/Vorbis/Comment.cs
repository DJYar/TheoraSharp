using System.Text;
using OggBuffer = TheoraSharp.Ogg.Buffer;
using PacketContext = TheoraSharp.Ogg.PacketContext;

namespace TheoraSharp.Vorbis;

public class Comment
{
    private const int OvEImpl = -130;

    private static readonly byte[] Vorbis =
    {
        (byte)'v',
        (byte)'o',
        (byte)'r',
        (byte)'b',
        (byte)'i',
        (byte)'s'
    };

    public byte[][] UserComments { get; set; }
    public int[] CommentLengths { get; set; }
    public int Comments { get; set; }
    public byte[] Vendor { get; set; }

    public void Initialize()
    {
        UserComments = null;
        Comments = 0;
        Vendor = null;
    }

    public void Add(string comment)
    {
        Add(Encoding.UTF8.GetBytes(comment));
    }

    private void Add(byte[] comment)
    {
        var userComments = new byte[Comments + 2][];
        if (UserComments != null)
        {
            Array.Copy(UserComments, 0, userComments, 0, Comments);
        }

        UserComments = userComments;

        var commentLengths = new int[Comments + 2];
        if (CommentLengths != null)
        {
            Array.Copy(CommentLengths, 0, commentLengths, 0, Comments);
        }

        CommentLengths = commentLengths;

        var storedComment = new byte[comment.Length + 1];
        Array.Copy(comment, 0, storedComment, 0, comment.Length);
        UserComments[Comments] = storedComment;
        CommentLengths[Comments] = comment.Length;
        Comments++;
        UserComments[Comments] = null;
    }

    public void AddTag(string tag, string contents)
    {
        Add(tag + "=" + (contents ?? string.Empty));
    }

    public string Query(string tag)
    {
        return Query(tag, 0);
    }

    public string Query(string tag, int count)
    {
        var index = Query(Encoding.UTF8.GetBytes(tag), count);
        if (index == -1)
        {
            return null;
        }

        var comment = UserComments[index];
        for (var i = 0; i < CommentLengths[index]; i++)
        {
            if (comment[i] == '=')
            {
                return Encoding.UTF8.GetString(comment, i + 1, CommentLengths[index] - i - 1);
            }
        }

        return null;
    }

    private int Query(byte[] tag, int count)
    {
        var found = 0;
        var tagLength = tag.Length;
        var fullTag = new byte[tagLength + 2];
        Array.Copy(tag, 0, fullTag, 0, tag.Length);
        fullTag[tag.Length] = (byte)'=';

        for (var i = 0; i < Comments; i++)
        {
            if (TagCompare(UserComments[i], fullTag, tagLength))
            {
                if (count == found)
                {
                    return i;
                }

                found++;
            }
        }

        return -1;
    }

    internal int Unpack(OggBuffer buffer)
    {
        var vendorLength = buffer.Read(32);
        if (vendorLength < 0)
        {
            Clear();
            return -1;
        }

        Vendor = new byte[vendorLength + 1];
        buffer.Read(Vendor, vendorLength);

        Comments = buffer.Read(32);
        if (Comments < 0)
        {
            Clear();
            return -1;
        }

        UserComments = new byte[Comments + 1][];
        CommentLengths = new int[Comments + 1];

        for (var i = 0; i < Comments; i++)
        {
            var length = buffer.Read(32);
            if (length < 0)
            {
                Clear();
                return -1;
            }

            CommentLengths[i] = length;
            UserComments[i] = new byte[length + 1];
            buffer.Read(UserComments[i], length);
        }

        if (buffer.Read(1) != 1)
        {
            Clear();
            return -1;
        }

        return 0;
    }

    internal int Pack(OggBuffer buffer)
    {
        var vendor = Encoding.UTF8.GetBytes("Xiphophorus libVorbis I 20000508");

        buffer.Write(0x03, 8);
        buffer.Write(Vorbis);

        buffer.Write(unchecked((uint)vendor.Length), 32);
        buffer.Write(vendor);

        buffer.Write(unchecked((uint)Comments), 32);
        if (Comments != 0)
        {
            for (var i = 0; i < Comments; i++)
            {
                if (UserComments[i] != null)
                {
                    buffer.Write(unchecked((uint)CommentLengths[i]), 32);
                    buffer.Write(UserComments[i]);
                }
                else
                {
                    buffer.Write(0, 32);
                }
            }
        }

        buffer.Write(1, 1);
        return 0;
    }

    public int HeaderOut(PacketContext packet)
    {
        var buffer = new OggBuffer();
        buffer.WriteInit();

        if (Pack(buffer) != 0)
        {
            return OvEImpl;
        }

        packet.PacketBase = new byte[buffer.Bytes()];
        packet.PacketPos = 0;
        packet.Bytes = buffer.Bytes();
        Array.Copy(buffer.GetBuffer(), 0, packet.PacketBase, 0, packet.Bytes);
        packet.BOS = 0;
        packet.EOS = 0;
        packet.GranulePos = 0;
        return 0;
    }

    public void Clear()
    {
        if (UserComments != null)
        {
            for (var i = 0; i < Comments && i < UserComments.Length; i++)
            {
                UserComments[i] = null;
            }
        }

        UserComments = null;
        Vendor = null;
    }

    public string GetVendor()
    {
        return Vendor == null ? null : Encoding.UTF8.GetString(Vendor, 0, Vendor.Length - 1);
    }

    public string GetComment(int index)
    {
        return Comments <= index ? null : Encoding.UTF8.GetString(UserComments[index], 0, UserComments[index].Length - 1);
    }

    public override string ToString()
    {
        var result = "Vendor: " + GetVendor();
        for (var i = 0; i < Comments; i++)
        {
            result += "\nComment: " + GetComment(i);
        }

        return result + "\n";
    }

    private static bool TagCompare(byte[] first, byte[] second, int count)
    {
        for (var i = 0; i < count; i++)
        {
            var c1 = first[i];
            var c2 = second[i];

            if (c1 >= 'A')
            {
                c1 = (byte)(c1 - 'A' + 'a');
            }

            if (c2 >= 'A')
            {
                c2 = (byte)(c2 - 'A' + 'a');
            }

            if (c1 != c2)
            {
                return false;
            }
        }

        return true;
    }
}
