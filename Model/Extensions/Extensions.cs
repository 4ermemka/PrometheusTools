using UnityEngine;

namespace Assets.Shared.Model.Extensions
{
    public static class Extensions
    {
        public static Vector2Dto FromVector2(this Vector2 vector)
        {
            return new Vector2Dto()
            {
                x = vector.x,
                y = vector.y
            };
        }

        public static Vector2 FromVector2DTO(this Vector2Dto vector)
        {
            return new Vector2()
            {
                x = vector.x,
                y = vector.y
            };
        }
    }
}