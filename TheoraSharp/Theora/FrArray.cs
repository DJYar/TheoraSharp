﻿using TheoraSharp.Utils;
using Buffer = TheoraSharp.Ogg.Buffer;

namespace TheoraSharp.Theora;

internal class FrArray
{

  private int   bit_pattern;
  private byte  bits_so_far;
  private byte  NextBit;
  private int   BitsLeft;

  internal FrArray() {
  }

  internal void init() {
    /* Initialise the decoding of a run.  */
    bit_pattern = 0;
    bits_so_far = 0;
  }

  private int deCodeBlockRun(int bit_value){
    /* Add in the new bit value. */
    bits_so_far++;
    bit_pattern = (bit_pattern << 1) + (bit_value & 1);

    /* Coding scheme:
       Codeword           RunLength
       0x                    1-2
       10x                   3-4
       110x                  5-6
       1110xx                7-10
       11110xx              11-14
       11111xxxx            15-30
    */

    switch ( bits_so_far ){
    case 2:
      /* If bit 1 is clear */
      if ((bit_pattern & 0x0002) == 0){
        BitsLeft = (bit_pattern & 0x0001) + 1;
        return 1;
      }
      break;
  
    case 3:
      /* If bit 1 is clear */
      if ((bit_pattern & 0x0002) == 0){
        BitsLeft = (bit_pattern & 0x0001) + 3;
        return 1;
      }
      break;
  
    case 4:
      /* If bit 1 is clear */
      if ((bit_pattern & 0x0002) == 0){
        BitsLeft = (bit_pattern & 0x0001) + 5;
        return 1;
      }
      break;
  
    case 6:
      /* If bit 2 is clear */
      if ((bit_pattern & 0x0004) == 0){
        BitsLeft = (bit_pattern & 0x0003) + 7;
        return 1;
      }
      break;
  
    case 7:
      /* If bit 2 is clear */
      if ((bit_pattern & 0x0004) == 0){
        BitsLeft = (bit_pattern & 0x0003) + 11;
        return 1;
      }
      break;
  
    case 9:
      BitsLeft = (bit_pattern & 0x000F) + 15;
      return 1;
    }
  
    return 0;
  }

  private int deCodeSBRun (int bit_value){
    /* Add in the new bit value. */
    bits_so_far++;
    bit_pattern = (bit_pattern << 1) + (bit_value & 1);

    /* Coding scheme:
       Codeword            RunLength
       0                       1
       10x                    2-3
       110x                   4-5
       1110xx                 6-9
       11110xxx              10-17
       111110xxxx            18-33
       111111xxxxxxxxxxxx    34-4129
    */

    switch ( bits_so_far ){
    case 1:
      if (bit_pattern == 0 ){
        BitsLeft = 1;
        return 1;
      }
      break;

    case 3:
      /* Bit 1 clear */
      if ((bit_pattern & 0x0002) == 0){
        BitsLeft = (bit_pattern & 0x0001) + 2;
        return 1;
      }
      break;

    case 4:
      /* Bit 1 clear */
      if ((bit_pattern & 0x0002) == 0){
        BitsLeft = (bit_pattern & 0x0001) + 4;
        return 1;
      }
      break;

    case 6:
      /* Bit 2 clear */
      if ((bit_pattern & 0x0004) == 0){
        BitsLeft = (bit_pattern & 0x0003) + 6;
        return 1;
      }
      break;

    case 8:
      /* Bit 3 clear */
      if ((bit_pattern & 0x0008) == 0){
        BitsLeft = (bit_pattern & 0x0007) + 10;
        return 1;
      }
      break;

    case 10:
      /* Bit 4 clear */
      if ((bit_pattern & 0x0010) == 0){
        BitsLeft = (bit_pattern & 0x000F) + 18;
        return 1;
      }
      break;

    case 18:
      BitsLeft = (bit_pattern & 0x0FFF) + 34;
      return 1;
    }
    return 0;
  }

