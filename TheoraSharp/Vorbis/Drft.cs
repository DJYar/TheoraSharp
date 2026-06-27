namespace TheoraSharp.Vorbis;

internal class Drft
{
    private static readonly int[] FactorTrialValues = { 4, 2, 3, 5 };
    private const float TwoPi = 6.28318530717958647692528676655900577f;
    private const float HalfSqrt2 = .70710678118654752440084436210485f;
    private const float Sqrt3Over2 = .86602540378443864676372317075293618f;
    private const float MinusHalf = -.5f;
    private const float Sqrt2 = 1.4142135623730950488016887242097f;

    private int _n;
    private float[] _trigCache;
    private int[] _splitCache;

    public void Backward(float[] data)
    {
        if (_n == 1)
        {
            return;
        }

        BackwardTransformInternal(_n, data, _trigCache, _trigCache, _n, _splitCache);
    }

    public void Initialize(int size)
    {
        _n = size;
        _trigCache = new float[3 * size];
        _splitCache = new int[32];
        InitializeWorkspace(size, _trigCache, _splitCache);
    }

    public void Clear()
    {
        _trigCache = null;
        _splitCache = null;
    }

    private static void InitializeFactorCache(int size, float[] twiddleFactors, int twiddleBaseOffset, int[] factorCache)
    {
        var trialFactor = 0;
        var radixIndex = -1;
        var remainingSize = size;
        var factorCount = 0;
        var state = 101;

        while (true)
        {
            int innerIndex;
            switch (state)
            {
                case 101:
                    radixIndex++;
                    if (radixIndex < 4)
                    {
                        trialFactor = FactorTrialValues[radixIndex];
                    }
                    else
                    {
                        trialFactor += 2;
                    }

                    goto case 104;
                case 104:
                    var quotient = remainingSize / trialFactor;
                    var remainder = remainingSize - trialFactor * quotient;
                    if (remainder != 0)
                    {
                        state = 101;
                        break;
                    }

                    factorCount++;
                    factorCache[factorCount + 1] = trialFactor;
                    remainingSize = quotient;
                    if (trialFactor != 2 || factorCount == 1)
                    {
                        state = 107;
                        break;
                    }

                    for (innerIndex = 1; innerIndex < factorCount; innerIndex++)
                    {
                        var factorMoveIndex = factorCount - innerIndex + 1;
                        factorCache[factorMoveIndex + 1] = factorCache[factorMoveIndex];
                    }

                    factorCache[2] = 2;
                    goto case 107;
                case 107:
                    if (remainingSize != 1)
                    {
                        state = 104;
                        break;
                    }

                    factorCache[0] = size;
                    factorCache[1] = factorCount;
                    var angleStep = TwoPi / size;
                    var twiddleWriteOffset = 0;
                    var factorCountMinusOne = factorCount - 1;
                    var groupCount = 1;

                    if (factorCountMinusOne == 0)
                    {
                        return;
                    }

                    for (var factorStageIndex = 0; factorStageIndex < factorCountMinusOne; factorStageIndex++)
                    {
                        var radix = factorCache[factorStageIndex + 2];
                        var factorOffset = 0;
                        var nextGroupCount = groupCount * radix;
                        var innerDimension = size / nextGroupCount;
                        var radixMinusOne = radix - 1;

                        for (radixIndex = 0; radixIndex < radixMinusOne; radixIndex++)
                        {
                            factorOffset += groupCount;
                            innerIndex = twiddleWriteOffset;
                            var factorAngleStep = factorOffset * angleStep;
                            var angleMultiplier = 0f;
                            for (var innerPairIndex = 2; innerPairIndex < innerDimension; innerPairIndex += 2)
                            {
                                angleMultiplier += 1f;
                                var angle = angleMultiplier * factorAngleStep;
                                twiddleFactors[twiddleBaseOffset + innerIndex++] = (float)Math.Cos(angle);
                                twiddleFactors[twiddleBaseOffset + innerIndex++] = (float)Math.Sin(angle);
                            }

                            twiddleWriteOffset += innerDimension;
                        }

                        groupCount = nextGroupCount;
                    }

                    return;
            }
        }
    }

    private static void InitializeWorkspace(int size, float[] workspace, int[] factorCache)
    {
        if (size == 1)
        {
            return;
        }

        InitializeFactorCache(size, workspace, size, factorCache);
    }

    private static void ForwardRadix2(int innerDimension, int groupCount, float[] input, float[] output, float[] twiddle1, int twiddleOffset)
    {
        int groupIndex;
        int offset2;

        var offset1 = 0;
        var groupBlockSize = offset2 = groupCount * innerDimension;
        var offset3 = innerDimension << 1;
        for (groupIndex = 0; groupIndex < groupCount; groupIndex++)
        {
            output[offset1 << 1] = input[offset1] + input[offset2];
            output[(offset1 << 1) + offset3 - 1] = input[offset1] - input[offset2];
            offset1 += innerDimension;
            offset2 += innerDimension;
        }

        if (innerDimension < 2)
        {
            return;
        }

        if (innerDimension != 2)
        {
            offset1 = 0;
            offset2 = groupBlockSize;
            for (groupIndex = 0; groupIndex < groupCount; groupIndex++)
            {
                offset3 = offset2;
                var offset4 = (offset1 << 1) + (innerDimension << 1);
                var offset5 = offset1;
                var offset6 = offset1 + offset1;
                for (var innerIndex = 2; innerIndex < innerDimension; innerIndex += 2)
                {
                    offset3 += 2;
                    offset4 -= 2;
                    offset5 += 2;
                    offset6 += 2;
                    var real2 = twiddle1[twiddleOffset + innerIndex - 2] * input[offset3 - 1] + twiddle1[twiddleOffset + innerIndex - 1] * input[offset3];
                    var imag2 = twiddle1[twiddleOffset + innerIndex - 2] * input[offset3] - twiddle1[twiddleOffset + innerIndex - 1] * input[offset3 - 1];
                    output[offset6] = input[offset5] + imag2;
                    output[offset4] = imag2 - input[offset5];
                    output[offset6 - 1] = input[offset5 - 1] + real2;
                    output[offset4 - 1] = input[offset5 - 1] - real2;
                }

                offset1 += innerDimension;
                offset2 += innerDimension;
            }

            if (innerDimension % 2 == 1)
            {
                return;
            }
        }

        offset3 = offset2 = (offset1 = innerDimension) - 1;
        offset2 += groupBlockSize;
        for (groupIndex = 0; groupIndex < groupCount; groupIndex++)
        {
            output[offset1] = -input[offset2];
            output[offset1 - 1] = input[offset3];
            offset1 += innerDimension << 1;
            offset2 += innerDimension;
            offset3 += innerDimension;
        }
    }

