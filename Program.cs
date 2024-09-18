using System;
using System.IO;
using System.Reflection.Metadata;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using System.IO.Compression;

class Program
{
    const int HEADER_BIT_LENGTH = 192;
    const float RESCALE_BIAS = 1.5f; // how much larger than original we allow before compression starts hitting harder
    static void Main(string[] args)
    {

        string mode = args[0];
        string coverImagePath = "";
        string secretImagePath = "";
        string encodedOutputPath = "";
        string encodedInputPath = "";
        string decodedOutputPath = "";

        if (mode.Equals("encode", StringComparison.OrdinalIgnoreCase))
        {
            if (args.Length > 1) coverImagePath = args[1];
            if (args.Length > 2) secretImagePath = args[2];
            if (args.Length > 3) encodedOutputPath = args[3];
        }
        else if (mode.Equals("decode", StringComparison.OrdinalIgnoreCase))
        {
            if (args.Length > 1) encodedInputPath = args[1];
            if (args.Length > 2) decodedOutputPath = args[2];
        }

        if (mode.Equals("encode", StringComparison.OrdinalIgnoreCase))
        {
            using (var coverImage = Image.Load<Rgba32>(coverImagePath))
            using (var secretImage = Image.Load<Rgba32>(secretImagePath))
            {
                ScaleEmbedImage(coverImage, secretImage);
                coverImage.Save(encodedOutputPath);
                Console.WriteLine("Image encoded successfully.");
            }
        }  else if (mode.Equals("decode", StringComparison.OrdinalIgnoreCase))
        {
            using (var encodedImage = Image.Load<Rgba32>(encodedInputPath))
            {
                var secretImage = ScaleDecodeImage(encodedImage);
                secretImage.Save(decodedOutputPath);
                Console.WriteLine("Image decoded successfully.");
            }
        }
    }

    static List<bool> GetImageDataBits(Image<Rgba32> image, int secretImageBits, int secretImageLengthInBits, int originalWidth, int originalHeight)
    {
        List<bool> imageDataBits = new List<bool>();
        int imageWidth = image.Width;
        int imageHeight = image.Height;
        imageDataBits.AddRange(GetBitsFromInt(imageWidth));
        imageDataBits.AddRange(GetBitsFromInt(imageHeight));
        imageDataBits.AddRange(GetBitsFromInt(secretImageBits));
        imageDataBits.AddRange(GetBitsFromInt(secretImageLengthInBits));
        imageDataBits.AddRange(GetBitsFromInt(originalWidth));
        imageDataBits.AddRange(GetBitsFromInt(originalHeight));

        return imageDataBits;

    }

    static Dictionary<string, int> LoadImageDataFromBits(List<bool> bits)
    {
        Dictionary<string, int> imageData = new Dictionary<string, int>();

        imageData.Add("imageWidth", GetIntFromBits(bits.GetRange(0,32)));
        imageData.Add("imageHeight", GetIntFromBits(bits.GetRange(32,32)));
        imageData.Add("secretImageBits", GetIntFromBits(bits.GetRange(64,32)));
        imageData.Add("secretImageLengthInBits", GetIntFromBits(bits.GetRange(96,32)));
        imageData.Add("originalWidth", GetIntFromBits(bits.GetRange(128,32)));
        imageData.Add("originalHeight", GetIntFromBits(bits.GetRange(160,32)));

        return imageData;
    }

    static List<bool> GetBitsFromInt(int number)
    {
        List<bool> bits = new List<bool>();

        for (int i = 31; i >= 0; i--) // Loop through each bit position
        {
            bits.Add((number & (1 << i)) != 0); // Check if the bit at position i is set
        }

        return bits;
    }

    static int GetIntFromBits(List<bool> bits)
    {
        int number = 0;

        // Loop through the bits and construct the integer
        for (int i = 0; i < bits.Count; i++)
        {
            if (bits[i])
            {
                number |= (1 << (bits.Count - 1 - i)); // Set the bit at the appropriate position
            }
        }

        return number;
    }

    static List<bool> GetBitsFromImage(Image<Rgba32> image, int bitCount = 2, bool leastSignificant = true)
    {
        List<bool> bits = new List<bool>();
        int startBit = 0;
        int endBit = bitCount;
        if (!leastSignificant) {
            startBit = 8 - bitCount;
            endBit = 8;
        }

        for (int y = 0; y < image.Height; y++)
        {
            for (int x = 0; x < image.Width; x++)
            {
                Rgba32 pixelData = image[x, y];
                bits.AddRange(GetBits(pixelData.R, startBit, endBit));
                bits.AddRange(GetBits(pixelData.G, startBit, endBit));
                bits.AddRange(GetBits(pixelData.B, startBit, endBit));
            }
        }

        return bits;
    }

