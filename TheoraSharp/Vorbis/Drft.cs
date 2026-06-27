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
        Span<int> offsets = stackalloc int[7];

        offsets[1] = 0;
        var groupBlockSize = offsets[2] = groupCount * innerDimension;
        offsets[3] = innerDimension << 1;
        for (groupIndex = 0; groupIndex < groupCount; groupIndex++)
        {
            output[offsets[1] << 1] = input[offsets[1]] + input[offsets[2]];
            output[(offsets[1] << 1) + offsets[3] - 1] = input[offsets[1]] - input[offsets[2]];
            offsets[1] += innerDimension;
            offsets[2] += innerDimension;
        }

        if (innerDimension < 2)
        {
            return;
        }

        if (innerDimension != 2)
        {
            offsets[1] = 0;
            offsets[2] = groupBlockSize;
            for (groupIndex = 0; groupIndex < groupCount; groupIndex++)
            {
                offsets[3] = offsets[2];
                offsets[4] = (offsets[1] << 1) + (innerDimension << 1);
                offsets[5] = offsets[1];
                offsets[6] = offsets[1] + offsets[1];
                for (var innerIndex = 2; innerIndex < innerDimension; innerIndex += 2)
                {
                    offsets[3] += 2;
                    offsets[4] -= 2;
                    offsets[5] += 2;
                    offsets[6] += 2;
                    var real2 = twiddle1[twiddleOffset + innerIndex - 2] * input[offsets[3] - 1] + twiddle1[twiddleOffset + innerIndex - 1] * input[offsets[3]];
                    var imag2 = twiddle1[twiddleOffset + innerIndex - 2] * input[offsets[3]] - twiddle1[twiddleOffset + innerIndex - 1] * input[offsets[3] - 1];
                    output[offsets[6]] = input[offsets[5]] + imag2;
                    output[offsets[4]] = imag2 - input[offsets[5]];
                    output[offsets[6] - 1] = input[offsets[5] - 1] + real2;
                    output[offsets[4] - 1] = input[offsets[5] - 1] - real2;
                }

                offsets[1] += innerDimension;
                offsets[2] += innerDimension;
            }

            if (innerDimension % 2 == 1)
            {
                return;
            }
        }

        offsets[3] = offsets[2] = (offsets[1] = innerDimension) - 1;
        offsets[2] += groupBlockSize;
        for (groupIndex = 0; groupIndex < groupCount; groupIndex++)
        {
            output[offsets[1]] = -input[offsets[2]];
            output[offsets[1] - 1] = input[offsets[3]];
            offsets[1] += innerDimension << 1;
            offsets[2] += innerDimension;
            offsets[3] += innerDimension;
        }
    }

    private static void ForwardRadix4(int innerDimension, int groupCount, float[] input, float[] output, float[] twiddle1, 
        int twiddleOffset1, float[] twiddle2, int twiddleOffset2, float[] twiddle3, int twiddleOffset3)
    {
        int groupIndex;
        Span<int> offsets = stackalloc int[7];
        float imag1, real1, real2;
        var groupBlockSize = groupCount * innerDimension;

        offsets[1] = groupBlockSize;
        offsets[4] = offsets[1] << 1;
        offsets[2] = offsets[1] + (offsets[1] << 1);
        offsets[3] = 0;

        for (groupIndex = 0; groupIndex < groupCount; groupIndex++)
        {
            real1 = input[offsets[1]] + input[offsets[2]];
            real2 = input[offsets[3]] + input[offsets[4]];

            output[offsets[5] = offsets[3] << 2] = real1 + real2;
            output[(innerDimension << 2) + offsets[5] - 1] = real2 - real1;
            output[(offsets[5] += innerDimension << 1) - 1] = input[offsets[3]] - input[offsets[4]];
            output[offsets[5]] = input[offsets[2]] - input[offsets[1]];

            offsets[1] += innerDimension;
            offsets[2] += innerDimension;
            offsets[3] += innerDimension;
            offsets[4] += innerDimension;
        }

        if (innerDimension < 2)
        {
            return;
        }

        if (innerDimension != 2)
        {
            offsets[1] = 0;
            for (groupIndex = 0; groupIndex < groupCount; groupIndex++)
            {
                offsets[2] = offsets[1];
                offsets[4] = offsets[1] << 2;
                offsets[5] = (offsets[6] = innerDimension << 1) + offsets[4];
                for (var innerIndex = 2; innerIndex < innerDimension; innerIndex += 2)
                {
                    offsets[3] = offsets[2] += 2;
                    offsets[4] += 2;
                    offsets[5] -= 2;

                    offsets[3] += groupBlockSize;
                    var rotatedReal2 = twiddle1[twiddleOffset1 + innerIndex - 2] * input[offsets[3] - 1] + twiddle1[twiddleOffset1 + innerIndex - 1] * input[offsets[3]];
                    var rotatedImag2 = twiddle1[twiddleOffset1 + innerIndex - 2] * input[offsets[3]] - twiddle1[twiddleOffset1 + innerIndex - 1] * input[offsets[3] - 1];
                    offsets[3] += groupBlockSize;
                    var rotatedReal3 = twiddle2[twiddleOffset2 + innerIndex - 2] * input[offsets[3] - 1] + twiddle2[twiddleOffset2 + innerIndex - 1] * input[offsets[3]];
                    var rotatedImag3 = twiddle2[twiddleOffset2 + innerIndex - 2] * input[offsets[3]] - twiddle2[twiddleOffset2 + innerIndex - 1] * input[offsets[3] - 1];
                    offsets[3] += groupBlockSize;
                    var rotatedReal4 = twiddle3[twiddleOffset3 + innerIndex - 2] * input[offsets[3] - 1] + twiddle3[twiddleOffset3 + innerIndex - 1] * input[offsets[3]];
                    var rotatedImag4 = twiddle3[twiddleOffset3 + innerIndex - 2] * input[offsets[3]] - twiddle3[twiddleOffset3 + innerIndex - 1] * input[offsets[3] - 1];

                    real1 = rotatedReal2 + rotatedReal4;
                    var real4 = rotatedReal4 - rotatedReal2;
                    imag1 = rotatedImag2 + rotatedImag4;
                    var imag4 = rotatedImag2 - rotatedImag4;

                    var imag2 = input[offsets[2]] + rotatedImag3;
                    var imag3 = input[offsets[2]] - rotatedImag3;
                    real2 = input[offsets[2] - 1] + rotatedReal3;
                    var real3 = input[offsets[2] - 1] - rotatedReal3;

                    output[offsets[4] - 1] = real1 + real2;
                    output[offsets[4]] = imag1 + imag2;

                    output[offsets[5] - 1] = real3 - imag4;
                    output[offsets[5]] = real4 - imag3;

                    output[offsets[4] + offsets[6] - 1] = imag4 + real3;
                    output[offsets[4] + offsets[6]] = real4 + imag3;

                    output[offsets[5] + offsets[6] - 1] = real2 - real1;
                    output[offsets[5] + offsets[6]] = imag1 - imag2;
                }

                offsets[1] += innerDimension;
            }

            if ((innerDimension & 1) != 0)
            {
                return;
            }
        }

        offsets[2] = (offsets[1] = groupBlockSize + innerDimension - 1) + (groupBlockSize << 1);
        offsets[3] = innerDimension << 2;
        offsets[4] = innerDimension;
        offsets[5] = innerDimension << 1;
        offsets[6] = innerDimension;

        for (groupIndex = 0; groupIndex < groupCount; groupIndex++)
        {
            imag1 = -HalfSqrt2 * (input[offsets[1]] + input[offsets[2]]);
            real1 = HalfSqrt2 * (input[offsets[1]] - input[offsets[2]]);

            output[offsets[4] - 1] = real1 + input[offsets[6] - 1];
            output[offsets[4] + offsets[5] - 1] = input[offsets[6] - 1] - real1;

            output[offsets[4]] = imag1 - input[offsets[1] + groupBlockSize];
            output[offsets[4] + offsets[5]] = imag1 + input[offsets[1] + groupBlockSize];

            offsets[1] += innerDimension;
            offsets[2] += innerDimension;
            offsets[4] += offsets[3];
            offsets[6] += innerDimension;
        }
    }

    private static void ForwardGeneralRadix(int innerDimension, int radix, int groupCount, int groupStride, float[] input, 
        float[] inputByGroup, float[] inputByStride, float[] output, float[] outputByStride, float[] twiddleFactors, int twiddleOffset)
    {
        Span<int> offsets = stackalloc int[10];
        offsets[2] = 0;
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

                    offsets[1] = 0;
                    for (radixIndex = 1; radixIndex < radix; radixIndex++)
                    {
                        offsets[1] += groupBlockSize;
                        offsets[2] = offsets[1];
                        for (groupIndex = 0; groupIndex < groupCount; groupIndex++)
                        {
                            output[offsets[2]] = inputByGroup[offsets[2]];
                            offsets[2] += innerDimension;
                        }
                    }

                    var twiddleBaseOffset = -innerDimension;
                    offsets[1] = 0;
                    int twiddleIndex;
                    if (halfInnerDimension > groupCount)
                    {
                        for (radixIndex = 1; radixIndex < radix; radixIndex++)
                        {
                            offsets[1] += groupBlockSize;
                            twiddleBaseOffset += innerDimension;
                            offsets[2] = -innerDimension + offsets[1];
                            for (groupIndex = 0; groupIndex < groupCount; groupIndex++)
                            {
                                twiddleIndex = twiddleBaseOffset - 1;
                                offsets[2] += innerDimension;
                                offsets[3] = offsets[2];
                                for (innerIndex = 2; innerIndex < innerDimension; innerIndex += 2)
                                {
                                    twiddleIndex += 2;
                                    offsets[3] += 2;
                                    output[offsets[3] - 1] = twiddleFactors[twiddleOffset + twiddleIndex - 1] * inputByGroup[offsets[3] - 1] + twiddleFactors[twiddleOffset + twiddleIndex] * inputByGroup[offsets[3]];
                                    output[offsets[3]] = twiddleFactors[twiddleOffset + twiddleIndex - 1] * inputByGroup[offsets[3]] - twiddleFactors[twiddleOffset + twiddleIndex] * inputByGroup[offsets[3] - 1];
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
                            offsets[1] += groupBlockSize;
                            offsets[2] = offsets[1];
                            for (innerIndex = 2; innerIndex < innerDimension; innerIndex += 2)
                            {
                                twiddleIndex += 2;
                                offsets[2] += 2;
                                offsets[3] = offsets[2];
                                for (groupIndex = 0; groupIndex < groupCount; groupIndex++)
                                {
                                    output[offsets[3] - 1] = twiddleFactors[twiddleOffset + twiddleIndex - 1] * inputByGroup[offsets[3] - 1] + twiddleFactors[twiddleOffset + twiddleIndex] * inputByGroup[offsets[3]];
                                    output[offsets[3]] = twiddleFactors[twiddleOffset + twiddleIndex - 1] * inputByGroup[offsets[3]] - twiddleFactors[twiddleOffset + twiddleIndex] * inputByGroup[offsets[3] - 1];
                                    offsets[3] += innerDimension;
                                }
                            }
                        }
                    }

                    offsets[1] = 0;
                    offsets[2] = radixOffsetLimit * groupBlockSize;
                    if (halfInnerDimension < groupCount)
                    {
                        for (radixIndex = 1; radixIndex < halfRadixCount; radixIndex++)
                        {
                            offsets[1] += groupBlockSize;
                            offsets[2] -= groupBlockSize;
                            offsets[3] = offsets[1];
                            offsets[4] = offsets[2];
                            for (innerIndex = 2; innerIndex < innerDimension; innerIndex += 2)
                            {
                                offsets[3] += 2;
                                offsets[4] += 2;
                                offsets[5] = offsets[3] - innerDimension;
                                offsets[6] = offsets[4] - innerDimension;
                                for (groupIndex = 0; groupIndex < groupCount; groupIndex++)
                                {
                                    offsets[5] += innerDimension;
                                    offsets[6] += innerDimension;
                                    inputByGroup[offsets[5] - 1] = output[offsets[5] - 1] + output[offsets[6] - 1];
                                    inputByGroup[offsets[6] - 1] = output[offsets[5]] - output[offsets[6]];
                                    inputByGroup[offsets[5]] = output[offsets[5]] + output[offsets[6]];
                                    inputByGroup[offsets[6]] = output[offsets[6] - 1] - output[offsets[5] - 1];
                                }
                            }
                        }
                    }
                    else
                    {
                        for (radixIndex = 1; radixIndex < halfRadixCount; radixIndex++)
                        {
                            offsets[1] += groupBlockSize;
                            offsets[2] -= groupBlockSize;
                            offsets[3] = offsets[1];
                            offsets[4] = offsets[2];
                            for (groupIndex = 0; groupIndex < groupCount; groupIndex++)
                            {
                                offsets[5] = offsets[3];
                                offsets[6] = offsets[4];
                                for (innerIndex = 2; innerIndex < innerDimension; innerIndex += 2)
                                {
                                    offsets[5] += 2;
                                    offsets[6] += 2;
                                    inputByGroup[offsets[5] - 1] = output[offsets[5] - 1] + output[offsets[6] - 1];
                                    inputByGroup[offsets[6] - 1] = output[offsets[5]] - output[offsets[6]];
                                    inputByGroup[offsets[5]] = output[offsets[5]] + output[offsets[6]];
                                    inputByGroup[offsets[6]] = output[offsets[6] - 1] - output[offsets[5] - 1];
                                }

                                offsets[3] += innerDimension;
                                offsets[4] += innerDimension;
                            }
                        }
                    }

                    goto case 119;
                case 119:
                    for (strideIndex = 0; strideIndex < groupStride; strideIndex++)
                    {
                        inputByStride[strideIndex] = outputByStride[strideIndex];
                    }

                    offsets[1] = 0;
                    offsets[2] = radixOffsetLimit * groupStride;
                    for (radixIndex = 1; radixIndex < halfRadixCount; radixIndex++)
                    {
                        offsets[1] += groupBlockSize;
                        offsets[2] -= groupBlockSize;
                        offsets[3] = offsets[1] - innerDimension;
                        offsets[4] = offsets[2] - innerDimension;
                        for (groupIndex = 0; groupIndex < groupCount; groupIndex++)
                        {
                            offsets[3] += innerDimension;
                            offsets[4] += innerDimension;
                            inputByGroup[offsets[3]] = output[offsets[3]] + output[offsets[4]];
                            inputByGroup[offsets[4]] = output[offsets[4]] - output[offsets[3]];
                        }
                    }

                    realAccumulator1 = 1f;
                    imagAccumulator1 = 0f;
                    offsets[1] = 0;
                    offsets[2] = radixOffsetLimit * groupStride;
                    offsets[3] = (radix - 1) * groupStride;
                    int harmonicIndex;
                    for (harmonicIndex = 1; harmonicIndex < halfRadixCount; harmonicIndex++)
                    {
                        offsets[1] += groupStride;
                        offsets[2] -= groupStride;
                        nextRealAccumulator1 = radixCosineStep * realAccumulator1 - radixSineStep * imagAccumulator1;
                        imagAccumulator1 = radixCosineStep * imagAccumulator1 + radixSineStep * realAccumulator1;
                        realAccumulator1 = nextRealAccumulator1;
                        offsets[4] = offsets[1];
                        offsets[5] = offsets[2];
                        offsets[6] = offsets[3];
                        offsets[7] = groupStride;

                        for (strideIndex = 0; strideIndex < groupStride; strideIndex++)
                        {
                            outputByStride[offsets[4]++] = inputByStride[strideIndex] + realAccumulator1 * inputByStride[offsets[7]++];
                            outputByStride[offsets[5]++] = imagAccumulator1 * inputByStride[offsets[6]++];
                        }

                        cosineStep = realAccumulator1;
                        sineStep = imagAccumulator1;
                        realAccumulator2 = realAccumulator1;
                        imagAccumulator2 = imagAccumulator1;

                        offsets[4] = groupStride;
                        offsets[5] = (radixOffsetLimit - 1) * groupStride;
                        for (radixIndex = 2; radixIndex < halfRadixCount; radixIndex++)
                        {
                            offsets[4] += groupStride;
                            offsets[5] -= groupStride;

                            nextRealAccumulator2 = cosineStep * realAccumulator2 - sineStep * imagAccumulator2;
                            imagAccumulator2 = cosineStep * imagAccumulator2 + sineStep * realAccumulator2;
                            realAccumulator2 = nextRealAccumulator2;

                            offsets[6] = offsets[1];
                            offsets[7] = offsets[2];
                            offsets[8] = offsets[4];
                            offsets[9] = offsets[5];
                            for (strideIndex = 0; strideIndex < groupStride; strideIndex++)
                            {
                                outputByStride[offsets[6]++] += realAccumulator2 * inputByStride[offsets[8]++];
                                outputByStride[offsets[7]++] += imagAccumulator2 * inputByStride[offsets[9]++];
                            }
                        }
                    }

                    offsets[1] = 0;
                    for (radixIndex = 1; radixIndex < halfRadixCount; radixIndex++)
                    {
                        offsets[1] += groupStride;
                        offsets[2] = offsets[1];
                        for (strideIndex = 0; strideIndex < groupStride; strideIndex++)
                        {
                            outputByStride[strideIndex] += inputByStride[offsets[2]++];
                        }
                    }

                    if (innerDimension < groupCount)
                    {
                        state = 132;
                        break;
                    }

                    offsets[1] = 0;
                    offsets[2] = 0;
                    for (groupIndex = 0; groupIndex < groupCount; groupIndex++)
                    {
                        offsets[3] = offsets[1];
                        offsets[4] = offsets[2];
                        for (innerIndex = 0; innerIndex < innerDimension; innerIndex++)
                        {
                            input[offsets[4]++] = output[offsets[3]++];
                        }

                        offsets[1] += innerDimension;
                        offsets[2] += radixBlockSize;
                    }

                    state = 135;
                    break;
                case 132:
                    for (innerIndex = 0; innerIndex < innerDimension; innerIndex++)
                    {
                        offsets[1] = innerIndex;
                        offsets[2] = innerIndex;
                        for (groupIndex = 0; groupIndex < groupCount; groupIndex++)
                        {
                            input[offsets[2]] = output[offsets[1]];
                            offsets[1] += innerDimension;
                            offsets[2] += radixBlockSize;
                        }
                    }

                    goto case 135;
                case 135:
                    offsets[1] = 0;
                    offsets[2] = innerDimension << 1;
                    offsets[3] = 0;
                    offsets[4] = radixOffsetLimit * groupBlockSize;
                    for (radixIndex = 1; radixIndex < halfRadixCount; radixIndex++)
                    {
                        offsets[1] += offsets[2];
                        offsets[3] += groupBlockSize;
                        offsets[4] -= groupBlockSize;

                        offsets[5] = offsets[1];
                        offsets[6] = offsets[3];
                        offsets[7] = offsets[4];

                        for (groupIndex = 0; groupIndex < groupCount; groupIndex++)
                        {
                            input[offsets[5] - 1] = output[offsets[6]];
                            input[offsets[5]] = output[offsets[7]];
                            offsets[5] += radixBlockSize;
                            offsets[6] += innerDimension;
                            offsets[7] += innerDimension;
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

                    offsets[1] = -innerDimension;
                    offsets[3] = 0;
                    offsets[4] = 0;
                    offsets[5] = radixOffsetLimit * groupBlockSize;
                    for (radixIndex = 1; radixIndex < halfRadixCount; radixIndex++)
                    {
                        offsets[1] += offsets[2];
                        offsets[3] += offsets[2];
                        offsets[4] += groupBlockSize;
                        offsets[5] -= groupBlockSize;
                        offsets[6] = offsets[1];
                        offsets[7] = offsets[3];
                        offsets[8] = offsets[4];
                        offsets[9] = offsets[5];
                        for (groupIndex = 0; groupIndex < groupCount; groupIndex++)
                        {
                            for (innerIndex = 2; innerIndex < innerDimension; innerIndex += 2)
                            {
                                var ic = innerOffsetLimit - innerIndex;
                                input[innerIndex + offsets[7] - 1] = output[innerIndex + offsets[8] - 1] + output[innerIndex + offsets[9] - 1];
                                input[ic + offsets[6] - 1] = output[innerIndex + offsets[8] - 1] - output[innerIndex + offsets[9] - 1];
                                input[innerIndex + offsets[7]] = output[innerIndex + offsets[8]] + output[innerIndex + offsets[9]];
                                input[ic + offsets[6]] = output[innerIndex + offsets[9]] - output[innerIndex + offsets[8]];
                            }

                            offsets[6] += radixBlockSize;
                            offsets[7] += radixBlockSize;
                            offsets[8] += innerDimension;
                            offsets[9] += innerDimension;
                        }
                    }

                    return;
                case 141:
                    offsets[1] = -innerDimension;
                    offsets[3] = 0;
                    offsets[4] = 0;
                    offsets[5] = radixOffsetLimit * groupBlockSize;
                    for (radixIndex = 1; radixIndex < halfRadixCount; radixIndex++)
                    {
                        offsets[1] += offsets[2];
                        offsets[3] += offsets[2];
                        offsets[4] += groupBlockSize;
                        offsets[5] -= groupBlockSize;
                        for (innerIndex = 2; innerIndex < innerDimension; innerIndex += 2)
                        {
                            offsets[6] = innerOffsetLimit + offsets[1] - innerIndex;
                            offsets[7] = innerIndex + offsets[3];
                            offsets[8] = innerIndex + offsets[4];
                            offsets[9] = innerIndex + offsets[5];
                            for (groupIndex = 0; groupIndex < groupCount; groupIndex++)
                            {
                                input[offsets[7] - 1] = output[offsets[8] - 1] + output[offsets[9] - 1];
                                input[offsets[6] - 1] = output[offsets[8] - 1] - output[offsets[9] - 1];
                                input[offsets[7]] = output[offsets[8]] + output[offsets[9]];
                                input[offsets[6]] = output[offsets[9]] - output[offsets[8]];
                                offsets[6] += radixBlockSize;
                                offsets[7] += radixBlockSize;
                                offsets[8] += innerDimension;
                                offsets[9] += innerDimension;
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
        int innerIndex, groupIndex, groupBlockSize;
        Span<int> offsets = stackalloc int[7];
        float imag2, real2;

        groupBlockSize = groupCount * innerDimension;

        offsets[1] = 0;
        offsets[2] = 0;
        offsets[3] = (innerDimension << 1) - 1;
        for (groupIndex = 0; groupIndex < groupCount; groupIndex++)
        {
            output[offsets[1]] = input[offsets[2]] + input[offsets[3] + offsets[2]];
            output[offsets[1] + groupBlockSize] = input[offsets[2]] - input[offsets[3] + offsets[2]];
            offsets[2] = (offsets[1] += innerDimension) << 1;
        }

        if (innerDimension < 2)
        {
            return;
        }

        if (innerDimension != 2)
        {
            offsets[1] = 0;
            offsets[2] = 0;
            for (groupIndex = 0; groupIndex < groupCount; groupIndex++)
            {
                offsets[3] = offsets[1];
                offsets[5] = (offsets[4] = offsets[2]) + (innerDimension << 1);
                offsets[6] = groupBlockSize + offsets[1];
                for (innerIndex = 2; innerIndex < innerDimension; innerIndex += 2)
                {
                    offsets[3] += 2;
                    offsets[4] += 2;
                    offsets[5] -= 2;
                    offsets[6] += 2;
                    output[offsets[3] - 1] = input[offsets[4] - 1] + input[offsets[5] - 1];
                    real2 = input[offsets[4] - 1] - input[offsets[5] - 1];
                    output[offsets[3]] = input[offsets[4]] - input[offsets[5]];
                    imag2 = input[offsets[4]] + input[offsets[5]];
                    output[offsets[6] - 1] = twiddle1[twiddleOffset + innerIndex - 2] * real2 - twiddle1[twiddleOffset + innerIndex - 1] * imag2;
                    output[offsets[6]] = twiddle1[twiddleOffset + innerIndex - 2] * imag2 + twiddle1[twiddleOffset + innerIndex - 1] * real2;
                }

                offsets[2] = (offsets[1] += innerDimension) << 1;
            }

            if ((innerDimension % 2) == 1)
            {
                return;
            }
        }

        offsets[1] = innerDimension - 1;
        offsets[2] = innerDimension - 1;
        for (groupIndex = 0; groupIndex < groupCount; groupIndex++)
        {
            output[offsets[1]] = input[offsets[2]] + input[offsets[2]];
            output[offsets[1] + groupBlockSize] = -(input[offsets[2] + 1] + input[offsets[2] + 1]);
            offsets[1] += innerDimension;
            offsets[2] += innerDimension << 1;
        }
    }

    private static void BackwardRadix3(int innerDimension, int groupCount, float[] input, float[] output, float[] twiddle1,
        int twiddleOffset1, float[] twiddle2, int twiddleOffset2)
    {
        int innerIndex, groupIndex, radixBlockSize;
        Span<int> offsets = stackalloc int[10];
        float rotatedImag2, rotatedImag3, combinedImag2, combinedImag3, rotatedReal2, rotatedReal3, combinedReal2, combinedReal3, imag2, real2;
        var groupBlockSize = groupCount * innerDimension;

        offsets[1] = 0;
        offsets[2] = groupBlockSize << 1;
        offsets[3] = innerDimension << 1;
        offsets[4] = innerDimension + (innerDimension << 1);
        offsets[5] = 0;
        for (groupIndex = 0; groupIndex < groupCount; groupIndex++)
        {
            real2 = input[offsets[3] - 1] + input[offsets[3] - 1];
            rotatedReal2 = input[offsets[5]] + MinusHalf * real2;
            output[offsets[1]] = input[offsets[5]] + real2;
            rotatedImag3 = Sqrt3Over2 * (input[offsets[3]] + input[offsets[3]]);
            output[offsets[1] + groupBlockSize] = rotatedReal2 - rotatedImag3;
            output[offsets[1] + offsets[2]] = rotatedReal2 + rotatedImag3;
            offsets[1] += innerDimension;
            offsets[3] += offsets[4];
            offsets[5] += offsets[4];
        }

        if (innerDimension == 1)
        {
            return;
        }

        offsets[1] = 0;
        offsets[3] = innerDimension << 1;
        for (groupIndex = 0; groupIndex < groupCount; groupIndex++)
        {
            offsets[7] = offsets[1] + (offsets[1] << 1);
            offsets[6] = offsets[5] = offsets[7] + offsets[3];
            offsets[8] = offsets[1];
            radixBlockSize = (offsets[9] = offsets[1] + groupBlockSize) + groupBlockSize;

            for (innerIndex = 2; innerIndex < innerDimension; innerIndex += 2)
            {
                offsets[5] += 2;
                offsets[6] -= 2;
                offsets[7] += 2;
                offsets[8] += 2;
                offsets[9] += 2;
                radixBlockSize += 2;
                real2 = input[offsets[5] - 1] + input[offsets[6] - 1];
                rotatedReal2 = input[offsets[7] - 1] + MinusHalf * real2;
                output[offsets[8] - 1] = input[offsets[7] - 1] + real2;
                imag2 = input[offsets[5]] - input[offsets[6]];
                rotatedImag2 = input[offsets[7]] + MinusHalf * imag2;
                output[offsets[8]] = input[offsets[7]] + imag2;
                rotatedReal3 = Sqrt3Over2 * (input[offsets[5] - 1] - input[offsets[6] - 1]);
                rotatedImag3 = Sqrt3Over2 * (input[offsets[5]] + input[offsets[6]]);
                combinedReal2 = rotatedReal2 - rotatedImag3;
                combinedReal3 = rotatedReal2 + rotatedImag3;
                combinedImag2 = rotatedImag2 + rotatedReal3;
                combinedImag3 = rotatedImag2 - rotatedReal3;
                output[offsets[9] - 1] = twiddle1[twiddleOffset1 + innerIndex - 2] * combinedReal2 - twiddle1[twiddleOffset1 + innerIndex - 1] * combinedImag2;
                output[offsets[9]] = twiddle1[twiddleOffset1 + innerIndex - 2] * combinedImag2 + twiddle1[twiddleOffset1 + innerIndex - 1] * combinedReal2;
                output[radixBlockSize - 1] = twiddle2[twiddleOffset2 + innerIndex - 2] * combinedReal3 - twiddle2[twiddleOffset2 + innerIndex - 1] * combinedImag3;
                output[radixBlockSize] = twiddle2[twiddleOffset2 + innerIndex - 2] * combinedImag3 + twiddle2[twiddleOffset2 + innerIndex - 1] * combinedReal3;
            }

            offsets[1] += innerDimension;
        }
    }

    private static void BackwardRadix4(int innerDimension, int groupCount, float[] input, float[] output, float[] twiddle1,
        int twiddleOffset1, float[] twiddle2, int twiddleOffset2, float[] twiddle3, int twiddleOffset3)
    {
        int innerIndex, groupIndex;
        Span<int> offsets = stackalloc int[9];
        float rotatedImag2, rotatedImag3, rotatedImag4, rotatedReal2, rotatedReal3, rotatedReal4, imag1, imag2, imag3, imag4, real1, real2, real3, real4;
        var groupBlockSize = groupCount * innerDimension;

        offsets[1] = 0;
        offsets[2] = innerDimension << 2;
        offsets[3] = 0;
        offsets[6] = innerDimension << 1;
        for (groupIndex = 0; groupIndex < groupCount; groupIndex++)
        {
            offsets[4] = offsets[3] + offsets[6];
            offsets[5] = offsets[1];
            real3 = input[offsets[4] - 1] + input[offsets[4] - 1];
            real4 = input[offsets[4]] + input[offsets[4]];
            real1 = input[offsets[3]] - input[(offsets[4] += offsets[6]) - 1];
            real2 = input[offsets[3]] + input[offsets[4] - 1];
            output[offsets[5]] = real2 + real3;
            output[offsets[5] += groupBlockSize] = real1 - real4;
            output[offsets[5] += groupBlockSize] = real2 - real3;
            output[offsets[5] += groupBlockSize] = real1 + real4;
            offsets[1] += innerDimension;
            offsets[3] += offsets[2];
        }

        if (innerDimension < 2)
        {
            return;
        }

        if (innerDimension != 2)
        {
            offsets[1] = 0;
            for (groupIndex = 0; groupIndex < groupCount; groupIndex++)
            {
                offsets[5] = (offsets[4] = (offsets[3] = (offsets[2] = offsets[1] << 2) + offsets[6])) + offsets[6];
                offsets[7] = offsets[1];
                for (innerIndex = 2; innerIndex < innerDimension; innerIndex += 2)
                {
                    offsets[2] += 2;
                    offsets[3] += 2;
                    offsets[4] -= 2;
                    offsets[5] -= 2;
                    offsets[7] += 2;
                    imag1 = input[offsets[2]] + input[offsets[5]];
                    imag2 = input[offsets[2]] - input[offsets[5]];
                    imag3 = input[offsets[3]] - input[offsets[4]];
                    real4 = input[offsets[3]] + input[offsets[4]];
                    real1 = input[offsets[2] - 1] - input[offsets[5] - 1];
                    real2 = input[offsets[2] - 1] + input[offsets[5] - 1];
                    imag4 = input[offsets[3] - 1] - input[offsets[4] - 1];
                    real3 = input[offsets[3] - 1] + input[offsets[4] - 1];
                    output[offsets[7] - 1] = real2 + real3;
                    rotatedReal3 = real2 - real3;
                    output[offsets[7]] = imag2 + imag3;
                    rotatedImag3 = imag2 - imag3;
                    rotatedReal2 = real1 - real4;
                    rotatedReal4 = real1 + real4;
                    rotatedImag2 = imag1 + imag4;
                    rotatedImag4 = imag1 - imag4;

                    output[(offsets[8] = offsets[7] + groupBlockSize) - 1] = twiddle1[twiddleOffset1 + innerIndex - 2] * rotatedReal2 - twiddle1[twiddleOffset1 + innerIndex - 1] * rotatedImag2;
                    output[offsets[8]] = twiddle1[twiddleOffset1 + innerIndex - 2] * rotatedImag2 + twiddle1[twiddleOffset1 + innerIndex - 1] * rotatedReal2;
                    output[(offsets[8] += groupBlockSize) - 1] = twiddle2[twiddleOffset2 + innerIndex - 2] * rotatedReal3 - twiddle2[twiddleOffset2 + innerIndex - 1] * rotatedImag3;
                    output[offsets[8]] = twiddle2[twiddleOffset2 + innerIndex - 2] * rotatedImag3 + twiddle2[twiddleOffset2 + innerIndex - 1] * rotatedReal3;
                    output[(offsets[8] += groupBlockSize) - 1] = twiddle3[twiddleOffset3 + innerIndex - 2] * rotatedReal4 - twiddle3[twiddleOffset3 + innerIndex - 1] * rotatedImag4;
                    output[offsets[8]] = twiddle3[twiddleOffset3 + innerIndex - 2] * rotatedImag4 + twiddle3[twiddleOffset3 + innerIndex - 1] * rotatedReal4;
                }

                offsets[1] += innerDimension;
            }

            if (innerDimension % 2 == 1)
            {
                return;
            }
        }

        offsets[1] = innerDimension;
        offsets[2] = innerDimension << 2;
        offsets[3] = innerDimension - 1;
        offsets[4] = innerDimension + (innerDimension << 1);
        for (groupIndex = 0; groupIndex < groupCount; groupIndex++)
        {
            offsets[5] = offsets[3];
            imag1 = input[offsets[1]] + input[offsets[4]];
            imag2 = input[offsets[4]] - input[offsets[1]];
            real1 = input[offsets[1] - 1] - input[offsets[4] - 1];
            real2 = input[offsets[1] - 1] + input[offsets[4] - 1];
            output[offsets[5]] = real2 + real2;
            output[offsets[5] += groupBlockSize] = Sqrt2 * (real1 - imag1);
            output[offsets[5] += groupBlockSize] = imag2 + imag2;
            output[offsets[5] += groupBlockSize] = -Sqrt2 * (real1 + imag1);

            offsets[3] += innerDimension;
            offsets[1] += offsets[2];
            offsets[4] += offsets[2];
        }
    }

    private static void BackwardGeneralRadix(int innerDimension, int radix, int groupCount, int groupStride, float[] input,
        float[] inputByGroup, float[] inputByStride, float[] output, float[] outputByStride, float[] twiddleFactors, int twiddleOffset)
    {
        int twiddleIndex, halfRadixCount = 0, innerIndex, radixIndex, groupIndex, harmonicIndex, strideIndex, twiddleBaseOffset, groupBlockSize = 0, radixBlockSize = 0;
        Span<int> offsets = stackalloc int[13];
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

                    offsets[1] = 0;
                    offsets[2] = 0;
                    for (groupIndex = 0; groupIndex < groupCount; groupIndex++)
                    {
                        offsets[3] = offsets[1];
                        offsets[4] = offsets[2];
                        for (innerIndex = 0; innerIndex < innerDimension; innerIndex++)
                        {
                            output[offsets[3]] = input[offsets[4]];
                            offsets[3]++;
                            offsets[4]++;
                        }

                        offsets[1] += innerDimension;
                        offsets[2] += radixBlockSize;
                    }

                    state = 106;
                    break;
                case 103:
                    offsets[1] = 0;
                    for (innerIndex = 0; innerIndex < innerDimension; innerIndex++)
                    {
                        offsets[2] = offsets[1];
                        offsets[3] = offsets[1];
                        for (groupIndex = 0; groupIndex < groupCount; groupIndex++)
                        {
                            output[offsets[2]] = input[offsets[3]];
                            offsets[2] += innerDimension;
                            offsets[3] += radixBlockSize;
                        }

                        offsets[1]++;
                    }

                    goto case 106;
                case 106:
                    offsets[1] = 0;
                    offsets[2] = radixOffsetLimit * groupBlockSize;
                    offsets[7] = offsets[5] = innerDimension << 1;
                    for (radixIndex = 1; radixIndex < halfRadixCount; radixIndex++)
                    {
                        offsets[1] += groupBlockSize;
                        offsets[2] -= groupBlockSize;
                        offsets[3] = offsets[1];
                        offsets[4] = offsets[2];
                        offsets[6] = offsets[5];
                        for (groupIndex = 0; groupIndex < groupCount; groupIndex++)
                        {
                            output[offsets[3]] = input[offsets[6] - 1] + input[offsets[6] - 1];
                            output[offsets[4]] = input[offsets[6]] + input[offsets[6]];
                            offsets[3] += innerDimension;
                            offsets[4] += innerDimension;
                            offsets[6] += radixBlockSize;
                        }

                        offsets[5] += offsets[7];
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

                    offsets[1] = 0;
                    offsets[2] = radixOffsetLimit * groupBlockSize;
                    offsets[7] = 0;
                    for (radixIndex = 1; radixIndex < halfRadixCount; radixIndex++)
                    {
                        offsets[1] += groupBlockSize;
                        offsets[2] -= groupBlockSize;
                        offsets[3] = offsets[1];
                        offsets[4] = offsets[2];
                        offsets[7] += innerDimension << 1;
                        offsets[8] = offsets[7];
                        for (groupIndex = 0; groupIndex < groupCount; groupIndex++)
                        {
                            offsets[5] = offsets[3];
                            offsets[6] = offsets[4];
                            offsets[9] = offsets[8];
                            offsets[11] = offsets[8];
                            for (innerIndex = 2; innerIndex < innerDimension; innerIndex += 2)
                            {
                                offsets[5] += 2;
                                offsets[6] += 2;
                                offsets[9] += 2;
                                offsets[11] -= 2;
                                output[offsets[5] - 1] = input[offsets[9] - 1] + input[offsets[11] - 1];
                                output[offsets[6] - 1] = input[offsets[9] - 1] - input[offsets[11] - 1];
                                output[offsets[5]] = input[offsets[9]] - input[offsets[11]];
                                output[offsets[6]] = input[offsets[9]] + input[offsets[11]];
                            }

                            offsets[3] += innerDimension;
                            offsets[4] += innerDimension;
                            offsets[8] += radixBlockSize;
                        }
                    }

                    state = 116;
                    break;
                case 112:
                    offsets[1] = 0;
                    offsets[2] = radixOffsetLimit * groupBlockSize;
                    offsets[7] = 0;
                    for (radixIndex = 1; radixIndex < halfRadixCount; radixIndex++)
                    {
                        offsets[1] += groupBlockSize;
                        offsets[2] -= groupBlockSize;
                        offsets[3] = offsets[1];
                        offsets[4] = offsets[2];
                        offsets[7] += innerDimension << 1;
                        offsets[8] = offsets[7];
                        offsets[9] = offsets[7];
                        for (innerIndex = 2; innerIndex < innerDimension; innerIndex += 2)
                        {
                            offsets[3] += 2;
                            offsets[4] += 2;
                            offsets[8] += 2;
                            offsets[9] -= 2;
                            offsets[5] = offsets[3];
                            offsets[6] = offsets[4];
                            offsets[11] = offsets[8];
                            offsets[12] = offsets[9];
                            for (groupIndex = 0; groupIndex < groupCount; groupIndex++)
                            {
                                output[offsets[5] - 1] = input[offsets[11] - 1] + input[offsets[12] - 1];
                                output[offsets[6] - 1] = input[offsets[11] - 1] - input[offsets[12] - 1];
                                output[offsets[5]] = input[offsets[11]] - input[offsets[12]];
                                output[offsets[6]] = input[offsets[11]] + input[offsets[12]];
                                offsets[5] += innerDimension;
                                offsets[6] += innerDimension;
                                offsets[11] += radixBlockSize;
                                offsets[12] += radixBlockSize;
                            }
                        }
                    }

                    goto case 116;
                case 116:
                    realAccumulator1 = 1f;
                    imagAccumulator1 = 0f;
                    offsets[1] = 0;
                    offsets[9] = offsets[2] = radixOffsetLimit * groupStride;
                    offsets[3] = (radix - 1) * groupStride;
                    for (harmonicIndex = 1; harmonicIndex < halfRadixCount; harmonicIndex++)
                    {
                        offsets[1] += groupStride;
                        offsets[2] -= groupStride;

                        nextRealAccumulator1 = radixCosineStep * realAccumulator1 - radixSineStep * imagAccumulator1;
                        imagAccumulator1 = radixCosineStep * imagAccumulator1 + radixSineStep * realAccumulator1;
                        realAccumulator1 = nextRealAccumulator1;
                        offsets[4] = offsets[1];
                        offsets[5] = offsets[2];
                        offsets[6] = 0;
                        offsets[7] = groupStride;
                        offsets[8] = offsets[3];
                        for (strideIndex = 0; strideIndex < groupStride; strideIndex++)
                        {
                            inputByStride[offsets[4]++] = outputByStride[offsets[6]++] + realAccumulator1 * outputByStride[offsets[7]++];
                            inputByStride[offsets[5]++] = imagAccumulator1 * outputByStride[offsets[8]++];
                        }

                        cosineStep = realAccumulator1;
                        sineStep = imagAccumulator1;
                        realAccumulator2 = realAccumulator1;
                        imagAccumulator2 = imagAccumulator1;

                        offsets[6] = groupStride;
                        offsets[7] = offsets[9] - groupStride;
                        for (radixIndex = 2; radixIndex < halfRadixCount; radixIndex++)
                        {
                            offsets[6] += groupStride;
                            offsets[7] -= groupStride;
                            nextRealAccumulator2 = cosineStep * realAccumulator2 - sineStep * imagAccumulator2;
                            imagAccumulator2 = cosineStep * imagAccumulator2 + sineStep * realAccumulator2;
                            realAccumulator2 = nextRealAccumulator2;
                            offsets[4] = offsets[1];
                            offsets[5] = offsets[2];
                            offsets[11] = offsets[6];
                            offsets[12] = offsets[7];
                            for (strideIndex = 0; strideIndex < groupStride; strideIndex++)
                            {
                                inputByStride[offsets[4]++] += realAccumulator2 * outputByStride[offsets[11]++];
                                inputByStride[offsets[5]++] += imagAccumulator2 * outputByStride[offsets[12]++];
                            }
                        }
                    }

                    offsets[1] = 0;
                    for (radixIndex = 1; radixIndex < halfRadixCount; radixIndex++)
                    {
                        offsets[1] += groupStride;
                        offsets[2] = offsets[1];
                        for (strideIndex = 0; strideIndex < groupStride; strideIndex++)
                        {
                            outputByStride[strideIndex] += outputByStride[offsets[2]++];
                        }
                    }

                    offsets[1] = 0;
                    offsets[2] = radixOffsetLimit * groupBlockSize;
                    for (radixIndex = 1; radixIndex < halfRadixCount; radixIndex++)
                    {
                        offsets[1] += groupBlockSize;
                        offsets[2] -= groupBlockSize;
                        offsets[3] = offsets[1];
                        offsets[4] = offsets[2];
                        for (groupIndex = 0; groupIndex < groupCount; groupIndex++)
                        {
                            output[offsets[3]] = inputByGroup[offsets[3]] - inputByGroup[offsets[4]];
                            output[offsets[4]] = inputByGroup[offsets[3]] + inputByGroup[offsets[4]];
                            offsets[3] += innerDimension;
                            offsets[4] += innerDimension;
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

                    offsets[1] = 0;
                    offsets[2] = radixOffsetLimit * groupBlockSize;
                    for (radixIndex = 1; radixIndex < halfRadixCount; radixIndex++)
                    {
                        offsets[1] += groupBlockSize;
                        offsets[2] -= groupBlockSize;
                        offsets[3] = offsets[1];
                        offsets[4] = offsets[2];
                        for (groupIndex = 0; groupIndex < groupCount; groupIndex++)
                        {
                            offsets[5] = offsets[3];
                            offsets[6] = offsets[4];
                            for (innerIndex = 2; innerIndex < innerDimension; innerIndex += 2)
                            {
                                offsets[5] += 2;
                                offsets[6] += 2;
                                output[offsets[5] - 1] = inputByGroup[offsets[5] - 1] - inputByGroup[offsets[6]];
                                output[offsets[6] - 1] = inputByGroup[offsets[5] - 1] + inputByGroup[offsets[6]];
                                output[offsets[5]] = inputByGroup[offsets[5]] + inputByGroup[offsets[6] - 1];
                                output[offsets[6]] = inputByGroup[offsets[5]] - inputByGroup[offsets[6] - 1];
                            }

                            offsets[3] += innerDimension;
                            offsets[4] += innerDimension;
                        }
                    }

                    state = 132;
                    break;
                case 128:
                    offsets[1] = 0;
                    offsets[2] = radixOffsetLimit * groupBlockSize;
                    for (radixIndex = 1; radixIndex < halfRadixCount; radixIndex++)
                    {
                        offsets[1] += groupBlockSize;
                        offsets[2] -= groupBlockSize;
                        offsets[3] = offsets[1];
                        offsets[4] = offsets[2];
                        for (innerIndex = 2; innerIndex < innerDimension; innerIndex += 2)
                        {
                            offsets[3] += 2;
                            offsets[4] += 2;
                            offsets[5] = offsets[3];
                            offsets[6] = offsets[4];
                            for (groupIndex = 0; groupIndex < groupCount; groupIndex++)
                            {
                                output[offsets[5] - 1] = inputByGroup[offsets[5] - 1] - inputByGroup[offsets[6]];
                                output[offsets[6] - 1] = inputByGroup[offsets[5] - 1] + inputByGroup[offsets[6]];
                                output[offsets[5]] = inputByGroup[offsets[5]] + inputByGroup[offsets[6] - 1];
                                output[offsets[6]] = inputByGroup[offsets[5]] - inputByGroup[offsets[6] - 1];
                                offsets[5] += innerDimension;
                                offsets[6] += innerDimension;
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

                    offsets[1] = 0;
                    for (radixIndex = 1; radixIndex < radix; radixIndex++)
                    {
                        offsets[2] = offsets[1] += groupBlockSize;
                        for (groupIndex = 0; groupIndex < groupCount; groupIndex++)
                        {
                            inputByGroup[offsets[2]] = output[offsets[2]];
                            offsets[2] += innerDimension;
                        }
                    }

                    if (halfInnerDimension > groupCount)
                    {
                        state = 139;
                        break;
                    }

                    twiddleBaseOffset = -innerDimension - 1;
                    offsets[1] = 0;
                    for (radixIndex = 1; radixIndex < radix; radixIndex++)
                    {
                        twiddleBaseOffset += innerDimension;
                        offsets[1] += groupBlockSize;
                        twiddleIndex = twiddleBaseOffset;
                        offsets[2] = offsets[1];
                        for (innerIndex = 2; innerIndex < innerDimension; innerIndex += 2)
                        {
                            offsets[2] += 2;
                            twiddleIndex += 2;
                            offsets[3] = offsets[2];
                            for (groupIndex = 0; groupIndex < groupCount; groupIndex++)
                            {
                                inputByGroup[offsets[3] - 1] = twiddleFactors[twiddleOffset + twiddleIndex - 1] * output[offsets[3] - 1] - twiddleFactors[twiddleOffset + twiddleIndex] * output[offsets[3]];
                                inputByGroup[offsets[3]] = twiddleFactors[twiddleOffset + twiddleIndex - 1] * output[offsets[3]] + twiddleFactors[twiddleOffset + twiddleIndex] * output[offsets[3] - 1];
                                offsets[3] += innerDimension;
                            }
                        }
                    }

                    return;
                case 139:
                    twiddleBaseOffset = -innerDimension - 1;
                    offsets[1] = 0;
                    for (radixIndex = 1; radixIndex < radix; radixIndex++)
                    {
                        twiddleBaseOffset += innerDimension;
                        offsets[1] += groupBlockSize;
                        offsets[2] = offsets[1];
                        for (groupIndex = 0; groupIndex < groupCount; groupIndex++)
                        {
                            twiddleIndex = twiddleBaseOffset;
                            offsets[3] = offsets[2];
                            for (innerIndex = 2; innerIndex < innerDimension; innerIndex += 2)
                            {
                                twiddleIndex += 2;
                                offsets[3] += 2;
                                inputByGroup[offsets[3] - 1] = twiddleFactors[twiddleOffset + twiddleIndex - 1] * output[offsets[3] - 1] - twiddleFactors[twiddleOffset + twiddleIndex] * output[offsets[3]];
                                inputByGroup[offsets[3]] = twiddleFactors[twiddleOffset + twiddleIndex - 1] * output[offsets[3]] + twiddleFactors[twiddleOffset + twiddleIndex] * output[offsets[3] - 1];
                            }

                            offsets[2] += innerDimension;
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