    private static void ForwardRadix4(int innerDimension, int groupCount, float[] input, float[] output, float[] twiddle1, 
        int twiddleOffset1, float[] twiddle2, int twiddleOffset2, float[] twiddle3, int twiddleOffset3)
    {
        int groupIndex, offset5, offset6;
        float imag1, real1, real2;
        var groupBlockSize = groupCount * innerDimension;

        var offset1 = groupBlockSize;
        var offset4 = offset1 << 1;
        var offset2 = offset1 + (offset1 << 1);
        var offset3 = 0;

        for (groupIndex = 0; groupIndex < groupCount; groupIndex++)
        {
            real1 = input[offset1] + input[offset2];
            real2 = input[offset3] + input[offset4];

            output[offset5 = offset3 << 2] = real1 + real2;
            output[(innerDimension << 2) + offset5 - 1] = real2 - real1;
            output[(offset5 += innerDimension << 1) - 1] = input[offset3] - input[offset4];
            output[offset5] = input[offset2] - input[offset1];

            offset1 += innerDimension;
            offset2 += innerDimension;
            offset3 += innerDimension;
            offset4 += innerDimension;
        }

        if (innerDimension < 2)
        {
            return;
        }

        if (innerDimension != 2)
        {
            offset1 = 0;
            for (groupIndex = 0; groupIndex < groupCount; groupIndex++)
            {
                offset2 = offset1;
                offset4 = offset1 << 2;
                offset5 = (offset6 = innerDimension << 1) + offset4;
                for (var innerIndex = 2; innerIndex < innerDimension; innerIndex += 2)
                {
                    offset3 = offset2 += 2;
                    offset4 += 2;
                    offset5 -= 2;

                    offset3 += groupBlockSize;
                    var rotatedReal2 = twiddle1[twiddleOffset1 + innerIndex - 2] * input[offset3 - 1] + twiddle1[twiddleOffset1 + innerIndex - 1] * input[offset3];
                    var rotatedImag2 = twiddle1[twiddleOffset1 + innerIndex - 2] * input[offset3] - twiddle1[twiddleOffset1 + innerIndex - 1] * input[offset3 - 1];
                    offset3 += groupBlockSize;
                    var rotatedReal3 = twiddle2[twiddleOffset2 + innerIndex - 2] * input[offset3 - 1] + twiddle2[twiddleOffset2 + innerIndex - 1] * input[offset3];
                    var rotatedImag3 = twiddle2[twiddleOffset2 + innerIndex - 2] * input[offset3] - twiddle2[twiddleOffset2 + innerIndex - 1] * input[offset3 - 1];
                    offset3 += groupBlockSize;
                    var rotatedReal4 = twiddle3[twiddleOffset3 + innerIndex - 2] * input[offset3 - 1] + twiddle3[twiddleOffset3 + innerIndex - 1] * input[offset3];
                    var rotatedImag4 = twiddle3[twiddleOffset3 + innerIndex - 2] * input[offset3] - twiddle3[twiddleOffset3 + innerIndex - 1] * input[offset3 - 1];

                    real1 = rotatedReal2 + rotatedReal4;
                    var real4 = rotatedReal4 - rotatedReal2;
                    imag1 = rotatedImag2 + rotatedImag4;
                    var imag4 = rotatedImag2 - rotatedImag4;

                    var imag2 = input[offset2] + rotatedImag3;
                    var imag3 = input[offset2] - rotatedImag3;
                    real2 = input[offset2 - 1] + rotatedReal3;
                    var real3 = input[offset2 - 1] - rotatedReal3;

                    output[offset4 - 1] = real1 + real2;
                    output[offset4] = imag1 + imag2;

                    output[offset5 - 1] = real3 - imag4;
                    output[offset5] = real4 - imag3;

                    output[offset4 + offset6 - 1] = imag4 + real3;
                    output[offset4 + offset6] = real4 + imag3;

                    output[offset5 + offset6 - 1] = real2 - real1;
                    output[offset5 + offset6] = imag1 - imag2;
                }

                offset1 += innerDimension;
            }

            if ((innerDimension & 1) != 0)
            {
                return;
            }
        }

        offset2 = (offset1 = groupBlockSize + innerDimension - 1) + (groupBlockSize << 1);
        offset3 = innerDimension << 2;
        offset4 = innerDimension;
        offset5 = innerDimension << 1;
        offset6 = innerDimension;

        for (groupIndex = 0; groupIndex < groupCount; groupIndex++)
        {
            imag1 = -HalfSqrt2 * (input[offset1] + input[offset2]);
            real1 = HalfSqrt2 * (input[offset1] - input[offset2]);

            output[offset4 - 1] = real1 + input[offset6 - 1];
            output[offset4 + offset5 - 1] = input[offset6 - 1] - real1;

            output[offset4] = imag1 - input[offset1 + groupBlockSize];
            output[offset4 + offset5] = imag1 + input[offset1 + groupBlockSize];

            offset1 += innerDimension;
            offset2 += innerDimension;
            offset4 += offset3;
            offset6 += innerDimension;
        }
    }

