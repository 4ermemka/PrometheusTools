using Newtonsoft.Json;
using System;

[Serializable]
public class Vector2Dto
{
    public float x { get; set; }
    public float y { get; set; }

    public static Vector2Dto Zero()
    {
        return new Vector2Dto()
        {
            x = 0,
            y = 0
        };
    }

    public static Vector2Dto One()
    {
        return new Vector2Dto()
        {
            x = 1,
            y = 1
        };
    }
}

