// using System.Drawing;
using System.Collections.Generic;
using System.Linq;
using System.CommandLine;
using Newtonsoft.Json;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using System;
using System.IO;

namespace HelloWorld
{


class Area
{
    public Rgba32 BaseColor;
    public Rgba32 RemapColor;
    public int Points;
    public float X;
    public float Y;
    public HashSet<Area> Neighbors = new HashSet<Area>();
    public bool IsEdge;

    public void Connect(Area other)
    {
        Neighbors.Add(other);
        other.Neighbors.Add(this);
    }
}

[Serializable]
class AreaReduced
{
    public int[] BaseColor;// = new int[4];
    public int[] RemapColor;// = new int[4];
    public int Points;
    public float X;
    public float Y;
    public List<int[]> Neighbors;
    public bool IsEdge;

    static int[] EncodeColor(Rgba32 c) => new int[]{c.R, c.G, c.B, c.A};

    public AreaReduced(Area area)
    {
        Neighbors = new List<int[]>();
        foreach(var neiArea in area.Neighbors)
            Neighbors.Add(EncodeColor(neiArea.BaseColor));

        BaseColor = EncodeColor(area.BaseColor);
        RemapColor = EncodeColor(area.RemapColor);

        Points = area.Points;
        X = area.X;
        Y = area.Y;
        IsEdge = area.IsEdge;
    }
}

[Serializable]
class JsonResult
{
    public List<AreaReduced> Areas;// = new List<AreaReduced>();
    public JsonResult(List<AreaReduced> Areas) => this.Areas = Areas;
}

class Program
{
    static void Process(string imagePath, bool listMinAreaLocs, bool indent)
    {
        // var stopWatch = new System.Diagnostics.Stopwatch();
        // stopWatch.Restart();

        var img = Image.Load<Rgba32>(imagePath);
        
        var width = img.Width;
        var height = img.Height;

        // stopWatch.Stop();

        Console.WriteLine($"width:{width}, height:{height}");

        // stopWatch.Restart();

        var idx = 0;
        var areaMap = new Dictionary<Rgba32, Area>();

        var remapImg = new Image<Rgba32>(width, height);
        for(int y=0; y<height; y++)
            for(int x=0; x<width; x++)
            {
                // var baseColor = img.GetPixel(x, y);
                var baseColor = img[x, y]; // TODO: https://docs.sixlabors.com/articles/imagesharp/pixelbuffers.html
                Area? area; // C# 8 requires Nullable reference type
                if(!areaMap.TryGetValue(baseColor, out area))
                {
                    var low = (byte)(idx % 256);
                    var high = (byte)(idx / 256);
                    var remapColor = new Rgba32(low, high, 0, 255);
                    area = new Area(){BaseColor=baseColor, RemapColor=remapColor};
                    areaMap[baseColor] = area;
                    idx += 1;
                }
                // remapImg.SetPixel(x, y, area.RemapColor);
                remapImg[x, y] = area.RemapColor;
                area.Points += 1;
                area.X += x;
                area.Y += y;
                if(x == 0 || y == 0 || y == height - 1 || x == height - 1)
                    area.IsEdge = true;
            }

        foreach(var area in areaMap.Values)
        {
            area.X /= area.Points;
            area.Y /= area.Points;
        }

        if(idx > 256 * 256)
            throw new ArgumentException("The size of province color should be < 256*256");

        Console.WriteLine($"Area size: {areaMap.Count}");

        var _name = Path.GetFileNameWithoutExtension(imagePath);
        string parent = Path.GetDirectoryName(imagePath) ?? throw new ArgumentNullException(nameof(imagePath), $"imagePath: {imagePath} failed to get parent"); // ?? "";
        
        var path = Path.Combine(parent, _name);
        remapImg.Save(path + "_remap.png");

        for(int y=0; y<height; y++)
        {
            for(int x=0; x<width; x++)
            {
                var c1 = img[x, y];

                if(y < height-1)
                {
                    var c2 = img[x, y+1];
                    if(!c1.Equals(c2))
                        areaMap[c1].Connect(areaMap[c2]);
                }
                if(x < width - 1)
                {
                    var c3 = img[x+1, y];
                    if(!c1.Equals(c3))
                        areaMap[c1].Connect(areaMap[c3]);
                }
            }
        }

        var reduceIter = from area in areaMap.Values select new AreaReduced(area);
        var res = new JsonResult(reduceIter.ToList());

        string jsonString;
        if(indent)
            jsonString = JsonConvert.SerializeObject(res, Formatting.Indented); // Well, I would like a custom depth, though.
        else
            jsonString = JsonConvert.SerializeObject(res);
        
        File.WriteAllText(path + "_data.json", jsonString);

        // Diagnosis

        Area minArea = areaMap.Values.MinBy(area => area.Points) ?? throw new ArgumentException("areaMap is empty");
        Console.WriteLine($"Min Area -> Points: {minArea.Points}, Min Area Colors: {minArea.BaseColor}");

        if(listMinAreaLocs)
        {
            var minRgba = minArea.BaseColor;

            img.ProcessPixelRows(accessor =>
            {
                for(int y=0; y < accessor.Height; y++)
                {
                    Span<Rgba32> pixelRow = accessor.GetRowSpan(y);

                    for(int x=0; x < pixelRow.Length; x++)
                    {
                        ref Rgba32 pixel = ref pixelRow[x];
                        if(pixel.Equals(minRgba))
                        {
                            Console.WriteLine($"{minRgba} -> (x={x}, y={y})");
                        }
                    }
                }
            });
        }
    }

    static int Main(string[] args)
    {
        
        var imgPathArg = new Argument<string>("imgPath", "The path of image");
        var listMinAreaLocs = new Option<bool>("--list-min-locs", "List locations of points of the area which has min points");
        var indent = new Option<bool>("--indent", "Prints indent for JSON output");

        var rootCommand = new RootCommand
        {
            imgPathArg,
            listMinAreaLocs,
            indent
        };

        rootCommand.Description = "Pixel based map processor. You can drag path into command line at most system.";

        // rootCommand.Handler = 
        rootCommand.SetHandler((string p, bool l, bool i) => Process(p, l, i), imgPathArg, listMinAreaLocs, indent);

        var stopWatch = new System.Diagnostics.Stopwatch();
        stopWatch.Restart();

        var ret = rootCommand.Invoke(args);

        stopWatch.Stop();
        Console.WriteLine($"Elaspsed : {stopWatch.Elapsed}");

        return ret;
        
        /*
        // Process("/home/yyz/Pictures/Provinces_2600_100_3600_1000.png");
        return 0;
        */
    }
}


}