    private static void ForwardGeneralRadix(int innerDimension, int radix, int groupCount, int groupStride, float[] input, 
        float[] inputByGroup, float[] inputByStride, float[] output, float[] outputByStride, float[] twiddleFactors, int twiddleOffset)
    {
        int offset1;
        var offset2 = 0;
        int offset3;
        int offset4;
        int offset5;
        int offset6;
        int offset7;
        int offset8;
        int offset9;
        float cosineStep, imagAccumulator1, imagAccumulator2, realAccumulator1, realAccumulator2, sineStep;
        float nextRealAccumulator1, nextRealAccumulator2;
        int innerIndex;
        int radixIndex;
        int groupIndex;
        int strideIndex;
        var angle = TwoPi / radix;
        var radixCosineStep = (float)Math.Cos(angle);
        var radixSineStep = (float)Math.Sin(angle);
        var halfRadixCount = (radix + 1) >> 1;
        var radixOffsetLimit = radix;
        var innerOffsetLimit = innerDimension;
        var halfInnerDimension = (innerDimension - 1) >> 1;
        var groupBlockSize = groupCount * innerDimension;
        var radixBlockSize = radix * innerDimension;
        var state = 101;
        
        while (true)
        {
            switch (state)
            {
                case 101:
                    if (innerDimension == 1)
                    {
                        state = 119;
                        break;
                    }

                    for (strideIndex = 0; strideIndex < groupStride; strideIndex++)
                    {
                        outputByStride[strideIndex] = inputByStride[strideIndex];
                    }

                    offset1 = 0;
                    for (radixIndex = 1; radixIndex < radix; radixIndex++)
                    {
                        offset1 += groupBlockSize;
                        offset2 = offset1;
                        for (groupIndex = 0; groupIndex < groupCount; groupIndex++)
                        {
                            output[offset2] = inputByGroup[offset2];
                            offset2 += innerDimension;
                        }
                    }

                    var twiddleBaseOffset = -innerDimension;
                    offset1 = 0;
                    int twiddleIndex;
                    if (halfInnerDimension > groupCount)
                    {
                        for (radixIndex = 1; radixIndex < radix; radixIndex++)
                        {
                            offset1 += groupBlockSize;
                            twiddleBaseOffset += innerDimension;
                            offset2 = -innerDimension + offset1;
                            for (groupIndex = 0; groupIndex < groupCount; groupIndex++)
                            {
                                twiddleIndex = twiddleBaseOffset - 1;
                                offset2 += innerDimension;
                                offset3 = offset2;
                                for (innerIndex = 2; innerIndex < innerDimension; innerIndex += 2)
                                {
                                    twiddleIndex += 2;
                                    offset3 += 2;
                                    output[offset3 - 1] = twiddleFactors[twiddleOffset + twiddleIndex - 1] * inputByGroup[offset3 - 1] + twiddleFactors[twiddleOffset + twiddleIndex] * inputByGroup[offset3];
                                    output[offset3] = twiddleFactors[twiddleOffset + twiddleIndex - 1] * inputByGroup[offset3] - twiddleFactors[twiddleOffset + twiddleIndex] * inputByGroup[offset3 - 1];
                                }
                            }
                        }
                    }
                    else
                    {
                        for (radixIndex = 1; radixIndex < radix; radixIndex++)
                        {
                            twiddleBaseOffset += innerDimension;
                            twiddleIndex = twiddleBaseOffset - 1;
                            offset1 += groupBlockSize;
                            offset2 = offset1;
                            for (innerIndex = 2; innerIndex < innerDimension; innerIndex += 2)
                            {
                                twiddleIndex += 2;
                                offset2 += 2;
                                offset3 = offset2;
                                for (groupIndex = 0; groupIndex < groupCount; groupIndex++)
                                {
                                    output[offset3 - 1] = twiddleFactors[twiddleOffset + twiddleIndex - 1] * inputByGroup[offset3 - 1] + twiddleFactors[twiddleOffset + twiddleIndex] * inputByGroup[offset3];
                                    output[offset3] = twiddleFactors[twiddleOffset + twiddleIndex - 1] * inputByGroup[offset3] - twiddleFactors[twiddleOffset + twiddleIndex] * inputByGroup[offset3 - 1];
                                    offset3 += innerDimension;
                                }
                            }
                        }
                    }

                    offset1 = 0;
                    offset2 = radixOffsetLimit * groupBlockSize;
                    if (halfInnerDimension < groupCount)
                    {
                        for (radixIndex = 1; radixIndex < halfRadixCount; radixIndex++)
                        {
                            offset1 += groupBlockSize;
                            offset2 -= groupBlockSize;
                            offset3 = offset1;
                            offset4 = offset2;
                            for (innerIndex = 2; innerIndex < innerDimension; innerIndex += 2)
                            {
                                offset3 += 2;
                                offset4 += 2;
                                offset5 = offset3 - innerDimension;
                                offset6 = offset4 - innerDimension;
                                for (groupIndex = 0; groupIndex < groupCount; groupIndex++)
                                {
                                    offset5 += innerDimension;
                                    offset6 += innerDimension;
                                    inputByGroup[offset5 - 1] = output[offset5 - 1] + output[offset6 - 1];
                                    inputByGroup[offset6 - 1] = output[offset5] - output[offset6];
                                    inputByGroup[offset5] = output[offset5] + output[offset6];
                                    inputByGroup[offset6] = output[offset6 - 1] - output[offset5 - 1];
                                }
                            }
                        }
                    }
                    else
                    {
                        for (radixIndex = 1; radixIndex < halfRadixCount; radixIndex++)
                        {
                            offset1 += groupBlockSize;
                            offset2 -= groupBlockSize;
                            offset3 = offset1;
                            offset4 = offset2;
                            for (groupIndex = 0; groupIndex < groupCount; groupIndex++)
                            {
                                offset5 = offset3;
                                offset6 = offset4;
                                for (innerIndex = 2; innerIndex < innerDimension; innerIndex += 2)
                                {
                                    offset5 += 2;
                                    offset6 += 2;
                                    inputByGroup[offset5 - 1] = output[offset5 - 1] + output[offset6 - 1];
                                    inputByGroup[offset6 - 1] = output[offset5] - output[offset6];
                                    inputByGroup[offset5] = output[offset5] + output[offset6];
                                    inputByGroup[offset6] = output[offset6 - 1] - output[offset5 - 1];
                                }

                                offset3 += innerDimension;
                                offset4 += innerDimension;
                            }
                        }
                    }

                    goto case 119;
                case 119:
                    for (strideIndex = 0; strideIndex < groupStride; strideIndex++)
                    {
                        inputByStride[strideIndex] = outputByStride[strideIndex];
                    }

                    offset1 = 0;
                    offset2 = radixOffsetLimit * groupStride;
                    for (radixIndex = 1; radixIndex < halfRadixCount; radixIndex++)
                    {
                        offset1 += groupBlockSize;
                        offset2 -= groupBlockSize;
                        offset3 = offset1 - innerDimension;
                        offset4 = offset2 - innerDimension;
                        for (groupIndex = 0; groupIndex < groupCount; groupIndex++)
                        {
                            offset3 += innerDimension;
                            offset4 += innerDimension;
                            inputByGroup[offset3] = output[offset3] + output[offset4];
                            inputByGroup[offset4] = output[offset4] - output[offset3];
                        }
                    }

                    realAccumulator1 = 1f;
                    imagAccumulator1 = 0f;
                    offset1 = 0;
                    offset2 = radixOffsetLimit * groupStride;
                    offset3 = (radix - 1) * groupStride;
                    int harmonicIndex;
                    for (harmonicIndex = 1; harmonicIndex < halfRadixCount; harmonicIndex++)
                    {
                        offset1 += groupStride;
                        offset2 -= groupStride;
                        nextRealAccumulator1 = radixCosineStep * realAccumulator1 - radixSineStep * imagAccumulator1;
                        imagAccumulator1 = radixCosineStep * imagAccumulator1 + radixSineStep * realAccumulator1;
                        realAccumulator1 = nextRealAccumulator1;
                        offset4 = offset1;
                        offset5 = offset2;
                        offset6 = offset3;
                        offset7 = groupStride;

                        for (strideIndex = 0; strideIndex < groupStride; strideIndex++)
                        {
                            outputByStride[offset4++] = inputByStride[strideIndex] + realAccumulator1 * inputByStride[offset7++];
                            outputByStride[offset5++] = imagAccumulator1 * inputByStride[offset6++];
                        }

                        cosineStep = realAccumulator1;
                        sineStep = imagAccumulator1;
                        realAccumulator2 = realAccumulator1;
                        imagAccumulator2 = imagAccumulator1;

                        offset4 = groupStride;
                        offset5 = (radixOffsetLimit - 1) * groupStride;
                        for (radixIndex = 2; radixIndex < halfRadixCount; radixIndex++)
                        {
                            offset4 += groupStride;
                            offset5 -= groupStride;

                            nextRealAccumulator2 = cosineStep * realAccumulator2 - sineStep * imagAccumulator2;
                            imagAccumulator2 = cosineStep * imagAccumulator2 + sineStep * realAccumulator2;
                            realAccumulator2 = nextRealAccumulator2;

                            offset6 = offset1;
                            offset7 = offset2;
                            offset8 = offset4;
                            offset9 = offset5;
                            for (strideIndex = 0; strideIndex < groupStride; strideIndex++)
                            {
                                outputByStride[offset6++] += realAccumulator2 * inputByStride[offset8++];
                                outputByStride[offset7++] += imagAccumulator2 * inputByStride[offset9++];
                            }
                        }
                    }

                    offset1 = 0;
                    for (radixIndex = 1; radixIndex < halfRadixCount; radixIndex++)
                    {
                        offset1 += groupStride;
                        offset2 = offset1;
                        for (strideIndex = 0; strideIndex < groupStride; strideIndex++)
                        {
                            outputByStride[strideIndex] += inputByStride[offset2++];
                        }
                    }

                    if (innerDimension < groupCount)
                    {
                        state = 132;
                        break;
                    }

                    offset1 = 0;
                    offset2 = 0;
                    for (groupIndex = 0; groupIndex < groupCount; groupIndex++)
                    {
                        offset3 = offset1;
                        offset4 = offset2;
                        for (innerIndex = 0; innerIndex < innerDimension; innerIndex++)
                        {
                            input[offset4++] = output[offset3++];
                        }

                        offset1 += innerDimension;
                        offset2 += radixBlockSize;
                    }

                    state = 135;
                    break;
                case 132:
                    for (innerIndex = 0; innerIndex < innerDimension; innerIndex++)
                    {
                        offset1 = innerIndex;
                        offset2 = innerIndex;
                        for (groupIndex = 0; groupIndex < groupCount; groupIndex++)
                        {
                            input[offset2] = output[offset1];
                            offset1 += innerDimension;
                            offset2 += radixBlockSize;
                        }
                    }

                    goto case 135;
                case 135:
                    offset1 = 0;
                    offset2 = innerDimension << 1;
                    offset3 = 0;
                    offset4 = radixOffsetLimit * groupBlockSize;
                    for (radixIndex = 1; radixIndex < halfRadixCount; radixIndex++)
                    {
                        offset1 += offset2;
                        offset3 += groupBlockSize;
                        offset4 -= groupBlockSize;

                        offset5 = offset1;
                        offset6 = offset3;
                        offset7 = offset4;

                        for (groupIndex = 0; groupIndex < groupCount; groupIndex++)
                        {
                            input[offset5 - 1] = output[offset6];
                            input[offset5] = output[offset7];
                            offset5 += radixBlockSize;
                            offset6 += innerDimension;
                            offset7 += innerDimension;
                        }
                    }

                    if (innerDimension == 1)
                    {
                        return;
                    }

                    if (halfInnerDimension < groupCount)
                    {
                        state = 141;
                        break;
                    }

                    offset1 = -innerDimension;
                    offset3 = 0;
                    offset4 = 0;
                    offset5 = radixOffsetLimit * groupBlockSize;
                    for (radixIndex = 1; radixIndex < halfRadixCount; radixIndex++)
                    {
                        offset1 += offset2;
                        offset3 += offset2;
                        offset4 += groupBlockSize;
                        offset5 -= groupBlockSize;
                        offset6 = offset1;
                        offset7 = offset3;
                        offset8 = offset4;
                        offset9 = offset5;
                        for (groupIndex = 0; groupIndex < groupCount; groupIndex++)
                        {
                            for (innerIndex = 2; innerIndex < innerDimension; innerIndex += 2)
                            {
                                var ic = innerOffsetLimit - innerIndex;
                                input[innerIndex + offset7 - 1] = output[innerIndex + offset8 - 1] + output[innerIndex + offset9 - 1];
                                input[ic + offset6 - 1] = output[innerIndex + offset8 - 1] - output[innerIndex + offset9 - 1];
                                input[innerIndex + offset7] = output[innerIndex + offset8] + output[innerIndex + offset9];
                                input[ic + offset6] = output[innerIndex + offset9] - output[innerIndex + offset8];
                            }

                            offset6 += radixBlockSize;
                            offset7 += radixBlockSize;
                            offset8 += innerDimension;
                            offset9 += innerDimension;
                        }
                    }

                    return;
                case 141:
                    offset1 = -innerDimension;
                    offset3 = 0;
                    offset4 = 0;
                    offset5 = radixOffsetLimit * groupBlockSize;
                    for (radixIndex = 1; radixIndex < halfRadixCount; radixIndex++)
                    {
                        offset1 += offset2;
                        offset3 += offset2;
                        offset4 += groupBlockSize;
                        offset5 -= groupBlockSize;
                        for (innerIndex = 2; innerIndex < innerDimension; innerIndex += 2)
                        {
                            offset6 = innerOffsetLimit + offset1 - innerIndex;
                            offset7 = innerIndex + offset3;
                            offset8 = innerIndex + offset4;
                            offset9 = innerIndex + offset5;
                            for (groupIndex = 0; groupIndex < groupCount; groupIndex++)
                            {
                                input[offset7 - 1] = output[offset8 - 1] + output[offset9 - 1];
                                input[offset6 - 1] = output[offset8 - 1] - output[offset9 - 1];
                                input[offset7] = output[offset8] + output[offset9];
                                input[offset6] = output[offset9] - output[offset8];
                                offset6 += radixBlockSize;
                                offset7 += radixBlockSize;
                                offset8 += innerDimension;
                                offset9 += innerDimension;
                            }
                        }
                    }

                    return;
            }
        }
    }

