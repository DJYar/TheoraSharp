using TheoraSharp.Utils;
using Buffer = TheoraSharp.Ogg.Buffer;

namespace TheoraSharp.Theora;

internal sealed class Decode
{

  private static ExtractMVectorComponent MVA = new ExtractMVectorComponentA();
  private static ExtractMVectorComponent MVB = new ExtractMVectorComponentB();

  private static CodingMode[][] modeAlphabet = {
    /* Last motion vector dominates */
    new[]{    CodingMode.InterLastMv,    CodingMode.InterPriorLast,
         CodingMode.InterPlusMv,    CodingMode.InterNoMv,
         CodingMode.Intra,            CodingMode.UsingGolden,
         CodingMode.GoldenMv,        CodingMode.InterFourMv },

    new[]{    CodingMode.InterLastMv,    CodingMode.InterPriorLast,
         CodingMode.InterNoMv,      CodingMode.InterPlusMv,
         CodingMode.Intra,            CodingMode.UsingGolden,
         CodingMode.GoldenMv,        CodingMode.InterFourMv },

    new[]{    CodingMode.InterLastMv,    CodingMode.InterPlusMv,
         CodingMode.InterPriorLast, CodingMode.InterNoMv,
         CodingMode.Intra,            CodingMode.UsingGolden,
         CodingMode.GoldenMv,        CodingMode.InterFourMv },

    new[]{    CodingMode.InterLastMv,    CodingMode.InterPlusMv,
         CodingMode.InterNoMv,      CodingMode.InterPriorLast,
         CodingMode.Intra,            CodingMode.UsingGolden,
         CodingMode.GoldenMv,        CodingMode.InterFourMv },

    /* No motion vector dominates */
    new[]{    CodingMode.InterNoMv,      CodingMode.InterLastMv,
         CodingMode.InterPriorLast, CodingMode.InterPlusMv,
         CodingMode.Intra,            CodingMode.UsingGolden,
         CodingMode.GoldenMv,        CodingMode.InterFourMv },

    new[]{    CodingMode.InterNoMv,      CodingMode.UsingGolden,
         CodingMode.InterLastMv,    CodingMode.InterPriorLast,
         CodingMode.InterPlusMv,    CodingMode.Intra,
         CodingMode.GoldenMv,        CodingMode.InterFourMv },

    /* dummy */
    new[]{    CodingMode.InterNoMv,      CodingMode.UsingGolden,
         CodingMode.InterLastMv,    CodingMode.InterPriorLast,
         CodingMode.InterPlusMv,    CodingMode.Intra,
         CodingMode.GoldenMv,        CodingMode.InterFourMv },
  };

  private int BlocksToDecode;
  private int EOB_Run;

  private DCTDecode dctDecode = new DCTDecode();

  private byte[] FragCoeffs;                /* # of coeffs decoded so far for
                                                 fragment */

  private MotionVector LastInterMV = new MotionVector ();
  private MotionVector PriorLastInterMV = new MotionVector ();
  private Playback pbi;

  internal Decode (Playback pbi) {
    FragCoeffs = new byte[pbi.UnitFragments];
    this.pbi = pbi;
  }
  
  private int longRunBitStringDecode() {
    /* lifted from new C Theora reference decoder */
    int bits;
    int ret;
    Buffer opb = pbi.opb;
    /*Coding scheme:
         Codeword            Run Length
       0                       1
       10x                     2-3
       110x                    4-5
       1110xx                  6-9
       11110xxx                10-17
       111110xxxx              18-33
       111111xxxxxxxxxxxx      34-4129*/

    bits = opb.ReadB(1);
    if(bits==0)return 1;
    bits = opb.ReadB(2);
    if((bits&2)==0)return 2+(int)bits;
    else if((bits&1)==0){
      bits = opb.ReadB(1);
      return 4+(int)bits;
    }
    bits = opb.ReadB(3);
    if((bits&4)==0)return 6+(int)bits;
    else if((bits&2)==0){
      ret=10+((bits&1)<<2);
      bits = opb.ReadB(2);
      return ret+(int)bits;
    }
    else if((bits&1)==0){
      bits = opb.ReadB(4);
      return 18+(int)bits;
    }
    bits = opb.ReadB(12);
    return 34+(int)bits;
  }

