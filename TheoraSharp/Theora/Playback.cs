using Buffer = TheoraSharp.Ogg.Buffer;

namespace TheoraSharp.Theora;

internal class Playback
{
  /* Different key frame types/methods */
  private static int DCT_KEY_FRAME = 0;

  //oggpack_buffer *opb;
  internal Buffer  opb = new Buffer();

  internal Info     info;
  /* how far do we shift the granulepos to seperate out P frame counts? */
  internal int             keyframe_granule_shift;


  /***********************************************************************/
  /* Decoder and Frame Type Information */

  internal int           DecoderErrorCode;
  int           FramesHaveBeenSkipped;

  int           PostProcessEnabled;
  internal int           PostProcessingLevel;    /* Perform post processing */

  /* Frame Info */
  internal byte 		FrameType;
  internal byte 		KeyFrameType;
  int           QualitySetting;

  internal int           FrameQIndex;            /* Quality specified as a
                                           table index */
  //int           ThisFrameQualityValue;  /* Quality value for this frame  */
  //int           LastFrameQualityValue;  /* Last Frame's Quality */
  internal int     	CodedBlockIndex;        /* Number of Coded Blocks */
  internal int           CodedBlocksThisFrame;   /* Index into coded blocks */
  int           FrameSize;              /* The number of bytes in the frame. */

  internal int[]         frameQIS = new int[3];
  internal int           frameNQIS; /* number of quality indices this frame uses */


  /**********************************************************************/
  /* Frame Size & Index Information */

  internal int           YPlaneSize;
  internal int           UVPlaneSize;
  internal int           YStride;
  internal int           UVStride;
  internal int           VFragments;
  internal int           HFragments;
  internal int           UnitFragments;
  internal int           YPlaneFragments;
  internal int           UVPlaneFragments;

  internal int           UVShiftX;       /* 1 unless Info.pixel_fmt == TH_PF_444 */
  internal int           UVShiftY;       /* 0 unless Info.pixel_fmt == TH_PF_420 */

  internal int           ReconYPlaneSize;
  internal int           ReconUVPlaneSize;

  internal int           YDataOffset;
  internal int           UDataOffset;
  internal int           VDataOffset;
  internal int           ReconYDataOffset;
  internal int           ReconUDataOffset;
  internal int           ReconVDataOffset;
  internal int           YSuperBlocks;   /* Number of SuperBlocks in a Y frame */
  internal int           UVSuperBlocks;  /* Number of SuperBlocks in a U or V frame */

  internal int           SuperBlocks;    /* Total number of SuperBlocks in a
                                   Y,U,V frame */

  internal int           YSBRows;        /* Number of rows of SuperBlocks in a
                                   Y frame */

  internal int           YSBCols;        /* Number of cols of SuperBlocks in a
                                   Y frame */

  internal int           UVSBRows;       /* Number of rows of SuperBlocks in a
                                   U or V frame */

  internal int           UVSBCols;       /* Number of cols of SuperBlocks in a
                                   U or V frame */

  internal int           YMacroBlocks;   /* Number of Macro-Blocks in Y component */
  internal int           UVMacroBlocks;  /* Number of Macro-Blocks in U/V component */
  internal int           MacroBlocks;    /* Total number of Macro-Blocks */

  /**********************************************************************/
  /* Frames  */
  internal short[] 	ThisFrameRecon;
  internal short[] 	GoldenFrame;
  internal short[] 	LastFrameRecon;
  internal short[] 	PostProcessBuffer;

  /**********************************************************************/
  /* Fragment Information */
  internal int[]         pixel_index_table;        /* start address of first
                                              pixel of fragment in
                                              source */

  internal int[]		recon_pixel_index_table;  /* start address of first
                                              pixel in recon buffer */

  internal byte[] 	display_fragments;        /* Fragment update map */

  internal int[]  	CodedBlockList;           /* A list of fragment indices for
                                              coded blocks. */

  internal MotionVector[] FragMVect;                /* fragment motion vectors */

  internal int[]         FragTokenCounts;          /* Number of tokens per fragment */

  internal int[]		FragQIndex;               /* Fragment Quality used in
                                              PostProcess */

  internal byte[] 	FragCoefEOB;               /* Position of last non 0 coef
                                                within QFragData */

  internal short[][] 	QFragData;            /* Fragment Coefficients
                                               Array Pointers */

  internal byte[]        FragQs;                 /* per block quantizers */