    static Image<Rgba32> ScaleImageDownIfNeccessary(Image<Rgba32> coverImage, Image<Rgba32> secretImage)
    {
        int maxWidth = (int)(coverImage.Width * RESCALE_BIAS);
        int maxHeight = (int)(coverImage.Height * RESCALE_BIAS);
        int heightAspectRatio = secretImage.Width / secretImage.Height;
        if (secretImage.Width > maxWidth || secretImage.Height > maxHeight) {
            Console.Write("Rescaling image original (" + secretImage.Width + "x" + secretImage.Height + ") resized ");
            int widthDiff = secretImage.Width / maxWidth;
            int heightDiff = secretImage.Height / maxHeight;
            if (heightDiff > widthDiff) {
                Console.WriteLine("(" + maxHeight / heightAspectRatio + "x" + maxHeight + ")");
                secretImage.Mutate(x => x.Resize(maxHeight / heightAspectRatio, maxHeight));
            } else if (heightDiff < widthDiff) {
                Console.WriteLine("(" + maxWidth + "x" + maxWidth * heightAspectRatio + ")");
                secretImage.Mutate(x => x.Resize(maxWidth, maxWidth * heightAspectRatio));
            } else {
                Console.WriteLine("(" + maxWidth + "x" + maxHeight + ")");
                secretImage.Mutate(x => x.Resize(maxWidth, maxHeight));
            }
        }
        
        return secretImage;
    }

    static void ScaleEmbedImage(Image<Rgba32> coverImage, Image<Rgba32> secretImage)
    {
        
        int originalWidth = secretImage.Width;
        int originalHeight = secretImage.Height;
        secretImage = ScaleImageDownIfNeccessary(coverImage, secretImage);
        List<bool> bits = GetBitsFromImage(coverImage, 2);
        string output = string.Join(", ", bits.Select(b => b ? "1" : "0"));
        Console.WriteLine("Cover image bits: count: " + bits.Count);

        int availableBits = bits.Count - HEADER_BIT_LENGTH; // const HEADER_BIT_LENGTH TODO image data header
        int secretImagePixels = secretImage.Width * secretImage.Height;
        int bitsPerPixel = availableBits / secretImagePixels;
        bitsPerPixel /= 3; // each pixel has 3 colors
        if (bitsPerPixel > 8) {
            bitsPerPixel = 8;
        }


        /// COMPRESSION

        bitsPerPixel = 8;
        List<bool> bitsToCompress = GetBitsFromImage(secretImage, 8, false);
        int originalBitsCount = bitsToCompress.Count;
        List<byte> imageBytes = ConvertBitsToBytes(bitsToCompress);
        byte[] compressedData = Compress(imageBytes.ToArray());
        List<bool> secretImageDataCompressed = ConvertBytesToBits(compressedData);

        while (secretImageDataCompressed.Count > availableBits && bitsPerPixel > 1) {
            bitsPerPixel --;
            Console.WriteLine("Increasing compression using " + bitsPerPixel + " bits...");
            bitsToCompress = GetBitsFromImage(secretImage, bitsPerPixel, false);
            imageBytes = ConvertBitsToBytes(bitsToCompress);
            compressedData = Compress(imageBytes.ToArray());
            secretImageDataCompressed = ConvertBytesToBits(compressedData);
        }

        //List<bool> secretImageBits = GetBitsFromImage(secretImage, bitsPerPixel, false);

        float compressionPercentage = (float)secretImageDataCompressed.Count/(float)originalBitsCount;
        compressionPercentage *= 100f;
        
        Console.WriteLine("Secret image data bits compressed/original(%): " + secretImageDataCompressed.Count + "/" + originalBitsCount + "(" + (int)compressionPercentage + "%)");
        Console.WriteLine("Using " + bitsPerPixel + " bits per pixel");

        // COMPRESSION END
        
        
        List<bool> secretImageData = GetImageDataBits(secretImage, bitsPerPixel, secretImageDataCompressed.Count, originalWidth, originalHeight);
        Console.WriteLine("Header bits count: " + secretImageData.Count);

        secretImageData.AddRange(secretImageDataCompressed);

        bits = SetBits(bits, secretImageData);
        int currentBit = 0;

        for (int y = 0; y < coverImage.Height; y++)
        {
            for (int x = 0; x < coverImage.Width; x++)
            {
                var coverPixel = coverImage[x, y];

                coverImage[x, y] = new Rgba32(
                    SetBits(coverPixel.R, 0, 2, bits.GetRange(currentBit, 2)),
                    SetBits(coverPixel.G, 0, 2, bits.GetRange(currentBit + 2, 2)),
                    SetBits(coverPixel.B, 0, 2, bits.GetRange(currentBit + 4, 2)),
                    coverPixel.A
                );

                currentBit += 6;
            }
        }

    }

