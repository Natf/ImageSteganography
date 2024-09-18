using System;
using System.IO;
using System.Reflection.Metadata;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

class Program
{
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

    static List<bool> GetImageDataBits(Image<Rgba32> image, int secretImageBits)
    {
        List<bool> imageDataBits = new List<bool>();
        int imageWidth = image.Width;
        int imageHeight = image.Height;
        imageDataBits.AddRange(GetBitsFromInt(imageWidth));
        imageDataBits.AddRange(GetBitsFromInt(imageHeight));
        imageDataBits.AddRange(GetBitsFromInt(secretImageBits));

        return imageDataBits;

    }

    static Dictionary<string, int> LoadImageDataFromBits(List<bool> bits)
    {
        Dictionary<string, int> imageData = new Dictionary<string, int>();

        imageData.Add("imageWidth", GetIntFromBits(bits.GetRange(0,32)));
        imageData.Add("imageHeight", GetIntFromBits(bits.GetRange(32,32)));
        imageData.Add("secretImageBits", GetIntFromBits(bits.GetRange(64,32)));

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

    static void ScaleEmbedImage(Image<Rgba32> coverImage, Image<Rgba32> secretImage)
    {
        List<bool> bits = GetBitsFromImage(coverImage, 2);
        string output = string.Join(", ", bits.Select(b => b ? "1" : "0"));
        Console.WriteLine("Got bits: count: " + bits.Count);

        int availableBits = bits.Count - 96; // const 96 TODO image data header
        int secretImagePixels = secretImage.Width * secretImage.Height;
        int bitsPerPixel = availableBits / secretImagePixels;
        bitsPerPixel /= 3; // each pixel has 3 colors
        if (bitsPerPixel > 8) {
            bitsPerPixel = 8;
        }
        
        List<bool> secretImageData = GetImageDataBits(secretImage, bitsPerPixel);
        Console.WriteLine("secret image data bits count: " + secretImageData.Count);

        Console.WriteLine("Using " + bitsPerPixel + " bits per pixel");

        secretImageData.AddRange(GetBitsFromImage(secretImage, bitsPerPixel, false));

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
        Console.WriteLine(imageData.Keys.Count);
        var secretWidth = imageData["imageWidth"];
        var secretHeight = imageData["imageHeight"];
        var secretImageBits = imageData["secretImageBits"];

        Console.WriteLine("Loading secret image (" + secretWidth + " x " +secretHeight + ") - bits: " + secretImageBits);
        var secretImage = new Image<Rgba32>(secretWidth, secretHeight);

        int currentBit = 96; // TODO header const
        int startBit = 8 - secretImageBits;
        int endBit = 8;

        for (int y = 0; y < secretImage.Height; y++)
        {
            for (int x = 0; x < secretImage.Width; x++)
            {
                secretImage[x, y] = new Rgba32(
                    SetBits((byte)0, startBit, endBit, encodedImageBits.GetRange(currentBit, secretImageBits)),
                    SetBits((byte)0, startBit, endBit, encodedImageBits.GetRange(currentBit + secretImageBits, secretImageBits)),
                    SetBits((byte)0, startBit, endBit, encodedImageBits.GetRange(currentBit + secretImageBits + secretImageBits, secretImageBits)),
                    255
                );

                currentBit += (3 * secretImageBits);
            }
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