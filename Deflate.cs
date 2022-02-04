namespace ZipCompressor
{
    class Deflate
    {
        private static readonly (int code, int numExtraBits, int startValue)[] LengthTable = new (int, int, int)[] {
            (257, 0, 3), (258, 0, 4), (259, 0, 5), (260, 0, 6), (261, 0, 7), (262, 0, 8), (263, 0, 9), (264, 0, 10),
            (265, 1, 11), (266, 1, 13), (267, 1, 15), (268, 1, 17), (269, 2, 19), (270, 2, 23), (271, 2, 27),
            (272, 2, 31), (273, 3, 35), (274, 3, 43), (275, 3, 51), (276, 3, 59), (277, 4, 67), (278, 4, 83),
            (279, 4, 99), (280, 4, 115), (281, 5, 131), (282, 5, 163), (283, 5, 195), (284, 5, 227), (285, 0, 258)
            };

        private static readonly (int code, int numExtraBits, int startValue)[] DistanceTable = new (int, int, int)[] {
            (0, 0, 1), (1, 0, 2), (2, 0, 3), (3, 0, 4), (4, 1, 5), (5, 1, 7), (6, 2, 9), (7, 2, 13), (8, 3, 17),
            (9, 3, 25), (10, 4, 33), (11, 4, 49), (12, 5, 65), (13, 5, 97), (14, 6, 129), (15, 6, 193), (16, 7, 257),
            (17, 7, 385), (18, 8, 513), (19, 8, 769), (20, 9, 1025), (21, 9, 1537), (22, 10, 2049), (23, 10, 3073),
            (24, 11, 4097), (25, 11, 6145), (26, 12, 8193), (27, 12, 12289), (28, 13, 16385), (29, 13, 24577)
            };

        public static byte[] Encode(byte[] data)
        {
            List<LDPair> values = GetValues(data);

            List<Code> codes = Encode(values);

            // Console.WriteLine(String.Join(' ', huffValues.Select(v => $"({v.value}, {v.numBits})")));

            BitPacker bp = new BitPacker();
            foreach (var code in codes)
            {
                if (code.huffman)
                    bp.AddReverse(code.value, code.numBits);
                else
                    bp.Add(code.value, code.numBits);
            }

            return bp.ToArray();
        }

        private static List<LDPair> GetValues(byte[] data)
        {
            Dictionary<byte[], List<int>> lookupTable = new Dictionary<byte[], List<int>>(new ByteArrayEqualityComparer());
            List<LDPair> values = new List<LDPair>();

            int index = 0;
            int oldIndex = 0;
            while (index < data.Length)
            {
                // If there are at least 3 bytes left a lookup might be possible
                if (index < data.Length - 2)
                {
                    byte[] next3 = data.Skip(index).Take(3).ToArray();

                    // Check if these three bytes have been encountered before
                    if (lookupTable.ContainsKey(next3))
                    {
                        // Find best match
                        LDPair best = FindBestLengthDistance(data, lookupTable[next3], index);

                        // Use length/distance if one was found
                        if (best.length != 0)
                        {
                            values.Add(best);

                            lookupTable[next3].Add(index);
                            oldIndex = index;
                            index += best.length;
                            continue;
                        }
                    }

                    // Create a new entry in the lookup table for a new set of three bytes
                    if (index < data.Length - 2 && !lookupTable.ContainsKey(next3))
                        lookupTable.Add(next3, new List<int> { index });
                }

                // Add literal to list
                values.Add((data[index], 0));
                oldIndex = index;
                index++;
            }

            return values;
        }

        private static LDPair FindBestLengthDistance(byte[] data, List<int> options, int index)
        {
            LDPair candidate = (0, 0);
            for (int i = options.Count - 1; i >= 0; i--)
            {
                int distance = index - options[i];
                // Check distance is within limit
                if (distance > 32768) break;

                int index2 = index - distance;

                // Increment length as long as there is a match
                int length = 3;
                while (index + length < data.Length && length < 258 && data[index + length] == data[index2 + length]) length++;

                // Update candidate if it is better
                if (length > candidate.length) candidate = new LDPair(length, distance);

                // Break if best case was found
                if (length == 258) break;
            }

            return candidate;
        }

        private static List<Code> Encode(List<LDPair> values)
        {
            // Starts with the values of 1 to indicate the last block and 2 to indicate a fixed huffman encoding is
            // being used (fixed huffman encoding is normally 0b01 but is will be inserted backwards, hence using 0b10)
            List<Code> code = new List<Code>() { (1, 1, false), (1, 2, false) };

            foreach (LDPair value in values)
            {
                // Literal
                if (value.distance == 0)
                {
                    if (value.literal <= 143) code.Add((value.literal + 0b00110000, 8, true));
                    else code.Add((value.literal - 144 + 0b110010000, 9, true));
                }
                // Length and distance
                else
                {
                    (int code, int extraBits, int numExtraBits) translatedLength = ConvertToCode(value.length, LengthTable);

                    // Add length
                    if (translatedLength.code <= 279) code.Add((translatedLength.code - 256 + 0b0000000, 7, true));
                    else code.Add((translatedLength.code - 280 + 0b11000000, 8, true));

                    // Add length extra bits
                    if (translatedLength.numExtraBits != 0)
                        code.Add((translatedLength.extraBits, translatedLength.numExtraBits, false));

                    (int code, int extraBits, int numExtraBits) translatedDistance = ConvertToCode(value.distance, DistanceTable);

                    // Add distance
                    code.Add((translatedDistance.code, 5, true));

                    // Add distance extra bits
                    if (translatedDistance.numExtraBits != 0)
                        code.Add((translatedDistance.extraBits, translatedDistance.numExtraBits, false));
                }
            }

            // End of block
            code.Add((256, 7, true));

            return code;
        }

        private static (int code, int extraBits, int numExtraBits) ConvertToCode(int length, (int code, int numExtraBits, int start)[] table)
        {
            int i;
            // Search for relevant entry in table
            for (i = 1; i < table.Length; i++)
            {
                if (length < table[i].start)
                {
                    i--;
                    break;
                }
                if (i == table.Length - 1) break;
            }

            // Use value in table to translate to code
            (int code, int numExtraBits, int startValue) code = table[i];
            return (code.code, length - code.startValue, code.numExtraBits);
        }

        internal record struct Code(int value, int numBits, bool huffman)
        {
            public static implicit operator Code((int value, int numBits, bool huffman) value)
            {
                return new Code(value.value, value.numBits, value.huffman);
            }
        }

        internal record struct LDPair(int length, int distance)
        {
            public static implicit operator LDPair((int length, int distance) value)
            {
                return new LDPair(value.length, value.distance);
            }

            public int literal { get { return length; } set { length = value; } }
        }
    }
}