    private static void ForwardTransformInternal(int size, float[] data, float[] output, float[] twiddleFactors, int[] factorCache)
    {
        int innerIndex, factorStageIndex, groupCount, nextGroupCount;
        int activeBuffer, reverseFactorIndex, factorCount;
        int radix, twiddleStart, innerDimension, groupStride, twiddleStart2, twiddleStart3;

        factorCount = factorCache[1];
        activeBuffer = 1;
        nextGroupCount = size;
        twiddleStart = size;

        for (factorStageIndex = 0; factorStageIndex < factorCount; factorStageIndex++)
        {
            reverseFactorIndex = factorCount - factorStageIndex;
            radix = factorCache[reverseFactorIndex + 1];
            groupCount = nextGroupCount / radix;
            innerDimension = size / nextGroupCount;
            groupStride = innerDimension * groupCount;
            twiddleStart -= (radix - 1) * innerDimension;
            activeBuffer = 1 - activeBuffer;
            var state = 100;

            while (true)
            {
                switch (state)
                {
                    case 100:
                        if (radix != 4)
                        {
                            state = 102;
                            break;
                        }

                        twiddleStart2 = twiddleStart + innerDimension;
                        twiddleStart3 = twiddleStart2 + innerDimension;
                        if (activeBuffer != 0)
                        {
                            ForwardRadix4(innerDimension, groupCount, output, data, twiddleFactors, twiddleStart - 1, twiddleFactors, twiddleStart2 - 1, twiddleFactors, twiddleStart3 - 1);
                        }
                        else
                        {
                            ForwardRadix4(innerDimension, groupCount, data, output, twiddleFactors, twiddleStart - 1, twiddleFactors, twiddleStart2 - 1, twiddleFactors, twiddleStart3 - 1);
                        }

                        state = 110;
                        break;
                    case 102:
                        if (radix != 2)
                        {
                            state = 104;
                            break;
                        }

                        if (activeBuffer != 0)
                        {
                            state = 103;
                            break;
                        }

                        ForwardRadix2(innerDimension, groupCount, data, output, twiddleFactors, twiddleStart - 1);
                        state = 110;
                        break;
                    case 103:
                        ForwardRadix2(innerDimension, groupCount, output, data, twiddleFactors, twiddleStart - 1);
                        goto case 104;
                    case 104:
                        if (innerDimension == 1)
                        {
                            activeBuffer = 1 - activeBuffer;
                        }

                        if (activeBuffer != 0)
                        {
                            state = 109;
                            break;
                        }

                        ForwardGeneralRadix(innerDimension, radix, groupCount, groupStride, data, data, data, output, output, twiddleFactors, twiddleStart - 1);
                        activeBuffer = 1;
                        state = 110;
                        break;
                    case 109:
                        ForwardGeneralRadix(innerDimension, radix, groupCount, groupStride, output, output, output, data, data, twiddleFactors, twiddleStart - 1);
                        activeBuffer = 0;
                        goto case 110;
                    case 110:
                        nextGroupCount = groupCount;
                        goto EndLoop;
                }
            }

            EndLoop:
            ;
        }

        if (activeBuffer == 1)
        {
            return;
        }

        for (innerIndex = 0; innerIndex < size; innerIndex++)
        {
            data[innerIndex] = output[innerIndex];
        }
    }

