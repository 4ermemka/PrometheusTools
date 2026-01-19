using Newtonsoft.Json;
using UnityEngine;

public class Vector2Dto
{
    [JsonProperty("x")]
    public float x { get; set; }
    [JsonProperty("y")]
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