    static Image<Rgba32> ScaleDecodeImage(Image<Rgba32> encodedImage)
    {
        List<bool> encodedImageBits = GetBitsFromImage(encodedImage);
        Dictionary<string, int> imageData = LoadImageDataFromBits(encodedImageBits);
        var secretWidth = imageData["imageWidth"];
        var secretHeight = imageData["imageHeight"];
        var secretImageBits = imageData["secretImageBits"];
        var secretImageLengthInBits = imageData["secretImageLengthInBits"];
        var originalWidth = imageData["originalWidth"];
        var originalHeight = imageData["originalHeight"];

        Console.WriteLine("Loading secret image (" + secretWidth + " x " +secretHeight + ") - bits per color: " + secretImageBits);
        var secretImage = new Image<Rgba32>(secretWidth, secretHeight);


        int currentBit = 0; // TODO header const
        int startBit = 8 - secretImageBits;
        int endBit = 8;
        List<bool> secretImageAllBits = encodedImageBits.GetRange(HEADER_BIT_LENGTH, secretImageLengthInBits);
        byte[] decompressedData = Decompress(ConvertBitsToBytes(secretImageAllBits).ToArray());
        List<bool> decompressedBits = ConvertBytesToBits(decompressedData);

        Console.WriteLine("Got " + decompressedBits.Count + " decompressed bits - expected: " + (secretWidth * secretHeight * 3 * secretImageBits));

        for (int y = 0; y < secretImage.Height; y++)
        {
            for (int x = 0; x < secretImage.Width; x++)
            {
                secretImage[x, y] = new Rgba32(
                    SetBits((byte)0, startBit, endBit, decompressedBits.GetRange(currentBit, secretImageBits)),
                    SetBits((byte)0, startBit, endBit, decompressedBits.GetRange(currentBit + secretImageBits, secretImageBits)),
                    SetBits((byte)0, startBit, endBit, decompressedBits.GetRange(currentBit + secretImageBits + secretImageBits, secretImageBits)),
                    255
                );

                currentBit += (3 * secretImageBits);
            }
        }

        if (originalHeight != secretHeight || originalWidth != secretWidth) {
            Console.WriteLine("Resizing image encoded size (" + secretWidth + "x" + secretHeight + ") original size (" + originalWidth + "x" + originalHeight + ")");
        }

        return secretImage;
    }

    static void PrintBits(List<bool> boolList)
    {
        // Convert to 0s and 1s
        string output = string.Join(", ", boolList.Select(b => b ? "1" : "0"));

        // Print to console
        Console.WriteLine(output);
    }

    public static List<byte> ConvertBitsToBytes(List<bool> bits)
    {
        List<byte> bytes = new List<byte>();
        byte currentByte = 0;

        for (int i = 0; i < bits.Count; i++)
        {
            // Set the appropriate bit in the current byte
            if (bits[i])
            {
                currentByte |= (byte)(1 << (7 - (i % 8)));
            }

            // If we've filled a byte (8 bits), add it to the list
            if (i % 8 == 7)
            {
                bytes.Add(currentByte);
                currentByte = 0; // Reset for the next byte
            }
        }

        // Add the last byte if it has any bits set
        if (bits.Count % 8 != 0)
        {
            bytes.Add(currentByte);
        }

        return bytes;
    }

    public static List<bool> ConvertBytesToBits(byte[] bytes)
    {
        List<bool> bits = new List<bool>();

        foreach (var b in bytes)
        {
            for (int i = 7; i >= 0; i--) // Extract bits from the most significant to least significant
            {
                bits.Add((b & (1 << i)) != 0);
            }
        }

        return bits;
    }

    public static byte[] Compress(byte[] data)
    {
        using (var outputStream = new MemoryStream())
        {
            using (var gzipStream = new GZipStream(outputStream, CompressionMode.Compress))
            {
                gzipStream.Write(data, 0, data.Length);
            }
            return outputStream.ToArray();
        }
    }

    public static byte[] Decompress(byte[] compressedData)
    {
        using (var inputStream = new MemoryStream(compressedData))
        {
            using (var gzipStream = new GZipStream(inputStream, CompressionMode.Decompress))
            {
                using (var outputStream = new MemoryStream())
                {
                    gzipStream.CopyTo(outputStream);
                    return outputStream.ToArray();
                }
            }
        }
    }

    static List<bool> SetBits(List<bool> destination, List<bool> data)
    {
        for(int bitIndex = 0; bitIndex < data.Count; bitIndex ++) {
            destination[bitIndex] = data[bitIndex];
        }

        return destination;
    }

