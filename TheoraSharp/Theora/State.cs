using TheoraSharp.Ogg;

namespace TheoraSharp.Theora;

public class State
{
  long granulepos;

  private Playback pbi;
  private Decode dec;

  public void clear()
  {
    if(pbi != null){
      pbi.info.clear();
      pbi.clearHuffmanSet();
      FrInit.ClearFragmentInfo(pbi);
      FrInit.ClearFrameInfo(pbi);
      pbi.clear();
    }
    pbi = null;
  }

  public int decodeInit(Info ci)
  {
    pbi = new Playback(ci);
    dec = new Decode(pbi);
    granulepos=-1;

    return(0);
  }

  public bool isKeyframe (Packet op)
  {
    return (op.PacketBase[op.PacketPos] & 0x40) == 0;
  }

  public int decodePacketin (Packet op)
  {
    long ret;

    pbi.DecoderErrorCode = 0;

    if (op.Bytes>0) {
      pbi.opb.ReadInit(op.PacketBase, op.PacketPos, op.Bytes);

      /* verify that this is a video frame */
      ret = pbi.opb.ReadB(1);

      if (ret==0) {
        try {
          ret=dec.loadAndDecode();
        } catch(Exception e) {
          /* If lock onto the bitstream is lost all sort of Exceptions can occur.
           * The bitstream damage may be local, so the next packet may be okay. */
          //e.printStackTrace();
          return (int)Result.BadPacket;
        }

        if(ret != 0)
          return (int) ret;
 
      } else {
        return (int)Result.BadPacket;
      }
   }
   if(op.GranulePos>-1)
      granulepos=op.GranulePos;
   else{
      if(granulepos==-1){
        granulepos=0;
      }
      else {
        if ((op.Bytes>0) && (pbi.FrameType == Constants.BASE_FRAME)){
          long frames= granulepos & ((1<<pbi.keyframe_granule_shift)-1);
          granulepos>>=pbi.keyframe_granule_shift;
          granulepos+=frames+1;
          granulepos<<=pbi.keyframe_granule_shift;
        }else
          granulepos++;
      }
   }

        return(0);
  }

  public int decodeYUVout (YUVBuffer yuv)
  {
    yuv.y_width = pbi.info.width;
    yuv.y_height = pbi.info.height;
    yuv.y_stride = pbi.YStride;

    yuv.uv_width = pbi.info.width >> pbi.UVShiftX;
    yuv.uv_height = pbi.info.height >> pbi.UVShiftY;
    yuv.uv_stride = pbi.UVStride;

    if(pbi.PostProcessingLevel != 0){
      yuv.data = pbi.PostProcessBuffer;
    }else{
      yuv.data = pbi.LastFrameRecon;
    }
    yuv.y_offset = pbi.ReconYDataOffset;
    yuv.u_offset = pbi.ReconUDataOffset;
    yuv.v_offset = pbi.ReconVDataOffset;
  
    /* we must flip the internal representation,
       so make the stride negative and start at the end */
    yuv.y_offset += yuv.y_stride * (yuv.y_height - 1);
    yuv.u_offset += yuv.uv_stride * (yuv.uv_height - 1);
    yuv.v_offset += yuv.uv_stride * (yuv.uv_height - 1);
    yuv.y_stride = - yuv.y_stride;
    yuv.uv_stride = - yuv.uv_stride;

    yuv.newPixels();
  
    return 0;
  }

  /* returns, in seconds, absolute time of current packet in given
     logical stream */
  public double granuleTime(long granulepos)
  {
    if(granulepos>=0){
      long iframe=granulepos>>pbi.keyframe_granule_shift;
      long pframe=granulepos-(iframe<<pbi.keyframe_granule_shift);

      return (iframe+pframe)*
        ((double)pbi.info.fps_denominator/pbi.info.fps_numerator);
    }
    return(-1);
  }
}