  private void getNextBInit(Buffer opb){
    long ret;

    ret = opb.ReadB(1);
    NextBit = (byte)ret;

    /* Read run length */
    init();
    do {
      ret = opb.ReadB(1);
    }  
    while (deCodeBlockRun((int)ret)==0);
  }

  private byte getNextBBit (Buffer opb){
    long ret;
    if (BitsLeft == 0){
      /* Toggle the value.   */
      NextBit = (byte) (NextBit ^ 1);
  
      /* Read next run */
      init();
      do {
        ret = opb.ReadB(1);
      }
      while (deCodeBlockRun((int)ret)==0);

    }

    /* Have  read a bit */
    BitsLeft--;

    /* Return next bit value */
    return NextBit;
  }

  private void getNextSbInit(Buffer opb){
    long ret;

    ret = opb.ReadB(1);
    NextBit = (byte)ret;

    /* Read run length */
    init();
    do {
      ret = opb.ReadB(1);
    }
    while (deCodeSBRun((int)ret)==0);

  }

  private byte getNextSbBit (Buffer opb){
    long ret;

    if (BitsLeft == 0){
      /* Toggle the value.   */
      NextBit = (byte) (NextBit ^ 1);
  
      /* Read next run */
      init();
      do {
        ret = opb.ReadB(1);
      }
      while (deCodeSBRun((int)ret)==0);

    }

    /* Have  read a bit */
    BitsLeft--;

    /* Return next bit value */
    return NextBit;
  }

  private short[] empty64 = new short[64];
  internal void quadDecodeDisplayFragments ( Playback pbi ){
    int  SB, MB, B;
    int    DataToDecode;

    int   dfIndex;
    int  MBIndex = 0;
    Buffer opb = pbi.opb;

    /* Reset various data structures common to key frames and inter frames. */
    pbi.CodedBlockIndex = 0;
    MemUtils.Set ( pbi.display_fragments, 0, 0, pbi.UnitFragments );

    /* For "Key frames" mark all blocks as coded and return. */
    /* Else initialise the ArrayPtr array to 0 (all blocks uncoded by default) */
    if ( pbi.getFrameType() == Constants.BASE_FRAME ) {
      MemUtils.Set( pbi.SBFullyFlags, 0, 1, pbi.SuperBlocks );
      MemUtils.Set( pbi.SBCodedFlags, 0, 1, pbi.SuperBlocks );
      MemUtils.Set( pbi.MBCodedFlags, 0, 0, pbi.MacroBlocks );
    }else{
      MemUtils.Set( pbi.SBFullyFlags, 0, 0, pbi.SuperBlocks );
      MemUtils.Set( pbi.MBCodedFlags, 0, 0, pbi.MacroBlocks );
  
      /* Un-pack the list of partially coded Super-Blocks */
      getNextSbInit(opb);
      for( SB = 0; SB < pbi.SuperBlocks; SB++){
        pbi.SBCodedFlags[SB] = getNextSbBit (opb);
      }

      /* Scan through the list of super blocks.  Unless all are marked
         as partially coded we have more to do. */
      DataToDecode = 0;
      for ( SB=0; SB<pbi.SuperBlocks; SB++ ) {
        if (pbi.SBCodedFlags[SB] == 0) {
          DataToDecode = 1;
          break;
        }
      }

      /* Are there further block map bits to decode ? */
      if (DataToDecode != 0) {
        /* Un-pack the Super-Block fully coded flags. */
        getNextSbInit(opb);
        for( SB = 0; SB < pbi.SuperBlocks; SB++) {
          /* Skip blocks already marked as partially coded */
          while( (SB < pbi.SuperBlocks) && (pbi.SBCodedFlags[SB] != 0 ))
            SB++;
  
          if (SB < pbi.SuperBlocks) {
            pbi.SBFullyFlags[SB] = getNextSbBit (opb);
  
            if (pbi.SBFullyFlags[SB] != 0)       /* If SB is fully coded. */
              pbi.SBCodedFlags[SB] = 1;       /* Mark the SB as coded */
          }
        }
      }
  
      /* Scan through the list of coded super blocks.  If at least one
         is marked as partially coded then we have a block list to
         decode. */
      for ( SB=0; SB<pbi.SuperBlocks; SB++ ) {
        if ((pbi.SBCodedFlags[SB] != 0) && (pbi.SBFullyFlags[SB] == 0)) {
          /* Initialise the block list decoder. */
          getNextBInit(opb);
          break;
        }
      }
    }

    /* Decode the block data from the bit stream. */
    for ( SB=0; SB<pbi.SuperBlocks; SB++ ){
      for ( MB=0; MB<4; MB++ ){
        /* If MB is in the frame */
        if (pbi.BlockMap.QuadMapToMBTopLeft(SB,MB) >= 0 ){
          /* Only read block level data if SB was fully or partially coded */
          if (pbi.SBCodedFlags[SB] != 0) {
            for ( B=0; B<4; B++ ){
              /* If block is valid (in frame)... */
              dfIndex = pbi.BlockMap.QuadMapToIndex1(SB, MB, B);
              if ( dfIndex >= 0 ){
                if ( pbi.SBFullyFlags[SB] != 0)
                  pbi.display_fragments[dfIndex] = 1;
                else
                  pbi.display_fragments[dfIndex] = getNextBBit(opb);
  
                /* Create linear list of coded block indices */
                if ( pbi.display_fragments[dfIndex] != 0) {
                  pbi.MBCodedFlags[MBIndex] = 1;
                  pbi.CodedBlockList[pbi.CodedBlockIndex] = dfIndex;
                  /* Clear down the pbi.QFragData structure for this coded block. */
                  Array.Copy(empty64, 0, pbi.QFragData[dfIndex], 0, 64);
                  pbi.CodedBlockIndex++;
                }
              }
            }
          }
          MBIndex++;
  
        }
      }
    }
  }
  
