using Buffer = TheoraSharp.Ogg.Buffer;

namespace TheoraSharp.Theora;

internal class Huffman
{
  internal const int NUM_HUFF_TABLES        = 80;
  internal const int DC_HUFF_OFFSET         = 0;
  internal const int AC_HUFF_OFFSET         = 16;
  internal const int AC_TABLE_2_THRESH      = 5;
  internal const int AC_TABLE_3_THRESH      = 14;
  internal const int AC_TABLE_4_THRESH      = 27;

  internal const int DC_HUFF_CHOICES        = 16;
  internal const int DC_HUFF_CHOICE_BITS    = 4;

  internal const int AC_HUFF_CHOICES        = 16;
  internal const int AC_HUFF_CHOICE_BITS    = 4;

/* Constants assosciated with entropy tokenisation. */
  internal const int MAX_SINGLE_TOKEN_VALUE = 6;
  internal const int DCT_VAL_CAT2_MIN       = 3;
  internal const int DCT_VAL_CAT3_MIN       = 7;
  internal const int DCT_VAL_CAT4_MIN       = 9;
  internal const int DCT_VAL_CAT5_MIN       = 13;
  internal const int DCT_VAL_CAT6_MIN       = 21;
  internal const int DCT_VAL_CAT7_MIN       = 37;
  internal const int DCT_VAL_CAT8_MIN       = 69;

  internal const int DCT_EOB_TOKEN          = 0;
  internal const int DCT_EOB_PAIR_TOKEN     = 1;
  internal const int DCT_EOB_TRIPLE_TOKEN   = 2;
  internal const int DCT_REPEAT_RUN_TOKEN   = 3;
  internal const int DCT_REPEAT_RUN2_TOKEN  = 4;
  internal const int DCT_REPEAT_RUN3_TOKEN  = 5;
  internal const int DCT_REPEAT_RUN4_TOKEN  = 6;

  internal const int DCT_SHORT_ZRL_TOKEN    = 7;
  internal const int DCT_ZRL_TOKEN          = 8;

  internal const int ONE_TOKEN              = 9;       /* Special tokens for -1,1,-2,2 */
  internal const int MINUS_ONE_TOKEN        = 10;
  internal const int TWO_TOKEN              = 11;
  internal const int MINUS_TWO_TOKEN        = 12;

  internal const int LOW_VAL_TOKENS         = (MINUS_TWO_TOKEN + 1);
  internal const int DCT_VAL_CATEGORY3      = (LOW_VAL_TOKENS + 4);
  internal const int DCT_VAL_CATEGORY4      = (DCT_VAL_CATEGORY3 + 1);
  internal const int DCT_VAL_CATEGORY5      = (DCT_VAL_CATEGORY4 + 1);
  internal const int DCT_VAL_CATEGORY6      = (DCT_VAL_CATEGORY5 + 1);
  internal const int DCT_VAL_CATEGORY7      = (DCT_VAL_CATEGORY6 + 1);
  internal const int DCT_VAL_CATEGORY8      = (DCT_VAL_CATEGORY7 + 1);

  internal const int DCT_RUN_CATEGORY1      = (DCT_VAL_CATEGORY8 + 1);
  internal const int DCT_RUN_CATEGORY1B     = (DCT_RUN_CATEGORY1 + 5);
  internal const int DCT_RUN_CATEGORY1C     = (DCT_RUN_CATEGORY1B + 1);
  internal const int DCT_RUN_CATEGORY2      = (DCT_RUN_CATEGORY1C + 1);

/* 32 */
  internal const int MAX_ENTROPY_TOKENS     = (DCT_RUN_CATEGORY2 + 2);

  private static void createHuffmanList (HuffEntry[] huffRoot,
                              int hIndex, short[] freqList) {
    HuffEntry entry_ptr;
    HuffEntry search_ptr;

    /* Create a HUFF entry for token zero. */
    huffRoot[hIndex] = new HuffEntry();
    huffRoot[hIndex].Previous = null;
    huffRoot[hIndex].Next = null;
    huffRoot[hIndex].Child[0] = null;
    huffRoot[hIndex].Child[1] = null;
    huffRoot[hIndex].Value = 0;
    huffRoot[hIndex].Frequency = freqList[0];

    if ( huffRoot[hIndex].Frequency == 0 )
      huffRoot[hIndex].Frequency = 1;

    /* Now add entries for all the other possible tokens. */
    for (int i = 1; i < Huffman.MAX_ENTROPY_TOKENS; i++) {
      entry_ptr = new HuffEntry();
      entry_ptr.Value = i;
      entry_ptr.Frequency = freqList[i];
      entry_ptr.Child[0] = null;
      entry_ptr.Child[1] = null;
  
      /* Force min value of 1. This prevents the tree getting too deep. */
      if ( entry_ptr.Frequency == 0 )
        entry_ptr.Frequency = 1;
  
      if ( entry_ptr.Frequency <= huffRoot[hIndex].Frequency ){
        entry_ptr.Next = huffRoot[hIndex];
        huffRoot[hIndex].Previous = entry_ptr;
        entry_ptr.Previous = null;
        huffRoot[hIndex] = entry_ptr;
      }
      else
      {
        search_ptr = huffRoot[hIndex];
        while ( (search_ptr.Next != null) &&
                (search_ptr.Frequency < entry_ptr.Frequency) ){
          search_ptr = search_ptr.Next;
        }
  
        if ( search_ptr.Frequency < entry_ptr.Frequency ){
          entry_ptr.Next = null;
          entry_ptr.Previous = search_ptr;
          search_ptr.Next = entry_ptr;
        } 
	else
	{
          entry_ptr.Next = search_ptr;
          entry_ptr.Previous = search_ptr.Previous;
          search_ptr.Previous.Next = entry_ptr;
          search_ptr.Previous = entry_ptr;
        }
      }
    }
  }