  private void decodeBlockLevelQi() {
    /* lifted from new C Theora reference decoder */

    /* pbi.CodedBlockIndex holds the number of coded blocks despite the
       suboptimal variable name */
    int ncoded_frags = pbi.CodedBlockIndex;

    if(ncoded_frags <= 0) return;
    if(pbi.frameNQIS == 1) {
      /*If this frame has only a single qi value, then just set it in all coded
         fragments.*/
      for(int coded_frag = 0; coded_frag < ncoded_frags; ++coded_frag) {
          pbi.FragQs[pbi.CodedBlockList[coded_frag]] = 0;
      }
    } else{
      Buffer opb = pbi.opb;
      int val;
      int  flag;
      int  nqi0;
      int  run_count;
      /*Otherwise, we decode a qi index for each fragment, using two passes of
        the same binary RLE scheme used for super-block coded bits.
       The first pass marks each fragment as having a qii of 0 or greater than
        0, and the second pass (if necessary), distinguishes between a qii of
        1 and 2.
       We store the qii of the fragment. */
      val = opb.ReadB(1);
      flag = val;
      run_count = nqi0 = 0;
      int coded_frag = 0;
      while(coded_frag < ncoded_frags){
        bool full_run;
        run_count = longRunBitStringDecode();
        full_run = (run_count >= 4129);
        do {
          pbi.FragQs[pbi.CodedBlockList[coded_frag++]] = (byte)flag;
          if(flag < 1) ++nqi0;
        } while(--run_count > 0 && coded_frag < ncoded_frags);
      
        if(full_run && coded_frag < ncoded_frags){
          val = opb.ReadB(1);
          flag=(int)val;
        } else {
          //flag=!flag;
          flag = (flag != 0) ? 0 : 1;
        }
      }
      /*TODO: run_count should be 0 here.
        If it's not, we should issue a warning of some kind.*/
      /*If we have 3 different qi's for this frame, and there was at least one
         fragment with a non-zero qi, make the second pass.*/
      if(pbi.frameNQIS==3 && nqi0 < ncoded_frags){
        coded_frag = 0;
        /*Skip qii==0 fragments.*/
        for(coded_frag = 0; coded_frag < ncoded_frags && pbi.FragQs[pbi.CodedBlockList[coded_frag]] == 0; ++coded_frag){}
        val = opb.ReadB(1);
        flag = val;
        while(coded_frag < ncoded_frags){
          bool full_run;
          run_count = longRunBitStringDecode();
          full_run = run_count >= 4129;
          for(; coded_frag < ncoded_frags; ++coded_frag){
            if(pbi.FragQs[pbi.CodedBlockList[coded_frag]] == 0) continue;
            if(run_count-- <= 0) break;
            pbi.FragQs[pbi.CodedBlockList[coded_frag]] += (byte)flag;
          }
          if(full_run && coded_frag < ncoded_frags){
            val = opb.ReadB(1);
            flag = val;
          } else {
            //flag=!flag;
            flag = (flag != 0) ? 0 : 1;
          }
        }
        /*TODO: run_count should be 0 here.
          If it's not, we should issue a warning of some kind.*/
      }
    }
  }  
  
  private int loadFrame()
  {
    int  DctQMask;
    Buffer opb = pbi.opb;

    /* Is the frame and inter frame or a key frame */
    pbi.FrameType = (byte)opb.ReadB(1);

    /* Quality (Q) index */
    DctQMask = (int) opb.ReadB(6);
    pbi.frameQIS[0] = DctQMask;
    pbi.frameNQIS = 1;

    /* look if there are additional frame quality indices */
    int moreQs = opb.ReadB(1);
    if(moreQs > 0) {
      pbi.frameQIS[1] = (int) opb.ReadB(6);
      pbi.frameNQIS = 2;
        
      moreQs = opb.ReadB(1);
      if(moreQs > 0) {
        pbi.frameQIS[2] = (int) opb.ReadB(6);
        pbi.frameNQIS = 3;
      }
    }
    
    if ( (pbi.FrameType == Constants.BASE_FRAME) ){
      /* Read the type / coding method for the key frame. */
      pbi.KeyFrameType = (byte)opb.ReadB(1);
      opb.ReadB(2);
    }

    /* Set this frame quality value from Q Index */
    //pbi.ThisFrameQualityValue = pbi.QThreshTable[pbi.frameQ];

    /* Read in the updated block map */
    pbi.frArray.quadDecodeDisplayFragments( pbi );

    return 1;
  }