    private static void BackwardRadix2(int innerDimension, int groupCount, float[] input, float[] output, float[] twiddle1, int twiddleOffset)
    {
        int innerIndex, groupIndex, groupBlockSize, offset1, offset2, offset3, offset4, offset5, offset6;
        float imag2, real2;

        groupBlockSize = groupCount * innerDimension;

        offset1 = 0;
        offset2 = 0;
        offset3 = (innerDimension << 1) - 1;
        for (groupIndex = 0; groupIndex < groupCount; groupIndex++)
        {
            output[offset1] = input[offset2] + input[offset3 + offset2];
            output[offset1 + groupBlockSize] = input[offset2] - input[offset3 + offset2];
            offset2 = (offset1 += innerDimension) << 1;
        }

        if (innerDimension < 2)
        {
            return;
        }

        if (innerDimension != 2)
        {
            offset1 = 0;
            offset2 = 0;
            for (groupIndex = 0; groupIndex < groupCount; groupIndex++)
            {
                offset3 = offset1;
                offset5 = (offset4 = offset2) + (innerDimension << 1);
                offset6 = groupBlockSize + offset1;
                for (innerIndex = 2; innerIndex < innerDimension; innerIndex += 2)
                {
                    offset3 += 2;
                    offset4 += 2;
                    offset5 -= 2;
                    offset6 += 2;
                    output[offset3 - 1] = input[offset4 - 1] + input[offset5 - 1];
                    real2 = input[offset4 - 1] - input[offset5 - 1];
                    output[offset3] = input[offset4] - input[offset5];
                    imag2 = input[offset4] + input[offset5];
                    output[offset6 - 1] = twiddle1[twiddleOffset + innerIndex - 2] * real2 - twiddle1[twiddleOffset + innerIndex - 1] * imag2;
                    output[offset6] = twiddle1[twiddleOffset + innerIndex - 2] * imag2 + twiddle1[twiddleOffset + innerIndex - 1] * real2;
                }

                offset2 = (offset1 += innerDimension) << 1;
            }

            if ((innerDimension % 2) == 1)
            {
                return;
            }
        }

        offset1 = innerDimension - 1;
        offset2 = innerDimension - 1;
        for (groupIndex = 0; groupIndex < groupCount; groupIndex++)
        {
            output[offset1] = input[offset2] + input[offset2];
            output[offset1 + groupBlockSize] = -(input[offset2 + 1] + input[offset2 + 1]);
            offset1 += innerDimension;
            offset2 += innerDimension << 1;
        }
    }

    private static void BackwardRadix3(int innerDimension, int groupCount, float[] input, float[] output, float[] twiddle1,
        int twiddleOffset1, float[] twiddle2, int twiddleOffset2)
    {
        int innerIndex, groupIndex, offset1, offset2, offset3, offset4, offset5, offset6, offset7, offset8, offset9, radixBlockSize;
        float rotatedImag2, rotatedImag3, combinedImag2, combinedImag3, rotatedReal2, rotatedReal3, combinedReal2, combinedReal3, imag2, real2;
        var groupBlockSize = groupCount * innerDimension;

        offset1 = 0;
        offset2 = groupBlockSize << 1;
        offset3 = innerDimension << 1;
        offset4 = innerDimension + (innerDimension << 1);
        offset5 = 0;
        for (groupIndex = 0; groupIndex < groupCount; groupIndex++)
        {
            real2 = input[offset3 - 1] + input[offset3 - 1];
            rotatedReal2 = input[offset5] + MinusHalf * real2;
            output[offset1] = input[offset5] + real2;
            rotatedImag3 = Sqrt3Over2 * (input[offset3] + input[offset3]);
            output[offset1 + groupBlockSize] = rotatedReal2 - rotatedImag3;
            output[offset1 + offset2] = rotatedReal2 + rotatedImag3;
            offset1 += innerDimension;
            offset3 += offset4;
            offset5 += offset4;
        }

        if (innerDimension == 1)
        {
            return;
        }

        offset1 = 0;
        offset3 = innerDimension << 1;
        for (groupIndex = 0; groupIndex < groupCount; groupIndex++)
        {
            offset7 = offset1 + (offset1 << 1);
            offset6 = offset5 = offset7 + offset3;
            offset8 = offset1;
            radixBlockSize = (offset9 = offset1 + groupBlockSize) + groupBlockSize;

            for (innerIndex = 2; innerIndex < innerDimension; innerIndex += 2)
            {
                offset5 += 2;
                offset6 -= 2;
                offset7 += 2;
                offset8 += 2;
                offset9 += 2;
                radixBlockSize += 2;
                real2 = input[offset5 - 1] + input[offset6 - 1];
                rotatedReal2 = input[offset7 - 1] + MinusHalf * real2;
                output[offset8 - 1] = input[offset7 - 1] + real2;
                imag2 = input[offset5] - input[offset6];
                rotatedImag2 = input[offset7] + MinusHalf * imag2;
                output[offset8] = input[offset7] + imag2;
                rotatedReal3 = Sqrt3Over2 * (input[offset5 - 1] - input[offset6 - 1]);
                rotatedImag3 = Sqrt3Over2 * (input[offset5] + input[offset6]);
                combinedReal2 = rotatedReal2 - rotatedImag3;
                combinedReal3 = rotatedReal2 + rotatedImag3;
                combinedImag2 = rotatedImag2 + rotatedReal3;
                combinedImag3 = rotatedImag2 - rotatedReal3;
                output[offset9 - 1] = twiddle1[twiddleOffset1 + innerIndex - 2] * combinedReal2 - twiddle1[twiddleOffset1 + innerIndex - 1] * combinedImag2;
                output[offset9] = twiddle1[twiddleOffset1 + innerIndex - 2] * combinedImag2 + twiddle1[twiddleOffset1 + innerIndex - 1] * combinedReal2;
                output[radixBlockSize - 1] = twiddle2[twiddleOffset2 + innerIndex - 2] * combinedReal3 - twiddle2[twiddleOffset2 + innerIndex - 1] * combinedImag3;
                output[radixBlockSize] = twiddle2[twiddleOffset2 + innerIndex - 2] * combinedImag3 + twiddle2[twiddleOffset2 + innerIndex - 1] * combinedReal3;
            }

            offset1 += innerDimension;
        }
    }

