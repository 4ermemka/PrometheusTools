namespace Assets.Scripts.Network.NetCore
{
    public interface IGameSerializer
    {
        byte[] Serialize<T>(T obj);
        T Deserialize<T>(byte[] bytes);
    }
}