  internal CodingMode unpackMode(Buffer opb){
    /* Coding scheme:
       Token                      Codeword           Bits
       Entry   0 (most frequent)  0                   1
       Entry   1                  10                  2
       Entry   2                  110                 3
       Entry   3                  1110                4
       Entry   4                  11110               5
       Entry   5                  111110              6
       Entry   6                  1111110             7
       Entry   7                  1111111             7
    */

    /* Initialise the decoding. */
    bits_so_far = 0;

    bit_pattern = (int) opb.ReadB(1);

    /* Do we have a match */
    if ( bit_pattern == 0 )
      return Enum.GetValues<CodingMode>()[0];
  
    /* Get the next bit */
    bit_pattern = (bit_pattern << 1) | (int)opb.ReadB(1);
  
    /* Do we have a match */
    if ( bit_pattern == 0x0002 )
      return Enum.GetValues<CodingMode>()[1];
  
    bit_pattern = (bit_pattern << 1) | (int)opb.ReadB(1);
  
    /* Do we have a match  */
    if ( bit_pattern == 0x0006 )
      return Enum.GetValues<CodingMode>()[2];
  
    bit_pattern = (bit_pattern << 1) | (int)opb.ReadB(1);
  
    /* Do we have a match */
    if ( bit_pattern == 0x000E )
      return Enum.GetValues<CodingMode>()[3];
  
    bit_pattern = (bit_pattern << 1) | (int)opb.ReadB(1);
  
    /* Do we have a match */
    if ( bit_pattern == 0x001E )
      return Enum.GetValues<CodingMode>()[4];
  
    bit_pattern = (bit_pattern << 1) | (int)opb.ReadB(1);
  
    /* Do we have a match */
    if ( bit_pattern == 0x003E )
      return Enum.GetValues<CodingMode>()[5];
  
    bit_pattern = (bit_pattern << 1) | (int)opb.ReadB(1);
  
    /* Do we have a match */
    if ( bit_pattern == 0x007E )
      return Enum.GetValues<CodingMode>()[6];
    else
      return Enum.GetValues<CodingMode>()[7];
  }
}