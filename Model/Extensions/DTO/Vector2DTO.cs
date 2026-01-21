using Newtonsoft.Json;
using System;

[Serializable]
public struct Vector2Dto
{
    [JsonProperty("x")]
    public float x;
    [JsonProperty("y")]
    public float y;

    public Vector2Dto(float x, float y)
    {
        this.x = x;
        this.y = y;
    }
    // Для отладки
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

    public override string ToString() => $"({x}, {y})";
}