  private void decodeModes (int SBRows, int SBCols)
  {
    int  MB;
    int  SBcol;
    int  SBrow;
    CodingMode[] FragCodingMethod;
    int  SB=0;
    long ret;
    int  FragIndex;
    CodingMode  CodingMethod;

    int  UVRow;
    int  UVColumn;
    int  UVFragOffset;
    int  MBListIndex = 0;
    int  i;

    FragCodingMethod = pbi.FragCodingMethod;

    /* If the frame is an intra frame then all blocks have mode intra. */
    if ( pbi.getFrameType() == Constants.BASE_FRAME ){
      MemUtils.Set(FragCodingMethod, 0, CodingMode.Intra, pbi.UnitFragments);
    }else{
      /* Clear down the macro block level mode and MV arrays. Default coding mode */
      MemUtils.Set(FragCodingMethod, 0, CodingMode.InterNoMv, pbi.UnitFragments);

      CodingMode ModeEntry; /* Mode bits read */
      CodingMode[] ModeList;

      /* Read the coding method */
      ret = pbi.opb.ReadB( Constants.MODE_METHOD_BITS);
      int CodingScheme=(int)ret;

      /* If the coding method is method 0 then we have to read in a
         custom coding scheme */
      if ( CodingScheme == 0 ){
        CodingMode[] CustomModeAlphabet = new CodingMode[Constants.MAX_MODES];
        /* Read the coding scheme. */
        for ( i = 0; i < Constants.MAX_MODES; i++ ){
          ret = pbi.opb.ReadB( Constants.MODE_BITS);
          CustomModeAlphabet[(int)ret]= Enum.GetValues<CodingMode>()[i];
        }
        ModeList=CustomModeAlphabet;
      }
      else{
        ModeList=modeAlphabet[CodingScheme-1];
      }

      /* Unravel the quad-tree */
      for ( SBrow=0; SBrow<SBRows; SBrow++ ){
        for ( SBcol=0; SBcol<SBCols; SBcol++ ){
          for ( MB=0; MB<4; MB++ ){
            /* There may be MB's lying out of frame which must be
               ignored. For these MB's top left block will have a negative
               Fragment Index. */
            /* Upack the block level modes and motion vectors */
            FragIndex = pbi.BlockMap.QuadMapToMBTopLeft(SB, MB);
            if (FragIndex >= 0){
              /* Is the Macro-Block coded: */
              if ( pbi.MBCodedFlags[MBListIndex++] != 0){
  
                /* Unpack the mode. */
                if ( CodingScheme == (Constants.MODE_METHODS-1) ){
                  /* This is the fall back coding scheme. */
                  /* Simply MODE_BITS bits per mode entry. */
                  ret = pbi.opb.ReadB( Constants.MODE_BITS);
                  CodingMethod = Enum.GetValues<CodingMode>()[(int)ret];
                }else{
                  ModeEntry = pbi.frArray.unpackMode(pbi.opb);
                  CodingMethod = ModeList[(int)ModeEntry];
                }
  
                /* Note the coding mode for each block in macro block. */
                FragCodingMethod[FragIndex] = CodingMethod;
                FragCodingMethod[FragIndex + 1] = CodingMethod;
                FragCodingMethod[FragIndex + pbi.HFragments] = CodingMethod;
                FragCodingMethod[FragIndex + pbi.HFragments + 1] = CodingMethod;
  
                /* Matching fragments in the U and V planes */
                if (pbi.UVShiftX == 1 && pbi.UVShiftY == 1){ /* TH_PF_420 */
                  UVRow = (FragIndex / (pbi.HFragments * 2));
                  UVColumn = (FragIndex % pbi.HFragments) / 2;
                  UVFragOffset = (UVRow * (pbi.HFragments / 2)) + UVColumn;
                  FragCodingMethod[pbi.YPlaneFragments + UVFragOffset] = CodingMethod;
                  FragCodingMethod[pbi.YPlaneFragments + pbi.UVPlaneFragments + UVFragOffset] =
                    CodingMethod;
                } else if (pbi.UVShiftX == 0) { /* TH_PF_444 */
                  FragIndex += pbi.YPlaneFragments;
                  FragCodingMethod[FragIndex] =
                  FragCodingMethod[FragIndex + 1] =
                  FragCodingMethod[FragIndex + pbi.HFragments] =
                  FragCodingMethod[FragIndex + pbi.HFragments + 1] = CodingMethod;
                  FragIndex += pbi.UVPlaneFragments;
                  FragCodingMethod[FragIndex] =
                  FragCodingMethod[FragIndex + 1] =
                  FragCodingMethod[FragIndex + pbi.HFragments] =
                  FragCodingMethod[FragIndex + pbi.HFragments + 1] = CodingMethod;
                } else { /*TH_PF_422 */
                  FragIndex = pbi.YPlaneFragments + FragIndex/2;
                  FragCodingMethod[FragIndex] =
                  FragCodingMethod[FragIndex + pbi.HFragments/2] = CodingMethod;
                  FragIndex += pbi.UVPlaneFragments;
                  FragCodingMethod[FragIndex] =
                  FragCodingMethod[FragIndex + pbi.HFragments/2] = CodingMethod;
                }
  
              }
            }
          }
  
          /* Next Super-Block */
          SB++;
        }
      }
    }
  }