  internal CodingMode[] 	FragCodingMethod;          /* coding method for the
                                               fragment */

  /***********************************************************************/
  /* Macro Block and SuperBlock Information */
  internal BlockMapping  BlockMap;          /* super block + sub macro
                                                   block + sub frag ->
                                                   FragIndex */

  /* Coded flag arrays and counters for them */
  internal byte[] 	SBCodedFlags;
  internal byte[] 	SBFullyFlags;
  internal byte[] 	MBCodedFlags;
  internal byte[] 	MBFullyFlags;

  /**********************************************************************/

  internal Coordinate[]  FragCoordinates;
  internal FrArray 	frArray = new FrArray();
  internal Filter 	filter = new Filter();

  
  /* quality index for each block */
  byte[]        blockQ;

  /* Dequantiser and rounding tables */
  int[]   	quant_index = new int[64];

  internal HuffEntry[]   HuffRoot_VP3x = new HuffEntry[Huffman.NUM_HUFF_TABLES];
  int[][] 	HuffCodeArray_VP3x;
  byte[][] 	HuffCodeLengthArray_VP3x;
  internal byte[]   	ExtraBitLengths_VP3x;
 

  internal void clear()
  {
    if (opb != null) {
      opb = null;
    }
  }

  private static int ilog (long v)
  {
    int ret=0;

    while (v != 0) {
      ret++;
      v>>=1;
    }
    return ret;
  }

  internal Playback (Info ci)
  {
    info = ci;

    DecoderErrorCode = 0;
    KeyFrameType = (byte)DCT_KEY_FRAME;
    FramesHaveBeenSkipped = 0;

    FrInit.InitFrameDetails(this);

    keyframe_granule_shift = ilog(ci.keyframe_frequency_force-1);
    //LastFrameQualityValue = 0;

    /* Initialise version specific quantiser and in-loop filter values */
    filter.copyFilterTables(ci);

    /* Huffman setup */
    initHuffmanTrees(ci);
  }

  internal int getFrameType() {
    return FrameType;
  }

  void setFrameType(byte FrType ){
    /* Set the appropriate frame type according to the request. */
    switch ( FrType ){
      case Constants.BASE_FRAME:
        FrameType = FrType;
	break;
      default:
        FrameType = FrType;
        break;
    }
  }

  internal void clearHuffmanSet()
  {
    Huffman.clearHuffmanTrees(HuffRoot_VP3x);

    HuffCodeArray_VP3x = null;
    HuffCodeLengthArray_VP3x = null;
  }

  internal void initHuffmanSet()
  {
    clearHuffmanSet();

    ExtraBitLengths_VP3x = HuffTables.ExtraBitLengths_VP31;

    HuffCodeArray_VP3x = new int[Huffman.NUM_HUFF_TABLES][];
    for (int i = 0; i < Huffman.NUM_HUFF_TABLES; i++)
    {
      HuffCodeArray_VP3x[i] = new int[Huffman.MAX_ENTROPY_TOKENS];
    }
    HuffCodeLengthArray_VP3x = new byte[Huffman.NUM_HUFF_TABLES][];
    for (int i = 0; i < Huffman.NUM_HUFF_TABLES; i++)
    {
      HuffCodeLengthArray_VP3x[i] = new byte[Huffman.MAX_ENTROPY_TOKENS];
    }

    for (int i = 0; i < Huffman.NUM_HUFF_TABLES; i++ ){
      Huffman.buildHuffmanTree(HuffRoot_VP3x,
                       HuffCodeArray_VP3x[i],
                       HuffCodeLengthArray_VP3x[i],
                       i, HuffTables.FrequencyCounts_VP3[i]);
    }
  }

  internal int readHuffmanTrees(Info ci, Buffer opb) {
    int i;
    for (i=0; i<Huffman.NUM_HUFF_TABLES; i++) {
       int ret;
       ci.HuffRoot[i] = new HuffEntry();
       ret = ci.HuffRoot[i].Read(0, opb);
       if (ret != 0) 
         return ret;
    }
    return 0;
  }

  internal void initHuffmanTrees(Info ci) 
  {
    int i;
    ExtraBitLengths_VP3x = HuffTables.ExtraBitLengths_VP31;
    for(i=0; i<Huffman.NUM_HUFF_TABLES; i++){
      HuffRoot_VP3x[i] = ci.HuffRoot[i].Copy();
    }
  }
}