    static void EmbedImage(Image<Rgba32> cover, Image<Rgba32> secret, int encodedQuality)
    {
        if (secret.Width > cover.Width || secret.Height > cover.Height)
        {
            throw new ArgumentException("The secret image must be smaller than the cover image.");
        }

        for (int y = 0; y < secret.Height; y++)
        {
            for (int x = 0; x < secret.Width; x++)
            {
                var coverPixel = cover[x, y];
                var secretPixel = secret[x, y];

                byte secretByte = GetByteFromColor(secretPixel, encodedQuality, false, false);

                cover[x, y] = AddByteToColor(coverPixel, secretByte, encodedQuality, false, true);
            }
        }
    }

    static Image<Rgba32> ExtractImage(Image<Rgba32> cover, int encodedQuality)
    {
        var secretWidth = cover.Width;
        var secretHeight = cover.Height;
        var secretImage = new Image<Rgba32>(secretWidth, secretHeight);

        for (int y = 0; y < secretHeight; y++)
        {
            for (int x = 0; x < secretWidth; x++)
            {
                var coverPixel = cover[x, y];

                byte secretByte = GetByteFromColor(coverPixel, encodedQuality, false, true);
                
                secretImage[x, y] = AddByteToColor(new Rgba32(0,0,0,255), secretByte, encodedQuality, false, false); // Assume fully opaque
            }
        }

        return secretImage;
    }

    static byte GetByteFromColor(Rgba32 colorData, int encodedQuality, bool useAlpha = false, bool leastSignificant = true)
    {
        int startIndex = 0;
        int endIndex = encodedQuality;

        if (!leastSignificant) {
            startIndex = 8 - encodedQuality;
            endIndex = 8;
        }

        byte byteData = 0;
        int multiplier = 1;

        for (int i = 0; i < 8; i++) {
            List<bool> allBits = new List<bool>();
            if (useAlpha) {
                allBits.AddRange(GetBits(colorData.A, startIndex, endIndex));
            } else {
                allBits.Add(false);
                allBits.Add(false);
            }
            allBits.AddRange(GetBits(colorData.B, startIndex, endIndex));
            allBits.AddRange(GetBits(colorData.G, startIndex, endIndex));
            allBits.AddRange(GetBits(colorData.R, startIndex, endIndex));

            foreach (bool bit in allBits) {
                byte bitValue = (byte)(bit ? 1 : 0);
                byteData += (byte)(bitValue * multiplier);
                multiplier *= 2;
            }
        }

        return byteData;
    }

    static Rgba32 AddByteToColor(Rgba32 originalColor, byte insertData, int encodedQuality, bool useAlpha = false, bool leastSignificant = true)
    {
        
        int startIndex = 0;
        int endIndex = encodedQuality;

        if (!leastSignificant) {
            startIndex = 8 - encodedQuality;
            endIndex = 8;
        }

        if (useAlpha) { // using alpha - useful 
            return new Rgba32(
                SetBits(originalColor.R, startIndex, endIndex, GetBits(insertData, 6, 8)),
                SetBits(originalColor.G, startIndex, endIndex, GetBits(insertData, 4, 6)),
                SetBits(originalColor.B, startIndex, endIndex, GetBits(insertData, 2, 4)),
                SetBits(originalColor.A, startIndex, endIndex, GetBits(insertData, 0, 2))
            );
        } else {
            return new Rgba32(
                SetBits(originalColor.R, startIndex, endIndex, GetBits(insertData, 6, 8)),
                SetBits(originalColor.G, startIndex, endIndex, GetBits(insertData, 4, 6)),
                SetBits(originalColor.B, startIndex, endIndex, GetBits(insertData, 2, 4)),
                originalColor.A
            );
        }
    }

    static List<bool> GetBits(byte number, int startBit, int endBit)
    {
        List<bool> bits = new List<bool>();

        for(int currentBit = startBit; currentBit < endBit; currentBit++)
        {
            bits.Add(GetBit(number, currentBit));
        }

        return bits;
    }

    static bool GetBit(byte number, int bitIndex)
    {
        return (number & (1 << bitIndex)) != 0;
    }

    static byte SetBits(byte number, int startBit, int endBit, List<bool> values)
    {
        int valueIndex = 0;
        for(int currentBit = startBit; currentBit < endBit; currentBit++)
        {
            number = SetBit(number, currentBit, values[valueIndex]);
            valueIndex++;
        }
        return number;
    }

    static byte SetBit(byte number, int bitIndex, bool value)
    {
        if (value)
        {
            return (byte)(number | (1 << bitIndex)); // Set the bit to 1
        }
        else
        {
            return (byte)(number & ~(1 << bitIndex)); // Set the bit to 0
        }
    }

}