  private void decodeMVectors (int SBRows, int SBCols)
  {
    int  FragIndex;
    int  MB;
    int  SBrow;
    int  SBcol;
    int  SB=0;
    CodingMode  CodingMethod;

    ExtractMVectorComponent MVC;
  
    int  UVRow;
    int  UVColumn;
    int  UVFragOffset;
    int  x,y;
  
    int  MBListIndex = 0;
    Buffer opb = pbi.opb;

    /* Should not be decoding motion vectors if in INTRA only mode. */
    if (pbi.getFrameType() == Constants.BASE_FRAME ){
      return;
    }
    
    MotionVector dummy = new MotionVector();

    /* set the default motion vector to 0,0 */
    LastInterMV.x = 0;
    LastInterMV.y = 0;
    PriorLastInterMV.x = 0;
    PriorLastInterMV.y = 0;

    /* Read the entropy method used and set up the appropriate decode option */
    if (opb.ReadB(1) == 0 )
      MVC = MVA;
    else
      MVC = MVB;

    /* Unravel the quad-tree */
    for ( SBrow=0; SBrow<SBRows; SBrow++ ){
  
      for ( SBcol=0; SBcol<SBCols; SBcol++ ){
        for ( MB=0; MB<4; MB++ ){
          /* There may be MB's lying out of frame which must be
             ignored. For these MB's the top left block will have a
             negative Fragment. */
          FragIndex = pbi.BlockMap.QuadMapToMBTopLeft(SB, MB );
          if (FragIndex  >= 0 ) {
            /* Is the Macro-Block further coded: */
            if (pbi.MBCodedFlags[MBListIndex++] != 0){
              /* Unpack the mode (and motion vectors if necessary). */
              CodingMethod = pbi.FragCodingMethod[FragIndex];

              /* Note the coding mode and vector for each block in the
                 current macro block. */
              MotionVector MVect0 = pbi.FragMVect[FragIndex];
              MotionVector MVect1 = pbi.FragMVect[FragIndex + 1];
              MotionVector MVect2 = pbi.FragMVect[FragIndex + pbi.HFragments];
              MotionVector MVect3 = pbi.FragMVect[FragIndex + pbi.HFragments + 1];
  
              /* Matching fragments in the U and V planes */
              UVRow = (FragIndex / (pbi.HFragments << pbi.UVShiftY));
              UVColumn = (FragIndex % pbi.HFragments) >> pbi.UVShiftX;
              UVFragOffset = (UVRow * (pbi.HFragments >> pbi.UVShiftX)) + UVColumn;
  
              MotionVector MVectU0 = pbi.FragMVect[pbi.YPlaneFragments + UVFragOffset];
              MotionVector MVectV0 = pbi.FragMVect[pbi.YPlaneFragments + pbi.UVPlaneFragments + UVFragOffset];
              MotionVector MVectU1 = dummy;
              MotionVector MVectV1 = dummy;
              MotionVector MVectU2 = dummy;
              MotionVector MVectV2 = dummy;
              MotionVector MVectU3 = dummy;
              MotionVector MVectV3 = dummy;
              if (pbi.UVShiftY == 0) {
                MVectU2 = pbi.FragMVect[pbi.YPlaneFragments + UVFragOffset + (pbi.HFragments>>pbi.UVShiftX)];
                MVectV2 = pbi.FragMVect[pbi.YPlaneFragments + pbi.UVPlaneFragments + UVFragOffset + (pbi.HFragments>>pbi.UVShiftX)];
                if (pbi.UVShiftX == 0){
                  MVectU1 = pbi.FragMVect[pbi.YPlaneFragments + UVFragOffset + 1];
                  MVectV1 = pbi.FragMVect[pbi.YPlaneFragments + pbi.UVPlaneFragments + UVFragOffset + 1];                  
                  MVectU3 = pbi.FragMVect[pbi.YPlaneFragments + UVFragOffset + pbi.HFragments + 1];
                  MVectV3 = pbi.FragMVect[pbi.YPlaneFragments + pbi.UVPlaneFragments + UVFragOffset + pbi.HFragments + 1];
                }
              }
  
              /* Read the motion vector or vectors if present. */
              if (CodingMethod == CodingMode.InterPlusMv) {
                PriorLastInterMV.x = LastInterMV.x;
                PriorLastInterMV.y = LastInterMV.y;
                LastInterMV.x = MVect0.x = 
		                MVect1.x = 
				MVect2.x = 
				MVect3.x = 
				MVectU0.x = 
				MVectV0.x = 
				MVectU1.x =
				MVectV1.x =
				MVectU2.x =
				MVectV2.x =
				MVectU3.x =
				MVectV3.x = MVC.Extract(opb);
				
                LastInterMV.y = MVect0.y = 
		                MVect1.y = 
				MVect2.y = 
				MVect3.y = 
				MVectU0.y = 
				MVectV0.y = 
				MVectU1.y =
				MVectV1.y =
				MVectU2.y =
				MVectV2.y =
				MVectU3.y =
				MVectV3.y = MVC.Extract(opb);
	      }
              else if (CodingMethod == CodingMode.GoldenMv){
                MVect0.x = MVect1.x = 
		           MVect2.x = 
			   MVect3.x = 
				MVectU0.x = 
				MVectV0.x = 
				MVectU1.x =
				MVectV1.x =
				MVectU2.x =
				MVectV2.x =
				MVectU3.x =
				MVectV3.x = MVC.Extract(opb);
                MVect0.y = MVect1.y = 
		           MVect2.y = 
			   MVect3.y = 
				MVectU0.y = 
				MVectV0.y = 
				MVectU1.y =
				MVectV1.y =
				MVectU2.y =
				MVectV2.y =
				MVectU3.y =
				MVectV3.y = MVC.Extract(opb);
              }
	      else if ( CodingMethod == CodingMode.InterFourMv ){
                  
                /* Update last MV and prior last mv */
                PriorLastInterMV.x = LastInterMV.x;
                PriorLastInterMV.y = LastInterMV.y;
                
                /* Extrac the 4 Y MVs */
                if(pbi.display_fragments[FragIndex] != 0) {
                  x  = MVect0.x = MVC.Extract(opb);
                  y  = MVect0.y = MVC.Extract(opb);
                  LastInterMV.x = MVect0.x;
                  LastInterMV.y = MVect0.y;
                } else {
                  x = MVect0.x = 0;
                  y = MVect0.y = 0;
                }
                
                if(pbi.display_fragments[FragIndex + 1] != 0) {
                  x += MVect1.x = MVC.Extract(opb);
                  y += MVect1.y = MVC.Extract(opb);
                  LastInterMV.x = MVect1.x;
                  LastInterMV.y = MVect1.y;
                } else {
                  x += MVect1.x = 0;
                  y += MVect1.y = 0;
                }
                
                if(pbi.display_fragments[FragIndex + pbi.HFragments] != 0) {
                  x += MVect2.x = MVC.Extract(opb);
                  y += MVect2.y = MVC.Extract(opb);
                  LastInterMV.x = MVect2.x;
                  LastInterMV.y = MVect2.y;
                } else {
                  x += MVect2.x = 0;
                  y += MVect2.y = 0;
                }
                
                if(pbi.display_fragments[FragIndex + pbi.HFragments + 1] != 0) {
                  x += MVect3.x = MVC.Extract(opb);
                  y += MVect3.y = MVC.Extract(opb);
                  LastInterMV.x = MVect3.x;
                  LastInterMV.y = MVect3.y;
                } else {
                  x += MVect3.x = 0;
                  y += MVect3.y = 0;
                }
                
                if(pbi.UVShiftY == 0) {
                  if(pbi.UVShiftX == 0) {
                    MVectU0.x = MVectV0.x = MVect0.x;
                    MVectU0.y = MVectV0.y = MVect0.y;
                    MVectU1.x = MVectV1.x = MVect1.x;
                    MVectU1.y = MVectV1.y = MVect1.y;
                    MVectU2.x = MVectV2.x = MVect2.x;
                    MVectU2.y = MVectV2.y = MVect2.y;
                    MVectU3.x = MVectV3.x = MVect3.x;
                    MVectU3.y = MVectV3.y = MVect3.y;
                  } else {
                    /* 4:2:2, so average components only horizontally */
                    x = MVect0.x + MVect1.x;
                    if (x >= 0 ) x = (x+1) / 2;
                    else         x = (x-1) / 2;
                    MVectU0.x =
                    MVectV0.x = x;
                    y = MVect0.y + MVect1.y;
                    if (y >= 0 ) y = (y+1) / 2;
                    else         y = (y-1) / 2;
                    MVectU0.y =
                    MVectV0.y = y;
                    x = MVect2.x + MVect3.x;
                    if (x >= 0 ) x = (x+1) / 2;
                    else         x = (x-1) / 2;
                    MVectU2.x =
                    MVectV2.x = x;
                    y = MVect2.y + MVect3.y;
                    if (y >= 0 ) y = (y+1) / 2;
                    else         y = (y-1) / 2;
                    MVectU2.y =
                    MVectV2.y = y;
                  }
                } else {
                  /* Calculate the U and V plane MVs as the average of the
                     Y plane MVs. */
                  /* First .x component */
                  if (x >= 0 ) x = (x+2) / 4;
                  else         x = (x-2) / 4;
                  MVectU0.x = x;
                  MVectV0.x = x;
                  /* Then .y component */
                  if (y >= 0 ) y = (y+2) / 4;
                  else         y = (y-2) / 4;
                  MVectU0.y = y;
                  MVectV0.y = y;
                }

              }
	      else if ( CodingMethod == CodingMode.InterLastMv ){
                /* Use the last coded Inter motion vector. */
                MVect0.x = MVect1.x = 
		           MVect2.x = 
			   MVect3.x = 
				MVectU0.x = 
				MVectV0.x = 
				MVectU1.x =
				MVectV1.x =
				MVectU2.x =
				MVectV2.x =
				MVectU3.x =
				MVectV3.x = LastInterMV.x;
                MVect0.y = MVect1.y = 
		           MVect2.y = 
			   MVect3.y = 
				MVectU0.y = 
				MVectV0.y = 
				MVectU1.y =
				MVectV1.y =
				MVectU2.y =
				MVectV2.y =
				MVectU3.y =
				MVectV3.y = LastInterMV.y;
              } 
	      else if ( CodingMethod == CodingMode.InterPriorLast ){
                /* Use the next-to-last coded Inter motion vector. */
                MVect0.x = MVect1.x = 
		           MVect2.x = 
			   MVect3.x = 
			   MVectU0.x = 
				MVectV0.x = 
				MVectU1.x =
				MVectV1.x =
				MVectU2.x =
				MVectV2.x =
				MVectU3.x =
				MVectV3.x = PriorLastInterMV.x;
                MVect0.y = MVect1.y = 
		           MVect2.y = 
			   MVect3.y = 
			   MVectU0.y = 
				MVectV0.y = 
				MVectU1.y =
				MVectV1.y =
				MVectU2.y =
				MVectV2.y =
				MVectU3.y =
				MVectV3.y = PriorLastInterMV.y;
  
                /* Swap the prior and last MV cases over */
                MotionVector TmpMVect = PriorLastInterMV;
                PriorLastInterMV = LastInterMV;
                LastInterMV = TmpMVect;
              }
	      else {
                /* Clear the motion vector else */
                MVect0.x = 0;
                MVect0.y = 0;
	      }
            }
          }
        }
        /* Next Super-Block */
        SB++;
      }
    }
  }