    private static void BackwardRadix4(int innerDimension, int groupCount, float[] input, float[] output, float[] twiddle1,
        int twiddleOffset1, float[] twiddle2, int twiddleOffset2, float[] twiddle3, int twiddleOffset3)
    {
        int innerIndex, groupIndex, offset1, offset2, offset3, offset4, offset5, offset6, offset7, offset8;
        float rotatedImag2, rotatedImag3, rotatedImag4, rotatedReal2, rotatedReal3, rotatedReal4, imag1, imag2, imag3, imag4, real1, real2, real3, real4;
        var groupBlockSize = groupCount * innerDimension;

        offset1 = 0;
        offset2 = innerDimension << 2;
        offset3 = 0;
        offset6 = innerDimension << 1;
        for (groupIndex = 0; groupIndex < groupCount; groupIndex++)
        {
            offset4 = offset3 + offset6;
            offset5 = offset1;
            real3 = input[offset4 - 1] + input[offset4 - 1];
            real4 = input[offset4] + input[offset4];
            real1 = input[offset3] - input[(offset4 += offset6) - 1];
            real2 = input[offset3] + input[offset4 - 1];
            output[offset5] = real2 + real3;
            output[offset5 += groupBlockSize] = real1 - real4;
            output[offset5 += groupBlockSize] = real2 - real3;
            output[offset5 += groupBlockSize] = real1 + real4;
            offset1 += innerDimension;
            offset3 += offset2;
        }

        if (innerDimension < 2)
        {
            return;
        }

        if (innerDimension != 2)
        {
            offset1 = 0;
            for (groupIndex = 0; groupIndex < groupCount; groupIndex++)
            {
                offset5 = (offset4 = (offset3 = (offset2 = offset1 << 2) + offset6)) + offset6;
                offset7 = offset1;
                for (innerIndex = 2; innerIndex < innerDimension; innerIndex += 2)
                {
                    offset2 += 2;
                    offset3 += 2;
                    offset4 -= 2;
                    offset5 -= 2;
                    offset7 += 2;
                    imag1 = input[offset2] + input[offset5];
                    imag2 = input[offset2] - input[offset5];
                    imag3 = input[offset3] - input[offset4];
                    real4 = input[offset3] + input[offset4];
                    real1 = input[offset2 - 1] - input[offset5 - 1];
                    real2 = input[offset2 - 1] + input[offset5 - 1];
                    imag4 = input[offset3 - 1] - input[offset4 - 1];
                    real3 = input[offset3 - 1] + input[offset4 - 1];
                    output[offset7 - 1] = real2 + real3;
                    rotatedReal3 = real2 - real3;
                    output[offset7] = imag2 + imag3;
                    rotatedImag3 = imag2 - imag3;
                    rotatedReal2 = real1 - real4;
                    rotatedReal4 = real1 + real4;
                    rotatedImag2 = imag1 + imag4;
                    rotatedImag4 = imag1 - imag4;

                    output[(offset8 = offset7 + groupBlockSize) - 1] = twiddle1[twiddleOffset1 + innerIndex - 2] * rotatedReal2 - twiddle1[twiddleOffset1 + innerIndex - 1] * rotatedImag2;
                    output[offset8] = twiddle1[twiddleOffset1 + innerIndex - 2] * rotatedImag2 + twiddle1[twiddleOffset1 + innerIndex - 1] * rotatedReal2;
                    output[(offset8 += groupBlockSize) - 1] = twiddle2[twiddleOffset2 + innerIndex - 2] * rotatedReal3 - twiddle2[twiddleOffset2 + innerIndex - 1] * rotatedImag3;
                    output[offset8] = twiddle2[twiddleOffset2 + innerIndex - 2] * rotatedImag3 + twiddle2[twiddleOffset2 + innerIndex - 1] * rotatedReal3;
                    output[(offset8 += groupBlockSize) - 1] = twiddle3[twiddleOffset3 + innerIndex - 2] * rotatedReal4 - twiddle3[twiddleOffset3 + innerIndex - 1] * rotatedImag4;
                    output[offset8] = twiddle3[twiddleOffset3 + innerIndex - 2] * rotatedImag4 + twiddle3[twiddleOffset3 + innerIndex - 1] * rotatedReal4;
                }

                offset1 += innerDimension;
            }

            if (innerDimension % 2 == 1)
            {
                return;
            }
        }

        offset1 = innerDimension;
        offset2 = innerDimension << 2;
        offset3 = innerDimension - 1;
        offset4 = innerDimension + (innerDimension << 1);
        for (groupIndex = 0; groupIndex < groupCount; groupIndex++)
        {
            offset5 = offset3;
            imag1 = input[offset1] + input[offset4];
            imag2 = input[offset4] - input[offset1];
            real1 = input[offset1 - 1] - input[offset4 - 1];
            real2 = input[offset1 - 1] + input[offset4 - 1];
            output[offset5] = real2 + real2;
            output[offset5 += groupBlockSize] = Sqrt2 * (real1 - imag1);
            output[offset5 += groupBlockSize] = imag2 + imag2;
            output[offset5 += groupBlockSize] = -Sqrt2 * (real1 + imag1);

            offset3 += innerDimension;
            offset1 += offset2;
            offset4 += offset2;
        }
    }

