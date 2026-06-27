/*
 * Collapsed Huffman lookup tree for Theora.
 *
 * The table layout and collapse heuristic are adapted from
 * Xiph.Org libtheora/lib/huffdec.c.
 *
 * Copyright (C) 2002-2009, 2025 Xiph.Org Foundation and contributors
 * See THIRD_PARTY_NOTICES.md for the BSD-style license.
 */

using Buffer = TheoraSharp.Ogg.Buffer;

namespace TheoraSharp.Theora;

internal sealed class PackedHuffmanTree
{
    private const int MaxStackDepth = 34;
    private const int HuffSlush = 2;
    private const int RootHuffSlush = 7;

    private readonly short[] nodes;

    private PackedHuffmanTree(short[] nodes)
    {
        this.nodes = nodes;
    }

    internal int Decode(Buffer opb)
    {
        var node = 0;

        while (true)
        {
            int bitCount = nodes[node];
            var bits = opb.LookB(bitCount);
            int child = nodes[node + 1 + bits];

            if (child <= 0)
            {
                var leaf = -child;
                var consumedBits = leaf >> 8;

                if (!opb.CanReadBits(consumedBits))
                    return -1;

                opb.Adv(consumedBits);
                return leaf & 0xFF;
            }

            if (!opb.CanReadBits(bitCount))
                return -1;

            opb.Adv(bitCount);
            node = child;
        }
    }

    internal static int Read(Buffer opb, out PackedHuffmanTree tree)
    {
        tree = null;

        Span<Token> tokens = stackalloc Token[Huffman.MAX_ENTROPY_TOKENS];
        var tokenCount = UnpackCodebook(opb, tokens);
        if (tokenCount < 0)
        {
            return tokenCount;
        }

        ReadOnlySpan<Token> usedTokens = tokens[..tokenCount];

        var treeSize = Collapse(usedTokens, Span<short>.Empty);
        if (treeSize is <= 0 or > short.MaxValue)
        {
            return (int)Result.Impl;
        }

        var packedNodes = new short[treeSize];
        var writtenSize = Collapse(usedTokens, packedNodes);
        if (writtenSize != treeSize)
        {
            return (int)Result.Fault;
        }

        tree = new PackedHuffmanTree(packedNodes);
        return 0;
    }

    private static int UnpackCodebook(Buffer opb, Span<Token> tokens)
    {
        uint code = 0;
        var depth = 0;
        var tokenCount = 0;
        var leafCount = 0;

        while (true)
        {
            var isLeaf = opb.ReadB(1);
            if (isLeaf < 0)
            {
                return (int)Result.BadHeader;
            }

            if (isLeaf == 0)
            {
                depth++;
                if (depth > 32)
                {
                    return (int)Result.BadHeader;
                }

                continue;
            }

            if (++leafCount > Huffman.MAX_ENTROPY_TOKENS)
            {
                return (int)Result.BadHeader;
            }

            var token = opb.ReadB(5);
            if ((uint)token >= Huffman.MAX_ENTROPY_TOKENS)
            {
                return (int)Result.BadHeader;
            }

            tokens[tokenCount++] = new Token((byte)token, (byte)depth);

            if (depth <= 0)
            {
                break;
            }

            var codeBit = 0x80000000U >> (depth - 1);

            while (depth > 0 && (code & codeBit) != 0)
            {
                code ^= codeBit;
                codeBit <<= 1;
                depth--;
            }

            if (depth <= 0)
            {
                break;
            }

            code |= codeBit;
        }

        return tokenCount;
    }

    private static int Collapse(ReadOnlySpan<Token> tokens, Span<short> tree)
    {
        Span<int> node = stackalloc int[MaxStackDepth];
        Span<int> depth = stackalloc int[MaxStackDepth];
        Span<int> last = stackalloc int[MaxStackDepth];

        var writeTree = !tree.IsEmpty;
        var treeSize = 0;
        var tokenIndex = 0;
        var level = 0;

        depth[0] = 0;
        last[0] = tokens.Length - 1;

        do
        {
            var nodeTokenCount = last[level] + 1 - tokenIndex;
            var lookupBits = GetCollapseDepth(tokens.Slice(tokenIndex, nodeTokenCount), depth[level]);

            node[level] = treeSize;
            treeSize += GetNodeSize(lookupBits);

            if (writeTree)
            {
                tree[node[level]++] = checked((short)lookupBits);
            }

            do
            {
                while (tokenIndex <= last[level] && tokens[tokenIndex].Length <= depth[level] + lookupBits)
                {
                    if (writeTree)
                    {
                        var duplicateCount = 1 << (depth[level] + lookupBits - tokens[tokenIndex].Length);
                        var leaf = ((tokens[tokenIndex].Length - depth[level]) << 8) | tokens[tokenIndex].Value;
                        var encodedLeaf = checked((short)-leaf);

                        while (duplicateCount-- > 0)
                        {
                            tree[node[level]++] = encodedLeaf;
                        }
                    }

                    tokenIndex++;
                }

                if (tokenIndex <= last[level])
                {
                    depth[level + 1] = depth[level] + lookupBits;

                    if (writeTree)
                    {
                        tree[node[level]++] = checked((short)treeSize);
                    }

                    level++;
                    last[level] = tokenIndex + CountSubtreeTokens(tokens[tokenIndex..], depth[level]) - 1;

                    break;
                }

                if (--level >= 0)
                {
                    lookupBits = depth[level + 1] - depth[level];
                }
            }
            while (level >= 0);
        }
        while (level >= 0);

        return treeSize;
    }

    private static int GetCollapseDepth(ReadOnlySpan<Token> tokens, int depth)
    {
        var slush = depth > 0 ? HuffSlush : RootHuffSlush;
        var lookupBits = 1;
        var occupancy = 2;
        var gotLeaves = true;
        var bestLookupBits = 1;

        while (true)
        {
            if (gotLeaves)
            {
                bestLookupBits = lookupBits;
            }

            lookupBits++;
            gotLeaves = false;

            var previousOccupancy = occupancy;
            occupancy = 0;
            var tokenIndex = 0;

            while (tokenIndex < tokens.Length)
            {
                int tokenLength = tokens[tokenIndex].Length;

                if (tokenLength < depth + lookupBits)
                {
                    tokenIndex++;
                }
                else if (tokenLength == depth + lookupBits)
                {
                    gotLeaves = true;
                    tokenIndex++;
                }
                else
                {
                    tokenIndex += CountSubtreeTokens(tokens[tokenIndex..], depth + lookupBits);
                }

                occupancy++;
            }

            if (occupancy <= previousOccupancy || occupancy * slush < (1 << lookupBits))
            {
                return bestLookupBits;
            }
        }
    }

    private static int CountSubtreeTokens(ReadOnlySpan<Token> tokens, int depth)
    {
        uint code = 0;
        var tokenIndex = 0;

        do
        {
            var delta = tokens[tokenIndex].Length - depth;

            if (delta < 32)
            {
                code = unchecked(code + (0x80000000U >> delta));
                tokenIndex++;
            }
            else
            {
                code = unchecked(code + 1);
                tokenIndex += CountSubtreeTokens(tokens[tokenIndex..], depth + 31);
            }
        }
        while (code < 0x80000000U);

        return tokenIndex;
    }

    private static int GetNodeSize(int lookupBits)
    {
        return 1 + (1 << lookupBits);
    }

    private readonly struct Token
    {
        internal Token(byte value, byte length)
        {
            Value = value;
            Length = length;
        }

        internal byte Value { get; }
        internal byte Length { get; }
    }
}