  private int ExtractToken(Buffer opb,
                        HuffEntry CurrentRoot){
    /* Loop searches down through tree based upon bits read from the
       bitstream */
    /* until it hits a leaf at which point we have decoded a token */
    while (CurrentRoot.Value < 0 ){
      CurrentRoot = CurrentRoot.Child[(int)opb.ReadB(1)];
    }
    return CurrentRoot.Value;
  }

  private void unpackAndExpandToken(short[] ExpandedBlock,
                                    byte[] CoeffIndex,
				    int FragIndex,
				    int HuffChoice){
    int          ExtraBits = 0;

    int Token = ExtractToken(pbi.opb, pbi.HuffRoot_VP3x[HuffChoice]);

    /* Now.. if we are using the DCT optimised coding system, extract any
     *  assosciated additional bits token.
     */
    if (pbi.ExtraBitLengths_VP3x[Token] > 0){
      /* Extract the appropriate number of extra bits. */
      ExtraBits = (int)pbi.opb.ReadB(pbi.ExtraBitLengths_VP3x[Token]);
    }

    /* Take token dependant action */
    if ( Token >= Huffman.DCT_SHORT_ZRL_TOKEN ) {
      /* "Value", "zero run" and "zero run value" tokens */
      dctDecode.ExpandToken(ExpandedBlock, CoeffIndex, FragIndex, Token, ExtraBits );
      if ( CoeffIndex[FragIndex] >= Constants.BLOCK_SIZE )
        BlocksToDecode --;
    }else{
      /* Special action and EOB tokens */
      switch ( Token ){
      case Huffman.DCT_EOB_PAIR_TOKEN:
        EOB_Run = 1;
        break;
      case Huffman.DCT_EOB_TRIPLE_TOKEN:
        EOB_Run = 2;
        break;
      case Huffman.DCT_REPEAT_RUN_TOKEN:
        EOB_Run = ExtraBits + 3;
        break;
      case Huffman.DCT_REPEAT_RUN2_TOKEN:
        EOB_Run = ExtraBits + 7;
        break;
      case Huffman.DCT_REPEAT_RUN3_TOKEN:
        EOB_Run = ExtraBits + 15;
        break;
      case Huffman.DCT_REPEAT_RUN4_TOKEN:
        EOB_Run = ExtraBits - 1;
        break;
      case Huffman.DCT_EOB_TOKEN:
        break;
      default:
        return;
      }
      CoeffIndex[FragIndex] = (byte)Constants.BLOCK_SIZE;
      BlocksToDecode --;
    }
  }