  private static void createCodeArray (HuffEntry huffRoot,
                      int[] huffCodeArray,
                      byte[] huffCodeLengthArray,
                      int codeValue,
                      byte codeLength) 
  {
    /* If we are at a leaf then fill in a code array entry. */
    if ((huffRoot.Child[0] == null) && (huffRoot.Child[1] == null)) {
      huffCodeArray[huffRoot.Value] = codeValue;
      huffCodeLengthArray[huffRoot.Value] = codeLength;
    } else {
      /* Recursive calls to scan down the tree. */
      codeLength++;
      createCodeArray(huffRoot.Child[0], huffCodeArray, huffCodeLengthArray,
                      ((codeValue << 1) + 0), codeLength);
      createCodeArray(huffRoot.Child[1], huffCodeArray, huffCodeLengthArray,
                      ((codeValue << 1) + 1), codeLength);
    }
  }

  internal static void buildHuffmanTree (HuffEntry[] huffRoot,
                        int[] huffCodeArray,
                        byte[] huffCodeLengthArray,
                        int hIndex,
                        short[] freqList )
  {
    HuffEntry entry_ptr;
    HuffEntry search_ptr;

    /* First create a sorted linked list representing the frequencies of
       each token. */
    createHuffmanList( huffRoot, hIndex, freqList );

    /* Now build the tree from the list. */

    /* While there are at least two items left in the list. */
    while ( huffRoot[hIndex].Next != null ){
      /* Create the new node as the parent of the first two in the list. */
      entry_ptr = new HuffEntry();
      entry_ptr.Value = -1;
      entry_ptr.Frequency = huffRoot[hIndex].Frequency +
        huffRoot[hIndex].Next.Frequency ;
      entry_ptr.Child[0] = huffRoot[hIndex];
      entry_ptr.Child[1] = huffRoot[hIndex].Next;

      /* If there are still more items in the list then insert the new
         node into the list. */
      if (entry_ptr.Child[1].Next != null ){
        /* Set up the provisional 'new root' */
        huffRoot[hIndex] = entry_ptr.Child[1].Next;
        huffRoot[hIndex].Previous = null;

        /* Now scan through the remaining list to insert the new entry
           at the appropriate point. */
        if ( entry_ptr.Frequency <= huffRoot[hIndex].Frequency ){
          entry_ptr.Next = huffRoot[hIndex];
          huffRoot[hIndex].Previous = entry_ptr;
          entry_ptr.Previous = null;
          huffRoot[hIndex] = entry_ptr;
        }else{
          search_ptr = huffRoot[hIndex];
          while ( (search_ptr.Next != null) &&
                (search_ptr.Frequency < entry_ptr.Frequency) ){
            search_ptr = search_ptr.Next;
          }

          if ( search_ptr.Frequency < entry_ptr.Frequency ){
            entry_ptr.Next = null;
            entry_ptr.Previous = search_ptr;
            search_ptr.Next = entry_ptr;
          }else{
            entry_ptr.Next = search_ptr;
            entry_ptr.Previous = search_ptr.Previous;
            search_ptr.Previous.Next = entry_ptr;
            search_ptr.Previous = entry_ptr;
          }
        }
      }else{
        /* Build has finished. */
        entry_ptr.Next = null;
        entry_ptr.Previous = null;
        huffRoot[hIndex] = entry_ptr;
      }

      /* Delete the next/previous properties of the children (PROB NOT NEC). */
      entry_ptr.Child[0].Next = null;
      entry_ptr.Child[0].Previous = null;
      entry_ptr.Child[1].Next = null;
      entry_ptr.Child[1].Previous = null;

    }

    /* Now build a code array from the tree. */
    createCodeArray( huffRoot[hIndex], huffCodeArray,
                     huffCodeLengthArray, 0, (byte)0);
  }

  internal static int readHuffmanTrees(HuffEntry[] huffRoot, Buffer opb) {
    int i;
    for (i=0; i<NUM_HUFF_TABLES; i++) {
       int ret;
       huffRoot[i] = new HuffEntry();
       ret = huffRoot[i].Read(0, opb);
       if (ret != 0) 
         return ret;
    }
    return 0;
  }

  internal static void clearHuffmanTrees(HuffEntry[] huffRoot){
    int i;
    for(i=0; i<Huffman.NUM_HUFF_TABLES; i++) {
      huffRoot[i] = null;
    }
  }
}