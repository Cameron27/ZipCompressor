namespace ZipCompressor
{
    class BitPacker
    {
        private List<byte> bytes = new List<byte>();
        private byte currentByte = 0;
        private int currentIndex = 0;

        public BitPacker() { }

        public void Add(int value, int numBits)
        {
            // Add each bit from least significant to most significant
            while (numBits > 0)
            {
                // Calculate number of bits to add
                int numBitsToAdd = Math.Min(numBits, 8 - currentIndex);

                // Add bits to current byte
                currentByte |= (byte)(value << currentIndex);

                value >>= numBitsToAdd;
                numBits -= numBitsToAdd;

                // Increment index and reset if end of current bit was reached
                currentIndex += numBitsToAdd;
                if (currentIndex == 8)
                {
                    currentIndex = 0;
                    bytes.Add(currentByte);
                    currentByte = 0;
                }
            }
        }

        public void AddReverse(int value, int numBits)
        {
            // Add each bit from most significant to least significant
            while (numBits > 0)
            {
                // Find out if bit is 0 or 1
                byte b = ((1 << (numBits - 1) & value) == 0) ? (byte)0 : (byte)1;

                // Offset bit and add it to current byte
                b <<= currentIndex;
                currentByte |= b;

                // Increment index and reset if end of current bit was reached
                currentIndex++;
                if (currentIndex == 8)
                {
                    currentIndex = 0;
                    bytes.Add(currentByte);
                    currentByte = 0;
                }

                numBits--;
            }
        }

        public byte[] ToArray()
        {
            // Add the current byte if there is something in it
            if (currentIndex != 0) bytes.Add(currentByte);

            byte[] res = bytes.ToArray();

            // Remove the byte that was just added
            if (currentIndex != 0) bytes.RemoveAt(bytes.Count - 1);

            return res;
        }
    }
}