  private void unPackVideo ()
  {
    int       EncodedCoeffs = 1;
    int       FragIndex;

    int     AcHuffChoice;
    int     AcHuffChoice1;
    int     AcHuffChoice2;

    int     DcHuffChoice;

    /* Bail out immediately if a decode error has already been reported. */
    if ( pbi.DecoderErrorCode != 0) 
      return;

    /* Clear down the array that indicates the current coefficient index
       for each block. */
    MemUtils.Set(FragCoeffs, 0, 0, pbi.UnitFragments);
    MemUtils.Set(pbi.FragCoefEOB, 0, 0, pbi.UnitFragments);

    /* Note the number of blocks to decode */
    BlocksToDecode = pbi.CodedBlockIndex;

    /* Get the DC huffman table choice for Y and then UV */
    int DcHuffChoice1 = (int)(pbi.opb.ReadB(Huffman.DC_HUFF_CHOICE_BITS) + Huffman.DC_HUFF_OFFSET);
    int DcHuffChoice2 = (int)(pbi.opb.ReadB(Huffman.DC_HUFF_CHOICE_BITS) + Huffman.DC_HUFF_OFFSET);

    /* UnPack DC coefficients / tokens */
    int cbl = 0;
    int cble = pbi.CodedBlockIndex;
    while (cbl < cble) {
      /* Get the block data index */
      FragIndex = pbi.CodedBlockList[cbl];
      pbi.FragCoefEOB[FragIndex] = FragCoeffs[FragIndex];

      /* Select the appropriate huffman table offset according to
         whether the token is from a Y or UV block */
      if (FragIndex < (int)pbi.YPlaneFragments )
        DcHuffChoice = DcHuffChoice1;
      else
        DcHuffChoice = DcHuffChoice2;

      /* If we are in the middle of an EOB run */
      if ( EOB_Run != 0){
        /* Mark the current block as fully expanded and decrement
           EOB_RUN count */
        FragCoeffs[FragIndex] = Constants.BLOCK_SIZE;
        EOB_Run --;
        BlocksToDecode --;
      }else{
        /* Else unpack a DC token */
        unpackAndExpandToken(pbi.QFragData[FragIndex],
                             FragCoeffs,
  			     FragIndex,
		 	     DcHuffChoice);
      }
      cbl++;
    }

    /* Get the AC huffman table choice for Y and then for UV. */
    int AcHuffIndex1 = (int) (pbi.opb.ReadB(Huffman.AC_HUFF_CHOICE_BITS) + Huffman.AC_HUFF_OFFSET);
    int AcHuffIndex2 = (int) (pbi.opb.ReadB(Huffman.AC_HUFF_CHOICE_BITS) + Huffman.AC_HUFF_OFFSET);

    /* Unpack Lower AC coefficients. */
    while ( EncodedCoeffs < 64 ) {
      /* Repeatedly scan through the list of blocks. */
      cbl = 0;
      cble = pbi.CodedBlockIndex;

      /* Huffman table selection based upon which AC coefficient we are on */
      if ( EncodedCoeffs <= Huffman.AC_TABLE_2_THRESH ){
        AcHuffChoice1 = AcHuffIndex1;
        AcHuffChoice2 = AcHuffIndex2;
      }else if ( EncodedCoeffs <= Huffman.AC_TABLE_3_THRESH ){
        AcHuffChoice1 = (AcHuffIndex1 + Huffman.AC_HUFF_CHOICES);
        AcHuffChoice2 = (AcHuffIndex2 + Huffman.AC_HUFF_CHOICES);
      } else if ( EncodedCoeffs <= Huffman.AC_TABLE_4_THRESH ){
        AcHuffChoice1 = (AcHuffIndex1 + (Huffman.AC_HUFF_CHOICES * 2));
        AcHuffChoice2 = (AcHuffIndex2 + (Huffman.AC_HUFF_CHOICES * 2));
      } else {
        AcHuffChoice1 = (AcHuffIndex1 + (Huffman.AC_HUFF_CHOICES * 3));
        AcHuffChoice2 = (AcHuffIndex2 + (Huffman.AC_HUFF_CHOICES * 3));
      }

      while(cbl < cble ) {
        /* Get the linear index for the current fragment. */
        FragIndex = pbi.CodedBlockList[cbl];

        /* Should we decode a token for this block on this pass. */
        if ( FragCoeffs[FragIndex] <= EncodedCoeffs ) {
          pbi.FragCoefEOB[FragIndex] = FragCoeffs[FragIndex];
          /* If we are in the middle of an EOB run */
          if ( EOB_Run != 0) {
            /* Mark the current block as fully expanded and decrement
               EOB_RUN count */
            FragCoeffs[FragIndex] = (byte)Constants.BLOCK_SIZE;
            EOB_Run --;
            BlocksToDecode --;
          }else{
            /* Else unpack an AC token */
            /* Work out which huffman table to use, then decode a token */
            if ( FragIndex < (int)pbi.YPlaneFragments )
              AcHuffChoice = AcHuffChoice1;
            else
              AcHuffChoice = AcHuffChoice2;
  
            unpackAndExpandToken(pbi.QFragData[FragIndex],
                                 FragCoeffs,
  				 FragIndex,
				 AcHuffChoice);
          }
        }
        cbl++;
      }
  
      /* Test for condition where there are no blocks left with any
         tokesn to decode */
      if ( BlocksToDecode == 0)
        break;
  
      EncodedCoeffs ++;
    }
  }
  
  internal int loadAndDecode()
  {
    int    loadFrameOK;

    /* Load the next frame. */
    loadFrameOK = loadFrame();
  
    if (loadFrameOK != 0){
    //System.out.println("Load: "+loadFrameOK+" "+pbi.ThisFrameQualityValue+" "+pbi.LastFrameQualityValue);
 
      /* Decode the data into the fragment buffer. */
      /* Bail out immediately if a decode error has already been reported. */
      if (pbi.DecoderErrorCode != 0) 
        return 0;

      /* Zero Decoder EOB run count */
      EOB_Run = 0;

      /* Make a note of the number of coded blocks this frame */
      pbi.CodedBlocksThisFrame = pbi.CodedBlockIndex;

      /* Decode the modes data */
      decodeModes(pbi.YSBRows, pbi.YSBCols);

      /* Unpack and decode the motion vectors. */
      decodeMVectors (pbi.YSBRows, pbi.YSBCols);
      
      /* Unpack per-block quantizer information */
      decodeBlockLevelQi();
      
      /* Unpack and decode the actual video data. */
      unPackVideo();

      /* Reconstruct and display the frame */
      dctDecode.ReconRefFrames(pbi);

      return 0;
    }
  
    return (int)(Result.BadPacket);
  }
}