    private static void BackwardGeneralRadix(int innerDimension, int radix, int groupCount, int groupStride, float[] input,
        float[] inputByGroup, float[] inputByStride, float[] output, float[] outputByStride, float[] twiddleFactors, int twiddleOffset)
    {
        int twiddleIndex, halfRadixCount = 0, innerIndex, radixIndex, groupIndex, harmonicIndex, strideIndex, twiddleBaseOffset, groupBlockSize = 0, offset1, offset2, offset3, offset4, offset5, offset6, offset7, offset8, offset9, radixBlockSize = 0, offset11, offset12;
        float cosineStep, imagAccumulator1, imagAccumulator2, realAccumulator1, realAccumulator2, sineStep;
        int halfInnerDimension = 0;
        float radixCosineStep = 0, angle, radixSineStep = 0, nextRealAccumulator1, nextRealAccumulator2;
        int radixOffsetLimit = 0;
        var state = 100;

        while (true)
        {
            switch (state)
            {
                case 100:
                    radixBlockSize = radix * innerDimension;
                    groupBlockSize = groupCount * innerDimension;
                    angle = TwoPi / radix;
                    radixCosineStep = (float)Math.Cos(angle);
                    radixSineStep = (float)Math.Sin(angle);
                    halfInnerDimension = (innerDimension - 1) >> 1;
                    radixOffsetLimit = radix;
                    halfRadixCount = (radix + 1) >> 1;
                    if (innerDimension < groupCount)
                    {
                        state = 103;
                        break;
                    }

                    offset1 = 0;
                    offset2 = 0;
                    for (groupIndex = 0; groupIndex < groupCount; groupIndex++)
                    {
                        offset3 = offset1;
                        offset4 = offset2;
                        for (innerIndex = 0; innerIndex < innerDimension; innerIndex++)
                        {
                            output[offset3] = input[offset4];
                            offset3++;
                            offset4++;
                        }

                        offset1 += innerDimension;
                        offset2 += radixBlockSize;
                    }

                    state = 106;
                    break;
                case 103:
                    offset1 = 0;
                    for (innerIndex = 0; innerIndex < innerDimension; innerIndex++)
                    {
                        offset2 = offset1;
                        offset3 = offset1;
                        for (groupIndex = 0; groupIndex < groupCount; groupIndex++)
                        {
                            output[offset2] = input[offset3];
                            offset2 += innerDimension;
                            offset3 += radixBlockSize;
                        }

                        offset1++;
                    }

                    goto case 106;
                case 106:
                    offset1 = 0;
                    offset2 = radixOffsetLimit * groupBlockSize;
                    offset7 = offset5 = innerDimension << 1;
                    for (radixIndex = 1; radixIndex < halfRadixCount; radixIndex++)
                    {
                        offset1 += groupBlockSize;
                        offset2 -= groupBlockSize;
                        offset3 = offset1;
                        offset4 = offset2;
                        offset6 = offset5;
                        for (groupIndex = 0; groupIndex < groupCount; groupIndex++)
                        {
                            output[offset3] = input[offset6 - 1] + input[offset6 - 1];
                            output[offset4] = input[offset6] + input[offset6];
                            offset3 += innerDimension;
                            offset4 += innerDimension;
                            offset6 += radixBlockSize;
                        }

                        offset5 += offset7;
                    }

                    if (innerDimension == 1)
                    {
                        state = 116;
                        break;
                    }

                    if (halfInnerDimension < groupCount)
                    {
                        state = 112;
                        break;
                    }

                    offset1 = 0;
                    offset2 = radixOffsetLimit * groupBlockSize;
                    offset7 = 0;
                    for (radixIndex = 1; radixIndex < halfRadixCount; radixIndex++)
                    {
                        offset1 += groupBlockSize;
                        offset2 -= groupBlockSize;
                        offset3 = offset1;
                        offset4 = offset2;
                        offset7 += innerDimension << 1;
                        offset8 = offset7;
                        for (groupIndex = 0; groupIndex < groupCount; groupIndex++)
                        {
                            offset5 = offset3;
                            offset6 = offset4;
                            offset9 = offset8;
                            offset11 = offset8;
                            for (innerIndex = 2; innerIndex < innerDimension; innerIndex += 2)
                            {
                                offset5 += 2;
                                offset6 += 2;
                                offset9 += 2;
                                offset11 -= 2;
                                output[offset5 - 1] = input[offset9 - 1] + input[offset11 - 1];
                                output[offset6 - 1] = input[offset9 - 1] - input[offset11 - 1];
                                output[offset5] = input[offset9] - input[offset11];
                                output[offset6] = input[offset9] + input[offset11];
                            }

                            offset3 += innerDimension;
                            offset4 += innerDimension;
                            offset8 += radixBlockSize;
                        }
                    }

                    state = 116;
                    break;
                case 112:
                    offset1 = 0;
                    offset2 = radixOffsetLimit * groupBlockSize;
                    offset7 = 0;
                    for (radixIndex = 1; radixIndex < halfRadixCount; radixIndex++)
                    {
                        offset1 += groupBlockSize;
                        offset2 -= groupBlockSize;
                        offset3 = offset1;
                        offset4 = offset2;
                        offset7 += innerDimension << 1;
                        offset8 = offset7;
                        offset9 = offset7;
                        for (innerIndex = 2; innerIndex < innerDimension; innerIndex += 2)
                        {
                            offset3 += 2;
                            offset4 += 2;
                            offset8 += 2;
                            offset9 -= 2;
                            offset5 = offset3;
                            offset6 = offset4;
                            offset11 = offset8;
                            offset12 = offset9;
                            for (groupIndex = 0; groupIndex < groupCount; groupIndex++)
                            {
                                output[offset5 - 1] = input[offset11 - 1] + input[offset12 - 1];
                                output[offset6 - 1] = input[offset11 - 1] - input[offset12 - 1];
                                output[offset5] = input[offset11] - input[offset12];
                                output[offset6] = input[offset11] + input[offset12];
                                offset5 += innerDimension;
                                offset6 += innerDimension;
                                offset11 += radixBlockSize;
                                offset12 += radixBlockSize;
                            }
                        }
                    }

                    goto case 116;
                case 116:
                    realAccumulator1 = 1f;
                    imagAccumulator1 = 0f;
                    offset1 = 0;
                    offset9 = offset2 = radixOffsetLimit * groupStride;
                    offset3 = (radix - 1) * groupStride;
                    for (harmonicIndex = 1; harmonicIndex < halfRadixCount; harmonicIndex++)
                    {
                        offset1 += groupStride;
                        offset2 -= groupStride;

                        nextRealAccumulator1 = radixCosineStep * realAccumulator1 - radixSineStep * imagAccumulator1;
                        imagAccumulator1 = radixCosineStep * imagAccumulator1 + radixSineStep * realAccumulator1;
                        realAccumulator1 = nextRealAccumulator1;
                        offset4 = offset1;
                        offset5 = offset2;
                        offset6 = 0;
                        offset7 = groupStride;
                        offset8 = offset3;
                        for (strideIndex = 0; strideIndex < groupStride; strideIndex++)
                        {
                            inputByStride[offset4++] = outputByStride[offset6++] + realAccumulator1 * outputByStride[offset7++];
                            inputByStride[offset5++] = imagAccumulator1 * outputByStride[offset8++];
                        }

                        cosineStep = realAccumulator1;
                        sineStep = imagAccumulator1;
                        realAccumulator2 = realAccumulator1;
                        imagAccumulator2 = imagAccumulator1;

                        offset6 = groupStride;
                        offset7 = offset9 - groupStride;
                        for (radixIndex = 2; radixIndex < halfRadixCount; radixIndex++)
                        {
                            offset6 += groupStride;
                            offset7 -= groupStride;
                            nextRealAccumulator2 = cosineStep * realAccumulator2 - sineStep * imagAccumulator2;
                            imagAccumulator2 = cosineStep * imagAccumulator2 + sineStep * realAccumulator2;
                            realAccumulator2 = nextRealAccumulator2;
                            offset4 = offset1;
                            offset5 = offset2;
                            offset11 = offset6;
                            offset12 = offset7;
                            for (strideIndex = 0; strideIndex < groupStride; strideIndex++)
                            {
                                inputByStride[offset4++] += realAccumulator2 * outputByStride[offset11++];
                                inputByStride[offset5++] += imagAccumulator2 * outputByStride[offset12++];
                            }
                        }
                    }

                    offset1 = 0;
                    for (radixIndex = 1; radixIndex < halfRadixCount; radixIndex++)
                    {
                        offset1 += groupStride;
                        offset2 = offset1;
                        for (strideIndex = 0; strideIndex < groupStride; strideIndex++)
                        {
                            outputByStride[strideIndex] += outputByStride[offset2++];
                        }
                    }

                    offset1 = 0;
                    offset2 = radixOffsetLimit * groupBlockSize;
                    for (radixIndex = 1; radixIndex < halfRadixCount; radixIndex++)
                    {
                        offset1 += groupBlockSize;
                        offset2 -= groupBlockSize;
                        offset3 = offset1;
                        offset4 = offset2;
                        for (groupIndex = 0; groupIndex < groupCount; groupIndex++)
                        {
                            output[offset3] = inputByGroup[offset3] - inputByGroup[offset4];
                            output[offset4] = inputByGroup[offset3] + inputByGroup[offset4];
                            offset3 += innerDimension;
                            offset4 += innerDimension;
                        }
                    }

                    if (innerDimension == 1)
                    {
                        state = 132;
                        break;
                    }

                    if (halfInnerDimension < groupCount)
                    {
                        state = 128;
                        break;
                    }

                    offset1 = 0;
                    offset2 = radixOffsetLimit * groupBlockSize;
                    for (radixIndex = 1; radixIndex < halfRadixCount; radixIndex++)
                    {
                        offset1 += groupBlockSize;
                        offset2 -= groupBlockSize;
                        offset3 = offset1;
                        offset4 = offset2;
                        for (groupIndex = 0; groupIndex < groupCount; groupIndex++)
                        {
                            offset5 = offset3;
                            offset6 = offset4;
                            for (innerIndex = 2; innerIndex < innerDimension; innerIndex += 2)
                            {
                                offset5 += 2;
                                offset6 += 2;
                                output[offset5 - 1] = inputByGroup[offset5 - 1] - inputByGroup[offset6];
                                output[offset6 - 1] = inputByGroup[offset5 - 1] + inputByGroup[offset6];
                                output[offset5] = inputByGroup[offset5] + inputByGroup[offset6 - 1];
                                output[offset6] = inputByGroup[offset5] - inputByGroup[offset6 - 1];
                            }

                            offset3 += innerDimension;
                            offset4 += innerDimension;
                        }
                    }

                    state = 132;
                    break;
                case 128:
                    offset1 = 0;
                    offset2 = radixOffsetLimit * groupBlockSize;
                    for (radixIndex = 1; radixIndex < halfRadixCount; radixIndex++)
                    {
                        offset1 += groupBlockSize;
                        offset2 -= groupBlockSize;
                        offset3 = offset1;
                        offset4 = offset2;
                        for (innerIndex = 2; innerIndex < innerDimension; innerIndex += 2)
                        {
                            offset3 += 2;
                            offset4 += 2;
                            offset5 = offset3;
                            offset6 = offset4;
                            for (groupIndex = 0; groupIndex < groupCount; groupIndex++)
                            {
                                output[offset5 - 1] = inputByGroup[offset5 - 1] - inputByGroup[offset6];
                                output[offset6 - 1] = inputByGroup[offset5 - 1] + inputByGroup[offset6];
                                output[offset5] = inputByGroup[offset5] + inputByGroup[offset6 - 1];
                                output[offset6] = inputByGroup[offset5] - inputByGroup[offset6 - 1];
                                offset5 += innerDimension;
                                offset6 += innerDimension;
                            }
                        }
                    }

                    goto case 132;
                case 132:
                    if (innerDimension == 1)
                    {
                        return;
                    }

                    for (strideIndex = 0; strideIndex < groupStride; strideIndex++)
                    {
                        inputByStride[strideIndex] = outputByStride[strideIndex];
                    }

                    offset1 = 0;
                    for (radixIndex = 1; radixIndex < radix; radixIndex++)
                    {
                        offset2 = offset1 += groupBlockSize;
                        for (groupIndex = 0; groupIndex < groupCount; groupIndex++)
                        {
                            inputByGroup[offset2] = output[offset2];
                            offset2 += innerDimension;
                        }
                    }

                    if (halfInnerDimension > groupCount)
                    {
                        state = 139;
                        break;
                    }

                    twiddleBaseOffset = -innerDimension - 1;
                    offset1 = 0;
                    for (radixIndex = 1; radixIndex < radix; radixIndex++)
                    {
                        twiddleBaseOffset += innerDimension;
                        offset1 += groupBlockSize;
                        twiddleIndex = twiddleBaseOffset;
                        offset2 = offset1;
                        for (innerIndex = 2; innerIndex < innerDimension; innerIndex += 2)
                        {
                            offset2 += 2;
                            twiddleIndex += 2;
                            offset3 = offset2;
                            for (groupIndex = 0; groupIndex < groupCount; groupIndex++)
                            {
                                inputByGroup[offset3 - 1] = twiddleFactors[twiddleOffset + twiddleIndex - 1] * output[offset3 - 1] - twiddleFactors[twiddleOffset + twiddleIndex] * output[offset3];
                                inputByGroup[offset3] = twiddleFactors[twiddleOffset + twiddleIndex - 1] * output[offset3] + twiddleFactors[twiddleOffset + twiddleIndex] * output[offset3 - 1];
                                offset3 += innerDimension;
                            }
                        }
                    }

                    return;
                case 139:
                    twiddleBaseOffset = -innerDimension - 1;
                    offset1 = 0;
                    for (radixIndex = 1; radixIndex < radix; radixIndex++)
                    {
                        twiddleBaseOffset += innerDimension;
                        offset1 += groupBlockSize;
                        offset2 = offset1;
                        for (groupIndex = 0; groupIndex < groupCount; groupIndex++)
                        {
                            twiddleIndex = twiddleBaseOffset;
                            offset3 = offset2;
                            for (innerIndex = 2; innerIndex < innerDimension; innerIndex += 2)
                            {
                                twiddleIndex += 2;
                                offset3 += 2;
                                inputByGroup[offset3 - 1] = twiddleFactors[twiddleOffset + twiddleIndex - 1] * output[offset3 - 1] - twiddleFactors[twiddleOffset + twiddleIndex] * output[offset3];
                                inputByGroup[offset3] = twiddleFactors[twiddleOffset + twiddleIndex - 1] * output[offset3] + twiddleFactors[twiddleOffset + twiddleIndex] * output[offset3 - 1];
                            }

                            offset2 += innerDimension;
                        }
                    }

                    return;
            }
        }
    }

