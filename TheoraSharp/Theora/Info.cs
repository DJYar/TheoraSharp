using System.Text;
using TheoraSharp.Ogg;
using Buffer = TheoraSharp.Ogg.Buffer;

namespace TheoraSharp.Theora;

public class Info
{
  public int width;
  public int height;
  public int frame_width;
  public int frame_height;
  public int offset_x;
  public int offset_y;
  public int fps_numerator;
  public int fps_denominator;
  public int aspect_numerator;
  public int aspect_denominator;
  public Colorspace colorspace;
  public PixelFormat pixel_fmt;
  public int  target_bitrate;
  public int  quality;
  public int  quick_p;  /* quick encode/decode */

  /* decode only */
  public byte version_major;
  public byte version_minor;
  public byte version_subminor;

  public int   keyframe_granule_shift;
  public long  keyframe_frequency_force;

  /* codec_setup_info */
  internal short[][][][] dequant_tables;
  internal int[] AcScaleFactorTable = new int[Constants.Q_TABLE_SIZE];
  internal short[] DcScaleFactorTable = new short[Constants.Q_TABLE_SIZE];
  internal int MaxQMatrixIndex;
  internal short[] qmats;

  internal HuffEntry[] HuffRoot = new HuffEntry[Huffman.NUM_HUFF_TABLES];
  internal byte[] LoopFilterLimitValues = new byte[Constants.Q_TABLE_SIZE];

  public Info()
  {
      //2.3.64.64
    dequant_tables = new short[2][][][];
    for (int i = 0; i < 2; i++)
    {
      dequant_tables[i] = new short[3][][];
      for (int j = 0; j < 3; j++)
      {
        dequant_tables[i][j] = new short[64][];
        for (int k = 0; k < 64; k++)
        {
          dequant_tables[i][j][k] = new short[64];
        }
      }
    }
  }

  private static void _tp_readbuffer(Buffer opb, byte[] buf, int len)
  {
    for (int i=0; i<len; i++) {
      buf[i] = (byte)opb.ReadB(8);
    }
  }

  private static int _tp_readlsbint(Buffer opb)
  {
    int value;

    value  = opb.ReadB(8);
    value |= opb.ReadB(8) << 8;
    value |= opb.ReadB(8) << 16;
    value |= opb.ReadB(8) << 24;

    return value;
  }

  private int unpackInfo(Buffer opb){
    version_major = (byte)opb.ReadB(8);
    version_minor = (byte)opb.ReadB(8);
    version_subminor = (byte)opb.ReadB(8);

    if (version_major != Version.VERSION_MAJOR) 
      return (int)Result.Version;
    if (version_minor > Version.VERSION_MINOR) 
      return (int)Result.Version;

    width  = (int)(opb.ReadB(16)<<4);
    height = (int)(opb.ReadB(16)<<4);
    frame_width = (int)opb.ReadB(24);
    frame_height = (int)opb.ReadB(24);
    offset_x = (int)opb.ReadB(8);
    offset_y = (int)opb.ReadB(8);

    /* Invert the offset so that it is from the top down */
    offset_y = height-frame_height-offset_y;

    fps_numerator = opb.ReadB(32);
    fps_denominator = opb.ReadB(32);
    aspect_numerator = opb.ReadB(24);
    aspect_denominator = opb.ReadB(24);

    colorspace = Enum.GetValues<Colorspace>()[opb.ReadB(8)];
    target_bitrate = opb.ReadB(24);
    quality = opb.ReadB(6);

    keyframe_granule_shift = opb.ReadB(5);
    keyframe_frequency_force = 1<<keyframe_granule_shift;
    
    pixel_fmt = Enum.GetValues<PixelFormat>()[opb.ReadB(2)];
    if (pixel_fmt==PixelFormat.TH_PF_RSVD)
      return (int)Result.BadHeader;

    /* spare configuration bits */
    if (opb.ReadB(3) == -1)
      return (int)Result.BadHeader;

    return(0);
  }

  static int unpackComment (Comment tc, Buffer opb)
  {
    int i;
    int len;
    byte[] tmp;
    int comments;

    len = _tp_readlsbint(opb);
    if(len<0)
      return (int)Result.BadHeader;

    tmp=new byte[len];
    _tp_readbuffer(opb, tmp, len);
    tc.vendor=Encoding.Default.GetString(tmp);

    comments = _tp_readlsbint(opb);
    if(comments<0) {
      tc.clear();
      return (int)Result.BadHeader;
    }
    tc.user_comments=new String[comments];
    for(i=0;i<comments;i++){
      len = _tp_readlsbint(opb);
      if(len<0) {
        tc.clear();
        return (int)Result.BadHeader;
      }

      tmp=new byte[len];
      _tp_readbuffer(opb,tmp,len);
      tc.user_comments[i] = Encoding.Default.GetString(tmp);
    }
    return 0;
  }

  /* handle the in-loop filter limit value table */
  private int readFilterTables(Buffer opb) 
  {
    int bits = opb.ReadB(3);
    for (int i=0; i<Constants.Q_TABLE_SIZE; i++) {
      int value = opb.ReadB(bits);

      LoopFilterLimitValues[i] = (byte) value;
    }
    if (bits<0) 
      return (int)Result.BadHeader;

    return 0;
  }


  private int unpackTables (Buffer opb)
  {
    int ret;

    ret = readFilterTables(opb);
    if (ret != 0)
      return ret;
    ret = Quant.readQTables(this, opb);
    if (ret != 0)
      return ret;
    ret = Huffman.readHuffmanTrees(HuffRoot, opb);
    if (ret != 0)
      return ret;

    return ret;
  }

  public void clear() {
    qmats = null;
    
    Huffman.clearHuffmanTrees(HuffRoot);
  }

  public int decodeHeader (Comment cc, Packet op)
  {
    long ret;
    Buffer opb = new Buffer();

    opb.ReadInit (op.PacketBase, op.PacketPos, op.Bytes);
  
    {
      byte[] id = new byte[6];
      int typeflag;
    
      typeflag = opb.ReadB(8);
      if((typeflag & 0x80) == 0) {
        return (int)Result.NotFormat;
      }

      _tp_readbuffer(opb,id,6);
      if (!"theora".Equals(Encoding.Default.GetString(id), StringComparison.InvariantCultureIgnoreCase)) {
        return (int)Result.NotFormat;
      }

      switch(typeflag){
      case 0x80:
        if(op.BOS == 0){
          /* Not the initial packet */
          return (int)Result.BadHeader;
        }
        if(version_major!=0){
          /* previously initialized info header */
          return (int)Result.BadHeader;
        }

        ret = unpackInfo(opb);
        return (int)ret;

      case 0x81:
        if(version_major==0){
          /* um... we didn't get the initial header */
          return (int)Result.BadHeader;
        }

        ret = unpackComment(cc,opb);
        return (int)ret;

      case 0x82:
        if(version_major==0 || cc.vendor==null){
          /* um... we didn't get the initial header or comments yet */
          return (int)Result.BadHeader;
        }

        ret = unpackTables(opb);
        return (int)ret;
    
      default:
        if(version_major==0 || cc.vendor==null || 
           HuffRoot[0]==null)
	{
          /* we haven't gotten the three required headers */
          return (int)Result.BadHeader;
        }
        /* ignore any trailing header packets for forward compatibility */
        return (int)Result.NewPacket;
      }
    }
  }
}