    private static void BackwardTransformInternal(int size, float[] data, float[] output, float[] twiddleFactors, int twiddleOffset, int[] factorCache)
    {
        int innerIndex, factorStageIndex, nextGroupCount = 0;
        var radix = 0;
        int twiddleStart2;
        int twiddleStart3;
        var innerDimension = 0;
        var groupStride = 0;
        var factorCount = factorCache[1];
        var activeBuffer = 0;
        var groupCount = 1;
        var twiddleStart = 1;

        for (factorStageIndex = 0; factorStageIndex < factorCount; factorStageIndex++)
        {
            var state = 100;
            while (true)
            {
                switch (state)
                {
                    case 100:
                        radix = factorCache[factorStageIndex + 2];
                        nextGroupCount = radix * groupCount;
                        innerDimension = size / nextGroupCount;
                        groupStride = innerDimension * groupCount;
                        if (radix != 4)
                        {
                            state = 103;
                            break;
                        }

                        twiddleStart2 = twiddleStart + innerDimension;
                        twiddleStart3 = twiddleStart2 + innerDimension;

                        if (activeBuffer != 0)
                        {
                            BackwardRadix4(innerDimension, groupCount, output, data, twiddleFactors, twiddleOffset + twiddleStart - 1, twiddleFactors, twiddleOffset + twiddleStart2 - 1, twiddleFactors, twiddleOffset + twiddleStart3 - 1);
                        }
                        else
                        {
                            BackwardRadix4(innerDimension, groupCount, data, output, twiddleFactors, twiddleOffset + twiddleStart - 1, twiddleFactors, twiddleOffset + twiddleStart2 - 1, twiddleFactors, twiddleOffset + twiddleStart3 - 1);
                        }

                        activeBuffer = 1 - activeBuffer;
                        state = 115;
                        break;
                    case 103:
                        if (radix != 2)
                        {
                            state = 106;
                            break;
                        }

                        if (activeBuffer != 0)
                        {
                            BackwardRadix2(innerDimension, groupCount, output, data, twiddleFactors, twiddleOffset + twiddleStart - 1);
                        }
                        else
                        {
                            BackwardRadix2(innerDimension, groupCount, data, output, twiddleFactors, twiddleOffset + twiddleStart - 1);
                        }

                        activeBuffer = 1 - activeBuffer;
                        state = 115;
                        break;
                    case 106:
                        if (radix != 3)
                        {
                            state = 109;
                            break;
                        }

                        twiddleStart2 = twiddleStart + innerDimension;
                        if (activeBuffer != 0)
                        {
                            BackwardRadix3(innerDimension, groupCount, output, data, twiddleFactors, twiddleOffset + twiddleStart - 1, twiddleFactors, twiddleOffset + twiddleStart2 - 1);
                        }
                        else
                        {
                            BackwardRadix3(innerDimension, groupCount, data, output, twiddleFactors, twiddleOffset + twiddleStart - 1, twiddleFactors, twiddleOffset + twiddleStart2 - 1);
                        }

                        activeBuffer = 1 - activeBuffer;
                        state = 115;
                        break;
                    case 109:
                        if (activeBuffer != 0)
                        {
                            BackwardGeneralRadix(innerDimension, radix, groupCount, groupStride, output, output, output, data, data, twiddleFactors, twiddleOffset + twiddleStart - 1);
                        }
                        else
                        {
                            BackwardGeneralRadix(innerDimension, radix, groupCount, groupStride, data, data, data, output, output, twiddleFactors, twiddleOffset + twiddleStart - 1);
                        }

                        if (innerDimension == 1)
                        {
                            activeBuffer = 1 - activeBuffer;
                        }

                        goto case 115;
                    case 115:
                        groupCount = nextGroupCount;
                        twiddleStart += (radix - 1) * innerDimension;
                        goto EndLoop;
                }
            }

        EndLoop:
            ;
        }

        if (activeBuffer == 0)
        {
            return;
        }

        for (innerIndex = 0; innerIndex < size; innerIndex++)
        {
            data[innerIndex] = output[innerIndex];
        }
    }
}
